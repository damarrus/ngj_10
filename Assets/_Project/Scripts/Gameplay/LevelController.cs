using System.Collections;
using Ngj10.Core;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Level flow: respawn into the start stream on falling below the kill
    /// line, win on touching the goal (sun).
    /// </summary>
    public class LevelController : MonoBehaviour
    {
        [SerializeField] private IcarusController _player;
        [SerializeField] private DeathSequence _deathSequence;
        [SerializeField] private ScreenFader _screenFader;
        [Tooltip("In-game control hint, revealed out of black on start/respawn and auto-hidden once flying.")]
        [SerializeField] private GameplayControlsHint _controlsHint;
        [Tooltip("ESC/R key hints — shown only in-game (off on the start screen). Starts inactive; Begin() shows it.")]
        [SerializeField] private GameObject _menuKeys;
        [SerializeField] private StreamPath _startStream;
        [SerializeField] private Transform _goal;
        [SerializeField] private float _goalRadius = 1.5f;
        [SerializeField] private float _killY = -2.5f;
        [SerializeField] private float _timeScale = 1.4f; // global game speed for this level
        [SerializeField] private GameObject _winPanel;
        [SerializeField] private bool _autoStart = true; // off when a StartScreen drives the start

        [Header("Camera / bounds")]
        [SerializeField] private Camera _camera;
        [SerializeField] private CameraFollow _cameraFollow;
        [SerializeField] private LevelMode _mode = LevelMode.Free;
        [Tooltip("Inset from the screen edge (world units) before a side/top kill triggers.")]
        [SerializeField] private float _edgeMargin = 0.3f;

        public float TimeScale => _timeScale;

        /// <summary>Raised when a run ends — on death (out of bounds / R restart) and
        /// on reaching the sun. The leaderboard reporter listens to snapshot the run's
        /// height/time/achievements; the level stays free of leaderboard knowledge.</summary>
        public event System.Action RunFinished;

        // Energetic track the level crossfades to on Begin(). Authored on GameConfig
        // (the one place game settings live) and pushed in at startup.
        private AudioClip _gameMusic;

        /// <summary>Set the track played while the level runs. Called by GameConfig at startup.</summary>
        public void SetGameMusic(AudioClip clip) => _gameMusic = clip;

        private void OnEnable() => Hazard.PlayerHit += Die;

        private void OnDisable() => Hazard.PlayerHit -= Die;

        /// <summary>
        /// Called by LevelBuilder after it spawns the level: overrides the
        /// inspector geometry with the level data and the spawned start stream.
        /// Runs before Start() since the builder spawns in Awake.
        /// </summary>
        public void Configure(LevelData data, StreamPath startStream)
        {
            _goalRadius = data.GoalRadius;
            _killY = data.KillY;
            _timeScale = data.TimeScale;
            _startStream = startStream;
            _spawnPoint = data.Start;
            _hasSpawnPoint = true;
            _mode = data.Mode;
            if (_camera != null)
                _camera.orthographicSize = data.CameraSize;
            if (_cameraFollow != null)
            {
                // Keep the bottom edge at or above the kill line: center >= killY + halfHeight.
                float minCenterY = data.KillY + data.CameraSize;
                _cameraFollow.SetMode(_mode, minCenterY, data.Start);
            }
            if (_mode == LevelMode.UpOnly)
                BuildSideWalls(data);
        }

        /// <summary>
        /// UpOnly: the screen sides are solid walls, not killers — the player just
        /// bumps into them. Two tall static colliders at the locked camera edges.
        /// </summary>
        private void BuildSideWalls(LevelData data)
        {
            float halfW = data.CameraSize * (_camera != null ? _camera.aspect : 16f / 9f);
            float centerX = _camera != null ? _camera.transform.position.x : data.Start.x;

            for (int side = -1; side <= 1; side += 2)
            {
                var wall = new GameObject(side < 0 ? "WallLeft" : "WallRight");
                wall.transform.SetParent(transform, false);
                wall.transform.position = new Vector3(centerX + side * (halfW + 0.5f), 0f, 0f);
                var col = wall.AddComponent<BoxCollider2D>();
                col.size = new Vector2(1f, 4000f);
            }
        }

        private void Start()
        {
            if (_autoStart)
                Begin();
        }

        /// <summary>Start the level: set the time scale and spawn the player. Called by the
        /// StartScreen on the Start button, or automatically from Start() when no start screen.</summary>
        /// <summary>
        /// Park the player at the spawn point with wings folded and physics idle,
        /// without starting the clock or the game music. Called by GameConfig when a
        /// start screen is up, so the hero waits at spawn instead of flying/streaming
        /// a trail behind the title. The level clock starts later in Begin().
        /// </summary>
        public void Park() => Respawn();

        public void Begin()
        {
            Time.timeScale = _timeScale;
            // Slow crossfade from the menu track to the energetic game track. A
            // no-op if it's already playing (e.g. restart after a win), so the
            // beat keeps going instead of restarting.
            AudioManager.Instance.CrossfadeTo(_gameMusic);
            Respawn();
            // Reveal the control hint as the screen lifts out of black; it auto-hides
            // once the player takes the first hold (see GameplayControlsHint).
            if (_controlsHint != null)
                _controlsHint.Reveal();
            // ESC/R key hints belong to the game, not the start screen — reveal them
            // only now that the level is running.
            if (_menuKeys != null)
                _menuKeys.SetActive(true);
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
        }

        private bool _won;
        private bool _dying;

        private void Update()
        {
            // ESC/R are live only once the level is running (Begin shows _menuKeys);
            // on the start screen they do nothing — same gate as the visible hints.
            bool inGame = _menuKeys == null || _menuKeys.activeSelf;

            // ESC returns to the menu (scene reload — GameConfig shows the start
            // screen again) from anywhere in the level.
            if (inGame && Input.GetKeyDown(KeyCode.Escape))
            {
                ReturnToMenu();
                return;
            }

            if (_won)
            {
                if (Input.anyKeyDown)
                    Restart();
                return;
            }

            // R restarts the run via the death path (shrink + fade + respawn), so
            // the retry plays the same animation as dying.
            if (inGame && Input.GetKeyDown(KeyCode.R))
            {
                RestartRun();
                return;
            }

            // No kill checks while dying (the fade/respawn routine owns the
            // player) or while parked at spawn — a Start placed in a bad spot
            // must not melt into an endless respawn loop.
            if (_dying || _player.IsWaitingForInput)
                return;

            Vector2 pos = _player.transform.position;
            if (IsOutOfBounds(pos))
            {
                Die();
                return;
            }
            if (_goal != null && Vector2.Distance(pos, _goal.position) < _goalRadius)
                Win();
        }

        /// <summary>Return to the start screen (scene reload). Driven by the ESC key
        /// and the ESC button — one path for both.</summary>
        public void ReturnToMenu() => Core.SceneLoader.ReloadCurrent();

        /// <summary>Restart the run via the death path (shrink + fade + respawn).
        /// Driven by the R key and the R button. No-op while already dying.</summary>
        public void RestartRun()
        {
            if (_dying || _won)
                return;
            Core.Achievements.AchievementManager.Instance.Unlock("restart_press"); // Single
            Die();
        }

        private void Win()
        {
            _won = true;
            Time.timeScale = 0f;
            Core.Achievements.AchievementManager.Instance.Unlock("reach_sun");
            RunFinished?.Invoke();
            if (_winPanel != null)
                _winPanel.SetActive(true);
        }

        private void Restart()
        {
            _won = false;
            Time.timeScale = _timeScale;
            if (_winPanel != null)
                _winPanel.SetActive(false);
            Respawn();
        }

        /// <summary>
        /// Bottom kill line is deadly in every mode. SingleScreen also kills on
        /// the side/top edges. UpOnly side edges are physical walls (see
        /// BuildSideWalls), not killers.
        /// </summary>
        private bool IsOutOfBounds(Vector2 pos)
        {
            if (pos.y < _killY)
                return true;

            if (_mode != LevelMode.SingleScreen || _camera == null)
                return false;

            Vector2 center = _camera.transform.position;
            float halfH = _camera.orthographicSize;
            float halfW = halfH * _camera.aspect;

            if (pos.x < center.x - halfW + _edgeMargin || pos.x > center.x + halfW - _edgeMargin)
                return true;
            if (pos.y > center.y + halfH - _edgeMargin)
                return true;

            return false;
        }

        /// <summary>Player died: fade the screen to black while the hero shrinks
        /// away, respawn behind the black screen, then fade back in.</summary>
        private void Die()
        {
            if (_dying)
                return;
            _dying = true;
            var ach = Core.Achievements.AchievementManager.Instance;
            ach.Report("first_death"); // Single: first death of any kind
            ach.Report("death_10");    // Counter: total deaths across runs
            // Snapshot the run for the leaderboard before the respawn routine zeroes
            // RunStats — listeners read height/time/achievements off the just-ended run.
            RunFinished?.Invoke();
            StartCoroutine(DieRoutine());
        }

        private IEnumerator DieRoutine()
        {
            // Fade the whole screen to black and shrink the hero at the same time.
            Coroutine shrink = _deathSequence != null
                ? StartCoroutine(_deathSequence.Shrink())
                : null;
            if (_screenFader != null)
                yield return _screenFader.FadeOut();
            if (shrink != null)
                yield return shrink;

            // On full black: re-enable physics first (so the Rigidbody2D accepts
            // the new position), then move the hero to the start with wings open.
            if (_deathSequence != null)
                _deathSequence.Restore();
            Respawn();

            // Out of black again at the start stream — show the hint as on level start.
            if (_controlsHint != null)
                _controlsHint.Reveal();

            if (_screenFader != null)
                yield return _screenFader.FadeIn();

            _dying = false;
        }

        // Spawn point from LevelData.Start; falls back to the start stream's first
        // waypoint when the level is wired in the scene without a LevelBuilder.
        private Vector2 _spawnPoint;
        private bool _hasSpawnPoint;

        private void Respawn()
        {
            _player.ResetAt(_hasSpawnPoint
                ? _spawnPoint
                : _startStream.SampleAtDistance(0f).Point);
        }
    }
}
