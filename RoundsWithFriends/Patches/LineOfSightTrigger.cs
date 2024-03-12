﻿using HarmonyLib;

using System.Linq;
using System.Reflection.Emit;
using System.Collections.Generic;
using Unbound.Core;

namespace RWF.Patches.Cards
{
    [HarmonyPatch(typeof(LineOfSightTrigger), "Update")]
    class LineOfSightTrigger_Patch_Update
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var f_pmInstance = AccessTools.Field(typeof(PlayerManager), "instance");
            var m_closestPlayer = ExtensionMethods.GetMethodInfo(typeof(PlayerManager), "GetClosestPlayerInTeam");
            var m_closestOtherPlayer = ExtensionMethods.GetMethodInfo(typeof(PlayerManagerExtensions), "GetClosestPlayerInOtherTeam");
            var m_otherTeam = ExtensionMethods.GetMethodInfo(typeof(PlayerManager), "GetOtherTeam");

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].LoadsField(f_pmInstance) && list[i + 4].Calls(m_otherTeam))
                {
                    yield return list[i + 1];
                    yield return list[i + 2];
                    yield return list[i + 3];
                    i += 4;
                }
                else if (list[i].Calls(m_closestPlayer))
                {
                    yield return new CodeInstruction(OpCodes.Call, m_closestOtherPlayer);
                }
                else
                {
                    yield return list[i];
                }
            }
        }
    }
}
