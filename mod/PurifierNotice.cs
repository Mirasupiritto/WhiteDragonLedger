using System;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        private string noticeToken, noticeText;
        private GenericConfirmationUIManager noticeOwner;

        private string PurifierMessage(GenericConfirmationUIManager ui)
        {
            if (ui?.panel == null || !ui.panel.activeInHierarchy || ui.confirmButton == null ||
                !ui.confirmButton.activeInHierarchy || ui.cancelButton == null || ui.cancelButton.activeInHierarchy) return null;
            string body = VisibleText(ui.bodyTMP);
            if (string.IsNullOrWhiteSpace(body)) return null;
            return body == LocHelper.GetLocalizedMechanic("mech_portable_purifier_already_clean") ||
                   body == LocHelper.GetLocalizedMechanic("mech_portable_purifier_not_enough_charge") ? body : null;
        }

        private void ExpirePurifierNotice()
        {
            if (noticeToken != null && (noticeOwner != GenericConfirmationUIManager.Instance ||
                PurifierMessage(noticeOwner) != noticeText)) noticeToken = null;
        }

        private string PurifierNoticeAction(string mode, string token, JObject receipt)
        {
            var ui = GenericConfirmationUIManager.Instance;
            string body = PurifierMessage(ui);
            if (body == null)
            {
                noticeToken = null;
                return mode == "notice_close" ? "panel_changed" : "other_confirmation_open";
            }
            if (mode == "read")
            {
                noticeToken = Guid.NewGuid().ToString("N");
                noticeText = body;
                noticeOwner = ui;
                receipt["notice"] = body;
                receipt["panel_token"] = noticeToken;
                receipt["available_strips"] = AvailableStrips();
                return "purifier_notice";
            }
            if (mode != "notice_close" || noticeToken == null || noticeOwner != ui ||
                token != noticeToken || body != noticeText) return "panel_changed";
            noticeToken = null;
            ui.ConfirmButtonPressed();
            receipt["notice"] = body;
            return !ui.panel.activeInHierarchy ? "notice_closed" : "close_unconfirmed";
        }
    }
}
