using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Core.Achievements
{
    /// <summary>
    /// "View all achievements" panel plus the button that opens it. Builds its UI
    /// in code on its own overlay Canvas — no scene wiring. The button sits in a
    /// screen corner; clicking it toggles a scrollable list showing every
    /// achievement with its unlocked / locked (and progress) state.
    ///
    /// Drop one in Boot (persistent) and it's available in every scene.
    /// </summary>
    public class AchievementListUI : MonoBehaviour
    {
        private static readonly Color PanelBg = new Color(0.08f, 0.09f, 0.12f, 0.97f);
        private static readonly Color RowBgUnlocked = new Color(0.16f, 0.20f, 0.14f, 1f);
        private static readonly Color RowBgLocked = new Color(0.14f, 0.14f, 0.17f, 1f);
        private static readonly Color Gold = new Color(1f, 0.78f, 0.25f);

        private GameObject _panel;
        private RectTransform _content;
        private Font _font;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUi();
        }

        private void TogglePanel()
        {
            bool show = !_panel.activeSelf;
            _panel.SetActive(show);
            if (show)
            {
                RefreshRows();
            }
        }

        private void RefreshRows()
        {
            var mgr = AchievementManager.Instance;
            if (mgr == null)
            {
                return;
            }

            // Cheap for jam-sized lists: rebuild rows each open.
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Destroy(_content.GetChild(i).gameObject);
            }

            foreach (var def in mgr.All)
            {
                bool unlocked = mgr.IsUnlocked(def.Id);
                AddRow(def, unlocked, mgr.GetProgress(def.Id));
            }
        }

        private void AddRow(AchievementDefinition def, bool unlocked, int progress)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(Image),
                typeof(LayoutElement));
            row.transform.SetParent(_content, false);
            row.GetComponent<Image>().color = unlocked ? RowBgUnlocked : RowBgLocked;
            row.GetComponent<LayoutElement>().minHeight = 64f;

            string status = unlocked ? "✓ " : "🔒 ";
            string progressTail = (!unlocked && def.Type != AchievementType.Single && def.Target > 1)
                ? $"   ({Mathf.Min(progress, def.Target)}/{def.Target})"
                : string.Empty;

            var title = MakeText(row.transform, "Title", 16, FontStyle.Bold,
                unlocked ? Gold : new Color(0.7f, 0.7f, 0.7f));
            title.text = status + def.Title + progressTail;
            StretchTop(title.rectTransform, 8f, 28f);

            var desc = MakeText(row.transform, "Desc", 13, FontStyle.Normal,
                unlocked ? Color.white : new Color(0.55f, 0.55f, 0.55f));
            desc.text = def.Description;
            StretchTop(desc.rectTransform, 32f, 24f);
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("AchievementListCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasGo.AddComponent<GraphicRaycaster>();

            BuildButton(canvasGo.transform);
            BuildPanel(canvasGo.transform);
        }

        private void BuildButton(Transform parent)
        {
            var btnGo = new GameObject("AchievementsButton", typeof(RectTransform),
                typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(170f, 40f);
            rt.anchoredPosition = new Vector2(-12f, -12f);
            btnGo.GetComponent<Image>().color = new Color(0.15f, 0.17f, 0.22f, 0.95f);
            btnGo.GetComponent<Button>().onClick.AddListener(TogglePanel);

            var label = MakeText(btnGo.transform, "Label", 16, FontStyle.Bold, Gold);
            label.text = "🏆 Achievements";
            label.alignment = TextAnchor.MiddleCenter;
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
        }

        private void BuildPanel(Transform parent)
        {
            _panel = new GameObject("AchievementsPanel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(parent, false);
            var prt = _panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(560f, 480f);
            prt.anchoredPosition = Vector2.zero;
            _panel.GetComponent<Image>().color = PanelBg;

            // Header.
            var header = MakeText(_panel.transform, "Header", 24, FontStyle.Bold, Gold);
            header.text = "Achievements";
            header.alignment = TextAnchor.MiddleCenter;
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(0f, 48f);
            hrt.anchoredPosition = new Vector2(0f, -8f);

            // Close button (top-right of panel).
            var closeGo = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_panel.transform, false);
            var crt = closeGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(40f, 40f);
            crt.anchoredPosition = new Vector2(-8f, -8f);
            closeGo.GetComponent<Image>().color = new Color(0.5f, 0.18f, 0.18f, 1f);
            closeGo.GetComponent<Button>().onClick.AddListener(() => _panel.SetActive(false));
            var x = MakeText(closeGo.transform, "X", 22, FontStyle.Bold, Color.white);
            x.text = "×";
            x.alignment = TextAnchor.MiddleCenter;
            var xrt = x.rectTransform;
            xrt.anchorMin = Vector2.zero;
            xrt.anchorMax = Vector2.one;
            xrt.offsetMin = Vector2.zero;
            xrt.offsetMax = Vector2.zero;

            BuildScrollView(_panel.transform);

            _panel.SetActive(false);
        }

        // Minimal vertical scroll list built in code.
        private void BuildScrollView(Transform parent)
        {
            var viewGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image),
                typeof(ScrollRect), typeof(Mask));
            viewGo.transform.SetParent(parent, false);
            var vrt = viewGo.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0f, 0f);
            vrt.anchorMax = new Vector2(1f, 1f);
            vrt.offsetMin = new Vector2(12f, 12f);
            vrt.offsetMax = new Vector2(-12f, -60f);
            viewGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewGo.transform, false);
            _content = contentGo.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;

            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewGo.GetComponent<ScrollRect>();
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = vrt;
        }

        // --- Text helpers ----------------------------------------------------

        private Text MakeText(Transform parent, string name, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        // Anchor a label to the top of its parent row, inset from the left.
        private static void StretchTop(RectTransform rt, float topOffset, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(12f, 0f);
            rt.offsetMax = new Vector2(-12f, 0f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -topOffset);
        }
    }
}
