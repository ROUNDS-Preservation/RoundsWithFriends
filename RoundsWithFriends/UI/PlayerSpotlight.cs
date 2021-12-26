﻿using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnboundLib;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnboundLib.GameModes;

namespace RWF.UI
{
    static class PlayerSpotlight
    {
        internal static float SpotlightSizeMult = 25f;

        private static bool fadeInProgress = false;

        private const float MaxShadowOpacity = 1f;
        private const float DefaultFadeInTime = 0.25f;
        private const float DefaultFadeOutTime = 1f;
        private const float DefaultFadeOutDelay = 1f;

        private static GameObject _Group = null;

        public static GameObject Group
        {
            get
            {
                if (PlayerSpotlight._Group != null) { return PlayerSpotlight._Group; }

                PlayerSpotlight._Group = new GameObject("SpotlightGroup", typeof(SortingGroup));
                PlayerSpotlight._Group.transform.SetParent(UnityEngine.GameObject.Find("Game/UI/UI_Game/Canvas").transform);
                PlayerSpotlight._Group.SetActive(true);
                PlayerSpotlight._Group.transform.localScale = Vector3.one;
                PlayerSpotlight._Group.GetComponent<SortingGroup>().sortingLayerName = "MapParticle";
                PlayerSpotlight._Group.GetComponent<SortingGroup>().sortingOrder = 10;

                return PlayerSpotlight._Group;
            }
        }


        private static GameObject _BG = null;

        public static GameObject BG
        {
            get
            {
                if (PlayerSpotlight._BG != null) { return PlayerSpotlight._BG; }

                GameObject bg = UnityEngine.GameObject.Find("Game/UI/UI_Game/Canvas/EscapeMenu/bg");
                PlayerSpotlight._BG = new GameObject("SpotlightShadow", typeof(SpriteRenderer));
                PlayerSpotlight._BG.transform.SetParent(PlayerSpotlight._Group.transform);
                PlayerSpotlight._BG.SetActive(false);
                PlayerSpotlight._BG.transform.localScale = 100f*Vector3.one;
                PlayerSpotlight._BG.GetComponent<SpriteRenderer>().sprite = Sprite.Create(new Texture2D(1920, 1080), new Rect(0f, 0f, 1920f, 1080f), new Vector2(0.5f, 0.5f));
                PlayerSpotlight._BG.GetComponent<SpriteRenderer>().color = Color.black;//bg.GetComponent<Graphic>().color;
                PlayerSpotlight._BG.GetComponent<SpriteRenderer>().sortingOrder = 0;
                PlayerSpotlight._BG.GetComponent<SpriteRenderer>().maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
                PlayerSpotlight._BG.GetComponent<SpriteRenderer>().sortingLayerName = "MapParticle";

                return PlayerSpotlight._BG;
            }
        }

        private static GameObject _Spot = null;
        public static GameObject Spot
        {
            get
            {
                if (PlayerSpotlight._Spot != null) { return PlayerSpotlight._Spot; }

                GameObject characterSelect = UnityEngine.GameObject.Find("Game/UI/UI_MainMenu/Canvas/ListSelector/CharacterSelect");
                GameObject portrait = characterSelect.GetComponentInChildren<CharacterCreatorPortrait>(true).gameObject;
                GameObject circle = portrait.transform.GetChild(2).GetChild(0).gameObject;

                PlayerSpotlight._Spot = new GameObject("Spotlight", typeof(SpriteMask), typeof(SpriteRenderer));
                GameObject.DontDestroyOnLoad(PlayerSpotlight._Spot);

                PlayerSpotlight._Spot.GetOrAddComponent<SpriteMask>().sprite = circle.GetComponent<SpriteRenderer>().sprite;
                PlayerSpotlight._Spot.GetOrAddComponent<SpriteMask>().sortingOrder = 1;
                PlayerSpotlight._Spot.GetOrAddComponent<SpriteMask>().sortingLayerName = "MapParticle";
                PlayerSpotlight._Spot.SetActive(false);

                return PlayerSpotlight._Spot;

            }
        }
        private static float GetShadowOpacity()
        {
            return PlayerSpotlight.BG.GetComponent<SpriteRenderer>().color.a;
        }
        private static void SetShadowOpacity(float a)
        {
            Color color = PlayerSpotlight.BG.GetComponent<SpriteRenderer>().color;
            PlayerSpotlight.BG.GetComponent<SpriteRenderer>().color = new Color(color.r, color.g, color.b, a);
        }

        public static void FadeIn(float time = PlayerSpotlight.DefaultFadeInTime)
        {
            if (PlayerSpotlight.fadeInProgress) { return; }

            PlayerSpotlight.SetShadowOpacity(0f);
            PlayerSpotlight.BG.SetActive(true);
            RWFMod.instance.StartCoroutine(PlayerSpotlight.FadeToCoroutine(PlayerSpotlight.MaxShadowOpacity, time));
        }

        public static void FadeOut(float time = PlayerSpotlight.DefaultFadeOutTime, float delay = PlayerSpotlight.DefaultFadeOutDelay)
        {
            if (PlayerSpotlight.fadeInProgress) { return; }

            PlayerSpotlight.SetShadowOpacity(PlayerSpotlight.MaxShadowOpacity);
            PlayerSpotlight.BG.SetActive(true);
            RWFMod.instance.StartCoroutine(PlayerSpotlight.FadeToCoroutine(0f, time, delay, true));
        }
        private static IEnumerator FadeToCoroutine(float a, float time, float delay = 0f, bool disableWhenComplete = false)
        {
            if (time <= 0f || PlayerSpotlight.fadeInProgress) { yield break; }
            PlayerSpotlight.fadeInProgress = true;

            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            float a0 = PlayerSpotlight.GetShadowOpacity();
            float totalTime = time;
            while (time > 0f)
            {
                PlayerSpotlight.SetShadowOpacity(UnityEngine.Mathf.Lerp(a, a0, time / totalTime));
                time -= Time.deltaTime;
                yield return null;
            }
            PlayerSpotlight.SetShadowOpacity(a);
            if (disableWhenComplete) { PlayerSpotlight.BG.SetActive(false); }

            PlayerSpotlight.fadeInProgress = false;
            yield break;
        }

        public static void AddSpotToPlayer(Player player)
        {
            GameObject Group = PlayerSpotlight.Group;
            GameObject spotlight = GameObject.Instantiate(PlayerSpotlight.Spot, Group.transform);
            spotlight.GetOrAddComponent<FollowPlayer>().SetPlayer(player);
            spotlight.SetActive(true);
            spotlight.transform.localScale = 25f * Vector3.one;
        }

        public static IEnumerator BattleStartFailsafe(IGameModeHandler gm)
        {
            PlayerSpotlight.SetShadowOpacity(0f);
            PlayerSpotlight.BG.SetActive(false);
            yield break;
        }
    }
    public class FollowPlayer : MonoBehaviour
    {
        Player player = null;

        public void SetPlayer(Player player)
        {
            this.player = player;
        }

        void Start()
        {
            if (this.player == null)
            {
                GameObject.Destroy(this);
            }
        }
        void Update()
        {
            this.transform.position = this.player.gameObject.transform.position;
            // scale with player size
            this.transform.localScale = (this.player.transform.localScale.x / 1.25f) * PlayerSpotlight.SpotlightSizeMult * Vector3.one;
        }
    }
}
