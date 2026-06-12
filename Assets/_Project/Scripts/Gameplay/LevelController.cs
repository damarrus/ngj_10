using System.Collections;
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
            _mode = data.Mode;
            if (_camera != null)
                _camera.orthographicSize = data.CameraSize;
            if (_cameraFollow != null)
            {
                // Keep the bottom edge at or above the kill line: center >= killY + halfHeight.
                float minCenterY = data.KillY + data.CameraSize;
                _cameraFollow.SetMode(_mode, minCenterY);
            }
        }

        private void Start()
        {
            if (_autoStart)
                Begin();
        }

        /// <summary>Start the level: set the time scale and spawn the player. Called by the
        /// StartScreen on the Start button, or automatically from Start() when no start screen.</summary>
        public void Begin()
        {
            Time.timeScale = _timeScale;
            Respawn();
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
        }

        private bool _won;
        private bool _dying;

        private void Update()
        {
            if (_won)
            {
                if (Input.anyKeyDown)
                    Restart();
                return;
            }

            if (_dying)
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

        private void Win()
        {
            _won = true;
            Time.timeScale = 0f;
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
        /// Bottom kill line is deadly in every mode. The side and top edges of the
        /// camera viewport become deadly per the level mode: UpOnly adds left/right,
        /// SingleScreen adds left/right and top.
        /// </summary>
        private bool IsOutOfBounds(Vector2 pos)
        {
            if (pos.y < _killY)
                return true;

            if (_mode == LevelMode.Free || _camera == null)
                return false;

            Vector2 center = _camera.transform.position;
            float halfH = _camera.orthographicSize;
            float halfW = halfH * _camera.aspect;

            if (pos.x < center.x - halfW + _edgeMargin || pos.x > center.x + halfW - _edgeMargin)
                return true;
            if (_mode == LevelMode.SingleScreen && pos.y > center.y + halfH - _edgeMargin)
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

            if (_screenFader != null)
                yield return _screenFader.FadeIn();

            _dying = false;
        }

        private void Respawn()
        {
            _player.ResetAt(_startStream.SampleAtDistance(0f).Point);
        }
    }
}
