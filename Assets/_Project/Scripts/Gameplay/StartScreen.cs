using System.Collections;
using Ngj10.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Title screen. Driven by GameConfig — Show() freezes the level and reveals the
    /// panel. Starting: click the START button or press SPACE, then the screen fades
    /// to black and hands off to the LevelController. The volume slider drives the
    /// global AudioListener and persists in PlayerPrefs.
    ///
    /// The volume slider is voiced as a wing: grab it and the wings spread (one-shot)
    /// and, while you drag, the wind loop rises to flight speed; release and the wings
    /// fold (one-shot) and the wind dies. A plain click with no drag is just the
    /// spread — same cue as the START button. Clips/volumes are authored on GameConfig
    /// and pushed in via <see cref="Configure"/>. The wind needs a continuous, fade-able
    /// channel, so it runs on a private looping AudioSource on this object, mirroring
    /// <see cref="WingSfx"/>.
    /// </summary>
    public class StartScreen : MonoBehaviour
    {
        private const string VolumeKey = "MasterVolume";

        [SerializeField] private GameObject _panel;
        [SerializeField] private CanvasGroup _panelGroup;
        [Tooltip("Full-screen menu backdrop (gradient). Hidden together with the panel on start.")]
        [SerializeField] private GameObject _background;
        [SerializeField] private Button _startButton;
        [SerializeField] private Slider _volumeSlider;
        [SerializeField] private LevelController _controller;
        [Tooltip("Full-screen black wipe used for the 1s fade into the level.")]
        [SerializeField] private ScreenFader _screenFader;
        [SerializeField] private float _fadeDuration = 1f;

        private AudioClip _spreadClip;
        private AudioClip _foldClip;
        private float _spreadVolume = 0.5f;
        private float _foldVolume = 0.4f;

        private AudioClip _windClip;
        private float _windMaxVolume = 0.4f;
        private float _windFadeInSeconds = 0.6f;
        private float _windFadeOutSeconds = 0.5f;

        private AudioSource _windSource;

        // Slider grab → release gesture state. _dragging holds the wind up; _changed
        // distinguishes a real drag (fold + wind on release) from a plain click
        // (spread only, no fold, no wind).
        private bool _dragging;
        private bool _changed;

        private bool _starting;

        /// <summary>
        /// Author the slider wing cues — the spread/fold one-shots and the wind loop
        /// that rises while dragging. Mirrors the live <see cref="WingSfx"/> tuning so
        /// the title sounds like the flight. Called by GameConfig.
        /// </summary>
        public void Configure(
            AudioClip spread, AudioClip fold, float spreadVolume, float foldVolume,
            AudioClip wind, float windMaxVolume, float windFadeInSeconds, float windFadeOutSeconds)
        {
            _spreadClip = spread;
            _foldClip = fold;
            _spreadVolume = spreadVolume;
            _foldVolume = foldVolume;

            _windClip = wind;
            _windMaxVolume = windMaxVolume;
            _windFadeInSeconds = windFadeInSeconds;
            _windFadeOutSeconds = windFadeOutSeconds;

            EnsureWindSource();
            _windSource.clip = _windClip;
        }

        private void Awake() => EnsureWindSource();

        private void EnsureWindSource()
        {
            if (_windSource == null)
            {
                _windSource = gameObject.AddComponent<AudioSource>();
                _windSource.loop = true;
                _windSource.playOnAwake = false;
                _windSource.volume = 0f;
            }
        }

        /// <summary>Freeze the level and show the title panel. Called by GameConfig.</summary>
        public void Show()
        {
            Time.timeScale = 0f; // freeze the level while the title screen is up
            if (_panelGroup != null)
                _panelGroup.alpha = 1f;
            if (_panel != null)
                _panel.SetActive(true);
            if (_background != null)
                _background.SetActive(true);
            _starting = false;
        }

        /// <summary>Hide the title panel without starting it (start screen disabled in config).</summary>
        public void Hide()
        {
            if (_panel != null)
                _panel.SetActive(false);
            if (_background != null)
                _background.SetActive(false);
        }

        private void OnEnable()
        {
            if (_startButton != null)
                _startButton.onClick.AddListener(StartGame);
            if (_volumeSlider != null)
            {
                _volumeSlider.SetValueWithoutNotify(AudioListener.volume);
                _volumeSlider.onValueChanged.AddListener(SetVolume);
                AddSliderWingCues(_volumeSlider);
            }
        }

        private void OnDisable()
        {
            if (_startButton != null)
                _startButton.onClick.RemoveListener(StartGame);
            if (_volumeSlider != null)
                _volumeSlider.onValueChanged.RemoveListener(SetVolume);
        }

        // The slider is voiced as a wing. PointerDown = grab → spread; the value
        // changing while held = a real drag → wind rises; PointerUp = release → fold
        // and wind down, but only if it was a drag (a plain click is spread only).
        // EventTrigger is set up in code (no scene wiring) to keep the slider plain.
        private void AddSliderWingCues(Slider slider)
        {
            var trigger = slider.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = slider.gameObject.AddComponent<EventTrigger>();

            AddTrigger(trigger, EventTriggerType.PointerDown, OnSliderDown);
            AddTrigger(trigger, EventTriggerType.PointerUp, OnSliderUp);
            // EndDrag is the backstop: a drag released off the handle/track fires
            // EndDrag but not always PointerUp, which would otherwise strand the wind
            // loop playing. OnSliderUp is idempotent (guarded by _dragging).
            AddTrigger(trigger, EventTriggerType.EndDrag, OnSliderUp);
        }

        private static void AddTrigger(
            EventTrigger trigger, EventTriggerType type, System.Action handler)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => handler());
            trigger.triggers.Add(entry);
        }

        private void OnSliderDown()
        {
            _dragging = true;
            _changed = false;
            PlaySpread();
        }

        private void OnSliderUp()
        {
            if (!_dragging)
                return; // already released (EndDrag + PointerUp can both fire)
            _dragging = false;
            if (_changed)
                PlayFold(); // released after a real drag — wings fold; wind eases out
            _changed = false;
        }

        private void PlaySpread()
        {
            if (_spreadClip != null)
                AudioManager.Instance.PlaySfx(_spreadClip, _spreadVolume);
        }

        private void PlayFold()
        {
            if (_foldClip != null)
                AudioManager.Instance.PlaySfx(_foldClip, _foldVolume);
        }

        private void Update()
        {
            // Space starts the game — but not while a text field has focus, or typing a
            // space into the rename input would also launch the level (double-handling
            // of one key). The focused input owns the keystroke; we stay out of it.
            if (!_starting && Input.GetKeyDown(KeyCode.Space) && !IsTypingInInput())
                StartGame();

            UpdateWind();
        }

        private static bool IsTypingInInput()
        {
            var selected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        // Wind rises toward max only while actively dragging the slider, eases to
        // silence otherwise. Per-frame ease (unscaled — the menu runs at timeScale 0)
        // mirrors WingSfx. The loop runs only while audible.
        private void UpdateWind()
        {
            if (_windClip == null || _windSource == null)
                return;

            float target = (_dragging && _changed) ? _windMaxVolume : 0f;
            bool rising = target > _windSource.volume;
            float seconds = Mathf.Max(0.01f, rising ? _windFadeInSeconds : _windFadeOutSeconds);
            _windSource.volume = Mathf.MoveTowards(
                _windSource.volume, target, Time.unscaledDeltaTime / seconds);

            if (_windSource.volume > 0.0001f)
            {
                if (!_windSource.isPlaying)
                    _windSource.Play();
            }
            else if (_windSource.isPlaying)
            {
                _windSource.Stop();
            }
        }

        private void SetVolume(float value)
        {
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(VolumeKey, value);
            if (_dragging)
                _changed = true; // value moved while held → it's a drag, not a click
        }

        private void StartGame()
        {
            if (_starting)
                return;
            _starting = true;
            if (_startButton != null)
                _startButton.interactable = false;
            PlaySpread();
            StartCoroutine(FadeOutAndBegin());
        }

        // Fade the whole screen to black over _fadeDuration (1s), hand off to the
        // level under cover of black, then fade back in. The panel is dropped at the
        // top of the fade so the title doesn't sit over the black wipe.
        private IEnumerator FadeOutAndBegin()
        {
            if (_panelGroup != null)
            {
                _panelGroup.interactable = false;
                _panelGroup.blocksRaycasts = false;
            }

            if (_screenFader != null)
                yield return _screenFader.FadeOut(_fadeDuration);

            if (_panelGroup != null)
                _panelGroup.alpha = 0f;
            if (_panel != null)
                _panel.SetActive(false);
            if (_background != null)
                _background.SetActive(false);

            if (_controller != null)
                _controller.Begin();

            if (_screenFader != null)
                yield return _screenFader.FadeIn(_fadeDuration);
        }
    }
}
