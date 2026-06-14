using Ngj10.Core.Achievements;
using Ngj10.Core.Leaderboard;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Bridges an Icarus run to the leaderboard backend, the same way
    /// <see cref="AchievementReporter"/> bridges to the achievement engine: the level
    /// and the leaderboard client stay free of each other, this is the one place that
    /// knows both. On every run end (death or reaching the sun) it snapshots the run's
    /// peak height, the time taken to reach it, and the unlocked-achievement count,
    /// folds them into the player's personal best, and upserts that best by uid.
    ///
    /// The "what counts as a new record" rules live here on the client (per the
    /// leaderboard design — no server trigger):
    ///   • Higher peak height        → record the new height AND its time (even if the
    ///                                  time is worse than the old best's time).
    ///   • Same height, faster climb → keep the height, record the faster time.
    ///   • More achievements unlocked → record the new count.
    /// The personal best is held in PlayerPrefs (WebGL-safe → IndexedDB) so the upsert,
    /// which overwrites the whole row, always sends a complete and monotonic best.
    /// </summary>
    public class LeaderboardReporter : MonoBehaviour
    {
        private const string BestHeightKey = "lb.best.height";
        private const string BestTimeKey = "lb.best.time";
        private const string BestAchKey = "lb.best.ach";

        [Tooltip("Raises RunFinished on death and on reaching the sun.")]
        [SerializeField] private LevelController _level;
        [Tooltip("Source of the run's peak height and time-to-peak. Lives on the Icarus object.")]
        [SerializeField] private RunStats _stats;

        private void Awake()
        {
            if (_level == null)
                _level = FindAnyObjectByType<LevelController>();
            if (_stats == null)
                _stats = FindAnyObjectByType<RunStats>();
        }

        private void OnEnable()
        {
            if (_level != null)
                _level.RunFinished += OnRunFinished;
        }

        private void OnDisable()
        {
            if (_level != null)
                _level.RunFinished -= OnRunFinished;
        }

        private void OnRunFinished()
        {
            if (_stats == null)
                return;

            // Round height UP: most players reach the level top and tie there, which
            // is by design — the contest then moves to the climb time.
            int runHeight = Mathf.CeilToInt(_stats.MaxHeightMeters);
            int runTime = _stats.TimeToMaxMs;
            int achievements = AchievementManager.Instance.UnlockedCount;

            int bestHeight = PlayerPrefs.GetInt(BestHeightKey, int.MinValue);
            int bestTime = PlayerPrefs.GetInt(BestTimeKey, 0);
            int bestAch = PlayerPrefs.GetInt(BestAchKey, 0);

            bool changed = false;

            if (runHeight > bestHeight)
            {
                // New peak: take the height and its time wholesale, even if slower.
                bestHeight = runHeight;
                bestTime = runTime;
                changed = true;
            }
            else if (runHeight == bestHeight && bestHeight != int.MinValue && runTime < bestTime)
            {
                // Same peak reached faster: improve only the time.
                bestTime = runTime;
                changed = true;
            }

            if (achievements > bestAch)
            {
                bestAch = achievements;
                changed = true;
            }

            if (!changed)
                return;

            PlayerPrefs.SetInt(BestHeightKey, bestHeight);
            PlayerPrefs.SetInt(BestTimeKey, bestTime);
            PlayerPrefs.SetInt(BestAchKey, bestAch);
            PlayerPrefs.Save();

            LeaderboardClient.Instance.SubmitRun(bestHeight, bestTime, bestAch);
        }
    }
}
