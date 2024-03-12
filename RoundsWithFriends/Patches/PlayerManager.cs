﻿using HarmonyLib;
using Photon.Pun;
using System.Reflection;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Sonigon;
using System.Linq;

using System;
using System.Reflection.Emit;
using RWF.Algorithms;

using RWF.UI;
using Unbound.Core.Networking;
using Unbound.Core;

namespace RWF.Patches
{
    // patch to return the correct number of TEAMS remaining
    [Serializable]
    [HarmonyPatch(typeof(PlayerManager), "PlayerDied")]
    class PlayerManagerPatchPlayerDied
    {
        private static bool Prefix(PlayerManager __instance, Player player)
        {
            int num = PlayerManager.instance.players.Where(p => !p.data.dead).Select(p => p.teamID).Distinct().Count();

            if ((Action<Player, int>) __instance.GetFieldValue("PlayerDiedAction") != null)
            {
                ((Action<Player, int>) __instance.GetFieldValue("PlayerDiedAction"))(player, num);
            }
            return false;

        }
    }

    [HarmonyPatch(typeof(PlayerManager), "GetPlayerWithActorID")]
    [HarmonyPriority(Priority.First)]
    class PlayerManager_Patch_GetPlayerWithActorID
    {
        static bool Prefix(PlayerManager __instance, ref Player __result, int actorID)
        {
            // if actorID is negative, then its actually a uniqueID
            if (actorID < 0)
            {
                __result = __instance.GetPlayerWithUniqueID(actorID);
                return false;
            }
            else
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerManager), "PlayerJoined")]
    class PlayerManager_Patch_PlayerJoined
    {
        static void Postfix(Player player)
        {
            if (!PhotonNetwork.OfflineMode)
            {
                PrivateRoomHandler.instance.PlayerJoined(player);
            }
        }
    }


    [HarmonyPatch(typeof(PlayerManager), "RemovePlayers")]
    class PlayerManager_Patch_RemovePlayers
    {
        static void Prefix(PlayerManager __instance)
        {
            if (!PhotonNetwork.OfflineMode)
            {
                var players = __instance.players;

                for (int i = players.Count - 1; i >= 0; i--)
                {
                    if (players[i].data.view.AmOwner)
                    {
                        PhotonNetwork.Destroy(players[i].data.view);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayerManager), "GetOtherPlayer")]
    class PlayerManager_Patch_GetOtherPlayer
    {
        static bool Prefix(PlayerManager __instance, Player asker, ref Player __result)
        {
            __result = __instance.GetClosestPlayerInOtherTeam(asker.transform.position, asker.teamID, false);
            return false;
        }
    }

    [HarmonyPatch]
    class PlayerManager_Patch_Move
    {
        /*
         * disable players' various colliders while they are being moved so that they do not interact with map objects
         * 
         * AND
         * 
         * do not set PlayerVelocity.simulated to true after the move, since this results in some players being active before others
         * 
         */
        static Type GetNestedMoveType()
        {
            var nestedTypes = typeof(PlayerManager).GetNestedTypes(BindingFlags.Instance | BindingFlags.NonPublic);
            Type nestedType = null;

            foreach (var type in nestedTypes)
            {
                if (type.Name.Contains("Move") && !type.Name.Contains("Player"))
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

        static void TrySetEnabled<T>(GameObject gameObject, bool enabled) where T : MonoBehaviour
        {
            if (gameObject?.GetComponent<T>() != null)
            {
                gameObject.GetComponent<T>().enabled = enabled;
            }
        }

        static void SetCollidersActive(PlayerVelocity player, bool active)
        {
            GameObject col = player.gameObject.transform.Find("ObjectCollider")?.gameObject;

            col?.SetActive(active);

            if (player?.gameObject?.GetComponent<CircleCollider2D>() != null)
            {
                player.gameObject.GetComponent<CircleCollider2D>().enabled = active;
            }

            TrySetEnabled<CollisionChecker>(player?.gameObject, active);
            TrySetEnabled<HealthHandler>(player?.gameObject, active);
            TrySetEnabled<PlayerCollision>(player?.gameObject, active);
            TrySetEnabled<LegRaycasters>(player?.gameObject?.transform?.Find("Limbs/LegStuff")?.gameObject, active);

            // Find the OutOfBoundsHandler for this player
            if (player?.GetComponent<Player>() != null)
            {
                OutOfBoundsHandler[] ooBs = UnityEngine.GameObject.FindObjectsOfType<OutOfBoundsHandler>();
                foreach (OutOfBoundsHandler ooB in ooBs)
                {
                    if (((CharacterData) ooB.GetFieldValue("data")).player.playerID == player.GetComponent<Player>().playerID)
                    {
                        ooB.enabled = active;
                        break;
                    }
                }
            }
        }

        static void FadeInOut(bool fadeIn)
        {
            if (fadeIn)
            {
                PlayerSpotlight.FadeIn();
            }
            else
            {
                PlayerSpotlight.FadeOut();
            }
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();

            var f_simulated = typeof(PlayerVelocity).GetFieldInfo("simulated");
            var f_isKinematic = typeof(PlayerVelocity).GetFieldInfo("isKinematic");
            var f_player = GetNestedMoveType().GetFieldInfo("player");

            var m_setCollidersActive = typeof(PlayerManager_Patch_Move).GetMethodInfo(nameof(PlayerManager_Patch_Move.SetCollidersActive));

            // Patch to disable player's ObjectCollider for the entirety of the move
            int disable_index = -1;
            int enable_index = -1;
            for (int i = 1; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Ldfld && codes[i + 1].LoadsField(f_player) && codes[i + 2].opcode == OpCodes.Ldc_I4_0 && codes[i + 3].opcode == OpCodes.Stfld && codes[i + 3].StoresField(f_simulated) && codes[i + 4].opcode == OpCodes.Ldarg_0 && codes[i + 5].opcode == OpCodes.Ldfld && codes[i + 6].opcode == OpCodes.Ldc_I4_1 && codes[i + 7].opcode == OpCodes.Stfld && codes[i + 7].StoresField(f_isKinematic))
                {
                    disable_index = i;
                }
                else if (codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Ldfld && codes[i + 1].LoadsField(f_player) && codes[i + 2].opcode == OpCodes.Ldc_I4_1 && codes[i + 3].opcode == OpCodes.Stfld && codes[i + 3].StoresField(f_simulated) && codes[i + 4].opcode == OpCodes.Ldarg_0 && codes[i + 5].opcode == OpCodes.Ldfld && codes[i + 6].opcode == OpCodes.Ldc_I4_0 && codes[i + 7].opcode == OpCodes.Stfld && codes[i + 7].StoresField(f_isKinematic))
                {
                    enable_index = i;
                }
            }
            if (disable_index == -1 || enable_index == -1)
            {
                throw new Exception("[PlayerManager.Move PATCH] INSTRUCTIONS FOR COLLIDER PATCH NOT FOUND");
            }
            else
            {
                codes.Insert(disable_index, new CodeInstruction(OpCodes.Ldarg_0)); // Load the PlayerManager.<Move>d__40 instance onto the stack [PlayerManager.<Move>d__40, ...]
                codes.Insert(disable_index + 1, new CodeInstruction(OpCodes.Ldfld, f_player)); // Load PlayerManager.<Move>d__40::player onto the stack (pops PlayerManager.<Move>d__40 off the stack) [PlayerManager.<Move>d__40::player, ...]
                codes.Insert(disable_index + 2, new CodeInstruction(OpCodes.Ldc_I4_0)); // Load 0 onto the stack [0, PlayerManager.<Move>d__40::player, ...]
                codes.Insert(disable_index + 3, new CodeInstruction(OpCodes.Call, m_setCollidersActive)); // Calls SetObjectColliderActive, taking the parameters off the top of the stack, leaving it how we found it [ ... ]

                codes.Insert(enable_index, new CodeInstruction(OpCodes.Ldarg_0)); // Load the PlayerManager.<Move>d__40 instance onto the stack [PlayerManager.<Move>d__40, ...]
                codes.Insert(enable_index + 1, new CodeInstruction(OpCodes.Ldfld, f_player)); // Load PlayerManager.<Move>d__40::player onto the stack (pops PlayerManager.<Move>d__40 off the stack) [PlayerManager.<Move>d__40::player, ...]
                codes.Insert(enable_index + 2, new CodeInstruction(OpCodes.Ldc_I4_1)); // Load 1 onto the stack [1, PlayerManager.<Move>d__40::player, ...]
                codes.Insert(enable_index + 3, new CodeInstruction(OpCodes.Call, m_setCollidersActive)); // Calls SetObjectColliderActive, taking the parameters off the top of the stack, leaving it how we found it [ ... ]
            }

            // Patch to remove PlayerVelocity.simulated = true
            int remove_idx = -1;
            for (int i = 1; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].LoadsField(f_player) && codes[i + 2].opcode == OpCodes.Ldc_I4_1 && codes[i + 3].StoresField(f_simulated))
                {
                    remove_idx = i;
                }
            }
            codes.RemoveRange(remove_idx, 4);

            return codes.AsEnumerable();
        }
    }

    [HarmonyPatch(typeof(PlayerManager), "MovePlayers")]
    class PlayerManager_Patch_MovePlayers
    {
        static bool Prefix(PlayerManager __instance, SpawnPoint[] spawnPoints)
        {
            // only calculate spawn positions on the host
            if (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode)
            {
                __instance.StartCoroutine(PlayerManager_Patch_MovePlayers.WaitForMapToLoad(__instance, spawnPoints));
            }
            return false;
        }
        static bool MapHasValidGround(Map map)
        {
            if (!(bool) map.GetFieldValue("hasCalledReady")) { return false; }

            foreach (Collider2D collider in map.gameObject.GetComponentsInChildren<Collider2D>())
            {
                if (collider.enabled)
                {
                    return true;
                }
            }
            return false;
        }

        static IEnumerator WaitForMapToLoad(PlayerManager __instance, SpawnPoint[] spawnPoints)
        {
            // Wait until the map has solid ground
            yield return new WaitUntil(() => PlayerManager_Patch_MovePlayers.MapHasValidGround(MapManager.instance.currentMap?.Map));

            // 10 extra frames to make the game happy, this is necessary for dynamic objects to be registered as valid ground
            for (int _ = 0; _ < 10; _++)
            {
                yield return null;
            }

            var spawnDictionary = GeneralizedSpawnPositions.GetSpawnDictionary(__instance.players, spawnPoints);
            var serializableSpawnDictionary = PlayerManager.instance.players.Select(p => spawnDictionary[p]).ToArray();

            // send the spawn dictionary to all clients as a vector2 array where the position is the playerID
            NetworkingManager.RPC(typeof(PlayerManager_Patch_MovePlayers), nameof(PlayerManager_Patch_MovePlayers.RPCA_MovePlayers), serializableSpawnDictionary);

            yield break;
        }
        [UnboundRPC]
        private static void RPCA_MovePlayers(Vector2[] spawnDictionary)
        {
            for (int i = 0; i < PlayerManager.instance.players.Count; i++)
            {

                PlayerManager.instance.StartCoroutine((IEnumerator) typeof(PlayerManager).InvokeMember("Move",
                    BindingFlags.Instance | BindingFlags.InvokeMethod |
                    BindingFlags.NonPublic, null, PlayerManager.instance, new object[] { PlayerManager.instance.players[i].data.playerVel, (Vector3) spawnDictionary[PlayerManager.instance.players[i].playerID] }));

                int j = i % PlayerManager.instance.soundCharacterSpawn.Length;

                SoundManager.Instance.Play(PlayerManager.instance.soundCharacterSpawn[j], PlayerManager.instance.players[i].transform);
            }
        }
    }
}
