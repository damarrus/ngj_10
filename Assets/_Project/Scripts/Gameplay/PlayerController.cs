using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// WASD movement via Rigidbody2D physics. Stays inside the camera view.
    /// Feeds Speed to the Animator to switch idle/run. Picks up / offers a sheep with E.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _interactRadius = 0.6f;
        [SerializeField] private Vector2 _carryOffset = new Vector2(0f, 0.45f);
        [SerializeField] private Animator _animator;
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private GameObject _interactPrompt;
        [SerializeField] private TMP_Text _interactPromptText;
        [SerializeField] private Color _promptColor = Color.white;
        [SerializeField] private Color _promptHighlightColor = new Color(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private float _promptHeight = 0.55f;
        [SerializeField] private float _promptHeightCarrying = 0.95f;
        [Tooltip("How far the player can be from a carried item's drop point — purely cosmetic, drop is always allowed.")]
        [SerializeField] private Vector2 _dropOffset = new Vector2(0f, -0.3f);

        private Rigidbody2D _rb;
        private Vector2 _input;
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly Collider2D[] _overlap = new Collider2D[16];

        private Vector2 _externalVelocity;   // wind etc., added on top of input
        private float _blinkTimer;
        private Color _baseColor = Color.white;
        private float _speedMultiplier = 1f; // puddles etc. set this each frame
        private Carryable _highlightedItem;  // pickable currently tinted as the target

        /// <summary>The resource currently held above the player, or null.</summary>
        public Carryable CarriedItem { get; private set; }

        /// <summary>Current world position (rigidbody-accurate).</summary>
        public Vector2 Position => _rb != null ? _rb.position : (Vector2)transform.position;

        public float MoveSpeed => _moveSpeed;

        /// <summary>Constant push applied on top of movement (wind). Vector2.zero = none.</summary>
        public void SetExternalVelocity(Vector2 v) => _externalVelocity = v;
        public void ClearExternalVelocity() => _externalVelocity = Vector2.zero;

        /// <summary>One-off shove (e.g. a single gust). Applied as an instant velocity impulse.</summary>
        public void Push(Vector2 impulse)
        {
            if (_rb != null) _rb.linearVelocity += impulse;
        }

        /// <summary>Flash the player red for a moment (e.g. hit by a meteor).</summary>
        public void Blink(float duration = 0.25f)
        {
            _blinkTimer = Mathf.Max(_blinkTimer, duration);
        }

        /// <summary>Slow the player this frame (e.g. standing in a puddle). 1 = normal, 0 = stopped.
        /// Call every frame while the slow applies; the strongest slow wins.</summary>
        public void ApplySlow(float factor) => _speedMultiplier = Mathf.Min(_speedMultiplier, factor);

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_camera == null) _camera = Camera.main;
            if (_spriteRenderer != null) _baseColor = _spriteRenderer.color;
            if (_interactPromptText == null && _interactPrompt != null)
                _interactPromptText = _interactPrompt.GetComponentInChildren<TMP_Text>();
        }

        private void Update()
        {
            ReadMovementInput();

            UpdateBlink();

            if (_animator != null)
                _animator.SetFloat(SpeedHash, _input.sqrMagnitude);

            if (_spriteRenderer != null && Mathf.Abs(_input.x) > 0.01f)
                _spriteRenderer.flipX = _input.x < 0f;

            var interactable = FindBestInteractable();

            // An E action is available if there's an interactable target, or we're carrying
            // something we can always drop. The prompt is highlighted only for the meaningful
            // targets (pick up an item / hand it to an altar), plain for a bare drop.
            bool hasTarget = interactable != null;
            bool canAct = hasTarget || CarriedItem != null;

            UpdateItemHighlight(interactable as Carryable);
            UpdatePrompt(canAct, hasTarget);

            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame)
            {
                if (interactable != null) interactable.Interact(this);
                else if (CarriedItem != null) DropCarriedItem();
            }
        }

        private void UpdatePrompt(bool show, bool highlighted)
        {
            if (_interactPrompt == null) return;

            if (_interactPrompt.activeSelf != show)
                _interactPrompt.SetActive(show);
            if (!show) return;

            // Lift the prompt above a carried item so it stays readable.
            var p = _interactPrompt.transform.localPosition;
            p.y = CarriedItem != null ? _promptHeightCarrying : _promptHeight;
            _interactPrompt.transform.localPosition = p;

            if (_interactPromptText != null)
                _interactPromptText.color = highlighted ? _promptHighlightColor : _promptColor;
        }

        private void UpdateItemHighlight(Carryable target)
        {
            if (_highlightedItem == target) return;
            if (_highlightedItem != null) _highlightedItem.SetHighlighted(false);
            _highlightedItem = target;
            if (_highlightedItem != null) _highlightedItem.SetHighlighted(true);
        }

        private void ReadMovementInput()
        {
            var kb = Keyboard.current;
            if (kb == null) { _input = Vector2.zero; return; }
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            _input = new Vector2(x, y);
            if (_input.sqrMagnitude > 1f) _input.Normalize();
        }

        /// <summary>Nearest interactable in range that currently allows interaction, or null.</summary>
        private IInteractable FindBestInteractable()
        {
            int n = Physics2D.OverlapCircleNonAlloc(_rb.position, _interactRadius, _overlap);
            IInteractable best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = _overlap[i];
                if (c == null) continue;
                var it = c.GetComponentInParent<IInteractable>();
                if (it == null || !it.CanInteract(this)) continue;
                float d = (it.Position - _rb.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = it; }
            }
            return best;
        }

        /// <summary>Hold a resource above the player's head.</summary>
        public void PickUp(Carryable item)
        {
            if (CarriedItem != null || item == null) return;
            CarriedItem = item;

            // Stop it being interactable / physical while carried.
            foreach (var col in item.GetComponentsInChildren<Collider2D>()) col.enabled = false;
            var srb = item.GetComponent<Rigidbody2D>();
            if (srb != null) srb.simulated = false;

            var t = item.transform;
            t.SetParent(transform, worldPositionStays: false);
            t.localPosition = _carryOffset;

            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null && _spriteRenderer != null)
                sr.sortingOrder = _spriteRenderer.sortingOrder + 1;
        }

        /// <summary>Offer the carried resource on the altar — it is consumed.</summary>
        public void OfferCarriedItem()
        {
            if (CarriedItem == null) return;
            Destroy(CarriedItem.gameObject);
            CarriedItem = null;
        }

        /// <summary>Put the carried resource back on the ground in front of the player.</summary>
        public void DropCarriedItem()
        {
            if (CarriedItem == null) return;
            var item = CarriedItem;
            CarriedItem = null;

            var t = item.transform;
            t.SetParent(null, worldPositionStays: true);
            t.position = _rb.position + _dropOffset;

            // Restore physics / interactability so it can be picked up again.
            foreach (var col in item.GetComponentsInChildren<Collider2D>()) col.enabled = true;
            var srb = item.GetComponent<Rigidbody2D>();
            if (srb != null) srb.simulated = true;

            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = 0;
        }

        private void UpdateBlink()
        {
            if (_spriteRenderer == null) return;
            if (_blinkTimer > 0f)
            {
                _blinkTimer -= Time.deltaTime;
                // toggle red ~10Hz
                bool red = Mathf.FloorToInt(_blinkTimer * 10f) % 2 == 0;
                _spriteRenderer.color = red ? Color.red : _baseColor;
                if (_blinkTimer <= 0f) _spriteRenderer.color = _baseColor;
            }
        }

        private void FixedUpdate()
        {
            _rb.linearVelocity = _input * (_moveSpeed * _speedMultiplier) + _externalVelocity;
            ClampToView();
            _speedMultiplier = 1f; // reset; puddles re-apply each frame while active
        }

        private void ClampToView()
        {
            if (_camera == null || !_camera.orthographic) return;

            float halfW = 0.5f, halfH = 0.5f;
            if (_spriteRenderer != null && _spriteRenderer.sprite != null)
            {
                var ext = _spriteRenderer.bounds.extents;
                halfW = ext.x;
                halfH = ext.y;
            }

            float camHalfH = _camera.orthographicSize;
            float camHalfW = camHalfH * _camera.aspect;
            Vector3 c = _camera.transform.position;

            float minX = c.x - camHalfW + halfW;
            float maxX = c.x + camHalfW - halfW;
            float minY = c.y - camHalfH + halfH;
            float maxY = c.y + camHalfH - halfH;

            Vector2 p = _rb.position;
            float cx = Mathf.Clamp(p.x, minX, maxX);
            float cy = Mathf.Clamp(p.y, minY, maxY);
            if (cx != p.x || cy != p.y)
                _rb.position = new Vector2(cx, cy);
        }
    }
}
