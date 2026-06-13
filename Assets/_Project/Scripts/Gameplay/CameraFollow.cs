using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Rigid camera follow — nailed to the target, no smoothing. The level mode
    /// decides which axes track: Free follows XY, UpOnly follows Y (X locked to
    /// its start), SingleScreen stays put.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _offset = new Vector3(0f, 0f, -10f);
        [SerializeField] private LevelMode _mode = LevelMode.Free;

        // Anchor captured at level start so UpOnly/SingleScreen keep their framing.
        private float _lockedX;
        private float _lockedY;

        // Lowest allowed camera-center Y, so the bottom edge never drops below the
        // kill line. Set by the controller; float.MinValue = no clamp.
        private float _minCenterY = float.MinValue;

        private void Awake() => CaptureAnchor();

        /// <summary>Set the follow mode, the lowest allowed camera-center Y, and the world
        /// anchor (the level's Start) that UpOnly/SingleScreen lock onto. Snaps the camera
        /// to the anchor at once so the first frame is framed on the spawn, not the camera's
        /// authored scene position.</summary>
        public void SetMode(LevelMode mode, float minCenterY, Vector2 anchor)
        {
            _mode = mode;
            _minCenterY = minCenterY;
            _lockedX = anchor.x;
            _lockedY = Mathf.Max(anchor.y, minCenterY);
            if (mode != LevelMode.Free)
                transform.position = new Vector3(_lockedX, _lockedY, transform.position.z);
        }

        private void CaptureAnchor()
        {
            _lockedX = transform.position.x;
            _lockedY = transform.position.y;
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            Vector3 followed = _target.position + _offset;
            switch (_mode)
            {
                case LevelMode.Free:
                    transform.position = new Vector3(followed.x, ClampY(followed.y), followed.z);
                    break;
                case LevelMode.UpOnly:
                    transform.position = new Vector3(_lockedX, ClampY(followed.y), followed.z);
                    break;
                case LevelMode.SingleScreen:
                    transform.position = new Vector3(_lockedX, _lockedY, followed.z);
                    break;
            }
        }

        private float ClampY(float y) => Mathf.Max(y, _minCenterY);
    }
}
