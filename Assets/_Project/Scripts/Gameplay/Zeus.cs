using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Runtime Zeus node: an anchor that strikes one or more target areas. Each
    /// <see cref="ZeusAreaDef"/> runs on its own clock — a first-strike delay, then
    /// a fixed period between strikes. A strike does not travel: on the frame it
    /// fires a lightning bolt sprite flashes instantly from the anchor down to the
    /// area (no projectile, no warning), small spark sprites scatter inside the
    /// ellipse, and Icarus standing in the ellipse that frame has his wings shocked
    /// via <see cref="ShockState"/>. The flash lingers a moment then fades out.
    /// Purely code-driven (no prefab) — built by LevelBuilder from a
    /// <see cref="ZeusDef"/>, which also hands in the bolt/spark sprite art.
    /// </summary>
    public class Zeus : MonoBehaviour
    {
        // The struck flash lingers briefly after it fires so the hit reads, then
        // fades out over its own (longer) tail.
        [SerializeField] private float _flashDuration = 0.12f;
        [SerializeField] private float _fadeDuration = 0.35f;

        [Header("Bolt sprite (anchor -> area)")]
        [Tooltip("World width of the struck bolt sprite (it is stretched vertically to span anchor->area).")]
        [SerializeField] private float _boltWidth = 1.2f;
        [SerializeField] private Color _boltColor = Color.white;
        [SerializeField] private int _sortingOrder = 3; // above cones, below the player

        [Header("Sparks (scatter inside the ellipse on impact)")]
        [SerializeField] private int _sparkCount = 5;
        [SerializeField] private float _sparkScale = 0.6f;

        private Sprite[] _boltSprites;  // tall vertical bolts; one picked per strike
        private Sprite[] _sparkSprites; // small fragments scattered in the ellipse

        // One live timer + visual set per area.
        private struct Area
        {
            public Vector2 Center;
            public float RadiusX, RadiusY;
            public float Period;
            public float NextStrikeTime; // local-clock time the next bolt fires
            public SpriteRenderer Bolt;
            public SpriteRenderer[] Sparks;
            public float StruckAt;        // local-clock time the current flash fired (<0 = idle)
        }

        private Area[] _areas;
        private ShockState _shock;
        private float _time; // local clock so timers survive a paused start

        public void Configure(ZeusDef def, Sprite[] boltSprites, Sprite[] sparkSprites)
        {
            _boltSprites = boltSprites;
            _sparkSprites = sparkSprites;
            BuildAreas(def);
            _shock = FindAnyObjectByType<ShockState>();
        }

        private void BuildAreas(ZeusDef def)
        {
            ZeusAreaDef[] defs = def.Areas ?? System.Array.Empty<ZeusAreaDef>();
            _areas = new Area[defs.Length];
            Vector2 anchor = transform.position;

            for (int i = 0; i < defs.Length; i++)
            {
                ZeusAreaDef d = defs[i];
                _areas[i] = new Area
                {
                    Center = anchor + d.Offset,
                    RadiusX = Mathf.Max(0.01f, d.RadiusX),
                    RadiusY = Mathf.Max(0.01f, d.RadiusY),
                    Period = Mathf.Max(0.05f, d.Period),
                    NextStrikeTime = Mathf.Max(0f, d.StartDelay),
                    Bolt = NewSprite("Bolt" + i),
                    Sparks = new SpriteRenderer[Mathf.Max(0, _sparkCount)],
                    StruckAt = -1f,
                };
                for (int s = 0; s < _areas[i].Sparks.Length; s++)
                    _areas[i].Sparks[s] = NewSprite($"Spark{i}_{s}");
            }
        }

        private SpriteRenderer NewSprite(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = _boltColor;
            sr.sortingOrder = _sortingOrder;
            sr.enabled = false;
            return sr;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            Vector2 anchor = transform.position;
            Vector2? icarus = _shock != null ? (Vector2?)_shock.transform.position : null;

            for (int i = 0; i < _areas.Length; i++)
                TickArea(ref _areas[i], anchor, icarus);
        }

        // One area's lifecycle: idle until its strike time, then flash instantly,
        // hold, fade, and arm the next strike. No travel, no telegraph.
        private void TickArea(ref Area a, Vector2 anchor, Vector2? icarus)
        {
            // The visual children are plain GameObjects under this Zeus. During a scene
            // reload (e.g. Q→menu) Unity tears them down, but this Zeus can still get one
            // more Update tick that frame — the SpriteRenderers are then Unity-null. Bail
            // instead of dereferencing them, which otherwise NREs every frame.
            if (a.Bolt == null)
                return;

            // Fire exactly on the frame the strike time passes.
            if (a.StruckAt < 0f && _time >= a.NextStrikeTime)
            {
                Strike(ref a, anchor, icarus);
                a.NextStrikeTime += a.Period;
            }

            if (a.StruckAt < 0f)
                return;

            float since = _time - a.StruckAt;
            float total = _flashDuration + _fadeDuration;
            if (since >= total)
            {
                HideVisuals(ref a);
                a.StruckAt = -1f;
                return;
            }

            // Full bright for the flash window, then a quadratic ease-out fade.
            float alpha;
            if (since < _flashDuration)
                alpha = 1f;
            else
            {
                float k = 1f - Mathf.Clamp01((since - _flashDuration) / _fadeDuration);
                alpha = k * k;
            }
            SetAlpha(ref a, alpha);
        }

        // Resolve the hit, pick a bolt sprite, stretch it anchor->centre, and
        // scatter the sparks. Everything is frozen for this strike's lifetime.
        private void Strike(ref Area a, Vector2 anchor, Vector2? icarus)
        {
            a.StruckAt = _time;

            if (icarus.HasValue && _shock != null && InEllipse(a, icarus.Value))
                _shock.Shock();

            PlaceBolt(ref a, anchor);
            ScatterSparks(ref a);
        }

        // Stretch the chosen tall-vertical bolt sprite to span from the anchor down
        // to the area centre. Sprite pivot is bottom-left, so we anchor at the
        // centre (bottom) and scale Y so its top reaches the anchor.
        private void PlaceBolt(ref Area a, Vector2 anchor)
        {
            if (_boltSprites == null || _boltSprites.Length == 0)
            {
                a.Bolt.enabled = false;
                return;
            }

            Sprite sprite = _boltSprites[Random.Range(0, _boltSprites.Length)];
            a.Bolt.sprite = sprite;

            Vector2 delta = anchor - a.Center;
            float span = delta.magnitude;
            Vector2 size = sprite.bounds.size; // local units before scale

            var t = a.Bolt.transform;
            t.position = a.Center;
            // Sprite's local +Y points along the bolt; rotate so +Y aims at the anchor.
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;
            t.rotation = Quaternion.Euler(0f, 0f, angle);
            float scaleY = size.y > 0.0001f ? span / size.y : 1f;
            float scaleX = size.x > 0.0001f ? _boltWidth / size.x : 1f;
            t.localScale = new Vector3(scaleX, scaleY, 1f);

            a.Bolt.color = _boltColor;
            a.Bolt.enabled = true;
        }

        // Each spark lands at a random spot inside the ellipse with a random sprite.
        private void ScatterSparks(ref Area a)
        {
            if (a.Sparks == null) return;
            bool hasArt = _sparkSprites != null && _sparkSprites.Length > 0;
            for (int s = 0; s < a.Sparks.Length; s++)
            {
                if (!hasArt) { a.Sparks[s].enabled = false; continue; }

                float ang = Random.value * Mathf.PI * 2f;
                float reach = Mathf.Sqrt(Random.value); // uniform over the disc
                Vector2 p = a.Center + new Vector2(
                    Mathf.Cos(ang) * a.RadiusX * reach,
                    Mathf.Sin(ang) * a.RadiusY * reach);

                var sr = a.Sparks[s];
                sr.sprite = _sparkSprites[Random.Range(0, _sparkSprites.Length)];
                sr.transform.position = p;
                sr.transform.rotation = Quaternion.Euler(0f, 0f, Random.value * 360f);
                sr.transform.localScale = Vector3.one * _sparkScale;
                sr.color = _boltColor;
                sr.enabled = true;
            }
        }

        private void HideVisuals(ref Area a)
        {
            a.Bolt.enabled = false;
            if (a.Sparks == null) return;
            for (int s = 0; s < a.Sparks.Length; s++)
                a.Sparks[s].enabled = false;
        }

        private void SetAlpha(ref Area a, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            Color c = _boltColor;
            c.a = _boltColor.a * alpha;
            if (a.Bolt.enabled) a.Bolt.color = c;
            if (a.Sparks == null) return;
            for (int s = 0; s < a.Sparks.Length; s++)
                if (a.Sparks[s].enabled) a.Sparks[s].color = c;
        }

        private static bool InEllipse(in Area a, Vector2 p)
        {
            Vector2 d = p - a.Center;
            float nx = d.x / a.RadiusX;
            float ny = d.y / a.RadiusY;
            return nx * nx + ny * ny <= 1f;
        }
    }
}
