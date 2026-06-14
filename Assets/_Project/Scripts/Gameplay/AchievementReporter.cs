using Ngj10.Core.Achievements;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Bridges Icarus gameplay to the reusable <see cref="AchievementManager"/>: the
    /// controller and the engine stay free of each other's concerns, this is the one
    /// place that knows both. Lives on the Icarus object beside
    /// <see cref="RunStats"/>.
    ///
    /// Covers the live, stats-driven achievements (distance, height, speed).
    /// Event-shaped ones fire from their own site instead, where the cause is known:
    /// death/restart in LevelController, shock in ShockState, the sun in
    /// LevelController.Win.
    ///
    /// Ids are kept in sync with the definition assets created by the editor tool
    /// (Ngj10 ▸ Achievements ▸ Create Example Assets).
    /// </summary>
    [RequireComponent(typeof(RunStats))]
    public class AchievementReporter : MonoBehaviour
    {
        // Ids — must match the AchievementDefinition assets under Resources.
        private const string Walk100 = "walk_100";
        private const string Walk1000 = "walk_1000";
        private const string Height100 = "height_100";
        private const string Height200 = "height_200";
        private const string MaxSpeedId = "max_speed";

        [Tooltip("Speed (m/s) that counts as 'top speed' for the max-speed achievement. " +
                 "Match Icarus' open-wing speed cap.")]
        [SerializeField] private float _topSpeed = 12f;

        private RunStats _stats;

        private void Awake()
        {
            _stats = GetComponent<RunStats>();
        }

        private void OnEnable()
        {
            _stats.Updated += OnStatsUpdated;
        }

        private void OnDisable()
        {
            _stats.Updated -= OnStatsUpdated;
        }

        // SingleGameMax tracks the best run; reporting live lets the toast pop the
        // moment a threshold is crossed mid-flight. Already-unlocked ids are no-ops.
        private void OnStatsUpdated(float pathMeters, float maxHeightMeters)
        {
            var mgr = AchievementManager.Instance;

            int path = Mathf.FloorToInt(pathMeters);
            mgr.ReportMax(Walk100, path);
            mgr.ReportMax(Walk1000, path);

            int height = Mathf.FloorToInt(maxHeightMeters);
            mgr.ReportMax(Height100, height);
            mgr.ReportMax(Height200, height);

            if (_stats.MaxSpeed >= _topSpeed)
                mgr.Unlock(MaxSpeedId); // Single
        }
    }
}
