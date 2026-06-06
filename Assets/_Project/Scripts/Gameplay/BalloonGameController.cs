using Ngj10.Core;
using Ngj10.Core.Achievements;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Drives the example gameplay, all within the single Game scene: spawns
    /// balloons, keeps the score, counts down a fixed round, then shows a
    /// game-over overlay and waits for a click to restart — no scene reload.
    /// This is the throwaway example; replace the balloon bits with the real
    /// game and keep the round/HUD structure.
    /// </summary>
    public class BalloonGameController : MonoBehaviour
    {
        [Header("Round")]
        [SerializeField] private float _roundSeconds = 10f;

        [Header("Scoring")]
        [SerializeField] private int _goodPoints = 1;
        [SerializeField] private int _badPenalty = 2;

        [Header("Audio - SFX")]
        [SerializeField] private AudioClip _popGoodClip;
        [SerializeField] private AudioClip _popBadClip;

        [Header("Audio - Music")]
        [Tooltip("Calm track playing on the start screen and after game over.")]
        [SerializeField] private AudioClip _menuMusic;

        [Tooltip("Energetic track playing during a round.")]
        [SerializeField] private AudioClip _gameMusic;

        [Tooltip("Short victory fanfare played over the music when a round ends.")]
        [SerializeField] private AudioClip _victoryStinger;

        private BalloonSpawner _spawner;
        private GameHud _hud;
        private int _score;
        private float _timeLeft;
        private bool _playing;
        private bool _paused;

        private void Awake()
        {
            _spawner = GetComponent<BalloonSpawner>();
            _hud = FindAnyObjectByType<GameHud>();
        }

        private void Start()
        {
            // Sit on the "Click to start game" screen with the calm menu track
            // playing. The round only begins once the player clicks — that click
            // is also the WebGL user gesture that unblocks audio.
            AudioManager.Instance.CrossfadeTo(_menuMusic);
        }

        private void OnEnable()
        {
            if (_hud != null)
            {
                _hud.StartRequested += BeginPlay;
                _hud.PlayAgainRequested += BeginPlay;
                _hud.PauseRequested += TogglePause;
                _hud.ResumeRequested += Resume;
            }
        }

        private void OnDisable()
        {
            if (_hud != null)
            {
                _hud.StartRequested -= BeginPlay;
                _hud.PlayAgainRequested -= BeginPlay;
                _hud.PauseRequested -= TogglePause;
                _hud.ResumeRequested -= Resume;
            }
        }

        private void Update()
        {
            // Esc mirrors the on-screen buttons. All pointer-driven actions
            // (start / pause / resume / play-again) go entirely through the UI
            // buttons (GameHud) so a single click is handled by exactly one path
            // — no manual mouse polling here.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                TogglePause();
            }

            if (_paused)
            {
                return;
            }

            if (_playing)
            {
                TickRound();
            }
        }

        /// <summary>
        /// Begins a round from the start screen or the game-over screen: swaps the
        /// calm menu track for the energetic game track and kicks off play. Wired
        /// to the HUD's StartRequested / PlayAgainRequested events.
        /// </summary>
        private void BeginPlay()
        {
            if (_hud != null)
            {
                _hud.HideStart();
            }

            AudioManager.Instance.CrossfadeTo(_gameMusic);
            StartRound();
        }

        /// <summary>Toggled by the Pause button and the Esc key.</summary>
        public void TogglePause()
        {
            if (_paused)
            {
                Resume();
            }
            else if (_playing)
            {
                Pause();
            }
        }

        private void Pause()
        {
            if (!_playing || _paused)
            {
                return;
            }

            _paused = true;
            Time.timeScale = 0f;
            AudioManager.Instance.MuffleMusic();
            if (_hud != null)
            {
                _hud.ShowPause();
            }
        }

        /// <summary>Resumes play; invoked by the "Click to continue" panel and Esc.</summary>
        public void Resume()
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            Time.timeScale = 1f;
            AudioManager.Instance.UnmuffleMusic();
            if (_hud != null)
            {
                _hud.HidePause();
            }
        }

        private void StartRound()
        {
            _score = 0;
            _timeLeft = _roundSeconds;
            _playing = true;
            _paused = false;
            Time.timeScale = 1f;

            if (_hud != null)
            {
                _hud.HideGameOver();
                _hud.SetScore(_score);
                _hud.SetTime(_timeLeft);
            }

            _spawner.Begin(this);
        }

        private void TickRound()
        {
            _timeLeft -= Time.deltaTime;
            if (_hud != null)
            {
                _hud.SetTime(_timeLeft);
            }

            if (_timeLeft <= 0f)
            {
                EndRound();
            }
        }

        private void EndRound()
        {
            _playing = false;
            _spawner.Stop();
            DestroyRemainingBalloons();

            // Victory fanfare over the still-playing game track, then ease the
            // music back to the calm menu bed so the game-over screen sits under it.
            var audio = AudioManager.Instance;
            audio.PlayStinger(_victoryStinger);
            audio.CrossfadeTo(_menuMusic);

            if (_hud != null)
            {
                _hud.SetTime(0f);
                _hud.ShowGameOver(_score);
            }
        }

        /// <summary>Called by a balloon when the player clicks it.</summary>
        public void OnBalloonClicked(bool isBad)
        {
            _score += isBad ? -_badPenalty : _goodPoints;
            if (_hud != null)
            {
                _hud.SetScore(_score);
            }

            PlayPopSound(isBad);
            ReportAchievements(isBad);
        }

        // Pop SFX goes through the persistent AudioManager so the sound isn't cut
        // off when the clicked balloon is destroyed. Null-safe: silent if the
        // manager wasn't booted or no clip is assigned yet.
        private void PlayPopSound(bool isBad)
        {
            var audio = AudioManager.Instance;
            if (audio != null)
            {
                audio.PlaySfx(isBad ? _popBadClip : _popGoodClip);
            }
        }

        // Feed gameplay events into the reusable achievement engine. Null-safe so
        // the Game scene still runs if the manager wasn't booted (e.g. opened directly).
        private void ReportAchievements(bool isBad)
        {
            var ach = AchievementManager.Instance;
            if (ach == null)
            {
                return;
            }

            if (!isBad)
            {
                ach.Report("pop_first");
                ach.Report("pop_total_100");
            }

            ach.ReportMax("score_10", _score);
        }

        /// <summary>Clear balloons still on screen so the next round starts clean.</summary>
        private static void DestroyRemainingBalloons()
        {
            foreach (var balloon in FindObjectsByType<Balloon>(FindObjectsInactive.Exclude))
            {
                Destroy(balloon.gameObject);
            }
        }
    }
}
