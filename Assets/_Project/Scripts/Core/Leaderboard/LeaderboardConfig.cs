using UnityEngine;

namespace Ngj10.Core.Leaderboard
{
    /// <summary>
    /// Supabase connection settings for the leaderboard. Lives as a single asset
    /// under a Resources folder so <see cref="LeaderboardClient"/> can load it
    /// without inspector wiring. The anon key is public by design (it's gated by
    /// row-level security on the server), so shipping it in the WebGL build is fine.
    /// </summary>
    [CreateAssetMenu(menuName = "Ngj10/Leaderboard Config", fileName = "LeaderboardConfig")]
    public class LeaderboardConfig : ScriptableObject
    {
        [Tooltip("Supabase Project URL, e.g. https://abcd.supabase.co")]
        public string ProjectUrl;

        [Tooltip("Supabase anon public API key")]
        public string AnonKey;

        [Tooltip("Table name holding the scores.")]
        public string Table = "scores";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ProjectUrl) && !string.IsNullOrWhiteSpace(AnonKey);
    }
}
