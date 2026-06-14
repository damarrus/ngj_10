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
        [Tooltip("CanvasGroup on the win panel, faded 0->1 so the panel eases out of the black.")]
        [SerializeField] private CanvasGroup _winPanelGroup;
        [Tooltip("Completion-time label on the win panel — \"Ваше время: mm:ss:mmm\".")]
        [SerializeField] private TMPro.TextMeshProUGUI _winTimeText;
        [Tooltip("Leaderboard-rank label on the win panel — \"Вы заняли N место в лидерборде\".")]
        [SerializeField] private TMPro.TextMeshProUGUI _winRankText;
        [Tooltip("Run timer/stats source. Auto-found if left empty.")]
        [SerializeField] private RunStats _stats;
        [Tooltip("Leaderboard bridge — submits the run on finish. Auto-found if left empty.")]
        [SerializeField] private LeaderboardReporter _leaderboard;
        [Tooltip("Victory flash: the screen fills with this colour (a sun-flash) over the duration below.")]
        [SerializeField] private Color _winFlashColor = new Color(1f, 0.92f, 0.35f, 1f);
        [SerializeField] private float _winFlashDuration = 3f;
        [Tooltip("After the flash, the screen eases from the flash colour to black over this time.")]
        [SerializeField] private float _winToBlackDuration = 1.5f;
        [Tooltip("The win panel then fades in out of the black over this time.")]
        [SerializeField] private float _winPanelFadeDuration = 1f;
        [SerializeField] private bool _autoStart = true; // off when a StartScreen drives the start

        [Header("Camera / bounds")]
        [SerializeField] private Camera _camera;
        [SerializeField] private CameraFollow _cameraFollow;
        [SerializeField] private LevelMode _mode = LevelMode.Free;
        [Tooltip("Inset from the screen edge (world units) before a side/top kill triggers.")]
        [SerializeField] private float _edgeMargin = 0.3f;

        public float TimeScale => _timeScale;

        /// <summary>True once the level has gone live (past the start screen). The in-game
        /// HUD (e.g. the run timer) uses this to stay hidden on the menu.</summary>
        public bool IsLive => _menuKeys == null || _menuKeys.activeSelf;

        /// <summary>Raised when a run ends — on death (out of bounds / R restart) and
        /// on reaching the sun. The leaderboard reporter listens to snapshot the run's
        /// height/time/achievements; the level stays free of leaderboard knowledge.</summary>
        public event System.Action RunFinished;

        // Energetic track the level crossfades to on Begin(). Authored on GameConfig
        // (the one place game settings live) and pushed in at startup.
        private AudioClip _gameMusic;

        /// <summary>Set the track played while the level runs. Called by GameConfig at startup.</summary>
        public void SetGameMusic(AudioClip clip) => _gameMusic = clip;

        private void Awake()
        {
            if (_stats == null)
                _stats = FindAnyObjectByType<RunStats>();
            if (_leaderboard == null)
                _leaderboard = FindAnyObjectByType<LeaderboardReporter>();
        }

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
                // No bottom clamp: the camera follows Icarus all the way down so the kill
                // line below stays off-screen, not pinned above it.
                _cameraFollow.SetMode(_mode, float.MinValue, data.Start);
            if (_mode == LevelMode.UpOnly)
                BuildSideWalls(data);
        }

        /// <summary>
        /// UpOnly: the screen sides are solid walls, not killers — the player just
        /// bumps into them. Two tall static colliders at the locked camera edges.
        /// </summary>
        private void BuildSideWalls(LevelData data)
        {
            // Anchor on Start.x (the authored corridor centre) so the runtime walls line
            // up exactly with what the Map Editor draws — not on the camera object, whose
            // X may not have been moved yet. Aspect 16/9 to match the editor preview.
            float halfW = data.CameraSize * (16f / 9f);
            float centerX = data.Start.x;

            // Thick walls so a fast player can't tunnel through; inner face sits at halfW.
            const float thickness = 10f;
            for (int side = -1; side <= 1; side += 2)
            {
                var wall = new GameObject(side < 0 ? "WallLeft" : "WallRight");
                wall.transform.SetParent(transform, false);
                wall.transform.position = new Vector3(centerX + side * (halfW + thickness * 0.5f), 0f, 0f);
                var col = wall.AddComponent<BoxCollider2D>();
                col.size = new Vector2(thickness, 4000f);
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

        /// <summary>Reframe the camera for the title screen (zoom + offset, Icarus posed
        /// for the art) while the menu is up. Called by GameConfig alongside Park() with
        /// the menu-camera tuning. Begin() leaves it.</summary>
        public void EnterMenuFraming(float orthoSize, float offsetX, float offsetY)
        {
            if (_cameraFollow != null)
                _cameraFollow.EnterMenu(_spawnPoint, orthoSize, offsetX, offsetY);
            SetMenuPose(true);
            // Hidden behind the title (fade in on Begin) unless GameConfig keeps them visible.
            SetStreamsVisibility(_hideStreamsInMenu ? 0f : 1f);
            // Kept visible under the menu → animate on unscaled time so they flow while
            // the level is frozen (timeScale 0).
            SetStreamsUnscaled(!_hideStreamsInMenu);
        }

        // The flow visuals are spawned by the LevelBuilder (no direct ref). Grab them
        // once and drive their master alpha so the menu can hide the streams and the
        // start transition can fade them in with the rest of the level.
        private StreamFlowVisual[] _streamVisuals;

        // Global "show flows" switch from GameConfig. When off, the streams stay
        // invisible regardless of the menu/transition fade — every visibility write
        // is forced to 0.
        private bool _streamsEnabled = true;

        // GameConfig "Hide Flows In Menu". When true (default) the streams are hidden
        // behind the title and fade in on START; when false they stay visible under
        // the menu and the start transition skips the fade.
        private bool _hideStreamsInMenu = true;

        /// <summary>Toggle the flow visuals globally (GameConfig "Show Flows"). Off forces
        /// the streams hidden for the whole run, ignoring the menu/transition fade.</summary>
        public void SetStreamsEnabled(bool enabled)
        {
            _streamsEnabled = enabled;
            SetStreamsVisibility(enabled ? 1f : 0f);
        }

        /// <summary>Whether the flow visuals hide behind the title screen (GameConfig).
        /// Off keeps them visible under the menu.</summary>
        public void SetHideStreamsInMenu(bool hide) => _hideStreamsInMenu = hide;

        private void SetStreamsVisibility(float v)
        {
            if (!_streamsEnabled)
                v = 0f;
            _streamVisuals ??= FindObjectsByType<StreamFlowVisual>(FindObjectsSortMode.None);
            for (int i = 0; i < _streamVisuals.Length; i++)
                if (_streamVisuals[i] != null)
                    _streamVisuals[i].SetVisibility(v);
        }

        // Drive the flow animation on unscaled time so it keeps moving while the title
        // screen freezes the level (timeScale 0); back to scaled time once live.
        private void SetStreamsUnscaled(bool on)
        {
            _streamVisuals ??= FindObjectsByType<StreamFlowVisual>(FindObjectsSortMode.None);
            for (int i = 0; i < _streamVisuals.Length; i++)
                if (_streamVisuals[i] != null)
                    _streamVisuals[i].SetUseUnscaledTime(on);
        }

        private void SetMenuPose(bool on)
        {
            if (_player != null)
            {
                var wings = _player.GetComponentInChildren<WingsVisual>(true);
                if (wings != null)
                    wings.SetMenuPose(on);
            }
        }

        /// <summary>Start the level immediately — snap out of the menu framing and go live.
        /// Used when no start screen drives the start (auto-start), or as the tail of the
        /// animated <see cref="BeginWithTransition"/>.</summary>
        public void Begin()
        {
            if (_cameraFollow != null)
                _cameraFollow.ExitMenu();
            GoLive();
        }

        /// <summary>Animated start from the title screen: glide the camera from the menu
        /// framing back to gameplay over <paramref name="cameraDuration"/> seconds (the
        /// level stays frozen meanwhile), then go live and hand control back. The music
        /// crossfade starts up front so it eases in under the glide.</summary>
        public IEnumerator BeginWithTransition(float cameraDuration)
        {
            // Start the menu→game music crossfade now so it rides the whole glide.
            AudioManager.Instance.CrossfadeTo(_gameMusic);

            // Streams appear out of the fade alongside the camera glide — only when they
            // were hidden in the menu; otherwise they're already visible, no fade needed.
            if (_hideStreamsInMenu)
                StartCoroutine(FadeInStreams(cameraDuration));

            if (_cameraFollow != null)
                yield return _cameraFollow.AnimateExitMenu(cameraDuration);

            GoLive();
        }

        // Ease the stream visuals from hidden to full over the transition. Unscaled —
        // the level is frozen (timeScale 0) until GoLive.
        private IEnumerator FadeInStreams(float duration)
        {
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                SetStreamsVisibility(t / duration);
                yield return null;
            }
            SetStreamsVisibility(1f);
        }

        // The level goes live: gameplay pose, normal time, player at spawn waiting for
        // the first hold, and the in-game hints fading in. Shared by the instant and the
        // animated start. The music crossfade is idempotent — a no-op if already playing.
        private void GoLive()
        {
            SetMenuPose(false);
            SetStreamsUnscaled(false); // back to scaled time — flows freeze with the level now
            Time.timeScale = _timeScale;
            AudioManager.Instance.CrossfadeTo(_gameMusic);
            Respawn();
            // Don't let the START click/Space (still held as we go live) count as the first
            // flap — Icarus would launch on his own. Flight waits for a fresh press.
            if (_player != null)
                _player.IgnoreCurrentHold();
            // Reveal the control hint as the level goes live; it auto-hides once the
            // player takes the first hold (see GameplayControlsHint).
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

        private bool _won;     // win panel up, any key restarts
        private bool _winning; // victory flash playing — kills off, no restart yet
        private bool _dying;

        private void Update()
        {
            // ESC/R are live only once the level is running (Begin shows _menuKeys);
            // on the start screen they do nothing — same gate as the visible hints.
            bool inGame = _menuKeys == null || _menuKeys.activeSelf;

            // ESC returns to the menu (scene reload — GameConfig shows the start
            // screen again) from anywhere in the level.
            if (inGame && Input.GetKeyDown(KeyCode.Q))
            {
                ReturnToMenu();
                return;
            }

            if (_won)
            {
                // Any key / click on the win screen returns to the main menu (scene reload).
                if (Input.anyKeyDown)
                    ReturnToMenu();
                return;
            }

            // R restarts the run via the death path (shrink + fade + respawn), so
            // the retry plays the same animation as dying.
            if (inGame && Input.GetKeyDown(KeyCode.R))
            {
                RestartRun();
                return;
            }

#if UNITY_EDITOR
            // H force-triggers the win for testing (as if the sun was reached).
            // Editor-only — not exposed in builds.
            if (inGame && Input.GetKeyDown(KeyCode.H))
            {
                Win();
                return;
            }
#endif

            // No kill checks while dying (the fade/respawn routine owns the
            // player), while the victory flash plays, or while parked at spawn —
            // a Start placed in a bad spot must not melt into an endless respawn loop.
            if (_dying || _winning || _player.IsWaitingForInput)
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

        /// <summary>Return to the start screen (scene reload). Driven by the ESC key, the
        /// ESC button, and "Продолжить" on the win screen. Restores the time scale first —
        /// the win freezes it to 0, and a reload alone wouldn't unfreeze the new scene.</summary>
        public void ReturnToMenu()
        {
            Time.timeScale = 1f;
            Core.SceneLoader.ReloadCurrent();
        }

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
            if (_winning || _won)
                return;
            _winning = true;
            // Stop the run timer the instant the sun is touched — before the flash and
            // before timeScale 0 — so the panel shows the exact completion time.
            if (_stats != null)
                _stats.Stop();
            // Freeze the hero where he is (hangs in the air) — timeScale 0 stops
            // physics; the flash fade runs on unscaled time below.
            Time.timeScale = 0f;
            Core.Achievements.AchievementManager.Instance.Unlock("reach_sun");
            RunFinished?.Invoke();
            StartCoroutine(WinRoutine());
        }

        /// <summary>Sun-touch victory: the hero hangs frozen while the screen fills with a
        /// yellow flash (the burst on contact with the sun), eases from that flash to black,
        /// then the win panel fades up out of the black.</summary>
        private IEnumerator WinRoutine()
        {
            // Fade the music out alongside the flash so the win screen is silent.
            AudioManager.Instance.CrossfadeTo(null, _winFlashDuration);

            // Show the completion time on the panel (frozen above at the sun-touch).
            if (_winTimeText != null && _stats != null)
                _winTimeText.text = $"Ваше время: {RunStats.FormatMs(_stats.RunMs)}";

            // Submit this run, THEN fetch the fresh board for the rank — both run through
            // the flash so the panel has the result by the time it fades in. Submitting
            // first means the board reflects this very run when we read the placement.
            SubmitThenShowRank();

            if (_screenFader != null)
            {
                yield return _screenFader.FadeToColor(_winFlashColor, _winFlashDuration);
                yield return _screenFader.FadeToColor(Color.black, _winToBlackDuration);
            }

            // Panel starts hidden (alpha 0); enable it on the black, then ease it in.
            if (_winPanelGroup != null)
                _winPanelGroup.alpha = 0f;
            if (_winPanel != null)
                _winPanel.SetActive(true);
            if (_winPanelGroup != null)
                yield return FadeCanvasGroup(_winPanelGroup, 0f, 1f, _winPanelFadeDuration);

            _winning = false;
            _won = true;
        }

        // How deep to fetch when working out the player's leaderboard placement.
        private const int RankFetchLimit = 1000;

        /// <summary>Submit this run (if it's a new record), then fetch the fresh board and
        /// show the player's placement. Offline / submit failure / not on the board / no
        /// reporter → the rank line is hidden (no leaderboard, no claim about a place).</summary>
        private void SubmitThenShowRank()
        {
            if (_winRankText == null)
                return;

            // No board configured at all → no rank line.
            if (!Core.Leaderboard.LeaderboardClient.Instance.IsAvailable || _leaderboard == null)
            {
                _winRankText.gameObject.SetActive(false);
                return;
            }

            // Submit first; only once the row is up to date do we read the placement.
            _leaderboard.SubmitRunIfRecord(submitOk =>
            {
                if (!submitOk)
                {
                    _winRankText.gameObject.SetActive(false);
                    return;
                }
                FetchAndShowRank();
            });
        }

        private void FetchAndShowRank()
        {
            string uid = Core.Leaderboard.PlayerIdentity.Uid;
            Core.Leaderboard.LeaderboardClient.Instance.FetchTop(RankFetchLimit,
                entries =>
                {
                    int rank = entries.FindIndex(e => e.uid == uid) + 1; // 0 -> not found
                    if (rank <= 0)
                        _winRankText.gameObject.SetActive(false);
                    else
                        _winRankText.text = $"Вы заняли {rank} место в лидерборде";
                },
                () => _winRankText.gameObject.SetActive(false));
        }

        private static IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
        {
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                cg.alpha = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }
            cg.alpha = to;
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
            // RunStats — read height/time/achievements off the just-ended run. Fire-and-
            // forget on death (unlike the win, nothing waits on the result here).
            if (_leaderboard != null)
                _leaderboard.SubmitRunIfRecord();
            RunFinished?.Invoke();
            // Zero the run timer now so the HUD reads 00:00:000 through the death fade
            // instead of ticking on while the player drifts out (height/time already read
            // above for the submit).
            if (_stats != null)
                _stats.ResetTimer();
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
