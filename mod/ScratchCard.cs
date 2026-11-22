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

        internal static string Act(GameItem card, string action, int? position, JObject view, JObject receipt)
        {
            var ui = ScratcherUIManager.Instance;
            string panel = (string)view["panel"];
            if (GeneralHelper.IsItemLocked(card) || PlayerStore.Instance.lockAllInventoryFlag != 0) return "locked";
            if (panel == "occupied") return "another_card_open";
            if (action == "open")
            {
                if (panel != "closed") return "already_open";
                if (((JArray)view["scratched_positions"]).Count == 3) return "already_finished";
                if (!card.MayActivateSlotItem() || !card.CanActivateSlotItem()) return "activation_unavailable";
                card.ActivateSlotItem();
                return ui.ui.activeInHierarchy && ui.currentActiveScratcherItem == card ? "opened" : "open_unconfirmed";
            }
            if (action != "reveal" || !position.HasValue || position < 1 || position > 3) return "invalid_action";
            if (panel != "front" || ScratchInput.isScratching) return "front_unavailable";
            int selected = position.Value;
            if (IsRevealed(card, selected)) return "already_revealed";
            var input = ui.scratchInputs.FirstOrDefault(entry => entry.slot == selected && entry.gameObject.activeInHierarchy);
            if (input == null) return "position_unavailable";
            input.OnAnyScratch();
            input.RevealAll();
            ui.OnScratchComplete(selected);
            if (!IsRevealed(card, selected)) return "reveal_unconfirmed";
            int symbol = card.GetTagReadonly("SCRATCHER_SYMBOL" + selected).GetInt();
            receipt["position"] = selected;
            receipt["symbol"] = new JObject { ["id"] = symbol, ["texture"] = ui.symbolTextures[symbol - 1].name };
            return "revealed";
        }
    }
}
