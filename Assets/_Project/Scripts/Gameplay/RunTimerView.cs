using TMPro;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// In-game run timer shown at the top centre — but only for players who have
    /// already beaten the level at least once (the "reach_sun" achievement). First-time
    /// players see a clean screen; once they've won, every later run shows the clock so
    /// they can chase a faster time. Reads the live <see cref="RunStats.RunMs"/>, which
    /// starts on the first flap, resets on death/restart, and freezes on the win.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class RunTimerView : MonoBehaviour
    {
        [Tooltip("Run timer source. Auto-found if left empty.")]
        [SerializeField] private RunStats _stats;
        [Tooltip("Level controller — the timer hides while on the start screen (not live).")]
        [SerializeField] private LevelController _level;
        [Tooltip("Achievement that gates visibility — the label shows only once this is unlocked.")]
        [SerializeField] private string _gateAchievementId = "reach_sun";

        private TextMeshProUGUI _label;
        private bool _unlocked; // player has beaten the level before — the gate for the clock

        private void Awake()
        {
            _label = GetComponent<TextMeshProUGUI>();
            if (_stats == null)
                _stats = FindAnyObjectByType<RunStats>();
            if (_level == null)
                _level = FindAnyObjectByType<LevelController>();
        }

        private void Start()
        {
            // Snapshot the gate at level start: a player who has won before sees the
            // clock; this run's win unlocking the achievement must not flip it on mid-run.
            _unlocked = Core.Achievements.AchievementManager.Instance.IsUnlocked(_gateAchievementId);
        }

        private void Update()
        {
            // Show only for returning winners AND only in-game — hidden on the start screen.
            bool show = _unlocked && _stats != null && (_level == null || _level.IsLive);
            _label.enabled = show;
            if (show)
                _label.text = RunStats.FormatMs(_stats.RunMs);
        }
    }
}
