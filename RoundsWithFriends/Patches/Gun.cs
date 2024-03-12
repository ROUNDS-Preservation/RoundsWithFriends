﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using System.Reflection.Emit;
using Photon.Pun;
using System.Reflection;
using Unbound.Core;

namespace RWF.Patches
{
    [HarmonyPatch]
    class Gun_Patch_FireBurst
    {
        static Type GetNestedMoveType()
        {
            var nestedTypes = typeof(Gun).GetNestedTypes(BindingFlags.Instance | BindingFlags.NonPublic);
            Type nestedType = null;

            foreach (var type in nestedTypes)
            {
                if (type.Name.Contains("FireBurst"))
                {
                    nestedType = type;
                    break;
                }
            }

            return nestedType;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(GetNestedMoveType(), "MoveNext");
        }
        static int GetPlayerUniqueID(PhotonView view)
        {
            return view.GetComponent<Player>().GetUniqueID();
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var p_actorID = ExtensionMethods.GetPropertyInfo(typeof(PhotonView), "OwnerActorNr");
            var m_uniqueID = ExtensionMethods.GetMethodInfo(typeof(Gun_Patch_FireBurst), nameof(Gun_Patch_FireBurst.GetPlayerUniqueID));

            foreach (var ins in instructions)
            {
                if (ins.GetsProperty(p_actorID))
                {
                    yield return new CodeInstruction(OpCodes.Call, m_uniqueID); // call the uniqueID method, which pops the photonview instance off the stack and leaves the result [uniqueID, ...]
                }
                else
                {
                    yield return ins;
                }
            }
        }
    }
}
