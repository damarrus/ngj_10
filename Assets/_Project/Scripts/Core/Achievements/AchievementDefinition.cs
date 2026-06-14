using UnityEngine;

namespace Ngj10.Core.Achievements
{
    /// <summary>
    /// Static design data for one achievement: identity, display text and the
    /// rule that unlocks it. Live progress lives in PlayerPrefs (see
    /// <see cref="AchievementManager"/>), never on the asset.
    ///
    /// Reusable across jam games: drop these assets anywhere under a "Resources"
    /// folder and the manager auto-loads them — no scene wiring needed.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Achievement",
        menuName = "Ngj10/Achievement",
        order = 0)]
    public class AchievementDefinition : ScriptableObject
    {
        [Tooltip("Uncheck to retire this achievement: the manager ignores it on load " +
                 "(never tracked, never shown, never unlockable) without deleting the asset.")]
        [SerializeField] private bool _enabled = true;

        [Tooltip("Stable unique key used by code and save data. Don't rename after release.")]
        [SerializeField] private string _id;

        [SerializeField] private string _title;

        [TextArea]
        [SerializeField] private string _description;

        [SerializeField] private AchievementType _type = AchievementType.Single;

        [Tooltip("Threshold for Counter / SingleGameMax. Ignored for Single.")]
        [SerializeField] private int _target = 1;

        public bool Enabled => _enabled;
        public string Id => _id;
        public string Title => _title;
        public string Description => _description;
        public AchievementType Type => _type;
        public int Target => Mathf.Max(1, _target);
    }
}
