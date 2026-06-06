using UnityEngine;

namespace Ngj10.Core
{
    /// <summary>
    /// Держит фиксированное соотношение сторон 16:9. Если окно/дисплей шире — чёрные
    /// полосы сверху/снизу (letterbox), если уже/перевёрнут — слева/справа (pillarbox).
    /// Вешать на Main Camera. UI-канвасы привязываются к ней через
    /// <see cref="LetterboxCanvasBinder"/> (берут камеру из <see cref="Current"/>).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LetterboxCamera : MonoBehaviour
    {
        /// <summary>Активная letterbox-камера. null, пока сцена с ней не загружена.</summary>
        public static LetterboxCamera Current { get; private set; }

        [SerializeField] private float _targetAspect = 16f / 9f;

        private Camera _camera;
        private Camera _barCamera;
        private int _lastWidth;
        private int _lastHeight;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            EnsureBarCamera();
            Apply();
        }

        private void OnEnable()
        {
            Current = this;
        }

        private void OnDisable()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        private void Update()
        {
            // Пересчитываем только при смене размера окна — не каждый кадр впустую.
            if (Screen.width != _lastWidth || Screen.height != _lastHeight)
                Apply();
        }

        /// <summary>
        /// Фоновая камера на весь экран заливает чёрным область вне основного rect
        /// каждый кадр — иначе на барах остаются покадровые артефакты (бар не чистится).
        /// </summary>
        private void EnsureBarCamera()
        {
            var go = new GameObject("LetterboxBarCamera");
            go.transform.SetParent(transform, false);
            _barCamera = go.AddComponent<Camera>();
            _barCamera.clearFlags = CameraClearFlags.SolidColor;
            _barCamera.backgroundColor = Color.black;
            _barCamera.cullingMask = 0;            // ничего не рендерит, только чистит
            _barCamera.depth = _camera.depth - 1;  // под основной
            _barCamera.rect = new Rect(0f, 0f, 1f, 1f);
            _barCamera.orthographic = true;
            _barCamera.allowHDR = false;
            _barCamera.allowMSAA = false;
        }

        private void Apply()
        {
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;

            float windowAspect = (float)Screen.width / Screen.height;
            float scaleHeight = windowAspect / _targetAspect;

            if (scaleHeight < 1f)
            {
                // Окно уже целевого — полосы сверху/снизу.
                _camera.rect = new Rect(0f, (1f - scaleHeight) / 2f, 1f, scaleHeight);
            }
            else
            {
                // Окно шире целевого — полосы слева/справа.
                float scaleWidth = 1f / scaleHeight;
                _camera.rect = new Rect((1f - scaleWidth) / 2f, 0f, scaleWidth, 1f);
            }
        }
    }
}
