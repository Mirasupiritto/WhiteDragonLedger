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
        private JArray purifierStock;
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
            ModHook.OnAfterhourLeaveLate += OnLeaveAfterhoursLate;
            ModHook.OnGameLoadedInit += ResetLoad;
            ModHook.OnGameLoadedLate += Ready;
            ModHook.OnNewGameStarted += StartNewGame;
            ModHook.OnGoingSleepEarly += BeforeSleep;
            ModHook.OnWakingUpEarly += CaptureMorningReport;
            ModHook.OnWakingUpLate += FinishMorningReport;
            ModHook.OnShutterOpenedLate += QueueCounterPapers;
        }

        private void ResetLoad()
        {
            ResetScene();
            ResetCalendar();
            morningReport = null;
            morningReportReady = false;
            pendingPapers.Clear();
            papersDirty = false;
            paperCleanup = null;
            observedNetworkFrame = null;
            previousNetwork = null;
            workstations.Clear();
            observedWaterPanel = null;
            noticeToken = null;
            stripReadings.Clear();
            waterPlan = null;
            autoReceiveDay = 0;
            receptionToken = null;
            DetachTrash();
            trashVersion = trashDay = sleepSubmittedDay = 0;
            trashDirty = true;
            DisconnectVisit();
            received.Clear();
            payments.Clear();
            purifierStock = null;
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

        private void Ready()
        {
            pending = true;
            // Loaded saves show their saved logs directly, without the waking hooks.
            if (PlayerStore.Instance.nightLogs.Count > 0) CaptureMorningReport();
        }
        private void StartNewGame(string _) { ResetLoad(); pending = true; }

        private void DetachAreas()
        {
            foreach (var area in areas.Keys)
            {
                area.onSlotAddItemLateFunc -= QueueFrontPaper;
                area.onSlotAddItemLateFunc -= Changed;
                area.onSlotRemoveItemLateFunc -= Changed;
                area.onSlotChangeAmountItemFunc -= Changed;
            }
            areas.Clear();
        }

        private void Changed(GameItem item, GameInventory inventory, SlotMarker marker)
        {
            changed.Add(item);
            if (pendingPapers.Count > 0) papersDirty = true;
        }

        private void Track(GameInventory area, string name)
        {
            areas.Add(area, name);
            if (name == "front") area.onSlotAddItemLateFunc += QueueFrontPaper;
            area.onSlotAddItemLateFunc += Changed;
            area.onSlotRemoveItemLateFunc += Changed;
            area.onSlotChangeAmountItemFunc += Changed;
        }

        private JObject Row(GameItem item, string area)
        {
            if (CardNumber(item.identifier) != 0) phoneCards.Add(item);
            if (item.identifier == "water_purifier") purifiers.Add(item);
            if (item.identifier == "water_test_strip" || item.identifier == "water_test_container") stripSupplies.Add(item);
            var row = StockRow(item, area);
            if (row["bottle_label"] != null) bottles.Add(item);
            return row;
        }

        private JObject StockRow(GameItem item, string area)
        {
            var row = new JObject { ["id"] = LocalId(item), ["save_id"] = item.uniqueId, ["name"] = item.GetDisplayName(true),
                ["kind"] = item.identifier, ["area"] = area, ["owned"] = GeneralHelper.IsItemOwned(item),
                ["quantity"] = item.unitCount, ["categories"] = new JArray(item.GetGameItemType()),
                ["contraband"] = item.IsTag(TypeHelper.CONTRABAND),
                ["trade_contraband"] = ContrabandHelper.IsContraband(item) };
            if (BottleLabel(item) is string label)
                row["bottle_label"] = label;
            return row;
        }

        private JArray ReadPurifierStock()
        {
            var rows = new JArray();
            foreach (var machine in purifiers)
            {
                if (machine.parentInventory == null || !areas.ContainsKey(machine.parentInventory) ||
                    !GeneralHelper.IsItemOwned(machine)) continue;
                var slotInventory = MachineHelper.GetBodyStorage(machine, 2) as GameSlotInventory;
                var bottle = slotInventory?.childItem;
                if (bottle == null) continue;
                var row = StockRow(bottle, "purifier");
                row["machine_id"] = LocalId(machine);
                var state = PlayerStore.Instance.storeState;
                row["can_take_now"] = (state == PlayerStore.StoreState.MORNING || state == PlayerStore.StoreState.CLOSED) &&
                    bottle.MayRemove() && !EmporiumEntry.Instance.invElement.IsInsertLocked() &&
                    EmporiumEntry.Instance.invElement.TryFindOneValidInventorySlot(bottle) != null;
                rows.Add(row);
            }
            return rows;
        }

        private void Publish(JObject record)
        {
            int revision = sequence + 1;
            record["phase"] = phase;
            record["day"] = StoreStation.Instance.dayCounter;
            Write(generation + "-" + revision + ".json", record);
            Write("head.json", new JObject { ["status"] = "observed", ["trade_panel_protocol"] = 1, ["stock_categories_protocol"] = 1, ["move_location_protocol"] = 1, ["counter_move_protocol"] = 1, ["move_protocol"] = 1, ["desk_protocol"] = 1, ["purifier_take_protocol"] = 1, ["purifier_put_protocol"] = 1, ["generation"] = generation,
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
            var categoryNames = new JObject();
            foreach (string code in TypeHelper.ALL_TYPES_LIST)
                categoryNames[code] = TypeHelper.GetTypeDisplayName(code);
            foreach (string code in TypeHelper.MAJOR_TYPES_LIST)
                categoryNames[code] = TypeHelper.GetTypeDisplayName(code);
            Write(generation + "-stock-categories.json", categoryNames);
            ++fullScans;
            ++loadScans;
            foreach (var area in areas)
                foreach (var item in area.Key.childItems)
                {
                    var row = Row(item, area.Value);
                    known.Add(ids[item], row);
                    rows.Add(row);
                }
            if (morningReport != null && !morningReportReady) FinishMorningReport();
            if (phase == "OPEN" || (PlayerStore.Instance.currentClientInstance == null && order == null)) QueueCounterPapers();
            LoadDesk();
            purifierStock = ReadPurifierStock();
            Publish(new JObject { ["op"] = "baseline", ["items"] = rows, ["purifier_stock"] = purifierStock });
            RefreshDesk();
        }

        private void LateUpdate()
        {

            if (!CanObserveStore()) { CheckJourney(); return; }
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
            FileCounterPapers();
            if (Time.realtimeSinceStartup >= nextView)
                foreach (var bottle in bottles)
                    if (bottle != null && BottleLabel(bottle) != (string)known[ids[bottle]]["bottle_label"])
                        changed.Add(bottle);
            FlushChanges();
            InvalidatePreviousShopVisit();
            ExpirePurifierNotice();
            RefreshDesk();
            MarkSceneReady(ShopScene, "當鋪");
            if (CheckJourney()) return;
            CheckMove();
            CheckOrder();
            CheckCounter();
            TickReception();
        }

        private void FlushChanges()
        {
            var currentPhase = PlayerStore.Instance.storeState.ToString();
            if (changed.Count == 0 && phase == currentPhase && Time.realtimeSinceStartup < nextView) return;
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
            var slots = ReadPurifierStock();
            bool slotsChanged = !JToken.DeepEquals(slots, purifierStock);
            if (upserts.Count == 0 && removed.Count == 0 && phase == currentPhase && !slotsChanged) return;
            phase = currentPhase;
            var record = new JObject { ["op"] = "delta", ["upsert"] = upserts, ["remove"] = removed };
            if (slotsChanged)
            {
                purifierStock = slots;
                record["purifier_stock"] = slots;
            }
            Publish(record);
        }

        private void OnDestroy()
        {
            DetachTrash();
            DetachAreas();

            ResetScene();
            ModHook.OnAfterhourArriveEarly -= OnArriveEarly;
            ModHook.OnAfterhourArriveLate -= OnTravelLate;
            ModHook.OnAfterhourLeaveEarly -= OnLeaveEarly;
            ModHook.OnAfterhourLeaveLate -= OnLeaveAfterhoursLate;
            ModHook.OnGameLoadedInit -= ResetLoad;
            ModHook.OnGameLoadedLate -= Ready;
            ModHook.OnNewGameStarted -= StartNewGame;
            ModHook.OnGoingSleepEarly -= BeforeSleep;
            ModHook.OnWakingUpEarly -= CaptureMorningReport;
            ModHook.OnWakingUpLate -= FinishMorningReport;
            ModHook.OnShutterOpenedLate -= QueueCounterPapers;
            DisconnectVisit();
        }
    }
}
