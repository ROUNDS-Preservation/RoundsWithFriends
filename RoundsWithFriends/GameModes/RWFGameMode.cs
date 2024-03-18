﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;



using Sonigon;
using RWF.UI;
using Unbound.Core.Networking;
using Unbound.Gamemodes;
using Unbound.Core;

namespace RWF.GameModes
{
    public class RWFGameMode : MonoBehaviour
    {
        private static RWFGameMode instance;

        protected internal Dictionary<int, int> teamPoints = new Dictionary<int, int>();
        protected internal Dictionary<int, int> teamRounds = new Dictionary<int, int>();

        protected bool isTransitioning;
        protected int playersNeededToStart = 2;
        protected int? timeUntilBattleStart = null;

        protected internal int[] previousRoundWinners = new int[] { };
        protected internal int[] previousPointWinners = new int[] { };

        protected virtual void Awake()
        {
            RWFGameMode.instance = this;
        }

        protected virtual void Start()
        {
            this.StartCoroutine(this.Init());
        }

        public virtual void OnDisable()
        {
            this.ResetMatch();
            this.teamPoints.Clear();
            this.teamRounds.Clear();
        }

        protected virtual IEnumerator Init()
        {
            yield return GameModeManager.TriggerHook(GameModeHooks.HookInitStart);

            PlayerManager.instance.SetPlayersSimulated(false);
            PlayerAssigner.instance.maxPlayers = this.playersNeededToStart;

            this.playersNeededToStart = RWFMod.instance.MinPlayers;
            PlayerAssigner.instance.maxPlayers = RWFMod.instance.MaxPlayers;

            yield return GameModeManager.TriggerHook(GameModeHooks.HookInitEnd);
        }

        [UnboundRPC]
        public static void RPC_SyncBattleStart(int requestingPlayer, int timeOfBattleStart)
        {

            // calculate the time in milliseconds until the battle starts
            RWFGameMode.instance.timeUntilBattleStart = timeOfBattleStart - PhotonNetwork.ServerTimestamp;

            NetworkingManager.RPC(typeof(RWFGameMode), nameof(RWFGameMode.RPC_SyncBattleStartResponse), requestingPlayer, PhotonNetwork.LocalPlayer.ActorNumber);
        }

        [UnboundRPC]
        public static void RPC_SyncBattleStartResponse(int requestingPlayer, int readyPlayer)
        {
            if (PhotonNetwork.LocalPlayer.ActorNumber == requestingPlayer)
            {
                RWFGameMode.instance.RemovePendingRequest(readyPlayer, nameof(RWFGameMode.RPC_SyncBattleStart));
            }
        }

        protected virtual IEnumerator SyncBattleStart()
        {
            if (PhotonNetwork.OfflineMode)
            {
                yield break;
            }

            // only the host will communicate when the battle should start

            if (PhotonNetwork.IsMasterClient)
            {
                // schedule the battle to start 5 times the maximum client ping + host client's ping from now, with a minimum of 1 second
                // 5 because the host and slowest client must:
                // Host 1) send the RPC
                // Host 2) receive ALL clients' responses
                // Host 3) retrieve the server time
                // Client 1) receive the RPC
                // Client 2) respond to the RPC
                // Client 3) retrieve the server time
                // + wiggle room

                // if the host client is the slowest client (very unlikely because of how Photon chooses servers),
                // then this is overkill - but better safe than sorry

                // this is in milliseconds and can overflow, but luckily all overflows will cancel out when a time difference is calculated
                int timeOfBattleStart = PhotonNetwork.ServerTimestamp + UnityEngine.Mathf.Clamp(5 * ((int) PhotonNetwork.LocalPlayer.CustomProperties["Ping"] + PhotonNetwork.CurrentRoom.Players.Select(kv => (int) kv.Value.CustomProperties["Ping"]).Max()), 1000, int.MaxValue);

                yield return this.SyncMethod(nameof(RWFGameMode.RPC_SyncBattleStart), null, PhotonNetwork.LocalPlayer.ActorNumber, timeOfBattleStart);
            }

            yield return new WaitUntil(() => this.timeUntilBattleStart != null);

            yield return new WaitForSecondsRealtime((float) this.timeUntilBattleStart * 0.001f);

            this.timeUntilBattleStart = null;
        }

