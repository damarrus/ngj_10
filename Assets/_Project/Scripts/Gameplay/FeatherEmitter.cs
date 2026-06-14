using System;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Drives a feather ParticleSystem off the wing state: emits while wings are
    /// open, stops (but lets live particles fade) while closed. The Icarus
    /// controller owns the wing state and raises WingsToggled; we just listen.
    ///
    /// The 6 feather sprites live in one Texture Sheet Animation, ordered
    /// normal → singed → burnt. As Icarus climbs toward the sun the burn tier
    /// rises and the random start-frame window widens to let singed, then burnt
    /// feathers into the mix — closer to the sun, the more scorched the feathers.
    /// Mix is approximate (uniform pick inside the window), tuned via burn tiers.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class FeatherEmitter : MonoBehaviour
    {
        /// <summary>
        /// One burn step authored by height. At or above <see cref="StartY"/> the
        /// random feather window spans frames [0 .. TopFrame], so a higher TopFrame
        /// lets more scorched sprites into the mix. Order tiers low → high Y.
        /// </summary>
        [Serializable]
        public struct BurnTier
        {
            [Tooltip("Icarus world Y at/above which this tier applies. Order tiers low to high.")]
            public float StartY;
            [Tooltip("Highest feather frame allowed at this tier (0-based index into the TSA sprite list). " +
                     "With sprites ordered normal(0..3), singed(4), burnt(5): 3 = normal only, 4 = +singed, 5 = +burnt.")]
            [Range(0, 5)]
            public int TopFrame;
        }

        [SerializeField] private IcarusController _icarus;

        [Header("Burn ramp (artist-authored, by height)")]
        [Tooltip("Total feather sprites in the Texture Sheet Animation, in burn order. The 6 feathers: " +
                 "normal x4, singed, burnt.")]
        [SerializeField] private int _spriteCount = 6;
        [Tooltip("Burn steps by height, low Y to high Y. Below the first tier, only the first tier's window " +
                 "is used. Leave empty for normal feathers only (window [0 .. _normalTopFrame]).")]
        [SerializeField] private BurnTier[] _burnTiers = Array.Empty<BurnTier>();
        [Tooltip("Top frame used when no tier matches yet (the all-normal window). With the 4 normal feathers " +
                 "at frames 0-3, this is 3.")]
        [Range(0, 5)]
        [SerializeField] private int _normalTopFrame = 3;

        private ParticleSystem _particles;
        private ParticleSystem.TextureSheetAnimationModule _tsa;
        private int _appliedTopFrame = -1;

        private void Awake()
        {
            _particles = GetComponent<ParticleSystem>();
            _tsa = _particles.textureSheetAnimation;
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

        private void Update()
        {
            if (_icarus == null)
                return;
            UpdateBurnWindow(_icarus.transform.position.y);
        }

        // Pick the burn tier for the current height and widen the TSA start-frame
        // window to [0 .. topFrame]. Idempotent: only touches the module when the
        // top frame actually changes, so it's cheap to call every frame.
        private void UpdateBurnWindow(float y)
        {
            int topFrame = _normalTopFrame;
            for (int i = 0; i < _burnTiers.Length; i++)
            {
                if (y >= _burnTiers[i].StartY)
                    topFrame = _burnTiers[i].TopFrame;
            }
            topFrame = Mathf.Clamp(topFrame, 0, _spriteCount - 1);
            if (topFrame == _appliedTopFrame)
                return;
            _appliedTopFrame = topFrame;

            // startFrame is a normalized 0..1 range over the sprite list. RandomBetween
            // picks a sprite uniformly in [0 .. topFrame]; frameOverTime is held at 0 in
            // the prefab so each particle stays on its chosen sprite (no flip-through).
            float max = _spriteCount > 1 ? (topFrame + 0.999f) / _spriteCount : 0f;
            _tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, max);
        }
    }
}
