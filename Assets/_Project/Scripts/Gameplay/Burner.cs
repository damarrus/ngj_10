using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Runtime burner: an anchor that emits one or more burn cones built from
    /// <see cref="ConeDef"/>s. Each cone draws as a tinted triangle sprite (bright
    /// at the tip, fading toward the far end) and checks every frame whether
    /// Icarus sits inside its sector — if so it feeds his <see cref="BurnState"/>.
    /// Purely code-driven: no prefab art, so it merges cleanly and survives MCP
    /// edits. Spawned by LevelBuilder from <see cref="BurnerDef"/>.
    /// </summary>
    public class Burner : MonoBehaviour
    {
        [SerializeField] private Color _coneColor = new Color(1f, 0.45f, 0.12f, 0.45f);

        private static Sprite _coneSprite; // shared triangle with a tip->base alpha fade

        private ConeDef[] _cones;
        private Transform[] _coneTransforms;
        private SpriteRenderer[] _coneRenderers;

        private BurnState _target;
        private float _time; // local clock so Pulse/Rotate survive a paused start cleanly

        public void Configure(BurnerDef def)
        {
            _cones = def.Cones ?? System.Array.Empty<ConeDef>();
            BuildCones();
            _target = FindAnyObjectByType<BurnState>();
        }

        private void BuildCones()
        {
            EnsureSprite();
            _coneTransforms = new Transform[_cones.Length];
            _coneRenderers = new SpriteRenderer[_cones.Length];
            for (int i = 0; i < _cones.Length; i++)
            {
                var go = new GameObject("Cone" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _coneSprite;
                sr.color = _coneColor;
                sr.sortingOrder = 2; // above streams, below the player
                _coneTransforms[i] = go.transform;
                _coneRenderers[i] = sr;
            }
        }

        private void Update()
        {
            _time += Time.deltaTime;
            Vector2 origin = transform.position;
            Vector2? icarus = _target != null ? (Vector2?)_target.transform.position : null;

            for (int i = 0; i < _cones.Length; i++)
            {
                ConeDef cone = _cones[i];
                float angle = CurrentAngle(cone);
                bool active = IsActive(cone);

                UpdateConeVisual(i, cone, angle, active);

                if (active && icarus.HasValue && _target != null
                    && InsideCone(origin, angle, cone, icarus.Value))
                {
                    _target.Expose();
                }
            }
        }

        private float CurrentAngle(ConeDef cone)
        {
            return cone.Motion == ConeMotion.Rotate
                ? cone.Angle + cone.RotateSpeed * _time
                : cone.Angle;
        }

        private bool IsActive(ConeDef cone)
        {
            if (cone.Motion != ConeMotion.Pulse)
                return true;
            float period = cone.OnDuration + cone.OffDuration;
            if (period <= 0f)
                return true;
            float t = Mathf.Repeat(_time + cone.PhaseOffset, period);
            return t < cone.OnDuration;
        }

        private void UpdateConeVisual(int i, ConeDef cone, float angle, bool active)
        {
            SpriteRenderer sr = _coneRenderers[i];
            sr.enabled = active;
            if (!active) return;

            Transform t = _coneTransforms[i];
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(0f, 0f, angle);

            // Source sprite: unit right-pointing triangle, tip at the local
            // origin, base one unit out at +X spanning y in [-0.5, 0.5]. Scale x
            // by Length; scale y so the base half-width = Length*tan(HalfAngle),
            // i.e. 2x that (sprite half-span is 0.5).
            float halfWidth = cone.Length * Mathf.Tan(cone.HalfAngle * Mathf.Deg2Rad);
            t.localScale = new Vector3(cone.Length, halfWidth * 2f, 1f);
        }

        /// <summary>True when <paramref name="point"/> falls inside the sector.</summary>
        private static bool InsideCone(Vector2 origin, float angleDeg, ConeDef cone, Vector2 point)
        {
            Vector2 to = point - origin;
            float dist = to.magnitude;
            if (dist > cone.Length || dist < 0.0001f)
                return false;

            float dir = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
            float delta = Mathf.Abs(Mathf.DeltaAngle(angleDeg, dir));
            return delta <= cone.HalfAngle;
        }

        // ── Shared triangle sprite ────────────────────────────────────────────

        private static void EnsureSprite()
        {
            if (_coneSprite != null)
                return;

            // 64x64: white triangle, tip at the left edge centre, fanning out to
            // the right edge. Alpha fades from 1 at the tip to ~0.15 at the far
            // end so the cone reads as a beam that dies out at its reach.
            const int w = 64, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[w * h];
            float cy = (h - 1) * 0.5f;
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);           // 0 at tip, 1 at base
                float halfSpan = u * cy;                // triangle widens linearly
                float a = Mathf.Lerp(0.65f, 0.05f, u);  // tip soft, end nearly clear
                byte alpha = (byte)(a * 255f);
                for (int y = 0; y < h; y++)
                {
                    bool inside = Mathf.Abs(y - cy) <= halfSpan;
                    pixels[y * w + x] = inside
                        ? new Color32(255, 255, 255, alpha)
                        : new Color32(255, 255, 255, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            // Pivot at the tip (left-centre) so rotation pivots about the anchor,
            // and 1 px-per-unit on x so localScale.x maps straight to length.
            _coneSprite = Sprite.Create(tex, new Rect(0, 0, w, h),
                new Vector2(0f, 0.5f), w);
        }

        // Domain-reload-off safety: drop the cached sprite between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _coneSprite = null;
    }
}
