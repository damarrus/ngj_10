using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// In-game HUD for the single Game scene: live score + time, plus a game-over
    /// overlay with the final score and a restart hint. Finds its children by
    /// name in Awake rather than via inspector references — the MCP bridge can't
    /// wire references reliably, and finding by name keeps the scene robust
    /// across reloads. Expects under it: "ScoreText", "TimeText",
    /// "GameOverPanel", "GameOverPanel/ResultText".
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        private const string ScorePath = "ScoreText";
        private const string TimePath = "TimeText";
        private const string PanelPath = "GameOverPanel";
        private const string ResultPath = "GameOverPanel/ResultText";

        private Text _scoreText;
        private Text _timeText;
        private Text _resultText;
        private GameObject _gameOverPanel;

        private void Awake()
        {
            _scoreText = FindChildText(ScorePath);
            _timeText = FindChildText(TimePath);
            _resultText = FindChildText(ResultPath);

            var panel = transform.Find(PanelPath);
            _gameOverPanel = panel != null ? panel.gameObject : null;

            HideGameOver();
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

        private Text FindChildText(string path)
        {
            var child = transform.Find(path);
            if (child == null)
            {
                Debug.LogError($"[GameHud] Child '{path}' not found under {name}.", this);
                return null;
            }
            return child.GetComponent<Text>();
        }
    }
}
