using System.Linq;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    internal static class ScratchCard
    {
        internal static JObject View(GameItem card, int itemId)
        {
            var ui = ScratcherUIManager.Instance;
            string panel = !ui.ui.activeInHierarchy ? "closed" : ui.currentActiveScratcherItem != card ? "occupied" :
                ui.back.activeInHierarchy ? "back" : "front";
            var scratched = new JArray();
            for (int position = 1; position <= 3; position++)
                if (IsRevealed(card, position)) scratched.Add(position);
            return new JObject { ["item_id"] = itemId, ["save_id"] = card.GetUniqueID(),
                ["panel"] = panel, ["scratched_positions"] = scratched };
        }

        private static bool IsRevealed(GameItem card, int position) =>
            card.GetTagReadonly("SCRATCHER_STATE" + position).GetInt() == 1;

        private static string RevealOne(GameItem card, ScratcherUIManager ui, int selected, out JObject symbol)
        {
            symbol = null;
            var input = ui.scratchInputs.FirstOrDefault(entry => entry.slot == selected && entry.gameObject.activeInHierarchy);
            if (input == null) return "position_unavailable";
            input.OnAnyScratch();
            input.RevealAll();
            ui.OnScratchComplete(selected);
            if (!IsRevealed(card, selected)) return "reveal_unconfirmed";
            int value = card.GetTagReadonly("SCRATCHER_SYMBOL" + selected).GetInt();
            symbol = new JObject { ["id"] = value, ["texture"] = ui.symbolTextures[value - 1].name };
            return "revealed";
        }

        internal static string Act(GameItem card, string action, int? position, JObject view, JObject receipt)
        {
            var ui = ScratcherUIManager.Instance;
            string panel = (string)view["panel"];
            if (GeneralHelper.IsItemLocked(card) || PlayerStore.Instance.lockAllInventoryFlag != 0) return "locked";
            if (action == "close")
            {
                if (panel == "closed") return "already_closed";
                ui.CloseUI();
                return !ui.ui.activeInHierarchy && ui.currentActiveScratcherItem == null ? "closed" : "close_unconfirmed";
            }
            if (panel == "occupied") return "another_card_open";
            if (action == "open")
            {
                if (panel != "closed") return "already_open";
                if (((JArray)view["scratched_positions"]).Count == 3) return "already_finished";
                if (!card.MayActivateSlotItem() || !card.CanActivateSlotItem()) return "activation_unavailable";
                card.ActivateSlotItem();
                return ui.ui.activeInHierarchy && ui.currentActiveScratcherItem == card ? "opened" : "open_unconfirmed";
            }
            if (action == "reveal_all")
            {
                if (panel != "front" || ScratchInput.isScratching) return "front_unavailable";
                var symbols = new JArray();
                for (int slot = 1; slot <= 3; slot++)
                {
                    if (IsRevealed(card, slot)) continue;
                    string status = RevealOne(card, ui, slot, out var sym);
                    if (status != "revealed")
                    {
                        if (symbols.Count > 0) receipt["symbols"] = symbols;
                        return status;
                    }
                    symbols.Add(new JObject { ["position"] = slot, ["symbol"] = sym });
                }
                if (symbols.Count == 0) return "already_revealed";
                receipt["symbols"] = symbols;
                return "revealed";
            }
            if (action != "reveal" || !position.HasValue || position < 1 || position > 3) return "invalid_action";
            if (panel != "front" || ScratchInput.isScratching) return "front_unavailable";
            int selected = position.Value;
            if (IsRevealed(card, selected)) return "already_revealed";
            string single = RevealOne(card, ui, selected, out var singleSymbol);
            if (single != "revealed") return single;
            receipt["position"] = selected;
            receipt["symbol"] = singleSymbol;
            return "revealed";
        }
    }
}
