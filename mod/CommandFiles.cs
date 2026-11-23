using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WhiteDragon.Ledger
{
    public sealed partial class InventoryObserver
    {
        // Shared file boundary for counter, journey, move and sale.
        private static JObject ReadInbox(string inbox)
        {
            string text;
            try { text = ModHelper.ReadModDataFileText(ModId, inbox); }
            catch (IOException) { return null; } // A transient sharing lock leaves the request pending.
            if (text == null) return null;
            try
            {
                var request = JObject.Parse(text);
                var id = request["command_id"];
                if (id?.Type == JTokenType.String && Guid.TryParseExact((string)id, "N", out var key) &&
                    (string)id == key.ToString("N")) return request;
            }
            catch (JsonReaderException) { }
            ModHelper.DeleteModDataFile(ModId, inbox);
            UnityEngine.Debug.LogWarning("[WhiteDragonLedger] Removed malformed command: " + inbox);
            return null;
        }

        private static string RecoveryFile(string channel) => "last-receipt-" + channel + ".json";

        private static void StoreCommandReceipt(string channel, JObject receipt)
        {
            Write(RecoveryFile(channel), receipt);
            Write("receipt-" + (string)receipt["command_id"] + ".json", receipt);
        }

        private static JObject MatchingReceipt(string file, string command, string generation)
        {
            if (!ModHelper.ModDataFileExists(ModId, file)) return null;
            var text = ModHelper.ReadModDataFileText(ModId, file);
            if (text == null) return null;
            JObject receipt;
            try { receipt = JObject.Parse(text); }
            catch (JsonReaderException) { return null; }
            return receipt["status"]?.Type == JTokenType.String && receipt["command_id"]?.Type == JTokenType.String &&
                receipt["generation"]?.Type == JTokenType.String && (string)receipt["command_id"] == command &&
                (string)receipt["generation"] == generation ? receipt : null;
        }

        private static bool AlreadyHandled(string channel, string command, string generation)
        {
            string file = "receipt-" + command + ".json";
            bool exists = ModHelper.ModDataFileExists(ModId, file);
            if (exists && MatchingReceipt(file, command, generation) != null) return true;
            var prior = MatchingReceipt(RecoveryFile(channel), command, generation);
            if (prior == null) return exists; // Even a damaged receipt prevents executing again.
            Write(file, prior);
            return true;
        }
    }
}
