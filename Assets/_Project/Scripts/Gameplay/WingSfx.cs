using Ngj10.Core;
using UnityEngine;

namespace Ngj10.Gameplay
{
    /// <summary>
    /// Wing audio off the controller's <see cref="IcarusController.WingsToggled"/>
    /// event: a spread one-shot on open, a fold one-shot on close, and a looping
    /// wind bed that lives only while the wings are open — faded up just after the
    /// spread, faded down after the fold.
    ///
    /// The two one-shots go through <see cref="AudioManager.PlaySfx"/> (shared SFX
    /// bus). The wind loop needs its own continuous, fade-able channel, so it runs on
    /// a private looping <see cref="AudioSource"/> on this object — a single source
    /// with loop=true is sample-accurate seamless, which is what a long flight needs.
    /// Clips, volume and fade timing are authored on <see cref="GameConfig"/> (the one
    /// tuning place) and pushed in via <see cref="Configure"/>, mirroring Burn/Shock.
    ///
    /// Wind volume tracks speed: it eases toward maxVolume * (speed / fullSpeed) while
    /// the wings are open and toward zero while folded. A per-frame ease (not a
    /// one-shot fade) is used because the target keeps moving with the flight speed.
    ///
    /// Fold also fires on a Zeus shock (the wings really do fold), which is the
    /// intended cue — and correctly drops the wind out too.
    /// </summary>
    [RequireComponent(typeof(IcarusController))]
    public class WingSfx : MonoBehaviour
    {
        private IcarusController _controller;
        private AudioSource _windSource;
        private AudioSource _spreadSource; // own source so the spread clip can start mid-clip (negative offset)

        private AudioClip _spreadClip;
        private AudioClip _foldClip;
        private float _spreadVolume = 1f;
        private float _foldVolume = 1f;
        private float _spreadDelay;
        private float _spreadMaxLength;

        private AudioClip _windClip;
        private float _windMaxVolume = 0.4f;
        private float _windFullSpeed = 12f;
        private float _windFadeInSeconds = 0.6f;
        private float _windFadeOutSeconds = 0.5f;
        private float _windStartDelay;
        private float _wingsOpenedTime = -999f; // when the wings last opened (for the wind delay)

        /// <summary>Author clips, volumes and wind speed/fade tuning (called by GameConfig at startup).</summary>
        public void Configure(
            AudioClip spread, AudioClip fold, float spreadVolume, float foldVolume,
            float spreadDelay, float spreadMaxLength,
            AudioClip wind, float windMaxVolume, float windFullSpeed,
            float windFadeInSeconds, float windFadeOutSeconds, float windStartDelay)
        {
            _spreadClip = spread;
            _foldClip = fold;
            _spreadVolume = spreadVolume;
            _foldVolume = foldVolume;
            _spreadDelay = spreadDelay;
            _spreadMaxLength = spreadMaxLength;

            _windClip = wind;
            _windMaxVolume = windMaxVolume;
            _windFullSpeed = Mathf.Max(0.01f, windFullSpeed);
            _windFadeInSeconds = windFadeInSeconds;
            _windFadeOutSeconds = windFadeOutSeconds;
            _windStartDelay = windStartDelay;

            EnsureWindSource();
            _windSource.clip = _windClip;
        }

        private void Awake()
        {
            _controller = GetComponent<IcarusController>();
            EnsureWindSource();
        }

        // GameConfig (exec order -100) calls Configure before our own Awake runs, so
        // build the source lazily instead of trusting Awake order.
        private void EnsureWindSource()
        {
            if (_windSource == null)
            {
                _windSource = gameObject.AddComponent<AudioSource>();
                _windSource.loop = true;
                _windSource.playOnAwake = false;
                _windSource.volume = 0f;
            }
            if (_spreadSource == null)
            {
                _spreadSource = gameObject.AddComponent<AudioSource>();
                _spreadSource.loop = false;
                _spreadSource.playOnAwake = false;
            }
        }

        private void OnEnable()
        {
            _controller.WingsToggled += OnWingsToggled;
        }

        private void OnDisable()
        {
            _controller.WingsToggled -= OnWingsToggled;
        }

        private void OnWingsToggled(bool open)
        {
            if (open)
            {
                _wingsOpenedTime = Time.time;
                if (_spreadDelay > 0f)
                    Invoke(nameof(PlaySpread), _spreadDelay);
                else
                    PlaySpread();
            }
            else
            {
                CancelInvoke(nameof(PlaySpread)); // folded before the delayed spread fired
                AudioManager.Instance.PlaySfx(_foldClip, _foldVolume);
            }
        }

        // Negative _spreadDelay = start the clip that far in (skip a quiet lead-in).
        private void PlaySpread()
        {
            if (_spreadClip == null)
                return;
            _spreadSource.clip = _spreadClip;
            _spreadSource.volume = _spreadVolume;
            _spreadSource.time = _spreadDelay < 0f
                ? Mathf.Clamp(-_spreadDelay, 0f, Mathf.Max(0f, _spreadClip.length - 0.01f))
                : 0f;
            _spreadSource.Play();
            if (_spreadMaxLength > 0f) // cut the one-shot off after this many seconds
                _spreadSource.SetScheduledEndTime(AudioSettings.dspTime + _spreadMaxLength);
        }

        private void Update()
        {
            if (_windClip == null)
                return;

            // Target volume: speed-scaled while open, silent while folded.
            float target = 0f;
            // Hold the wind silent until the start delay after opening has elapsed.
            if (_controller.WingsOpen && Time.time - _wingsOpenedTime >= _windStartDelay)
            {
                float speed = _controller.Body != null ? _controller.Body.linearVelocity.magnitude : 0f;
                target = _windMaxVolume * Mathf.Clamp01(speed / _windFullSpeed);
            }

            // Ease toward the target — faster easing on the way down (fold) than up
            // (spread). A time-constant per direction keeps the move framerate-stable.
            bool rising = target > _windSource.volume;
            float seconds = Mathf.Max(0.01f, rising ? _windFadeInSeconds : _windFadeOutSeconds);
            _windSource.volume = Mathf.MoveTowards(
                _windSource.volume, target, Time.deltaTime / seconds);

            // Run the loop only while audible; stop once it eases to silence.
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
    }
}
