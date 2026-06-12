using System.Collections;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// The hero half of the death effect: freeze physics/input and shrink the
    /// model to nothing (transform scale). Screen fade and respawn timing are
    /// orchestrated by <see cref="LevelController"/>; this component only owns
    /// the Icarus visual so a multi-part model shrinks as one.
    /// </summary>
    [RequireComponent(typeof(IcarusController))]
    public class DeathSequence : MonoBehaviour
    {
        [SerializeField] private float _shrinkDuration = 0.45f;
        [SerializeField] private AnimationCurve _ease =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private IcarusController _controller;
        private Rigidbody2D _body;
        private Vector3 _baseScale;

        private void Awake()
        {
            _controller = GetComponent<IcarusController>();
            _body = GetComponent<Rigidbody2D>();
            _baseScale = transform.localScale;
        }

        /// <summary>Freeze the hero and shrink it to nothing.</summary>
        public IEnumerator Shrink()
        {
            _controller.enabled = false;
            _body.linearVelocity = Vector2.zero;
            _body.simulated = false;

            for (float t = 0f; t < _shrinkDuration; t += Time.unscaledDeltaTime)
            {
                float k = _ease.Evaluate(t / _shrinkDuration);
                transform.localScale = _baseScale * (1f - k);
                yield return null;
            }
            transform.localScale = Vector3.zero;
        }

        /// <summary>Restore full size and re-enable physics/input. Call after the
        /// hero has been repositioned at the spawn point (behind a black screen).</summary>
        public void Restore()
        {
            transform.localScale = _baseScale;
            _body.simulated = true;
            _controller.enabled = true;
        }
    }
}
