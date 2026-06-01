using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Smoke-test behaviour for the Game scene. Logs on start and spins a sprite
    /// so it is obvious the scene is alive and rendering. Delete once real
    /// gameplay exists.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class HelloWorld : MonoBehaviour
    {
        [SerializeField] private float _rotateSpeed = 90f;

        private void Start()
        {
            Debug.Log("[HelloWorld] Game scene is running. NGJ 10 ready.");

            // Ensure something is visible even before real art exists:
            // generate a 1x1 white sprite at runtime if none was assigned.
            var sr = GetComponent<SpriteRenderer>();
            if (sr.sprite == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                sr.sprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, 1, 1),
                    new Vector2(0.5f, 0.5f),
                    1f);
            }
        }

        private void Update()
        {
            transform.Rotate(0f, 0f, _rotateSpeed * Time.deltaTime);
        }
    }
}
