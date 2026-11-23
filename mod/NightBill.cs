using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        // The native bill builds its visible controls in this private list.
        private static readonly FieldInfo ServiceRows = typeof(EndOfDayUIManager).GetField("rows",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private JObject previousBill;
        private string billToken;

        private List<(StoreService, TabletCheckbox)> BillRows(EndOfDayUIManager summary) =>
            ServiceRows?.GetValue(summary) as List<(StoreService, TabletCheckbox)>;

        private JObject NightBill()
        {
            var summary = EndOfDayUIManager.Instance;
            bool open = summary != null && summary.IsOpen;
            var bill = new JObject { ["open"]=open, ["day"]=StoreStation.Instance.dayCounter,
                ["service_cost"]=ServiceCost() };
            if (open)
            {
                var rows = BillRows(summary);
                if (rows == null) bill["status"]="service_rows_unavailable";
                else
                {
                    var services = new JArray();
                    foreach (var (service, box) in rows)
                    {
                        if (box == null || !box.gameObject.activeInHierarchy) continue;
                        bool enabled = !service.isMandatory && !service.IsOnCooldown() &&
                            box.button != null && box.button.isActiveAndEnabled && box.button.IsInteractable();
                        services.Add(new JObject { ["id"]=service.id, ["name"]=service.displayName,
                            ["cost"]=service.cost, ["selected"]=box.IsOn, ["enabled"]=enabled,
                            ["mandatory"]=service.isMandatory, ["cooldown_days"]=service.currentCooldown });
                    }
                    bill["services"] = services;
                }
            }
            if (!JToken.DeepEquals(previousBill, bill))
            {
                previousBill = (JObject)bill.DeepClone();
                billToken = Guid.NewGuid().ToString("N");
            }
            bill["bill_token"] = billToken;
            return bill;
        }

        private static bool OpenBill(EndOfDayUIManager summary)
        {
            if (summary == null) return false;
            if (summary.IsOpen) return true;
            var bed = UnityEngine.Object.FindObjectOfType<StoreBedroll>();
            if (bed == null) return false;
            bed.OnClick();
            return summary.IsOpen;
        }

        private string ActOnBill(JObject request, JObject receipt)
        {
            var store = PlayerStore.Instance;
            if (store.storeState != PlayerStore.StoreState.CLOSED) return "sleep_requires_closed_shop";
            if (ResponseInventory() != null) return "response_required";
            int day = StoreStation.Instance.dayCounter;
            if ((int)request["day"] != day || sleepSubmittedDay == day) return "day_changed_or_pending";
            var bill = NightBill();
            receipt["bill"] = bill;
            receipt["bill_token"] = billToken;
            if ((string)request["bill_token"] != billToken) return "bill_changed";
            var summary = EndOfDayUIManager.Instance;
            if ((string)request["action"] == "prepare_sleep")
            {
                if (!OpenBill(summary)) return "bedroll_unavailable";
                var opened = NightBill();
                receipt["bill"] = opened;
                receipt["bill_token"] = billToken;
                return (string)opened["status"] ?? "bill_open";
            }
            if (!(bool)bill["open"]) return "bill_closed";
            if (bill["status"] != null) return (string)bill["status"];
            string id = (string)request["service_id"];
            foreach (var row in (JArray)bill["services"])
            {
                if ((string)row["id"] != id) continue;
                if ((int)row["cost"] != (int)request["cost"]) return "service_cost_changed";
                bool selected = (bool)request["selected"];
                if ((bool)row["selected"] == selected) return "already_set";
                if (!(bool)row["enabled"]) return "service_unavailable";
                var entry = BillRows(summary).Find(pair => pair.Item1.id == id && pair.Item2 != null &&
                    pair.Item2.gameObject.activeInHierarchy);
                entry.Item2.Toggle();
                receipt["bill"] = NightBill();
                receipt["bill_token"] = billToken;
                receipt["service_id"] = id;
                receipt["selected"] = entry.Item2.IsOn;
                return entry.Item2.IsOn == selected && entry.Item1.isUsing == selected ? "service_set" : "change_unconfirmed";
            }
            return "service_unavailable";
        }
    }
}
