using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Modular Icarus built from the artist's atlas. A head sprite overlays a body
    /// sprite, with a separate legs sprite added only for the wings-spread pose
    /// (whose body is an arms-raised torso with no legs of its own). The two
    /// wings-down bodies already contain the legs, so they need only a head.
    ///
    /// Four poses, read from the controller every frame:
    ///   • Standing (parked at spawn): idle body, forward-looking head, flying legs.
    ///   • Flying   (wings open):      spread-wing torso, upward head, standing legs.
    ///   • Diving   (wings folded):    tucked-wing body, upward head, standing legs.
    ///   • Transition: brief mid-frame between Flying and Diving (idle body, upward
    ///     head, standing legs), shown for a fixed time on every wings toggle.
    ///
    /// Body banking/heading is owned by the controller (it rotates the rigidbody to
    /// face velocity), so this component only assembles and swaps sprites — it never
    /// rotates the root. Sprite pivots are authored in the importer so each part sits
    /// correctly; the head carries one fixed local offset, legs sit at the origin.
    /// </summary>
    public class WingsVisual : MonoBehaviour
    {
        [Header("Body sprites (headless)")]
        [SerializeField] private Sprite _bodyIdle;    // full figure, wings relaxed — spawn idle
        [SerializeField] private Sprite _bodySpread;  // arms-raised torso, no legs (flying)
        [SerializeField] private Sprite _bodyTucked;  // full figure, wings pinned (diving)

        [Header("Head sprites")]
        [SerializeField] private Sprite _headForward;  // looking ahead (standing)
        [SerializeField] private Sprite _headUp;       // looking up (in flight)

        [Header("Legs sprites")]
        [Tooltip("Legs for the spread (flying) pose.")]
        [SerializeField] private Sprite _legsFly;
        [Tooltip("Legs for the standing pose (idle torso has no legs of its own).")]
        [SerializeField] private Sprite _legsStand;

        [Header("Head offset (local units, same for every head sprite)")]
        [SerializeField] private float _headOffsetX = -0.012f;
        [SerializeField] private float _headOffsetY = 1.384f;

        [Header("Legs offset (local units, same for every legs sprite)")]
        [SerializeField] private float _legsOffsetX = -0.05f;
        [SerializeField] private float _legsOffsetY = -0.613f;

        [Header("Wings-toggle transition")]
        [Tooltip("How long the idle-body transition frame shows on each spread↔tuck toggle.")]
        [SerializeField] private float _transitionDuration = 0.1f;

        [Header("Halo")]
        [SerializeField] private Sprite _haloSprite;
        [SerializeField] private float _haloAlpha = 0.16f;

        [Header("Rig")]
        [Tooltip("Uniform scale so the atlas art fits the play field.")]
        [SerializeField] private float _scale = 0.16f;

        [Header("Sorting (back → front) — head & legs always behind body")]
        [SerializeField] private int _orderHalo = 6;
        [SerializeField] private int _orderHead = 8;
        [SerializeField] private int _orderLegs = 9;
        [SerializeField] private int _orderBody = 10;

        private IcarusController _controller;
        private SpriteRenderer _body;
        private SpriteRenderer _head;
        private SpriteRenderer _legs;
        private SpriteRenderer _halo;
        private float _haloT;

        // Counts down while the post-toggle transition frame is showing.
        private float _transitionT;
        private bool _hooked;

        private enum Pose { Standing, Flying, Diving, Transition }

        // While the title screen is up, force the standing pose (full idle figure on the
        // island) regardless of the parked controller state. GameConfig sets it;
        // LevelController clears it on start.
        private bool _menuPose;

        public void SetMenuPose(bool on)
        {
            _menuPose = on;
            Apply(CurrentPose());
        }

        private void Awake()
        {
            _controller = GetComponentInParent<IcarusController>();
            transform.localScale = new Vector3(_scale, _scale, 1f);

            _halo = NewLayer("Halo", _orderHalo);
            _halo.sprite = _haloSprite;
            _halo.color = new Color(1f, 1f, 1f, 0f);

            _legs = NewLayer("Legs", _orderLegs);
            _body = NewLayer("Body", _orderBody);
            _head = NewLayer("Head", _orderHead);
            _head.transform.localPosition = new Vector3(_headOffsetX, _headOffsetY, 0f);
            _legs.transform.localPosition = new Vector3(_legsOffsetX, _legsOffsetY, 0f);

            HookController();
            Apply(CurrentPose());
        }

        private void OnEnable() => HookController();

        private void OnDisable()
        {
            if (_controller != null)
                _controller.WingsToggled -= OnWingsToggled;
            _hooked = false;
        }

        private void HookController()
        {
            if (_hooked || _controller == null)
                return;
            _controller.WingsToggled += OnWingsToggled;
            _hooked = true;
        }

        // Every spread↔tuck toggle shows the idle-body transition frame briefly,
        // symmetrically in both directions.
        private void OnWingsToggled(bool open) => _transitionT = _transitionDuration;

        private SpriteRenderer NewLayer(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            return sr;
        }

        private Pose CurrentPose()
        {
            if (_menuPose)
                return Pose.Standing;
            if (_controller == null)
                return Pose.Flying;
            if (_controller.IsWaitingForInput)
                return Pose.Standing;
            if (_transitionT > 0f)
                return Pose.Transition;
            return _controller.WingsOpen ? Pose.Flying : Pose.Diving;
        }

        private void Apply(Pose pose)
        {
            // Renderers are built in Awake. GameConfig (exec order -100) can call SetMenuPose
            // before that runs, so guard: Awake re-applies the (persisted) pose once ready.
            if (_body == null)
                return;

            switch (pose)
            {
                case Pose.Standing:
                    _body.sprite = _bodyIdle;
                    _head.sprite = _headForward;
                    _legs.sprite = _legsFly;
                    break;
                case Pose.Flying:
                    _body.sprite = _bodySpread;
                    _head.sprite = _headUp;
                    _legs.sprite = _legsStand;
                    break;
                case Pose.Diving:
                    _body.sprite = _bodyTucked;
                    _head.sprite = _headUp;
                    _legs.sprite = _legsStand;
                    break;
                case Pose.Transition:
                    _body.sprite = _bodyIdle;
                    _head.sprite = _headUp;
                    _legs.sprite = _legsStand;
                    break;
            }
        }

        private void Update()
        {
            if (_transitionT > 0f)
                _transitionT -= Time.deltaTime;

            Apply(CurrentPose());

            bool carried = _controller != null
                && _controller.CurrentStream != null
                && _controller.WingsOpen;
            _haloT = Mathf.Lerp(_haloT, carried ? _haloAlpha : 0f,
                1f - Mathf.Exp(-8f * Time.deltaTime));
            if (_halo.sprite != null)
            {
                var c = _halo.color;
                c.a = _haloT;
                _halo.color = c;
            }
        }
    }
}
