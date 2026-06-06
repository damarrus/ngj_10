using UnityEngine;

namespace Ngj10.Core
{
    /// <summary>
    /// Привязывает Canvas к активной <see cref="LetterboxCamera"/>, чтобы UI рендерился
    /// внутри 16:9 зоны и обрезался барами (не лез на чёрные полосы).
    ///
    /// Нужен для persistent-канвасов из Boot: камера живёт в Game-сцене и меняется
    /// между сценами, поэтому привязку держим в рантайме, а не ссылкой в инспекторе.
    /// Пока камеры нет (Boot до Game) — Canvas остаётся Overlay, чтобы не пропасть.
    ///
    /// Использование: <c>canvasGo.AddComponent&lt;LetterboxCanvasBinder&gt;()</c> сразу
    /// после <c>AddComponent&lt;Canvas&gt;()</c>.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class LetterboxCanvasBinder : MonoBehaviour
    {
        private Canvas _canvas;
        private LetterboxCamera _bound;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
        }

        private void OnEnable()
        {
            Rebind();
        }

        private void Update()
        {
            // Камера сменилась (новая сцена) или появилась/пропала — перепривязать.
            if (LetterboxCamera.Current != _bound)
            {
                Rebind();
            }
        }

        private void Rebind()
        {
            _bound = LetterboxCamera.Current;

            if (_bound != null)
            {
                _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                _canvas.worldCamera = _bound.GetComponent<Camera>();
                _canvas.planeDistance = 10f;
                _canvas.enabled = true;
            }
            else
            {
                // Камеры пока нет (splash / Boot) — прячем, иначе UI мелькнёт на весь
                // экран и «прыгнет» в зону при загрузке Game.
                _canvas.enabled = false;
            }
        }
    }
}
