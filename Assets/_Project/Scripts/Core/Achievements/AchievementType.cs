namespace Ngj10.Core.Achievements
{
    /// <summary>
    /// How an achievement's progress accumulates and when it unlocks.
    /// </summary>
    public enum AchievementType
    {
        /// <summary>Fires the first time it is reported. Target is ignored.</summary>
        Single,

        /// <summary>Sums reported amounts across all sessions; unlocks at Target.</summary>
        Counter,

        /// <summary>Tracks the best single value ever reported; unlocks when it reaches Target.</summary>
        SingleGameMax,
    }
}
