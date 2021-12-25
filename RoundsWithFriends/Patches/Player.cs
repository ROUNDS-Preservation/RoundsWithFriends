﻿using System.Collections.Generic;
using HarmonyLib;
using UnboundLib;
using UnityEngine;
using System.Reflection.Emit;

namespace RWF.Patches
{
    [HarmonyPatch(typeof(Player), "AssignPlayerID")]
    class Player_Patch_AssignPlayerID
    {
        // postfix to ensure sprite layer is set correctly on remote clients
        static void Postfix(Player __instance) 
        {
            if (__instance?.gameObject?.GetComponentInChildren<SetPlayerSpriteLayer>(true) != null)
            {
                UnityEngine.Debug.Log("SET SPRITE LAYER: " + (__instance.playerID + 1));
                __instance.gameObject.GetComponentInChildren<SetPlayerSpriteLayer>(true).InvokeMethod("Start");
            }

        }
    }
    [HarmonyPatch(typeof(Player), "ReadTeamID")]
    class Player_Patch_ReadTeamID
    {
        static bool Prefix()
        {
            return false;
        }
    }
    [HarmonyPatch(typeof(Player), "ReadPlayerID")]
    class Player_Patch_ReadPlayerID
    {
        static bool Prefix()
        {
            return false;
        }
    }
    [HarmonyPatch(typeof(Player), "AssignTeamID")]
    class Player_Patch_AssignTeamID
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            // Somewhy the AssignTeamID method assigns playerID to teamID when player joins a room the second time
            var f_playerID = UnboundLib.ExtensionMethods.GetFieldInfo(typeof(Player), "playerID");
            var f_teamID = UnboundLib.ExtensionMethods.GetFieldInfo(typeof(Player), "teamID");

            foreach (var ins in instructions) {
                if (ins.LoadsField(f_playerID)) {
                    // Instead of `this.teamID = playerID`, we obviously want `this.teamID = teamID`
                    ins.operand = f_teamID;
                }

                yield return ins;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "SetColors")]
    class Player_Patch_SetColors
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var f_playerID = UnboundLib.ExtensionMethods.GetFieldInfo(typeof(Player), "playerID");
            var m_colorID = UnboundLib.ExtensionMethods.GetMethodInfo(typeof(PlayerExtensions), nameof(PlayerExtensions.colorID));

            foreach (var ins in instructions)
            {
                if (ins.LoadsField(f_playerID))
                {
                    // we want colorID instead of teamID
                    yield return new CodeInstruction(OpCodes.Call, m_colorID); // call the colorID method, which pops the player instance off the stack and leaves the result [colorID, ...]
                }
                else
                {
                    yield return ins;
                }
            }
        }
    }
    [HarmonyPatch(typeof(Player), "GetTeamColors")]
    class Player_Patch_GetTeamColors
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var f_playerID = UnboundLib.ExtensionMethods.GetFieldInfo(typeof(Player), "playerID");
            var m_colorID = UnboundLib.ExtensionMethods.GetMethodInfo(typeof(PlayerExtensions), nameof(PlayerExtensions.colorID));

            foreach (var ins in instructions)
            {
                if (ins.LoadsField(f_playerID))
                {
                    // we want colorID instead of teamID
                    yield return new CodeInstruction(OpCodes.Call, m_colorID); // call the colorID method, which pops the player instance off the stack and leaves the result [colorID, ...]
                }
                else
                {
                    yield return ins;
                }
            }
        }
    }
}
