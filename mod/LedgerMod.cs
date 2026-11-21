using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WhiteDragon.Ledger
{
    public sealed class LedgerMod : IMod
    {
        private GameObject observer;
        public void Init(ModManifest manifest) { }
        public void OnEnable()
        {
            observer = new GameObject("White Dragon Ledger Observer");
            UnityEngine.Object.DontDestroyOnLoad(observer);
            observer.AddComponent<InventoryObserver>();
        }
        public void OnDisable()
        {
            if (observer != null) UnityEngine.Object.Destroy(observer);
        }
    }

    public sealed partial class InventoryObserver : MonoBehaviour
    {
        private const string ModId = "white_dragon_ledger";
        private readonly Dictionary<GameInventory, string> areas = new Dictionary<GameInventory, string>();
        private readonly Dictionary<GameItem, int> ids = new Dictionary<GameItem, int>();
        private readonly Dictionary<int, GameItem> itemsById = new Dictionary<int, GameItem>();
        private readonly HashSet<GameItem> changed = new HashSet<GameItem>();
        private readonly Dictionary<int, JObject> known = new Dictionary<int, JObject>();

        private readonly HashSet<GameItem> bottles = new HashSet<GameItem>();
        private string generation;
        private int sequence, fullScans, loadScans, nextId, slot;
        private bool pending;
        private string phase;

        private static void Write(string name, JObject body)
        {
            ModHelper.WriteModDataFileText(ModId, name, body.ToString(Formatting.None));
        }

        private void Awake()
        {
            ResetLoad();
        }

        private void OnEnable()
        {

            ModHook.OnAfterhourArriveEarly += OnArriveEarly;
            ModHook.OnAfterhourArriveLate += OnTravelLate;
            ModHook.OnAfterhourLeaveEarly += OnLeaveEarly;
            ModHook.OnAfterhourLeaveLate += OnTravelLate;
            ModHook.OnGameLoadedInit += ResetLoad;
            ModHook.OnGameLoadedLate += Ready;
            ModHook.OnGoingSleepEarly += BeforeSleep;
        }

        private void ResetLoad()
        {
            ResetScene();
            ResetCalendar();
            workstations.Clear();
            observedWaterPanel = null;
            autoReceiveDay = 0;
            receptionToken = null;
            DetachTrash();
            trashVersion = trashDay = sleepSubmittedDay = 0;
            trashDirty = true;
            DisconnectVisit();
            received.Clear();
            payments.Clear();
            purifiers.Clear();
            stripSupplies.Clear();
            phoneCards.Clear();
            previousPhone = null;
            previousPhonePage = null;
            order = completedOrder = line = null;
            staged = null;
            DetachAreas();
            ids.Clear();
            itemsById.Clear();
            changed.Clear();
            known.Clear();
            bottles.Clear();
            pending = false;
            generation = null;
            nextId = sequence = loadScans = 0;
            Write("head.json", new JObject { ["status"] = "waiting_for_load" });
        }

        private void Ready() { pending = true; }

        private void DetachAreas()
        {
            foreach (var area in areas.Keys)
            {
                area.onSlotAddItemLateFunc -= Changed;
                area.onSlotRemoveItemLateFunc -= Changed;
                area.onSlotChangeAmountItemFunc -= Changed;
            }
            areas.Clear();
        }

        private void Changed(GameItem item, GameInventory inventory, SlotMarker marker)
        {
            changed.Add(item);
        }

        private void Track(GameInventory area, string name)
        {
            areas.Add(area, name);
            area.onSlotAddItemLateFunc += Changed;
            area.onSlotRemoveItemLateFunc += Changed;
            area.onSlotChangeAmountItemFunc += Changed;
        }

        private JObject Row(GameItem item, string area)
        {
            if (CardNumber(item.identifier) != 0) phoneCards.Add(item);
            if (item.identifier == "water_purifier") purifiers.Add(item);
            if (item.identifier == "water_test_strip" || item.identifier == "water_test_container") stripSupplies.Add(item);
            var row = new JObject { ["id"] = LocalId(item), ["save_id"] = item.uniqueId, ["name"] = item.GetDisplayName(true),
                ["kind"] = item.identifier, ["area"] = area, ["owned"] = GeneralHelper.IsItemOwned(item),
                ["quantity"] = item.unitCount };
            if (BottleLabel(item) is string label)
            {
                bottles.Add(item);
                row["bottle_label"] = label;
            }
            return row;
        }

        private void Publish(JObject record)
        {
            int revision = sequence + 1;
            Write(generation + "-" + revision + ".json", record);
            Write("head.json", new JObject { ["status"] = "observed", ["move_protocol"] = 1, ["desk_protocol"] = 1, ["purifier_take_protocol"] = 1, ["purifier_put_protocol"] = 1, ["generation"] = generation,
                ["revision"] = revision, ["slot"] = slot, ["phase"] = phase,
                ["full_scans"] = fullScans, ["load_scans"] = loadScans });
            sequence = revision;
        }

        private void Baseline(EmporiumEntry shop)
        {
            pending = false;
            generation = Guid.NewGuid().ToString("N");
            slot = PlayerStore.Instance.saveSlotId;
            phase = PlayerStore.Instance.storeState.ToString();
            Track(shop.invElement, "inventory");
            Track(shop.showcaseElement, "showcase");
            Track(shop.frontInvinvElement, "front");
            Track(shop.backInvinvElement, "back");
            var rows = new JArray();
            ++fullScans;
            ++loadScans;
            foreach (var area in areas)
                foreach (var item in area.Key.childItems)
                {
                    var row = Row(item, area.Value);
                    known.Add(ids[item], row);
                    rows.Add(row);
                }
            LoadDesk();
            Publish(new JObject { ["op"] = "baseline", ["items"] = rows });
            RefreshDesk();
        }

        private void LateUpdate()
        {

            if (!CanObserveStore()) return;
            if (pending)
            {
                var shop = EmporiumEntry.Instance;
                if (shop != null && PlayerStore.HasInstance && shop.invElement != null &&
                    shop.showcaseElement != null && shop.frontInvinvElement != null && shop.backInvinvElement != null)
                {
                    Baseline(shop);
                    shopBoundary = false;
                    MarkSceneReady(ShopScene, "當鋪");
                }
                return;
            }
            if (generation == null || !PlayerStore.HasInstance) return;
            CloseTransactions();
            if (Time.realtimeSinceStartup >= nextView)
                foreach (var bottle in bottles)
                    if (bottle != null && BottleLabel(bottle) != (string)known[ids[bottle]]["bottle_label"])
                        changed.Add(bottle);
            FlushChanges();
            InvalidatePreviousShopVisit();
            RefreshDesk();
            MarkSceneReady(ShopScene, "當鋪");
            CheckMove();
            CheckOrder();
            CheckCounter();
            TickReception();
        }

        private void FlushChanges()
        {
            var currentPhase = PlayerStore.Instance.storeState.ToString();
            if (changed.Count == 0 && phase == currentPhase) return;
            var upserts = new JArray();
            var removed = new JArray();
            foreach (var item in changed)
            {
                var parent = item.parentInventory;
                if (parent != null && areas.TryGetValue(parent, out var area))
                {
                    var row = Row(item, area);
                    int id = ids[item];
                    if (known.TryGetValue(id, out var old) && JToken.DeepEquals(old, row)) continue;
                    known[id] = row;
                    upserts.Add(row);
                }
                else if (ids.TryGetValue(item, out var id) && known.Remove(id))
                {
                    bottles.Remove(item);
                    purifiers.Remove(item);
                    stripSupplies.Remove(item);
                    phoneCards.Remove(item);
                    removed.Add(id);
                }
            }
            changed.Clear();
            if (upserts.Count == 0 && removed.Count == 0 && phase == currentPhase) return;
            phase = currentPhase;
            Publish(new JObject { ["op"] = "delta", ["upsert"] = upserts, ["remove"] = removed });
        }

        private void OnDestroy()
        {
            DetachTrash();
            DetachAreas();

            ResetScene();
            ModHook.OnAfterhourArriveEarly -= OnArriveEarly;
            ModHook.OnAfterhourArriveLate -= OnTravelLate;
            ModHook.OnAfterhourLeaveEarly -= OnLeaveEarly;
            ModHook.OnAfterhourLeaveLate -= OnTravelLate;
            ModHook.OnGameLoadedInit -= ResetLoad;
            ModHook.OnGameLoadedLate -= Ready;
            ModHook.OnGoingSleepEarly -= BeforeSleep;
            DisconnectVisit();
        }
    }
}
