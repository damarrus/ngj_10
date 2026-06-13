using System.Collections.Generic;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Icarus' electric-shock reaction. Zeus nodes call <see cref="Shock"/> the
    /// frame he is caught inside a charged stream; that folds and blocks his wings
    /// for a fixed window and blinks every body sprite toward yellow for the same
    /// window. Re-triggering while already shocked refreshes the timer. The block
    /// itself is owned by <see cref="IcarusController"/> — here we only drive the
    /// blink and hand it the duration.
    /// </summary>
    [RequireComponent(typeof(IcarusController))]
    public class ShockState : MonoBehaviour
    {
        // Tuning lives on GameConfig and is pushed in via Configure(). These are
        // fallback defaults for running a scene without a GameConfig.
        private float _blockDuration = 1f;
        private Color _shockColor = new Color(1f, 0.92f, 0.15f);

        private IcarusController _icarus;

        // Set by Shock() during the frame, consumed in LateUpdate so a single path
        // owns the timer — no double processing.
        private bool _shockedThisFrame;
        private float _timer; // seconds of shock remaining

        private SpriteRenderer[] _sprites;
        private Color[] _baseColors;

        private void Awake()
        {
            _icarus = GetComponent<IcarusController>();
            CacheSprites();
        }

        private void CacheSprites()
        {
            var found = GetComponentsInChildren<SpriteRenderer>(true);
            var sprites = new List<SpriteRenderer>(found.Length);
            var colors = new List<Color>(found.Length);
            foreach (var sr in found)
            {
                sprites.Add(sr);
                colors.Add(sr.color);
            }
            _sprites = sprites.ToArray();
            _baseColors = colors.ToArray();
        }

        /// <summary>Push tuning from GameConfig at startup.</summary>
        public void Configure(float blockDuration, Color shockColor)
        {
            _blockDuration = blockDuration;
            _shockColor = shockColor;
        }

        /// <summary>Called by a Zeus node each frame Icarus sits in a charged stream.</summary>
        public void Shock() => _shockedThisFrame = true;

        private void LateUpdate()
        {
            // WingsVisual builds its sprites in its own Awake, which may run after
            // ours — re-cache once if we came up empty.
            if (_sprites == null || _sprites.Length == 0)
                CacheSprites();

            if (_shockedThisFrame)
            {
                _shockedThisFrame = false;
                _timer = _blockDuration;
                _icarus.BlockWings(_blockDuration);
            }
            else if (_timer > 0f)
            {
                _timer -= Time.deltaTime;
            }

            ApplyBlink();
        }

        // Strobe between base and shock colour while the timer runs, off otherwise.
        private void ApplyBlink()
        {
            if (_sprites == null) return;
            bool on = _timer > 0f && Mathf.Repeat(Time.time * 12f, 1f) < 0.5f;
            for (int i = 0; i < _sprites.Length; i++)
            {
                if (_sprites[i] == null) continue;
                Color target = on ? _shockColor : _baseColors[i];
                target.a = _sprites[i].color.a; // keep each sprite's own alpha
                _sprites[i].color = target;
            }
        }

        /// <summary>Reset on respawn: clear the shock and restore colours.</summary>
        public void ResetShock()
        {
            _shockedThisFrame = false;
            _timer = 0f;
            ApplyBlink();
        }
    }
}
