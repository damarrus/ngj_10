using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ngj10.Core
{
    /// <summary>
    /// Thin wrapper over <see cref="SceneManager"/> for loading scenes by name.
    /// Use the helpers so scene transitions stay in one place.
    /// </summary>
    public static class SceneLoader
    {
        public static void Load(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }

        public static void ReloadCurrent()
        {
            Load(SceneManager.GetActiveScene().name);
        }

        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
