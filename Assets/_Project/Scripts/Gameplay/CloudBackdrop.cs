using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>Soft decorative cloud blobs slowly swaying, far behind the action.</summary>
    public class CloudBackdrop : MonoBehaviour
    {
        [SerializeField] private Sprite _sprite;
        [SerializeField] private int _count = 7;
        [SerializeField] private Vector2 _area = new Vector2(36f, 16f);
        [SerializeField] private Color _color = new Color(0.85f, 0.83f, 0.95f, 0.07f);

        private Transform[] _clouds;
        private Vector3[] _origins;

        private void Awake()
        {
            _clouds = new Transform[_count];
            _origins = new Vector3[_count];
            for (int i = 0; i < _count; i++)
            {
                var cloud = new GameObject("Cloud");
                cloud.transform.SetParent(transform, false);
                float u = Frac(i * 0.6180f + 0.07f);
                float v = Frac(i * 0.2754f + 0.29f);
                var origin = new Vector3((u - 0.5f) * _area.x, (v - 0.5f) * _area.y, 0f);
                cloud.transform.localPosition = origin;
                _clouds[i] = cloud.transform;
                _origins[i] = origin;

                float w = Mathf.Lerp(2f, 4.5f, Frac(i * 3.3f));
                Puff(cloud.transform, Vector3.zero, new Vector3(w, w * 0.33f, 1f));
                Puff(cloud.transform, new Vector3(-w * 0.3f, 0.15f, 0f), new Vector3(w * 0.62f, w * 0.24f, 1f));
                Puff(cloud.transform, new Vector3(w * 0.32f, 0.11f, 0f), new Vector3(w * 0.55f, w * 0.22f, 1f));
            }
        }

        private void Puff(Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject("Puff");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _sprite;
            sr.color = _color;
            sr.sortingOrder = -8;
        }

        private void Update()
        {
            for (int i = 0; i < _clouds.Length; i++)
                _clouds[i].localPosition = _origins[i]
                    + Vector3.right * (Mathf.Sin(Time.time * 0.1f + i) * 0.35f);
        }

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
