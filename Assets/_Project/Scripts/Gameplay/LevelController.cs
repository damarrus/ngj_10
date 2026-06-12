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

        private void OnEnable() => Hazard.PlayerHit += Respawn;

        private void OnDisable() => Hazard.PlayerHit -= Respawn;

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

        private void Update()
        {
            if (_won)
            {
                if (Input.anyKeyDown)
                    Restart();
                return;
            }

            Vector2 pos = _player.transform.position;
            if (IsOutOfBounds(pos))
            {
                Respawn();
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

        private void Respawn()
        {
            _player.ResetAt(_startStream.SampleAtDistance(0f).Point);
        }
    }
}
