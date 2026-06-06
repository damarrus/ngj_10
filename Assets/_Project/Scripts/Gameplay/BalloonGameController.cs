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

        [Header("Audio")]
        [SerializeField] private AudioClip _popGoodClip;
        [SerializeField] private AudioClip _popBadClip;

        private BalloonSpawner _spawner;
        private GameHud _hud;
        private int _score;
        private float _timeLeft;
        private bool _playing;

        private void Awake()
        {
            _spawner = GetComponent<BalloonSpawner>();
            _hud = FindAnyObjectByType<GameHud>();
        }

        private void Start()
        {
            StartRound();
        }

        private void Update()
        {
            if (_playing)
            {
                TickRound();
            }
            else if (Input.GetMouseButtonDown(0))
            {
                StartRound();
            }
        }

        private void StartRound()
        {
            _score = 0;
            _timeLeft = _roundSeconds;
            _playing = true;

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
            foreach (var balloon in FindObjectsByType<Balloon>(FindObjectsSortMode.None))
            {
                Destroy(balloon.gameObject);
            }
        }
    }
}
