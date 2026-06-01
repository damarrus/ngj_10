using UnityEngine;

namespace Ngj10.Core
{
    /// <summary>
    /// Persistent game-wide manager. Holds the current <see cref="GameState"/>
    /// and survives scene loads. Created once from the Boot scene.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private GameState _state = GameState.Boot;

        public GameState State => _state;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SetState(GameState state)
        {
            _state = state;
            Debug.Log($"[GameManager] State -> {state}");
        }

        public void StartGame()
        {
            SetState(GameState.Playing);
            SceneLoader.LoadGame();
        }
    }
}
