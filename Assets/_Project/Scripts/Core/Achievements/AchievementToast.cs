using System.Collections;
using System.Collections.Generic;
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

        private RectTransform _card;
        private Text _titleText;
        private Text _descText;
        private CanvasGroup _group;

        private readonly Queue<AchievementDefinition> _pending = new Queue<AchievementDefinition>();
        private bool _showing;

        private void Awake()
        {
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
            _titleText.text = $"Achievement unlocked!\n{def.Title}";
            _descText.text = def.Description;
            _group.alpha = 1f;

            // Slide up from just below the bottom-right resting spot.
            Vector2 rest = new Vector2(-_margin, _margin);
            Vector2 off = rest + new Vector2(0f, -(_cardSize.y + _margin));

            yield return Slide(off, rest, _slideIn);
            yield return new WaitForSeconds(_hold);
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
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9000; // above gameplay HUD
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
                new Vector2(-16f, -8f), TextAnchor.UpperLeft, 18, FontStyle.Bold,
                new Color(1f, 0.78f, 0.25f));

            _descText = MakeText(cardGo.transform, "Desc", new Vector2(20f, 10f),
                new Vector2(-16f, 44f), TextAnchor.LowerLeft, 15, FontStyle.Normal,
                Color.white);
        }

        // Stretched text inset by the given left/bottom and right/top offsets.
        private static Text MakeText(
            Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax,
            TextAnchor anchor, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}
