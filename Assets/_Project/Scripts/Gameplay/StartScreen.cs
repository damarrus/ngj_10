using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Title screen: game title, master-volume slider, Start button. Driven by
    /// GameConfig — Show() freezes the level and reveals the panel, the Start button
    /// fades it out and hands off to the LevelController. The volume slider drives the
    /// global AudioListener and persists in PlayerPrefs.
    /// </summary>
    public class StartScreen : MonoBehaviour
    {
        private const string VolumeKey = "MasterVolume";

        [SerializeField] private GameObject _panel;
        [SerializeField] private CanvasGroup _panelGroup;
        [SerializeField] private Button _startButton;
        [SerializeField] private Slider _volumeSlider;
        [SerializeField] private LevelController _controller;
        [SerializeField] private float _fadeDuration = 0.6f;

        private bool _starting;

        /// <summary>Freeze the level and show the title panel. Called by GameConfig.</summary>
        public void Show()
        {
            Time.timeScale = 0f; // freeze the level while the title screen is up
            if (_panelGroup != null)
                _panelGroup.alpha = 1f;
            if (_panel != null)
                _panel.SetActive(true);
        }

        /// <summary>Hide the title panel without starting it (start screen disabled in config).</summary>
        public void Hide()
        {
            if (_panel != null)
                _panel.SetActive(false);
        }

        private void OnEnable()
        {
            if (_startButton != null)
                _startButton.onClick.AddListener(StartGame);
            if (_volumeSlider != null)
            {
                _volumeSlider.SetValueWithoutNotify(AudioListener.volume);
                _volumeSlider.onValueChanged.AddListener(SetVolume);
            }
        }

        private void OnDisable()
        {
            if (_startButton != null)
                _startButton.onClick.RemoveListener(StartGame);
            if (_volumeSlider != null)
                _volumeSlider.onValueChanged.RemoveListener(SetVolume);
        }

        private void SetVolume(float value)
        {
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(VolumeKey, value);
        }

        private void StartGame()
        {
            if (_starting)
                return;
            _starting = true;
            if (_startButton != null)
                _startButton.interactable = false;
            StartCoroutine(FadeOutAndBegin());
        }

        private IEnumerator FadeOutAndBegin()
        {
            if (_panelGroup != null)
            {
                _panelGroup.interactable = false;
                _panelGroup.blocksRaycasts = false;

                float t = 0f;
                while (t < _fadeDuration)
                {
                    t += Time.unscaledDeltaTime; // game is frozen (timeScale 0)
                    _panelGroup.alpha = Mathf.Clamp01(1f - t / _fadeDuration);
                    yield return null;
                }
                _panelGroup.alpha = 0f;
            }

            if (_panel != null)
                _panel.SetActive(false);
            if (_controller != null)
                _controller.Begin();
        }
    }
}