        [UnboundRPC]
        public static void RPC_RequestSync(int requestingPlayer)
        {
            NetworkingManager.RPC(typeof(RWFGameMode), nameof(RWFGameMode.RPC_SyncResponse), requestingPlayer, PhotonNetwork.LocalPlayer.ActorNumber);
        }

        [UnboundRPC]
        public static void RPC_SyncResponse(int requestingPlayer, int readyPlayer)
        {
            if (PhotonNetwork.LocalPlayer.ActorNumber == requestingPlayer)
            {
                RWFGameMode.instance.RemovePendingRequest(readyPlayer, nameof(RWFGameMode.RPC_RequestSync));
            }
        }

        protected virtual IEnumerator WaitForSyncUp()
        {
            if (PhotonNetwork.OfflineMode)
            {
                yield break;
            }
            yield return this.SyncMethod(nameof(RWFGameMode.RPC_RequestSync), null, PhotonNetwork.LocalPlayer.ActorNumber);
        }

        public virtual void PlayerJoined(Player player)
        {
            if (!this.teamPoints.ContainsKey(player.teamID)) { this.teamPoints.Add(player.teamID, 0); }
            if (!this.teamRounds.ContainsKey(player.teamID)) { this.teamRounds.Add(player.teamID, 0); }
        }

        public virtual void PlayerDied(Player killedPlayer, int teamsAlive)
        {
            if (teamsAlive == 1)
            {
                TimeHandler.instance.DoSlowDown();

                if (PhotonNetwork.IsMasterClient)
                {
                    NetworkingManager.RPC(
                        typeof(RWFGameMode),
                        nameof(RWFGameMode.RPCA_NextRound),
                        new int[] { PlayerManager.instance.GetLastPlayerAlive().teamID },
                        this.teamPoints,
                        this.teamRounds
                    );
                }
            }
        }

        public virtual void StartGame()
        {
            if (GameManager.instance.isPlaying)
            {
                return;
            }

            // clear teams and redo them
            this.teamPoints.Clear();
            this.teamRounds.Clear();

            // clear previous winners
            this.previousPointWinners = new int[] { };
            this.previousRoundWinners = new int[] { };

            foreach (Player player in PlayerManager.instance.players)
            {
                this.PlayerJoined(player);
            }
            // set up pick order
            PlayerManager.instance.ResetPickOrder();

            GameManager.instance.isPlaying = true;
            this.StartCoroutine(this.DoStartGame());
        }

        public virtual IEnumerator DoStartGame()
        {
            CardBarHandler.instance.Rebuild();
            UIHandler.instance.InvokeMethod("SetNumberOfRounds", (int) GameModeManager.CurrentHandler.Settings["roundsToWinGame"]);
            ArtHandler.instance.NextArt();

            yield return GameModeManager.TriggerHook(GameModeHooks.HookGameStart);

            GameManager.instance.battleOngoing = false;

            UIHandler.instance.ShowJoinGameText(LocalizedStrings.LetsGoText, PlayerSkinBank.GetPlayerSkinColors(1).winText);
            yield return new WaitForSecondsRealtime(0.25f);
            UIHandler.instance.HideJoinGameText();

            PlayerSpotlight.CancelFade(true);

            PlayerManager.instance.SetPlayersSimulated(false);
            PlayerManager.instance.InvokeMethod("SetPlayersVisible", false);
            MapManager.instance.LoadNextLevel(false, false);
            TimeHandler.instance.DoSpeedUp();

            yield return new WaitForSecondsRealtime(1f);

            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickStart);
            List<Player> pickOrder = PlayerManager.instance.GetPickOrder(null);

            foreach (Player player in pickOrder)
            {
                yield return this.WaitForSyncUp();

                yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickStart);

                CardChoiceVisuals.instance.Show(player.playerID, true);
                yield return CardChoice.instance.DoPick(1, player.playerID, PickerType.Player);

                yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickEnd);

