using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// In-game HUD for the single Game scene: live score + time, plus a game-over
    /// overlay with the final score and a restart hint, and a pause overlay.
    /// Finds its children by name in Awake rather than via inspector references —
    /// the MCP bridge can't wire references reliably, and finding by name keeps
    /// the scene robust across reloads. Expects under it: "ScoreText", "TimeText",
    /// "GameOverPanel", "GameOverPanel/ResultText", "PauseButton", "PausePanel"
    /// (a full-screen Button labelled "Click to continue"), and "StartPanel"
    /// (a full-screen Button labelled "Click to start game").
    ///
    /// Owns the start / pause / game-over UI widgets and raises events
    /// (<see cref="StartRequested"/>, <see cref="PauseRequested"/>,
    /// <see cref="ResumeRequested"/>, <see cref="PlayAgainRequested"/>) so the
    /// game controller never touches the buttons directly. Each click is routed
    /// by the EventSystem to a single widget, so there is no double-handling —
    /// no manual mouse polling anywhere.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        private const string ScorePath = "ScoreText";
        private const string TimePath = "TimeText";
        private const string PanelPath = "GameOverPanel";
        private const string ResultPath = "GameOverPanel/ResultText";
        private const string PauseButtonPath = "PauseButton";
        private const string PausePath = "PausePanel";
        private const string StartPath = "StartPanel";

        /// <summary>Raised when the player taps the "Click to start game" overlay.</summary>
        public event Action StartRequested;

        /// <summary>Raised when the player taps the on-screen Pause button.</summary>
        public event Action PauseRequested;

        /// <summary>Raised when the player taps the "Click to continue" overlay.</summary>
        public event Action ResumeRequested;

        /// <summary>Raised when the player taps the game-over "Click to play again" overlay.</summary>
        public event Action PlayAgainRequested;

        private TextMeshProUGUI _scoreText;
        private TextMeshProUGUI _timeText;
        private TextMeshProUGUI _resultText;
        private GameObject _gameOverPanel;
        private GameObject _pausePanel;
        private GameObject _startPanel;
        private Button _pauseButton;
        private Button _pausePanelButton;
        private Button _gameOverButton;
        private Button _startButton;

        private void Awake()
        {
            _scoreText = FindChildText(ScorePath);
            _timeText = FindChildText(TimePath);
            _resultText = FindChildText(ResultPath);

            var panel = transform.Find(PanelPath);
            _gameOverPanel = panel != null ? panel.gameObject : null;
            _gameOverButton = _gameOverPanel != null ? _gameOverPanel.GetComponent<Button>() : null;

            var pause = transform.Find(PausePath);
            _pausePanel = pause != null ? pause.gameObject : null;
            _pausePanelButton = _pausePanel != null ? _pausePanel.GetComponent<Button>() : null;

            var start = transform.Find(StartPath);
            _startPanel = start != null ? start.gameObject : null;
            _startButton = _startPanel != null ? _startPanel.GetComponent<Button>() : null;

            var pauseButton = transform.Find(PauseButtonPath);
            _pauseButton = pauseButton != null ? pauseButton.GetComponent<Button>() : null;

            if (_pauseButton != null)
            {
                _pauseButton.onClick.AddListener(() => PauseRequested?.Invoke());
            }
            if (_pausePanelButton != null)
            {
                _pausePanelButton.onClick.AddListener(() => ResumeRequested?.Invoke());
            }
            if (_startButton != null)
            {
                _startButton.onClick.AddListener(() => StartRequested?.Invoke());
            }
            if (_gameOverButton != null)
            {
                _gameOverButton.onClick.AddListener(() => PlayAgainRequested?.Invoke());
            }

            HideGameOver();
            HidePause();
            ShowStart();
        }

        public void SetScore(int score)
        {
            if (_scoreText != null)
            {
                _scoreText.text = $"Score: {score}";
            }
        }

        public void SetTime(float seconds)
        {
            if (_timeText != null)
            {
                _timeText.text = $"Time: {Mathf.Max(0f, seconds):0.0}";
            }
        }

        public void ShowGameOver(int finalScore)
        {
            if (_resultText != null)
            {
                _resultText.text = $"Final score: {finalScore}\nClick to play again";
            }
            if (_gameOverPanel != null)
            {
                _gameOverPanel.SetActive(true);
            }
        }

        public void HideGameOver()
        {
            if (_gameOverPanel != null)
            {
                _gameOverPanel.SetActive(false);
            }
        }

        public void ShowPause()
        {
            if (_pausePanel != null)
            {
                _pausePanel.SetActive(true);
            }
        }

        public void HidePause()
        {
            if (_pausePanel != null)
            {
                _pausePanel.SetActive(false);
            }
        }

        public void ShowStart()
        {
            if (_startPanel != null)
            {
                _startPanel.SetActive(true);
            }
        }

        public void HideStart()
        {
            if (_startPanel != null)
            {
                _startPanel.SetActive(false);
            }
        }

        private TextMeshProUGUI FindChildText(string path)
        {
            var child = transform.Find(path);
            if (child == null)
            {
                Debug.LogError($"[GameHud] Child '{path}' not found under {name}.", this);
                return null;
            }
            return child.GetComponent<TextMeshProUGUI>();
        }
    }
}
