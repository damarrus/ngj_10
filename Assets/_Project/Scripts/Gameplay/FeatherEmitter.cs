using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Drives a feather ParticleSystem off the wing state: emits while wings are
    /// open, stops (but lets live particles fade) while closed. The Icarus
    /// controller owns the wing state and raises WingsToggled; we just listen.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class FeatherEmitter : MonoBehaviour
    {
        [SerializeField] private IcarusController _icarus;

        private ParticleSystem _particles;

        private void Awake()
        {
            _particles = GetComponent<ParticleSystem>();
            if (_icarus == null)
                _icarus = GetComponentInParent<IcarusController>();
        }

        private void OnEnable()
        {
            if (_icarus != null)
            {
                _icarus.WingsToggled += OnWingsToggled;
                Apply(_icarus.WingsOpen);
            }
        }

        private void OnDisable()
        {
            if (_icarus != null)
                _icarus.WingsToggled -= OnWingsToggled;
        }

        private void OnWingsToggled(bool open) => Apply(open);

        private void Apply(bool open)
        {
            var emission = _particles.emission;
            emission.enabled = open;
            if (open && !_particles.isPlaying)
                _particles.Play();
        }
    }
}
