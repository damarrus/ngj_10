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
            _spawnPoint = data.Start;
            _hasSpawnPoint = true;
            _mode = data.Mode;
            if (_camera != null)
                _camera.orthographicSize = data.CameraSize;
            if (_cameraFollow != null)
            {
                // Keep the bottom edge at or above the kill line: center >= killY + halfHeight.
                float minCenterY = data.KillY + data.CameraSize;
                _cameraFollow.SetMode(_mode, minCenterY);
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

            // No kill checks while parked at spawn — a Start placed in a bad spot
            // must not melt into an endless respawn loop.
            if (_player.IsWaitingForInput)
                return;

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
