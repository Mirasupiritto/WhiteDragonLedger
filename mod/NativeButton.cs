using UnityEngine.UI;

namespace WhiteDragon.Ledger
{
    internal static class NativeButton
    {
        internal static bool Enabled(Button button) => button != null && button.isActiveAndEnabled && button.IsInteractable();
    }
}
