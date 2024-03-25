﻿using System.Reflection;
using System.Collections;
using System;
using Photon.Realtime;
using Landfall.Network;

using HarmonyLib;
using Unbound.Core;
using Photon.Pun;

namespace RWF
{
    public static class NetworkConnectionHandlerExtensions
    {
        public static void SetSearchingQuickMatch(this NetworkConnectionHandler instance, bool value) {
            instance.SetFieldValue("m_SearchingQuickMatch", value);
        }

        public static bool IsSearchingQuickMatch(this NetworkConnectionHandler instance) {
            return (bool)instance.GetFieldValue("m_SearchingQuickMatch");
        }

        public static void SetSearchingTwitch(this NetworkConnectionHandler instance, bool value) {
            instance.SetFieldValue("m_SearchingTwitch", value);
        }

        public static bool IsSearchingTwitch(this NetworkConnectionHandler instance) {
            return (bool) instance.GetFieldValue("m_SearchingTwitch");
        }

        public static void SetForceRegion(this NetworkConnectionHandler instance, bool value) {
            instance.SetFieldValue("m_ForceRegion", value);
        }

        public static void HostPrivate(this NetworkConnectionHandler instance) {
            instance.SetSearchingQuickMatch(false);
            instance.SetSearchingTwitch(false);

            TimeHandler.instance.gameStartTime = 1f;
            RoomOptions options = new RoomOptions();
            options.MaxPlayers = (byte) RWFMod.instance.MaxPlayers;
            options.IsOpen = true;
            options.IsVisible = false;

            Action createRoomFn = () => CreateRWFRoom(options);
            instance.StartCoroutine((IEnumerator) instance.InvokeMethod("DoActionWhenConnected", createRoomFn));
        }
        public static void CreateRWFRoom(RoomOptions roomOptions)
        {
            ((ClientSteamLobby) typeof(NetworkConnectionHandler).GetField("m_SteamLobby",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).CreateLobby(roomOptions.MaxPlayers, delegate (string RoomName)
            {
                PhotonNetwork.CreateRoom(RoomName, roomOptions, null, null);
            });
        }
    }
}
