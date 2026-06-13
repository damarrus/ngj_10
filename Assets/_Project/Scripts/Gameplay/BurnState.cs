using System.Collections.Generic;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Icarus' heat meter for burn cones. Burners call <see cref="Expose"/> every
    /// frame he sits inside a cone; the meter fills to 1 over HeatUpTime under a
    /// ray and cools back to 0 over CoolDownTime in the open. At full heat he
    /// burns — the same death as any other hazard. The meter itself is never
    /// shown: it only tints every body sprite from its base colour toward red.
    /// </summary>
    public class BurnState : MonoBehaviour
    {
        // Tuning lives on GameConfig (the one place game settings are authored) and
        // is pushed in via Configure(). These are only fallback defaults for running
        // a scene without a GameConfig.
        private float _heatUpTime = 1f;
        private float _coolDownTime = 2f;
        private Color _burntColor = new Color(0.85f, 0.12f, 0.05f);

        // 0 = cold, 1 = burnt. Never displayed as a bar — only via the tint.
        private float _heat;

        // Set by Expose() during the frame, consumed and cleared in Update so a
        // single integration path owns the rise/fall — no double processing.
        private bool _exposedThisFrame;

        private SpriteRenderer[] _sprites;
        private Color[] _baseColors;
        private bool _burning; // already fired the death this run — fire once

        private void Awake()
        {
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
        public void Configure(float heatUpTime, float coolDownTime, Color burntColor)
        {
            _heatUpTime = heatUpTime;
            _coolDownTime = coolDownTime;
            _burntColor = burntColor;
        }

        /// <summary>Called by a Burner each frame Icarus is inside one of its cones.</summary>
        public void Expose() => _exposedThisFrame = true;

        private void Update()
        {
            // WingsVisual builds its sprites in its own Awake, which may run after
            // ours — re-cache once if we came up empty.
            if (_sprites == null || _sprites.Length == 0)
                CacheSprites();

            float rate = _exposedThisFrame
                ? (_heatUpTime > 0f ? 1f / _heatUpTime : 1f)
                : (_coolDownTime > 0f ? -1f / _coolDownTime : -1f);
            _exposedThisFrame = false;

            _heat = Mathf.Clamp01(_heat + rate * Time.deltaTime);
            ApplyTint();

            if (_heat >= 1f && !_burning)
            {
                _burning = true;
                Hazard.Kill();
            }
        }

        private void ApplyTint()
        {
            if (_sprites == null) return; // not cached yet (early respawn before Awake)
            for (int i = 0; i < _sprites.Length; i++)
            {
                if (_sprites[i] == null) continue;
                Color tinted = Color.Lerp(_baseColors[i], _burntColor, _heat);
                // Keep each sprite's own alpha (halo fades, feathers are translucent).
                tinted.a = _sprites[i].color.a;
                _sprites[i].color = tinted;
            }
        }

        /// <summary>Reset on respawn: cold again, tint cleared.</summary>
        public void ResetHeat()
        {
            _heat = 0f;
            _burning = false;
            _exposedThisFrame = false;
            ApplyTint();
        }
    }
}
