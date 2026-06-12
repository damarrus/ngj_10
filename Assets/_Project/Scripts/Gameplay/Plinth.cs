using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Solid filled disc at the field centre — the stone slab the altars stand on.
    /// Visualises the spawn keep-out zone (FieldSpawner._centerKeepout): nothing
    /// spawns inside this radius. Procedural sprite, no art asset needed.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Plinth : MonoBehaviour
    {
        [Tooltip("World radius of the slab. Match FieldSpawner._centerKeepout to show the no-spawn zone.")]
        [SerializeField] private float _radius = 3f;
        [SerializeField] private Color _color = new Color(0.62f, 0.60f, 0.55f, 1f);
        [Tooltip("Above the grass (-100), below altars/player (0).")]
        [SerializeField] private int _sortingOrder = -50;

        private void Awake()
        {
            var sr = GetComponent<SpriteRenderer>();
            sr.sprite = CircleSprite.Build(256, 0f); // hard edge, fully filled
            sr.color = _color;
            sr.sortingOrder = _sortingOrder;
            // Sprite is 1 world unit in diameter; scale = diameter.
            transform.localScale = new Vector3(_radius * 2f, _radius * 2f, 1f);
        }
    }
}
