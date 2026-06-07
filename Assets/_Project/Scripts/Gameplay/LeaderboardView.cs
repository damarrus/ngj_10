using System.Collections.Generic;
using Ngj10.Core.Leaderboard;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// The end-of-game leaderboard: a scrollable top-100 list plus a name editor,
    /// shown alongside the game-over score. The UI is built on the scene (wired via
    /// the inspector) so the layout can be tuned visually, including in Play mode;
    /// this component only drives it — submits the score, fetches the top N, and
    /// spawns one <see cref="_rowPrefab"/> per entry under <see cref="_content"/>.
    ///
    /// Flow on game-over: submit the local score (upsert by uid), then fetch the
    /// top 100 and populate the list. If the leaderboard isn't configured or any
    /// request fails (no internet, server down), the whole panel stays hidden and
    /// the player just sees their score — per the offline requirement.
    ///
    /// Renaming: edit the field, hit the button; the new name is persisted, the
    /// score re-submitted under it, and the list refreshed.
    /// </summary>
    public class LeaderboardView : MonoBehaviour
    {
        [SerializeField] private GameHud _hud;
        [SerializeField] private GameObject _panel;
        [SerializeField] private RectTransform _content;
        [SerializeField] private LeaderboardRow _rowPrefab;
        [SerializeField] private TMP_InputField _nameInput;
        [SerializeField] private Button _renameButton;
        [SerializeField] private int _topCount = 100;

        private int _lastScore;

        private void Awake()
        {
            if (_hud == null)
            {
                _hud = FindAnyObjectByType<GameHud>();
            }
        }

        private void OnEnable()
        {
            if (_hud != null)
            {
                _hud.GameOverShown += OnGameOverShown;
            }
            if (_renameButton != null)
            {
                _renameButton.onClick.AddListener(OnRenameClicked);
            }
            if (_panel != null)
            {
                _panel.SetActive(false);
            }
            if (_nameInput != null)
            {
                _nameInput.SetTextWithoutNotify(PlayerIdentity.Name);
            }
        }

        private void OnDisable()
        {
            if (_hud != null)
            {
                _hud.GameOverShown -= OnGameOverShown;
            }
            if (_renameButton != null)
            {
                _renameButton.onClick.RemoveListener(OnRenameClicked);
            }
        }

        private void OnGameOverShown(int finalScore)
        {
            _lastScore = finalScore;

            // No backend configured at all → never show the board, just the score.
            if (!LeaderboardClient.Instance.IsAvailable)
            {
                _panel.SetActive(false);
                return;
            }

            SubmitThenRefresh();
        }

        private void SubmitThenRefresh()
        {
            LeaderboardClient.Instance.SubmitScore(_lastScore, _ => Refresh());
        }

        private void Refresh()
        {
            _nameInput.SetTextWithoutNotify(PlayerIdentity.Name);
            LeaderboardClient.Instance.FetchTop(_topCount, OnFetched, OnFetchError);
        }

        private void OnFetched(List<ScoreEntry> entries)
        {
            _panel.SetActive(true);
            Populate(entries);
        }

        private void OnFetchError()
        {
            // Offline / server error: hide the board entirely, leave the bare score.
            _panel.SetActive(false);
        }

        private void OnRenameClicked()
        {
            PlayerIdentity.Rename(_nameInput.text);
            _nameInput.SetTextWithoutNotify(PlayerIdentity.Name);
            SubmitThenRefresh();
        }

        private void Populate(IReadOnlyList<ScoreEntry> entries)
        {
            for (var i = _content.childCount - 1; i >= 0; i--)
            {
                Destroy(_content.GetChild(i).gameObject);
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var row = Instantiate(_rowPrefab, _content);
                row.gameObject.SetActive(true);
                row.Set(i + 1, entries[i].name, entries[i].score);
            }
        }
    }
}