                yield return new WaitForSecondsRealtime(0.1f);
            }

            yield return this.WaitForSyncUp();
            CardChoiceVisuals.instance.Hide();

            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickEnd);

            PlayerSpotlight.FadeIn();
            MapManager.instance.CallInNewMapAndMovePlayers(MapManager.instance.currentLevelID);
            TimeHandler.instance.DoSpeedUp();
            TimeHandler.instance.StartGame();
            GameManager.instance.battleOngoing = true;
            UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);
            PlayerManager.instance.InvokeMethod("SetPlayersVisible", true);

            this.StartCoroutine(this.DoRoundStart());
        }
        public virtual IEnumerator RoundTransition(int winningTeamID)
        {
            yield return this.RoundTransition(new int[] { winningTeamID });
        }
        public virtual IEnumerator RoundTransition(int[] winningTeamIDs)
        {
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPointEnd);
            yield return GameModeManager.TriggerHook(GameModeHooks.HookRoundEnd);

            int[] winningTeams = GameModeManager.CurrentHandler.GetGameWinners();
            if (winningTeams.Any())
            {
                this.GameOver(winningTeamIDs);
                yield break;
            }

            this.StartCoroutine(PointVisualizer.instance.DoWinSequence(this.teamPoints, this.teamRounds, winningTeamIDs));

            yield return new WaitForSecondsRealtime(1f);
            MapManager.instance.LoadNextLevel(false, false);

            yield return new WaitForSecondsRealtime(1.3f);

            PlayerManager.instance.SetPlayersSimulated(false);
            TimeHandler.instance.DoSpeedUp();

            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickStart);

            PlayerManager.instance.InvokeMethod("SetPlayersVisible", false);

            List<Player> pickOrder = PlayerManager.instance.GetPickOrder(winningTeamIDs);

            foreach (Player player in pickOrder)
            {
                if (!winningTeamIDs.Contains(player.teamID))
                {
                    yield return this.WaitForSyncUp();

                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickStart);

                    CardChoiceVisuals.instance.Show(player.playerID, true);
                    yield return CardChoice.instance.DoPick(1, player.playerID, PickerType.Player);

                    yield return GameModeManager.TriggerHook(GameModeHooks.HookPlayerPickEnd);

                    yield return new WaitForSecondsRealtime(0.1f);
                }
            }

            PlayerManager.instance.InvokeMethod("SetPlayersVisible", true);

            yield return GameModeManager.TriggerHook(GameModeHooks.HookPickEnd);

            yield return this.StartCoroutine(this.WaitForSyncUp());
            PlayerSpotlight.FadeIn();

            TimeHandler.instance.DoSlowDown();
            MapManager.instance.CallInNewMapAndMovePlayers(MapManager.instance.currentLevelID);
            PlayerManager.instance.RevivePlayers();

            yield return new WaitForSecondsRealtime(0.3f);

            TimeHandler.instance.DoSpeedUp();
            GameManager.instance.battleOngoing = true;
            this.isTransitioning = false;
            UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);

            this.StartCoroutine(this.DoRoundStart());
        }

        public virtual IEnumerator PointTransition(int winningTeamID)
        {
            yield return this.PointTransition(new int[] { winningTeamID });
        }
        public virtual IEnumerator PointTransition(int[] winningTeamIDs)
        {
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPointEnd);

            this.StartCoroutine(PointVisualizer.instance.DoSequence(this.teamPoints, this.teamRounds, winningTeamIDs));
            yield return new WaitForSecondsRealtime(1f);

            MapManager.instance.LoadNextLevel(false, false);

            yield return new WaitForSecondsRealtime(0.5f);
            yield return this.WaitForSyncUp();
            PlayerSpotlight.FadeIn();

            MapManager.instance.CallInNewMapAndMovePlayers(MapManager.instance.currentLevelID);
            PlayerManager.instance.RevivePlayers();

            yield return new WaitForSecondsRealtime(0.3f);

            TimeHandler.instance.DoSpeedUp();
            GameManager.instance.battleOngoing = true;
            this.isTransitioning = false;
            UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);

            this.StartCoroutine(this.DoPointStart());
        }

        public virtual IEnumerator DoRoundStart()
        {
            // Wait for MapManager to set all players to playing after map transition
            while (PlayerManager.instance.players.ToList().Any(p => !(bool) p.data.isPlaying))
            {
                yield return null;
            }

            //PlayerManager.instance.SetPlayersSimulated(false);
            yield return this.WaitForSyncUp();
            PlayerSpotlight.FadeOut();

            yield return GameModeManager.TriggerHook(GameModeHooks.HookRoundStart);
            yield return GameModeManager.TriggerHook(GameModeHooks.HookPointStart);

            var sounds = GameObject.Find("/SonigonSoundEventPool");

            yield return this.SyncBattleStart();

            /*
            for (int i = 3; i >= 1; i--)
            {
                UIHandler.instance.DisplayRoundStartText($"{i}");
                SoundManager.Instance.Play(PointVisualizer.instance.sound_UI_Arms_Race_A_Ball_Shrink_Go_To_Left_Corner, this.transform);
                yield return new WaitForSecondsRealtime(0.5f);
            }

            UIHandler.instance.DisplayRoundStartText("FIGHT");
            */
            SoundManager.Instance.Play(PointVisualizer.instance.sound_UI_Arms_Race_C_Ball_Pop_Shake, this.transform);
            PlayerManager.instance.SetPlayersSimulated(true);

            yield return GameModeManager.TriggerHook(GameModeHooks.HookBattleStart);

            this.ExecuteAfterSeconds(0.5f, () => {
                UIHandler.instance.HideRoundStartText();
            });
        }

        public virtual IEnumerator DoPointStart()
        {
            // Wait for MapManager to set all players to playing after map transition
            while (PlayerManager.instance.players.ToList().Any(p => !(bool) p.data.isPlaying))
            {
                yield return null;
            }

            //PlayerManager.instance.SetPlayersSimulated(false);
            yield return this.WaitForSyncUp();
            PlayerSpotlight.FadeOut();

            yield return GameModeManager.TriggerHook(GameModeHooks.HookPointStart);

            var sounds = GameObject.Find("/SonigonSoundEventPool");

            yield return this.SyncBattleStart();

            /*
            for (int i = 3; i >= 1; i--)
            {
                UIHandler.instance.DisplayRoundStartText($"{i}");
                SoundManager.Instance.Play(PointVisualizer.instance.sound_UI_Arms_Race_A_Ball_Shrink_Go_To_Left_Corner, this.transform);
                yield return new WaitForSecondsRealtime(0.5f);
            }

            UIHandler.instance.DisplayRoundStartText("FIGHT");
            */
            SoundManager.Instance.Play(PointVisualizer.instance.sound_UI_Arms_Race_C_Ball_Pop_Shake, this.transform);
            PlayerManager.instance.SetPlayersSimulated(true);

            yield return GameModeManager.TriggerHook(GameModeHooks.HookBattleStart);

            this.ExecuteAfterSeconds(0.5f, () => {
                UIHandler.instance.HideRoundStartText();
            });
        }

        public virtual void RoundOver(int winningTeamID)
        {
            this.RoundOver(new int[] { winningTeamID });
        }
        public virtual void RoundOver(int[] winningTeamIDs)
        {
            foreach (var teamID in this.teamPoints.Keys.ToList())
            {
                this.teamPoints[teamID] = 0;
            }

            this.previousRoundWinners = winningTeamIDs.ToArray();

            this.StartCoroutine(this.RoundTransition(winningTeamIDs));
        }

        public virtual void PointOver(int winningTeamID)
        {
            this.PointOver(new int[] { winningTeamID });
        }
        public virtual void PointOver(int[] winningTeamIDs)
        {
            this.previousPointWinners = winningTeamIDs.ToArray();

            this.StartCoroutine(this.PointTransition(winningTeamIDs));
        }

        public virtual IEnumerator GameOverTransition(int winningTeamID)
        {
            yield return this.GameOverTransition(new int[] { winningTeamID });
        }
        public virtual IEnumerator GameOverTransition(int[] winningTeamIDs)
        {
            yield return GameModeManager.TriggerHook(GameModeHooks.HookGameEnd);

            UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);
            List<Color> colors = winningTeamIDs.Select(tID => PlayerManager.instance.GetPlayersInTeam(tID).First().GetTeamColors().color).ToList();
            Color color = AverageColor.Average(colors);
            UIHandler.instance.DisplayScreenText(color, "VICTORY!", 1f);
            yield return new WaitForSecondsRealtime(2f);
            this.GameOverRematch(winningTeamIDs);
            yield break;
        }

        public virtual void GameOverRematch(int winningTeamID)
        {
            this.GameOverRematch(new int[] { winningTeamID });
        }
        protected virtual void GameOverRematch(int[] winningTeamIDs)
        {
            if (PhotonNetwork.OfflineMode)
            {
                var winningPlayer = PlayerManager.instance.players.Find(p => winningTeamIDs.Contains(p.playerID));
                UIHandler.instance.DisplayScreenTextLoop(winningPlayer.GetTeamColors().winText, "REMATCH?");
                UIHandler.instance.popUpHandler.StartPicking(winningPlayer, this.GetRematchYesNo);
                MapManager.instance.LoadNextLevel(false, false);
                return;
            }

            if (PhotonNetwork.IsMasterClient)
            {
                foreach (var player in PhotonNetwork.CurrentRoom.Players.Values.ToList())
                {
                    PhotonNetwork.DestroyPlayerObjects(player);
                }
            }

            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        protected virtual void GetRematchYesNo(PopUpHandler.YesNo yesNo)
        {
            if (yesNo == PopUpHandler.YesNo.Yes)
            {
                base.StartCoroutine(this.IDoRematch());
                return;
            }
            this.DoRestart();
        }

        protected virtual IEnumerator IDoRematch()
        {
            yield return null;
            this.ResetMatch();
            this.StartCoroutine(this.DoStartGame());
        }

        public virtual void ResetMatch()
        {
            UIHandler.instance.StopScreenTextLoop();
            PlayerManager.instance.InvokeMethod("ResetCharacters");

            foreach (var player in PlayerManager.instance.players)
            {
                this.teamPoints[player.teamID] = 0;
                this.teamRounds[player.teamID] = 0;
            }

            this.isTransitioning = false;
            UIHandler.instance.ShowRoundCounterSmall(this.teamPoints, this.teamRounds);
            CardBarHandler.instance.ResetCardBards();
            PointVisualizer.instance.ResetPoints();
        }

        protected virtual void DoRestart()
        {
            GameManager.instance.battleOngoing = false;
            if (PhotonNetwork.OfflineMode)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }
            NetworkConnectionHandler.instance.NetworkRestart();
        }

        protected virtual void GameOver(int winningTeamID)
        {
            this.GameOver(new int[] { winningTeamID });
        }
        protected virtual void GameOver(int[] winningTeamIDs)
        {
            base.StartCoroutine(this.GameOverTransition(winningTeamIDs));
        }
        [UnboundRPC]
        public static void RPCA_NextRound(int[] winningTeamIDs, Dictionary<int, int> teamPoints, Dictionary<int, int> teamRounds)
        {
            var instance = RWFGameMode.instance;

            if (instance.isTransitioning)
            {
                return;
            }

            GameManager.instance.battleOngoing = false;
            instance.teamPoints = teamPoints;
            instance.teamRounds = teamRounds;
            instance.isTransitioning = true;

            PlayerManager.instance.SetPlayersSimulated(false);

            if (winningTeamIDs.Count() == 0)
            {
                instance.PointOver(winningTeamIDs);
                return;
            }

            else
            {
                foreach (int winningTeamID in winningTeamIDs)
                {
                    instance.teamPoints[winningTeamID] = instance.teamPoints[winningTeamID] + 1;
                }

                if (winningTeamIDs.Select(tID => instance.teamPoints[tID]).All(p => p < (int) GameModeManager.CurrentHandler.Settings["pointsToWinRound"]))
                {
                    instance.PointOver(winningTeamIDs);
                    return;
                }

                int[] roundWinningTeamIDs = winningTeamIDs.Where(tID => instance.teamPoints[tID] >= (int) GameModeManager.CurrentHandler.Settings["pointsToWinRound"]).ToArray();
                foreach (int winningTeamID in roundWinningTeamIDs)
                {
                    instance.teamRounds[winningTeamID] = instance.teamRounds[winningTeamID] + 1;
                }
                instance.RoundOver(roundWinningTeamIDs);
            }
        }

    }
}