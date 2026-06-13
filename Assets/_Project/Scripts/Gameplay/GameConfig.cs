using Ngj10.Core;
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

        [Tooltip("Calm track on the title screen. Crossfades into the game track on start.")]
        [SerializeField] private AudioClip _menuMusic;

        [Tooltip("Energetic track during play. Crossfades back from the menu track when the level begins.")]
        [SerializeField] private AudioClip _gameMusic;

        [Header("Flow")]
        [SerializeField] private bool _showStartScreen = true;

        [Header("Burn (cone rays)")]
        [Tooltip("Seconds under a ray to go from cold to fully burnt (death).")]
        [SerializeField] private float _burnHeatUpTime = 1f;
        [Tooltip("Seconds in the open to cool fully back down.")]
        [SerializeField] private float _burnCoolDownTime = 2f;
        [Tooltip("Colour body sprites tint toward at full heat.")]
        [SerializeField] private Color _burntColor = new Color(0.85f, 0.12f, 0.05f);

        [Header("Shock (Zeus lightning)")]
        [Tooltip("Seconds the wings stay folded and blocked after a shock.")]
        [SerializeField] private float _wingBlockDuration = 1f;
        [Tooltip("Colour body sprites blink toward while shocked.")]
        [SerializeField] private Color _shockColor = new Color(1f, 0.92f, 0.15f);

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

            // Push the burn tuning onto the player's BurnState (the meter lives on
            // the Icarus instance; here is the one place these numbers are authored).
            var burn = FindAnyObjectByType<BurnState>();
            if (burn != null)
                burn.Configure(_burnHeatUpTime, _burnCoolDownTime, _burntColor);

            var shock = FindAnyObjectByType<ShockState>();
            if (shock != null)
                shock.Configure(_wingBlockDuration, _shockColor);

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

            // The level swaps to the game track itself when it begins, so it owns
            // the clip. The menu track plays here while the title screen is up.
            if (_controller != null)
                _controller.SetGameMusic(_gameMusic);

            if (_showStartScreen && _startScreen != null)
            {
                AudioManager.Instance.CrossfadeTo(_menuMusic);
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
