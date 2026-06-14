using System.Collections;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// In-game copy of the start-screen control hint. Same block, same place — it
    /// fades in when the level is ready (level start out of black, or after a death
    /// respawn out of black) and, once the player starts flying (the first hold off
    /// the spawn point), fades out after a short delay. Dying reveals it again.
    ///
    /// The visual press loop is driven by the sibling <see cref="ControlsHint"/> on
    /// the same object; this only owns visibility (a CanvasGroup) and the
    /// reveal/auto-hide timing. <see cref="LevelController"/> calls <see cref="Reveal"/>
    /// at each fade-in; the first player hold is detected off the player itself.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class GameplayControlsHint : MonoBehaviour
    {
        [SerializeField] private IcarusController _player;
        [Tooltip("Seconds the hint lingers after the player starts flying, before fading out.")]
        [SerializeField] private float _hideDelay = 1.5f;
        [Tooltip("Fade in/out duration (seconds).")]
        [SerializeField] private float _fadeDuration = 0.4f;

        private CanvasGroup _group;
        private Coroutine _fade;
        private Coroutine _autoHide;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false; // never eat gameplay clicks
        }

        /// <summary>
        /// Show the hint (fade in) and arm the auto-hide: once the player takes the
        /// first hold off the spawn point, the hint lingers <see cref="_hideDelay"/>
        /// seconds and fades out. Called by LevelController at each fade-in.
        /// </summary>
        public void Reveal()
        {
            StartFade(1f);

            if (_autoHide != null)
                StopCoroutine(_autoHide);
            _autoHide = StartCoroutine(WaitForFlightThenHide());
        }

        private IEnumerator WaitForFlightThenHide()
        {
            // The player parks at spawn (IsWaitingForInput) until the first hold.
            while (_player != null && _player.IsWaitingForInput)
                yield return null;

            float t = 0f;
            while (t < _hideDelay)
            {
                t += Time.deltaTime; // gameplay time — paused with the level
                yield return null;
            }

            StartFade(0f);
            _autoHide = null;
        }

        private void StartFade(float target)
        {
            if (_fade != null)
                StopCoroutine(_fade);
            _fade = StartCoroutine(FadeTo(target));
        }

        private IEnumerator FadeTo(float target)
        {
            float start = _group.alpha;
            for (float t = 0f; t < _fadeDuration; t += Time.unscaledDeltaTime)
            {
                _group.alpha = Mathf.Lerp(start, target, t / _fadeDuration);
                yield return null;
            }
            _group.alpha = target;
            _fade = null;
        }
    }
}
