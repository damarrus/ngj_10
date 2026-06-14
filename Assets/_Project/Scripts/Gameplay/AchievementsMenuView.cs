using System.Collections.Generic;
using Ngj10.Core.Achievements;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Achievements browser woven into the title menu. Adds a gold "Достижения"
    /// button under START that shows a live unlocked-count (e.g. 2/15); opening it
    /// hides the menu rows and reveals a 3-per-row grid of every achievement with its
    /// progress, plus a Back button that restores the menu.
    ///
    /// Lives on the StartPanel and builds its own UI in code (the button, the grid
    /// overlay) so no extra scene wiring is needed beyond the rows to hide. Reads
    /// achievement state from <see cref="AchievementManager"/>; never writes it.
    /// </summary>
    public class AchievementsMenuView : MonoBehaviour
    {
        private static readonly Color Gold = new Color(0.957f, 0.788f, 0.365f);
        private static readonly Color CardUnlocked = new Color(0.957f, 0.788f, 0.365f, 0.95f);
        private static readonly Color CardLocked = new Color(0.18f, 0.20f, 0.26f, 0.95f);
        private static readonly Color TextDark = new Color(0.12f, 0.10f, 0.06f);
        private static readonly Color TextDim = new Color(0.65f, 0.66f, 0.72f);

        [Tooltip("Menu rows hidden while the achievements grid is open (START, slider, hint).")]
        [SerializeField] private GameObject[] _menuRows;

        [Tooltip("The 'Достижения' button laid out on the scene (a clone of START). " +
                 "Its click opens the grid and its label shows the unlocked count.")]
        [SerializeField] private Button _openButton;

        [Tooltip("Label inside the open button — gets the 'Достижения  X/Y' text.")]
        [SerializeField] private TextMeshProUGUI _openButtonLabel;

        private GameObject _gridOverlay;
        private RectTransform _gridContent;

        private void Awake()
        {
            BuildGridOverlay();
            if (_openButton != null)
                _openButton.onClick.AddListener(Open);
        }

        private void OnEnable()
        {
            // Returning to the menu (panel re-shown) — refresh the count.
            RefreshButtonLabel();
        }

        private void RefreshButtonLabel()
        {
            if (_openButtonLabel == null)
                return;

            int total = 0, unlocked = 0;
            var mgr = AchievementManager.Instance;
            foreach (var def in mgr.All)
            {
                total++;
                if (mgr.IsUnlocked(def.Id))
                    unlocked++;
            }
            _openButtonLabel.text = $"Достижения  {unlocked}/{total}";
        }

        // --- Grid overlay ---------------------------------------------------

        private void BuildGridOverlay()
        {
            _gridOverlay = new GameObject("AchievementsGrid", typeof(RectTransform), typeof(Image));
            _gridOverlay.transform.SetParent(transform, false);
            var ort = _gridOverlay.GetComponent<RectTransform>();
            Stretch(ort);
            _gridOverlay.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 0.98f);

            // Header.
            var header = MakeLabel(_gridOverlay.transform, "Header", 40, FontStyles.Bold, Gold);
            header.text = "Достижения";
            header.alignment = TextAlignmentOptions.Center;
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(0f, 64f);
            hrt.anchoredPosition = new Vector2(0f, -16f);

            BuildScroll(_gridOverlay.transform);
            BuildBackButton(_gridOverlay.transform);

            _gridOverlay.SetActive(false);
        }

        private void BuildScroll(Transform parent)
        {
            var viewGo = new GameObject("Scroll",
                typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            viewGo.transform.SetParent(parent, false);
            var vrt = viewGo.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0f, 0f);
            vrt.anchorMax = new Vector2(1f, 1f);
            vrt.offsetMin = new Vector2(24f, 88f);  // leave room for the Back button
            vrt.offsetMax = new Vector2(-24f, -88f); // and the header
            viewGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // raycast catcher

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewGo.transform, false);
            _gridContent = contentGo.GetComponent<RectTransform>();
            _gridContent.anchorMin = new Vector2(0f, 1f);
            _gridContent.anchorMax = new Vector2(1f, 1f);
            _gridContent.pivot = new Vector2(0.5f, 1f);
            _gridContent.anchoredPosition = Vector2.zero;

            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(230f, 120f);
            grid.spacing = new Vector2(16f, 16f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewGo.GetComponent<ScrollRect>();
            scroll.content = _gridContent;
            scroll.viewport = vrt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
        }

        private void BuildBackButton(Transform parent)
        {
            var go = new GameObject("BackButton",
                typeof(RectTransform), typeof(Image), typeof(RoundedRectSprite), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(240f, 56f);
            rt.anchoredPosition = new Vector2(0f, 18f);
            go.GetComponent<Image>().color = Gold;
            go.GetComponent<Button>().onClick.AddListener(Close);

            var label = MakeLabel(go.transform, "Label", 26, FontStyles.Bold, TextDark);
            label.text = "Назад";
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
        }

        // --- Open / Close ---------------------------------------------------

        private void Open()
        {
            SetMenuRows(false);
            RebuildTiles();
            _gridOverlay.SetActive(true);
        }

        private void Close()
        {
            _gridOverlay.SetActive(false);
            SetMenuRows(true);
            RefreshButtonLabel();
        }

        private void SetMenuRows(bool show)
        {
            // The open button is one of the menu rows in spirit, but it lives on this
            // object and is hidden via the overlay covering it; toggle the authored
            // rows plus the open button.
            if (_menuRows != null)
                foreach (var row in _menuRows)
                    if (row != null)
                        row.SetActive(show);
        }

        private void RebuildTiles()
        {
            for (int i = _gridContent.childCount - 1; i >= 0; i--)
                Destroy(_gridContent.GetChild(i).gameObject);

            var mgr = AchievementManager.Instance;
            foreach (var def in mgr.All)
                AddTile(def, mgr.IsUnlocked(def.Id), mgr.GetProgress(def.Id));
        }

        private void AddTile(AchievementDefinition def, bool unlocked, int progress)
        {
            var tile = new GameObject("Tile",
                typeof(RectTransform), typeof(Image), typeof(RoundedRectSprite));
            tile.transform.SetParent(_gridContent, false);
            tile.GetComponent<Image>().color = unlocked ? CardUnlocked : CardLocked;

            Color titleColor = unlocked ? TextDark : Color.white;
            Color descColor = unlocked ? new Color(0.20f, 0.16f, 0.08f) : TextDim;

            var title = MakeLabel(tile.transform, "Title", 18, FontStyles.Bold, titleColor);
            title.text = (unlocked ? "✓ " : "🔒 ") + def.Title;
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(12f, 0f);
            trt.offsetMax = new Vector2(-12f, 0f);
            trt.sizeDelta = new Vector2(trt.sizeDelta.x, 26f);
            trt.anchoredPosition = new Vector2(0f, -10f);

            var desc = MakeLabel(tile.transform, "Desc", 13, FontStyles.Normal, descColor);
            desc.text = def.Description;
            var drt = desc.rectTransform;
            drt.anchorMin = new Vector2(0f, 1f);
            drt.anchorMax = new Vector2(1f, 1f);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.offsetMin = new Vector2(12f, 0f);
            drt.offsetMax = new Vector2(-12f, 0f);
            drt.sizeDelta = new Vector2(drt.sizeDelta.x, 44f);
            drt.anchoredPosition = new Vector2(0f, -40f);

            // Progress bar for graded achievements that aren't done yet.
            bool graded = def.Type != AchievementType.Single && def.Target > 1;
            if (graded && !unlocked)
                AddProgressBar(tile.transform, Mathf.Min(progress, def.Target), def.Target);
        }

        private void AddProgressBar(Transform parent, int value, int target)
        {
            var track = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(parent, false);
            var brt = track.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(12f, 12f);
            brt.offsetMax = new Vector2(-12f, 12f);
            brt.sizeDelta = new Vector2(brt.sizeDelta.x, 16f);
            track.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            float fill = target > 0 ? Mathf.Clamp01((float)value / target) : 0f;
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(track.transform, false);
            var frt = fillGo.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(fill, 1f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            fillGo.GetComponent<Image>().color = Gold;

            var label = MakeLabel(track.transform, "Count", 11, FontStyles.Bold, Color.white);
            label.text = $"{value}/{target}";
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
        }

        // --- Helpers --------------------------------------------------------

        private static TextMeshProUGUI MakeLabel(
            Transform parent, string name, float size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
