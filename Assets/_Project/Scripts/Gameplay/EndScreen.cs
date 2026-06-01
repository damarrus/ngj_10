using Ngj10.Core;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Lives in the End scene. Waits for input, then restarts the game from
    /// the Game scene. Placeholder until a real end screen / UI exists.
    /// </summary>
    public class EndScreen : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log("[EndScreen] Game over. Press any key / click to play again.");
        }

        private void Update()
        {
            if (Input.anyKeyDown)
            {
                SceneLoader.LoadGame();
            }
        }
    }
}
