using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Modular Icarus built from the artist's atlas. A head sprite overlays a body
    /// sprite, with a separate legs sprite added only for the wings-spread pose
    /// (whose body is an arms-raised torso with no legs of its own). The two
    /// wings-down bodies already contain the legs, so they need only a head.
    ///
    /// Three poses, read from the controller every frame:
    ///   • Standing (parked at spawn): calm body, forward-looking head.
    ///   • Flying   (wings open):      spread-wing torso, upward head, flying legs.
    ///   • Diving   (wings folded):    tucked-wing body, upward head.
    ///
    /// Body banking/heading is owned by the controller (it rotates the rigidbody to
    /// face velocity), so this component only assembles and swaps sprites — it never
    /// rotates the root. Each body anchors its neck at a different height, so the
    /// head (and legs) offset is per-pose, measured from each sprite's centre pivot.
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

        [Header("Per-pose head offset (local units from body centre)")]
        [SerializeField] private float _headOffsetStanding = 3.78f;
        [SerializeField] private float _headOffsetFlying = 0.73f;
        [SerializeField] private float _headOffsetDiving = 3.35f;

        [Tooltip("Legs offset below the spread-pose torso (flying only).")]
        [SerializeField] private float _legsOffsetFlying = -4.16f;
        [Tooltip("Legs offset below the standing torso.")]
        [SerializeField] private float _legsOffsetStanding = -3f;

        [Header("Halo")]
        [SerializeField] private Sprite _haloSprite;
        [SerializeField] private float _haloAlpha = 0.16f;

        [Header("Rig")]
        [Tooltip("Uniform scale so the atlas art fits the play field.")]
        [SerializeField] private float _scale = 0.16f;

        [Header("Sorting (back → front)")]
        [SerializeField] private int _orderHalo = 6;
        [SerializeField] private int _orderLegs = 8;
        [SerializeField] private int _orderBody = 10;
        [SerializeField] private int _orderHead = 12;

        private IcarusController _controller;
        private SpriteRenderer _body;
        private SpriteRenderer _head;
        private SpriteRenderer _legs;
        private SpriteRenderer _halo;
        private float _haloT;

        private enum Pose { Standing, Flying, Diving }

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

            Apply(CurrentPose());
        }

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
                    _head.sprite = _headUp;
                    SetHead(_headOffsetStanding);
                    _legs.sprite = _legsStand;
                    _legs.transform.localPosition = new Vector3(0f, _legsOffsetStanding, 0f);
                    break;
                case Pose.Flying:
                    _body.sprite = _bodySpread;
                    _head.sprite = _headUp;
                    SetHead(_headOffsetFlying);
                    _legs.sprite = _legsFly;
                    _legs.transform.localPosition = new Vector3(0f, _legsOffsetFlying, 0f);
                    break;
                case Pose.Diving:
                    _body.sprite = _bodyTucked;
                    _head.sprite = _headUp;
                    SetHead(_headOffsetDiving);
                    _legs.sprite = null;
                    break;
            }
        }

        private void SetHead(float y) =>
            _head.transform.localPosition = new Vector3(0f, y, 0f);

        private void Update()
        {
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
