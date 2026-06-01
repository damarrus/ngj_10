using Ngj10.Core.Achievements;
using UnityEngine;

namespace Ngj10.Core
{
    /// <summary>
    /// Lives in the Boot scene. Ensures a <see cref="GameManager"/> exists, then
    /// hands off to the Game scene. Boot stays tiny so it loads instantly.
    /// </summary>
    public class BootStarter : MonoBehaviour
    {
        [Tooltip("Seconds to wait on Boot before loading the Game scene.")]
        [SerializeField] private float _bootDelay = 0.5f;

        [Tooltip("If true, jump straight to Game. If false, stay on Boot/menu.")]
        [SerializeField] private bool _autoStart = true;

        private void Start()
        {
            if (GameManager.Instance == null)
            {
                var go = new GameObject(nameof(GameManager));
                go.AddComponent<GameManager>();
            }

            EnsureAchievements();

            if (_autoStart)
            {
                Invoke(nameof(GoToGame), _bootDelay);
            }
        }

        private void GoToGame()
        {
            GameManager.Instance.StartGame();
        }

        // Spawn the persistent achievement stack in code (engine + toast + list
        // UI), same pattern as GameManager. Avoids fragile scene wiring; the
        // components build their own UI and DontDestroyOnLoad keeps them alive.
        private static void EnsureAchievements()
        {
            if (AchievementManager.Instance != null)
            {
                return;
            }

            var go = new GameObject("Achievements");
            DontDestroyOnLoad(go);
            go.AddComponent<AchievementManager>();
            go.AddComponent<AchievementToast>();
            go.AddComponent<AchievementListUI>();
        }
    }
}
