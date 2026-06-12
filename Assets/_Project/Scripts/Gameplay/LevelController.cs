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

        private void OnEnable() => Hazard.PlayerHit += Respawn;

        private void OnDisable() => Hazard.PlayerHit -= Respawn;

        private void Start()
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
            if (pos.y < _killY)
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

        private void Respawn()
        {
            _player.ResetAt(_startStream.SampleAtDistance(0f).Point);
        }
    }
}
