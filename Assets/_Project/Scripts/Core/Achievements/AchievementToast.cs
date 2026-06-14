using System.Collections;
using System.Collections.Generic;
using Ngj10.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Core.Achievements
{
    /// <summary>
    /// Steam-style unlock popup. Subscribes to <see cref="AchievementManager"/>
    /// and slides a card in from a screen corner, holds, then slides out. Builds
    /// its whole UI in code on its own overlay Canvas — no scene wiring, works in
    /// any scene. Drop one in the Boot scene (persistent) and forget it.
    /// </summary>
    public class AchievementToast : MonoBehaviour
    {
        [Header("Timing (seconds)")]
        [SerializeField] private float _slideIn = 0.35f;
        [SerializeField] private float _hold = 2.5f;
        [SerializeField] private float _slideOut = 0.35f;

        [Header("Card")]
        [SerializeField] private Vector2 _cardSize = new Vector2(360f, 90f);
        [SerializeField] private float _margin = 16f;

        [Header("Audio")]
        // Loaded from Resources in Awake — the toast is usually spawned via
        // AddComponent (see AchievementManager) so there's no Inspector to wire it.
        [SerializeField] private AudioClip _unlockSfx;

        // Volume is authored on GameConfig (one place for top-level audio tuning);
        // this is the fallback when no GameConfig is present in the scene.
        private const float DefaultSfxVolume = 0.8f;
        private const string UnlockSfxResource = "Audio/achievement";

        private RectTransform _card;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _descText;
        private CanvasGroup _group;
        private AudioSource _audio;

        private readonly Queue<AchievementDefinition> _pending = new Queue<AchievementDefinition>();
        private bool _showing;

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.ignoreListenerPause = true; // play during the win freeze (timeScale 0)
            if (_unlockSfx == null)
            {
                _unlockSfx = Resources.Load<AudioClip>(UnlockSfxResource);
            }
            BuildUi();
        }

        private void OnEnable()
        {
            StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (AchievementManager.Instance != null)
            {
                AchievementManager.Instance.OnUnlocked -= Enqueue;
            }
        }

        // Manager may spawn the same frame; wait a beat then subscribe.
        private IEnumerator BindWhenReady()
        {
            while (AchievementManager.Instance == null)
            {
                yield return null;
            }
            AchievementManager.Instance.OnUnlocked -= Enqueue;
            AchievementManager.Instance.OnUnlocked += Enqueue;
        }

        private void Enqueue(AchievementDefinition def)
        {
            _pending.Enqueue(def);
            if (!_showing)
            {
                StartCoroutine(ShowLoop());
            }
        }

        private IEnumerator ShowLoop()
        {
            _showing = true;
            while (_pending.Count > 0)
            {
                yield return Present(_pending.Dequeue());
            }
            _showing = false;
        }

        private IEnumerator Present(AchievementDefinition def)
        {
            _titleText.text = def.Title;
            _descText.text = def.Description;
            _group.alpha = 1f;

            if (_unlockSfx != null)
            {
                var config = FindAnyObjectByType<GameConfig>();
                float volume = config != null ? config.AchievementVolume : DefaultSfxVolume;
                _audio.PlayOneShot(_unlockSfx, volume);
            }

            // Slide up from just below the bottom-right resting spot.
            Vector2 rest = new Vector2(-_margin, _margin);
            Vector2 off = rest + new Vector2(0f, -(_cardSize.y + _margin));

            yield return Slide(off, rest, _slideIn);
            // Realtime so the hold keeps counting during the win freeze (timeScale 0).
            yield return new WaitForSecondsRealtime(_hold);
            yield return Slide(rest, off, _slideOut);

            _group.alpha = 0f;
        }

        private IEnumerator Slide(Vector2 from, Vector2 to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                _card.anchoredPosition = Vector2.Lerp(from, to, k);
                yield return null;
            }
            _card.anchoredPosition = to;
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("AchievementToastCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.sortingOrder = 9000; // above gameplay HUD
            // Привязка к letterbox-камере (ScreenSpaceCamera) — UI не лезет на бары.
            canvasGo.AddComponent<LetterboxCanvasBinder>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasGo.AddComponent<GraphicRaycaster>();

            // Card anchored to bottom-right.
            var cardGo = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            cardGo.transform.SetParent(canvasGo.transform, false);
            _card = cardGo.GetComponent<RectTransform>();
            _card.anchorMin = new Vector2(1f, 0f);
            _card.anchorMax = new Vector2(1f, 0f);
            _card.pivot = new Vector2(1f, 0f);
            _card.sizeDelta = _cardSize;
            _card.anchoredPosition = new Vector2(-_margin, -_cardSize.y);

            var bg = cardGo.GetComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.16f, 0.95f);

            _group = cardGo.GetComponent<CanvasGroup>();
            _group.alpha = 0f;

            // Gold accent strip on the left edge.
            var strip = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(cardGo.transform, false);
            var stripRt = strip.GetComponent<RectTransform>();
            stripRt.anchorMin = new Vector2(0f, 0f);
            stripRt.anchorMax = new Vector2(0f, 1f);
            stripRt.pivot = new Vector2(0f, 0.5f);
            stripRt.sizeDelta = new Vector2(8f, 0f);
            stripRt.anchoredPosition = Vector2.zero;
            strip.GetComponent<Image>().color = new Color(1f, 0.78f, 0.25f, 1f);

            _titleText = MakeText(cardGo.transform, "Title", new Vector2(20f, -10f),
                new Vector2(-16f, -8f), TextAlignmentOptions.TopLeft, 18, FontStyles.Bold,
                new Color(1f, 0.78f, 0.25f));

            _descText = MakeText(cardGo.transform, "Desc", new Vector2(20f, 10f),
                new Vector2(-16f, 44f), TextAlignmentOptions.BottomLeft, 15, FontStyles.Normal,
                Color.white);
        }

        // Stretched text inset by the given left/bottom and right/top offsets.
        private static TextMeshProUGUI MakeText(
            Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax,
            TextAlignmentOptions anchor, int size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }
    }
}
