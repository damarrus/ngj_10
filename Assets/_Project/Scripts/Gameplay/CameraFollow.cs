using System.Collections;
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

        // Menu framing: while the title screen is up the follow is frozen and the
        // camera is reframed (zoom + offset) so Icarus poses for the title art,
        // instead of mid-screen as gameplay frames him. Begin() exits it. The zoom
        // and offset come from GameConfig (the one place these are tuned).
        private bool _inMenu;
        private float _gameplayOrthoSize; // saved on entering the menu, restored on exit
        private Camera _camera;

        // Lazily fetched, not cached in Awake: GameConfig runs at execution order -100
        // and calls EnterMenu before this component's Awake, so a cached _camera would
        // still be null then. The property guarantees a live reference whenever needed.
        private Camera Cam => _camera != null ? _camera : (_camera = GetComponent<Camera>());

        private void Awake() => CaptureAnchor();

        /// <summary>Freeze the follow and reframe the camera for the title screen: zoom to
        /// <paramref name="orthoSize"/> and offset off the spawn (offsetX &gt; 0 pushes Icarus
        /// left, offsetY &gt; 0 pushes him down). Called by GameConfig while the menu is up.</summary>
        public void EnterMenu(Vector2 spawn, float orthoSize, float offsetX, float offsetY)
        {
            _inMenu = true;
            if (Cam != null)
            {
                _gameplayOrthoSize = Cam.orthographicSize;
                Cam.orthographicSize = orthoSize;
            }
            transform.position = new Vector3(spawn.x + offsetX, spawn.y + offsetY, transform.position.z);
        }

        /// <summary>Leave the menu framing at once: restore the gameplay zoom and snap.
        /// Used when no transition is wanted (e.g. auto-start without a title screen).</summary>
        public void ExitMenu()
        {
            _inMenu = false;
            if (Cam != null && _gameplayOrthoSize > 0f)
                Cam.orthographicSize = _gameplayOrthoSize;
            SetMode(_mode, _minCenterY, new Vector2(_lockedX, _lockedY));
        }

        /// <summary>Glide the camera from the menu framing back to the gameplay position
        /// and zoom over <paramref name="duration"/> seconds (unscaled — the level is still
        /// frozen during the transition), then leave the menu state so the follow resumes.
        /// Runs only while in the menu; a zero/negative duration falls back to an instant
        /// <see cref="ExitMenu"/>.</summary>
        public IEnumerator AnimateExitMenu(float duration)
        {
            if (!_inMenu || duration <= 0f)
            {
                ExitMenu();
                yield break;
            }

            Vector3 startPos = transform.position;
            float startOrtho = Cam != null ? Cam.orthographicSize : 0f;
            Vector3 endPos = GameplayPosition();
            float endOrtho = _gameplayOrthoSize > 0f ? _gameplayOrthoSize : startOrtho;

            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                transform.position = Vector3.Lerp(startPos, endPos, k);
                if (Cam != null)
                    Cam.orthographicSize = Mathf.Lerp(startOrtho, endOrtho, k);
                yield return null;
            }

            // Land exactly on the gameplay framing, then hand control back to LateUpdate.
            if (Cam != null)
                Cam.orthographicSize = endOrtho;
            _inMenu = false;
            SetMode(_mode, _minCenterY, new Vector2(_lockedX, _lockedY));
        }

        /// <summary>The camera position gameplay would frame this instant for the current
        /// mode — the destination of the menu transition. Mirrors <see cref="LateUpdate"/>.</summary>
        private Vector3 GameplayPosition()
        {
            float z = transform.position.z;
            switch (_mode)
            {
                case LevelMode.Free:
                    Vector3 followed = (_target != null ? _target.position : new Vector3(_lockedX, _lockedY)) + _offset;
                    return new Vector3(followed.x, ClampY(followed.y), z);
                default: // UpOnly / SingleScreen lock onto the anchor
                    return new Vector3(_lockedX, _lockedY, z);
            }
        }

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
            if (_inMenu || _target == null)
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
