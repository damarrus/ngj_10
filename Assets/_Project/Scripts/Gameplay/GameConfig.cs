using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// One place to tune the game's top-level settings, edited on the scene.
    /// Owns the startup flow: applies the initial volume and either shows the
    /// start screen or begins the level immediately.
    /// </summary>
    [DefaultExecutionOrder(-100)] // run before StartScreen / LevelController
    public class GameConfig : MonoBehaviour
    {
        private const string VolumeKey = "MasterVolume";

        [Header("Audio")]
        [Range(0f, 1f)]
        [SerializeField] private float _initialVolume = 1f;

        [Header("Flow")]
        [SerializeField] private bool _showStartScreen = true;

        [Header("Level")]
        [Tooltip("Level to load on start. Overrides the LevelBuilder's own Data.")]
        [SerializeField] private LevelData _startLevel;

        [Header("Refs")]
        [SerializeField] private StartScreen _startScreen;
        [SerializeField] private LevelController _controller;
        [SerializeField] private LevelBuilder _builder;

        public float InitialVolume => _initialVolume;

        private void Awake()
        {
            // PlayerPrefs (player's last choice) wins over the configured default.
            AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, _initialVolume);

            // Build the chosen level now, before anything starts it. GameConfig
            // runs at exec order -100, ahead of LevelBuilder's own Awake build —
            // so we set the data and build here, otherwise Begin() below (or the
            // start screen's Start) would respawn into a not-yet-spawned stream.
            if (_builder != null)
            {
                if (_startLevel != null)
                    _builder.SetData(_startLevel);
                _builder.BuildNow();
            }

            if (_showStartScreen && _startScreen != null)
            {
                _startScreen.Show();
            }
            else
            {
                if (_startScreen != null)
                    _startScreen.Hide();
                if (_controller != null)
                    _controller.Begin();
            }
        }
    }
}
