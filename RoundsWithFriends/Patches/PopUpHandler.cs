using HarmonyLib;
using Unbound.Core;

namespace RWF.Patches
{
    [HarmonyPatch(typeof(PopUpHandler), "StartPicking")]
    class PopUpHandler_Patch_StartPicking
    {
        static void Postfix() {
            PlayerManager.instance.SetPlayersSimulated(false);
            PlayerManager.instance.InvokeMethod("SetPlayersVisible", false);
        }
    }
}
