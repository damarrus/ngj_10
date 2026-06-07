using TMPro;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// One leaderboard row: rank, name, score. Built as a scene/prefab object so
    /// the layout is tunable in the editor; <see cref="LeaderboardView"/> spawns
    /// one per entry and calls <see cref="Set"/>.
    /// </summary>
    public class LeaderboardRow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _rankText;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _scoreText;

        public void Set(int rank, string playerName, int score)
        {
            if (_rankText != null)
            {
                _rankText.text = $"{rank}.";
            }
            if (_nameText != null)
            {
                _nameText.text = playerName;
            }
            if (_scoreText != null)
            {
                _scoreText.text = score.ToString();
            }
        }
    }
}
