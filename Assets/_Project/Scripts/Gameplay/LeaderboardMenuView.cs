using System.Collections;
using System.Collections.Generic;
using Ngj10.Core.Leaderboard;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Leaderboard card woven into the title menu, sitting on the right side of the
    /// start screen. Mirrors <see cref="AchievementsMenuView"/>: it builds its own UI
    /// in code (rounded card, header, a scrollable list of rows) so no extra scene
    /// wiring is needed beyond a font reference.
    ///
    /// On every menu show it fetches the top 100, lays out the first ten rows plus —
    /// when the local player is outside that ten — one extra highlighted row pinned
    /// below with the player's real rank. The card starts invisible and fades in only
    /// once the data is in; if the leaderboard isn't configured, there's no network,
    /// or the request fails, the card stays hidden and the menu shows nothing extra
    /// (the offline requirement). Runs on unscaled time — the menu sits at timeScale 0.
    /// </summary>
    public class LeaderboardMenuView : MonoBehaviour
    {
        // Shared palette with the achievements menu so the title reads as one style.
        private static readonly Color Gold = new Color(0.957f, 0.788f, 0.365f);
        private static readonly Color CardBg = new Color(0.06f, 0.07f, 0.10f, 0.82f);
        private static readonly Color RowBg = new Color(0.18f, 0.20f, 0.26f, 0.55f);
        private static readonly Color RowSelfBg = new Color(0.957f, 0.788f, 0.365f, 0.92f);
        private static readonly Color TextLight = new Color(0.96f, 0.97f, 1f);
        private static readonly Color TextDim = new Color(0.65f, 0.66f, 0.72f);
        private static readonly Color TextDark = new Color(0.12f, 0.10f, 0.06f);

        [Tooltip("Title font — matches the menu (Forum SDF). Falls back to the TMP default when null.")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Layout (relative to panel centre)")]
        [Tooltip("Card size in reference pixels (1920×1080 canvas).")]
        [SerializeField] private Vector2 _cardSize = new Vector2(560f, 620f);
        [Tooltip("Card centre offset from the panel centre — positive X pushes it right.")]
        [SerializeField] private Vector2 _cardAnchoredPos = new Vector2(540f, 0f);

        [Tooltip("How many top rows are visible before the list scrolls.")]
        [SerializeField] private int _visibleRows = 10;
        [SerializeField] private float _rowHeight = 46f;
        [SerializeField] private float _rowSpacing = 6f;
        [SerializeField] private int _fetchCount = 100;
        [SerializeField] private float _fadeDuration = 0.5f;

        [Header("Rename")]
        [Tooltip("Height of the rename bar (input + button) pinned at the card bottom.")]
        [SerializeField] private float _renameBarHeight = 44f;
        [SerializeField] private int _nameMaxLength = 18;

        private CanvasGroup _group;
        private RectTransform _listContent;
        private RectTransform _selfSlot;
        private Coroutine _fade;

        private GameObject _renameBar;
        private TMP_InputField _nameInput;
        private Button _renameButton;
        private TextMeshProUGUI _renameButtonLabel;

        private void Awake()
        {
            BuildCard();
            // Hidden until a fetch succeeds — never flash an empty card.
            _group.alpha = 0f;
            gameObject.SetActive(true);
        }

        // Re-fetch each time the menu is shown (the panel toggles active, taking this
        // child with it). A restart-to-menu thus always sees fresh standings.
        private void OnEnable()
        {
            HideInstant();
            TryFetch();
        }

        private void OnDisable()
        {
            if (_fade != null)
                StopCoroutine(_fade);
            _fade = null;
        }

        private void TryFetch()
        {
            var client = LeaderboardClient.Instance;
            if (!client.IsAvailable)
                return; // offline / no config → card stays hidden

            client.FetchTop(_fetchCount, OnFetched, OnError);
        }

        private void OnError() => HideInstant(); // network / parse failure → no board

        private void OnFetched(List<ScoreEntry> entries)
        {
            bool selfInFetch = Populate(entries);
            FadeIn();

            // Rename is gated on the player already owning a server row. Seeing
            // yourself in the fetched window proves it (no extra request). Otherwise
            // fall back to HasRecord (local flag → server GET by uid) — the operator's
            // chosen gate. Hide the bar until the answer is yes.
            SetRenameVisible(false);
            if (selfInFetch)
                SetRenameVisible(true);
            else
                LeaderboardClient.Instance.HasRecord(SetRenameVisible);
        }

        // --- Population -----------------------------------------------------

        // Returns whether the local player appears anywhere in the fetched window —
        // proof of an existing row, used to gate the rename bar without a round-trip.
        private bool Populate(List<ScoreEntry> entries)
        {
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);
            for (int i = _selfSlot.childCount - 1; i >= 0; i--)
                Destroy(_selfSlot.GetChild(i).gameObject);

            string myUid = PlayerIdentity.Uid;
            int selfIndex = entries.FindIndex(e => e.uid == myUid);

            int topCount = Mathf.Min(entries.Count, _visibleRows);
            for (int i = 0; i < topCount; i++)
                AddRow(_listContent, i + 1, entries[i], isSelf: i == selfIndex);

            // The local player isn't in the visible top rows → pin their own row below.
            // Within the fetched window we know the real rank; beyond it we can't, so
            // the row is omitted (we never invent a rank).
            _selfSlot.gameObject.SetActive(false);
            if (selfIndex >= topCount && selfIndex >= 0)
            {
                _selfSlot.gameObject.SetActive(true);
                AddRow(_selfSlot, selfIndex + 1, entries[selfIndex], isSelf: true);
            }

            // Resize the scroll content so exactly the rows we added are scrollable.
            float h = topCount * _rowHeight + Mathf.Max(0, topCount - 1) * _rowSpacing
                      + 2f * _rowSpacing;
            _listContent.sizeDelta = new Vector2(_listContent.sizeDelta.x, h);

            return selfIndex >= 0;
        }

        private void AddRow(Transform parent, int rank, ScoreEntry e, bool isSelf)
        {
            var row = new GameObject(isSelf ? "RowSelf" : "Row",
                typeof(RectTransform), typeof(Image), typeof(RoundedRectSprite), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var rrt = row.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(0f, _rowHeight);
            // The scroll list uses a VerticalLayoutGroup with childControlHeight — give
            // it a fixed preferred height so rows don't collapse to zero. (Harmless on
            // the pinned self-slot, which isn't under a layout group.)
            row.GetComponent<LayoutElement>().preferredHeight = _rowHeight;
            row.GetComponent<Image>().color = isSelf ? RowSelfBg : RowBg;

            Color main = isSelf ? TextDark : TextLight;
            Color dim = isSelf ? new Color(0.20f, 0.16f, 0.08f) : TextDim;

            // Columns: rank | name | height | time.
            MakeCol(row.transform, $"{rank}", 22, TextAlignmentOptions.Center, isSelf ? TextDark : Gold,
                0.00f, 0.10f);
            MakeCol(row.transform, e.name, 22, TextAlignmentOptions.Left, main, 0.11f, 0.58f);
            MakeCol(row.transform, $"{e.max_height} м", 22, TextAlignmentOptions.Right, main, 0.58f, 0.80f);
            MakeCol(row.transform, FormatTime(e.time_to_max), 20, TextAlignmentOptions.Right, dim, 0.80f, 1.00f);
        }

        // mm:ss.mmm-ish, compact: seconds with one decimal under a minute, m:ss above.
        private static string FormatTime(int ms)
        {
            float s = ms / 1000f;
            if (s < 60f)
                return $"{s:0.0}с";
            int m = (int)(s / 60f);
            int rem = Mathf.RoundToInt(s - m * 60f);
            return $"{m}:{rem:00}";
        }

        // --- Build the card -------------------------------------------------

        private void BuildCard()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null)
                rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = _cardSize;
            rt.anchoredPosition = _cardAnchoredPos;

            var bg = gameObject.AddComponent<Image>();
            bg.color = CardBg;
            gameObject.AddComponent<RoundedRectSprite>();
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false; // purely informational — don't eat menu clicks

            var header = MakeLabel(transform, "Header", 34, FontStyles.Bold, Gold);
            header.text = "Лидерборд";
            header.alignment = TextAlignmentOptions.Center;
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(0f, 52f);
            hrt.anchoredPosition = new Vector2(0f, -18f);

            BuildScroll();
            BuildSelfSlot();
            BuildRenameBar();
        }

        // Bottom-stack geometry, measured up from the card's bottom edge:
        //   [pad] rename bar [gap] self-slot [gap] scroll …
        private const float BottomPad = 14f;
        private const float StackGap = 8f;
        private float RenameTopY => BottomPad + _renameBarHeight;
        private float SelfSlotBottomY => RenameTopY + StackGap;
        private float SelfSlotTopY => SelfSlotBottomY + _rowHeight;
        private float ScrollBottomY => SelfSlotTopY + 10f;

        private void BuildScroll()
        {
            var viewGo = new GameObject("Scroll",
                typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            viewGo.transform.SetParent(transform, false);
            var vrt = viewGo.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0f, 0f);
            vrt.anchorMax = new Vector2(1f, 1f);
            // Leave room for the header (top) and the self-row + rename bar (bottom).
            vrt.offsetMin = new Vector2(18f, ScrollBottomY);
            vrt.offsetMax = new Vector2(-18f, -78f);
            viewGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // raycast catcher

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup));
            contentGo.transform.SetParent(viewGo.transform, false);
            _listContent = contentGo.GetComponent<RectTransform>();
            // Content edges flush to the viewport — keeps rows inside the mask
            // (ui-conventions.md: the leaderboard's clipped-column trap).
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.offsetMin = new Vector2(0f, _listContent.offsetMin.y);
            _listContent.offsetMax = new Vector2(0f, _listContent.offsetMax.y);
            _listContent.anchoredPosition = Vector2.zero;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = _rowSpacing;
            vlg.padding = new RectOffset(0, 0, (int)_rowSpacing, (int)_rowSpacing);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            var scroll = viewGo.GetComponent<ScrollRect>();
            scroll.content = _listContent;
            scroll.viewport = vrt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
        }

        // The pinned row below the scroll for the local player when they rank outside
        // the visible top rows. A fixed slot rather than part of the scroll so it stays
        // put while the list scrolls.
        private void BuildSelfSlot()
        {
            var go = new GameObject("SelfSlot", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _selfSlot = go.GetComponent<RectTransform>();
            _selfSlot.anchorMin = new Vector2(0f, 0f);
            _selfSlot.anchorMax = new Vector2(1f, 0f);
            _selfSlot.pivot = new Vector2(0.5f, 0f);
            _selfSlot.offsetMin = new Vector2(18f, SelfSlotBottomY);
            _selfSlot.offsetMax = new Vector2(-18f, SelfSlotTopY);
            go.SetActive(false);
        }

        // Rename bar pinned at the very bottom: an input field + an apply button. Built
        // only once; shown via SetRenameVisible after the gate passes (player owns a
        // row). The card's CanvasGroup blocks raycasts globally (informational board),
        // so this bar gets its own group that re-enables interaction for just itself.
        private void BuildRenameBar()
        {
            _renameBar = new GameObject("RenameBar",
                typeof(RectTransform), typeof(CanvasGroup));
            _renameBar.transform.SetParent(transform, false);
            var brt = _renameBar.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(18f, BottomPad);
            brt.offsetMax = new Vector2(-18f, RenameTopY);
            var barGroup = _renameBar.GetComponent<CanvasGroup>();
            barGroup.interactable = true;
            barGroup.blocksRaycasts = true;
            // The card's CanvasGroup has blocksRaycasts=false (informational board);
            // child groups AND with parents, so the bar must ignore the parent to
            // receive its own clicks.
            barGroup.ignoreParentGroups = true;

            // Input field (left ~70%).
            var inputGo = new GameObject("NameInput",
                typeof(RectTransform), typeof(Image), typeof(RoundedRectSprite), typeof(TMP_InputField));
            inputGo.transform.SetParent(_renameBar.transform, false);
            var irt = inputGo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0f, 0f);
            irt.anchorMax = new Vector2(0.68f, 1f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = new Vector2(-6f, 0f);
            inputGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.92f);

            var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(inputGo.transform, false);
            var tart = textArea.GetComponent<RectTransform>();
            tart.anchorMin = Vector2.zero;
            tart.anchorMax = Vector2.one;
            tart.offsetMin = new Vector2(12f, 2f);
            tart.offsetMax = new Vector2(-12f, -2f);

            var inputText = MakeLabel(textArea.transform, "Text", 22, FontStyles.Normal, TextDark);
            inputText.alignment = TextAlignmentOptions.Left;
            Stretch(inputText.rectTransform);

            _nameInput = inputGo.GetComponent<TMP_InputField>();
            _nameInput.textViewport = tart;
            _nameInput.textComponent = inputText;
            _nameInput.characterLimit = _nameMaxLength;
            _nameInput.lineType = TMP_InputField.LineType.SingleLine;
            _nameInput.text = PlayerIdentity.Name;
            _nameInput.fontAsset = _font;

            // Apply button (right ~30%).
            var btnGo = new GameObject("RenameButton",
                typeof(RectTransform), typeof(Image), typeof(RoundedRectSprite), typeof(Button));
            btnGo.transform.SetParent(_renameBar.transform, false);
            var bgrt = btnGo.GetComponent<RectTransform>();
            bgrt.anchorMin = new Vector2(0.68f, 0f);
            bgrt.anchorMax = new Vector2(1f, 1f);
            bgrt.offsetMin = new Vector2(6f, 0f);
            bgrt.offsetMax = Vector2.zero;
            btnGo.GetComponent<Image>().color = Gold;
            _renameButton = btnGo.GetComponent<Button>();
            _renameButton.onClick.AddListener(OnRenameClicked);

            _renameButtonLabel = MakeLabel(btnGo.transform, "Label", 20, FontStyles.Bold, TextDark);
            _renameButtonLabel.text = "Сменить имя";
            _renameButtonLabel.alignment = TextAlignmentOptions.Center;
            Stretch(_renameButtonLabel.rectTransform);

            _renameBar.SetActive(false);
        }

        private void SetRenameVisible(bool visible)
        {
            if (_renameBar == null)
                return;
            if (visible && !_renameBar.activeSelf)
                _nameInput.SetTextWithoutNotify(PlayerIdentity.Name); // sync to current name
            _renameBar.SetActive(visible);
        }

        private void OnRenameClicked()
        {
            string newName = _nameInput.text;
            if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == PlayerIdentity.Name)
                return;

            _renameButton.interactable = false;
            _renameButtonLabel.text = "…";
            LeaderboardClient.Instance.Rename(newName, ok =>
            {
                _renameButton.interactable = true;
                _renameButtonLabel.text = ok ? "Готово" : "Ошибка";
                if (ok)
                {
                    _nameInput.SetTextWithoutNotify(PlayerIdentity.Name);
                    TryFetch(); // refresh the board to show the new name
                }
            });
        }

        // --- Fade -----------------------------------------------------------

        private void FadeIn()
        {
            if (_fade != null)
                StopCoroutine(_fade);
            _fade = StartCoroutine(FadeRoutine());
        }

        private IEnumerator FadeRoutine()
        {
            float t = 0f;
            while (t < _fadeDuration)
            {
                t += Time.unscaledDeltaTime; // menu runs at timeScale 0
                _group.alpha = Mathf.Clamp01(t / _fadeDuration);
                yield return null;
            }
            _group.alpha = 1f;
            _fade = null;
        }

        private void HideInstant()
        {
            if (_group != null)
                _group.alpha = 0f;
        }

        // --- Helpers --------------------------------------------------------

        private TextMeshProUGUI MakeCol(
            Transform parent, string text, float size, TextAlignmentOptions align, Color color,
            float anchorMinX, float anchorMaxX)
        {
            var label = MakeLabel(parent, "Col", size, FontStyles.Normal, color);
            label.text = text;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(anchorMinX, 0f);
            lrt.anchorMax = new Vector2(anchorMaxX, 1f);
            lrt.offsetMin = new Vector2(6f, 0f);
            lrt.offsetMax = new Vector2(-6f, 0f);
            return label;
        }

        private TextMeshProUGUI MakeLabel(
            Transform parent, string name, float size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (_font != null)
                text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
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
