using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        // Assembly-CSharp: StoreClientListWanted.POSTER_IDS and each CreateWanted1..9 SetWanted(reward, localizedName).
        // These are the base poster amounts. For example, CreateWanted4Normal has a different encounter reward.
        // Creating the client just to read these values can consume the game RNG.
        private static readonly Dictionary<string,(string name,int bounty)> PosterData =
            new Dictionary<string,(string,int)> {
                {"wanted1",("name_james_bailey",300)}, {"wanted2",("name_the_butcher",450)},
                {"wanted3",("name_arthur_lafleur",100)}, {"wanted4",("name_odin",300)},
                {"wanted5",("name_lou",1500)}, {"wanted6",("name_li_bei_wen",400)},
                {"wanted7",("name_juan",125)}, {"wanted8",("name_john_smith",125)}, {"wanted9",("name_leroy",300)}
            };

        private static readonly System.Reflection.FieldInfo BoardElements=typeof(WantedUIManager).GetField(
            "wantedElements",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static JArray WantedRows()
        {
            var rows=new JArray();
            var board=WantedUIManager.Instance;
            var registered=board==null ? null : (List<WantedElement>)BoardElements.GetValue(board);
            // Before the panel's first Awake, the native poster catalog supplies the same board IDs.
            IEnumerable<string> ids=registered!=null && registered.Count>0
                ? registered.Where(e=>e!=null).Select(e=>e.identifier).Distinct() : StoreClientListWanted.POSTER_IDS;
            foreach (string id in ids.OrderBy(id=>id))
            {
                if (!PosterData.TryGetValue(id,out var poster)) continue;
                rows.Add(new JObject { ["name"]=LocHelper.GetLocalizedClientName(poster.name), ["bounty"]=poster.bounty,
                    ["arrested"]=PlayerStore.Instance.wantedStatus.TryGetValue(id,out int status) && status==1 });
            }
            return rows;
        }

        private void AttachWantedMatch(JObject view)
        {
            if (string.IsNullOrEmpty(merchant?.displayName)) return;
            var match=WantedRows().FirstOrDefault(row=>!(bool)row["arrested"] &&
                string.Equals((string)row["name"],merchant.displayName,StringComparison.Ordinal));
            if (match==null) return;
            view["wanted_name_match"]=new JObject { ["name"]=match["name"], ["bounty"]=match["bounty"] };
            view["next_action"]="player_visual_check";
        }

        private void AttachPosterMatch(JObject view)
        {
            view["report_protocol"]=1;
            var avatar=StoreUIManager.Instance?.clientGameObject;
            if (merchant==null || !PlayerStore.Instance.isClientArrived || avatar?.image==null ||
                !avatar.image.enabled || !avatar.gameObject.activeInHierarchy) return;
            var portrait=avatar.image.sprite;
            var sprites=SpriteDict.Instance?.spriteDictionary;
            if (portrait==null || sprites==null) return;
            // WantedElement registers the poster ID. The native wanted portraits use those
            // same keys in SpriteDict. Compare the rendered sprite, including scene overrides.
            foreach (string id in StoreClientListWanted.POSTER_IDS)
            {
                if (!PosterData.TryGetValue(id,out var poster) || !sprites.TryGetValue(id,out var expected)) continue;
                bool match = portrait == expected ||
                    (expected != null && string.Equals(portrait.name, expected.name, StringComparison.Ordinal)) ||
                    string.Equals(portrait.name, "cp_hd_portrait_" + id, StringComparison.Ordinal);
                if (!match) continue;
                view["poster_match"]=new JObject { ["poster_id"]=id, ["name"]=LocHelper.GetLocalizedClientName(poster.name),
                    ["bounty"]=poster.bounty, ["arrested"]=PlayerStore.Instance.wantedStatus.TryGetValue(id,out int status) && status==1 };
                view["next_action"]="report_decision";
                return;
            }
        }

        private static bool IsPosterPulled() => StorePoster.Instance != null && StorePoster.Instance.isPulled;

        private static JObject ContrabandCounts(bool includeCounter=false, bool forInspection=false)
        {
            var shop=EmporiumEntry.Instance;
            var counts=new JObject { ["inventory"]=0, ["showcase"]=0, ["smuggler_bay"]=0, ["hidden_grid"]=0 };
            var stolenCounts=new JObject { ["inventory"]=0, ["showcase"]=0, ["smuggler_bay"]=0, ["hidden_grid"]=0 };
            void Count(GameInventory inventory,string area, bool ownedOnly=false)
            {
                if (inventory==null) return;
                foreach (var root in inventory.childItems)
                {
                    if (ownedOnly && !GeneralHelper.IsItemEffectivelyOwned(root)) continue;
                    var items=root.FindAllChildItems();
                    items.Add(root);
                    var hidden=new HashSet<GameItem>(items.Where(ContrabandHelper.IsSmugglerBay)
                        .SelectMany(bay=>bay.FindAllChildItems()));
                    foreach (var item in items)
                    {
                        string key=hidden.Contains(item) ? "smuggler_bay" : area;
                        if (ContrabandHelper.IsContraband(item))
                            counts[key]=(int)counts[key]+item.unitCount;
                        if (StolenHelper.IsStolenItem(item))
                            stolenCounts[key]=(int)stolenCounts[key]+item.unitCount;
                    }
                }
            }
            Count(shop.invElement,"inventory");
            Count(shop.showcaseElement,"showcase");
            Count(shop.hiddenElement,"hidden_grid");
            if (includeCounter)
            {
                counts["on_counter"]=0;
                stolenCounts["on_counter"]=0;
                Count(shop.frontInvinvElement,"on_counter",true);
                Count(shop.backInvinvElement,"on_counter",true);
            }
            bool pulled = IsPosterPulled();
            if (forInspection)
            {
                counts["hidden_grid_exposed"]=pulled ? (int)counts["hidden_grid"] : 0;
                stolenCounts["hidden_grid_exposed"]=pulled ? (int)stolenCounts["hidden_grid"] : 0;
            }
            int stolenOutside = (int)stolenCounts["inventory"] + (int)stolenCounts["showcase"] +
                (pulled ? (int)stolenCounts["hidden_grid"] : 0) +
                (includeCounter ? (int)stolenCounts["on_counter"] : 0);
            counts["stolen"]=stolenCounts;
            counts["stolen_outside_bay"]=stolenOutside;
            return counts;
        }

        private static JObject SecurityView()
        {
            var sec=PlayerStore.Instance.secData;
            var result=new JObject {
                ["contraband"]=ContrabandCounts(),
                ["poster"]=new JObject { ["pulled"]=IsPosterPulled() }
            };
            if (sec!=null)
            {
                result["evidence_level"]=sec.evidenceLevel;
                result["evidence_display"]=sec.GetEvidenceLevelDisplay();
                // SecData.OnNewDay clamps evidenceLevel at 100 and calls SendOfficer() at that threshold.
                result["officer_dispatch_threshold"]=100;
            }
            return result;
        }

        private static JObject LastSave(int slot)
        {
            // SavePreviewData has no phase. PlayerStore.InitialSave and EndOfDayUIManager after BeginDay
            // are the native SaveGame callers; both capture the morning. Day and time come from the slot preview.
            var saved=SaveUIManager.GetPreview(slot);
            return saved==null ? new JObject { ["status"]="unavailable", ["slot"]=slot } :
                new JObject { ["slot"]=slot, ["day"]=saved.dayCount, ["phase"]="MORNING",
                    ["time"]=saved.lastSaveDateTime.ToString("o") };
        }

        private string TogglePosterAction(JObject request, JObject receipt)
        {
            if (phase == "OPEN" && PlayerStore.Instance.IsClientPresent())
            {
                receipt["reason"] = "customer_present";
                return "poster_unavailable";
            }
            var poster = StorePoster.Instance;
            if (poster == null) return "poster_unavailable";
            string mode = (string)request["mode"];
            if (mode == "up")
            {
                if (!poster.isPulled) poster.PullUp(false);
            }
            else if (mode == "down")
            {
                if (poster.isPulled) poster.PullDown(false);
            }
            else return "invalid_mode";
            nextView = 0;
            RefreshDesk();
            var counts = ContrabandCounts();
            receipt["poster"] = new JObject { ["pulled"] = poster.isPulled };
            receipt["hidden_grid"] = counts["hidden_grid"];
            return poster.isPulled ? "poster_up" : "poster_down";
        }
    }
}
