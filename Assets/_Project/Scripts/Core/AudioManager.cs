using System.Collections;
using UnityEngine;

namespace Ngj10.Core
{
    /// <summary>
    /// Persistent audio hub. Survives scene loads (created from Boot, same pattern
    /// as <see cref="GameManager"/>). Owns the music bed and a fire-and-forget SFX
    /// channel, all built in code.
    ///
    /// Music uses two AudioSources (A/B) so one track can crossfade into another
    /// without a gap: the incoming track fades up on the idle source while the
    /// outgoing track fades down on the active one. Each music source carries an
    /// <see cref="AudioLowPassFilter"/> — animating its cutoff down gives the
    /// "muffled, heard through a closed door" effect used while paused, far more
    /// convincing than a plain volume duck.
    ///
    /// WebGL note: browsers block audio until the first user gesture. The
    /// Click-to-start screen is that gesture, so calling <see cref="CrossfadeTo"/>
    /// from it is safe. Pause sets Time.timeScale = 0, which freezes Time.deltaTime;
    /// every fade here runs on Time.unscaledDeltaTime so muffling still animates
    /// while the game is paused. SFX plays on this persistent object, so a clip
    /// outlives the GameObject that triggered it.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Header("Music")]
        [Tooltip("Target volume of the music bed at full (un-muffled) playback.")]
        [SerializeField, Range(0f, 1f)] private float _musicVolume = 0.6f;

        [Tooltip("Default seconds for a track-to-track crossfade.")]
        [SerializeField] private float _crossfadeSeconds = 1.5f;

        [Header("Muffle (\"behind a door\")")]
        [Tooltip("Low-pass cutoff while muffled. Lower = more muffled. ~700 Hz reads as 'through a door'.")]
        [SerializeField] private float _muffledCutoffHz = 700f;

        [Tooltip("Extra volume duck applied on top of the low-pass while muffled (0.5 = half volume).")]
        [SerializeField, Range(0f, 1f)] private float _muffledVolumeScale = 0.5f;

        [Tooltip("Seconds for the muffle / un-muffle transition.")]
        [SerializeField] private float _muffleSeconds = 0.4f;

        // Cutoff that counts as "fully open" — above the audible range, so no filtering.
        private const float OpenCutoffHz = 22000f;

        private static AudioManager _instance;

        /// <summary>
        /// Persistent instance. Boot creates it explicitly; this lazy fallback
        /// also spawns it the first time it's needed when Game is started
        /// directly (no Boot) — common in the Editor. Always non-null.
        /// </summary>
        public static AudioManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject(nameof(AudioManager));
                    go.AddComponent<AudioManager>(); // Awake sets _instance
                }

                return _instance;
            }
        }

        private AudioSource _musicA;
        private AudioSource _musicB;
        private AudioLowPassFilter _lowPassA;
        private AudioLowPassFilter _lowPassB;
        private AudioSource _sfxSource;

        // Which of the two music sources currently holds the playing track.
        private AudioSource _activeMusic;
        private AudioLowPassFilter _activeLowPass;

        private Coroutine _crossfadeRoutine;
        private Coroutine _muffleRoutine;
        private bool _muffled;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            (_musicA, _lowPassA) = BuildMusicSource("MusicA");
            (_musicB, _lowPassB) = BuildMusicSource("MusicB");
            _activeMusic = _musicA;
            _activeLowPass = _lowPassA;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
        }

        // Each music source lives on its own child GameObject together with its
        // filter. An AudioLowPassFilter processes only the AudioSource(s) on the
        // same GameObject, so isolating them per child is what lets us muffle the
        // active track without touching the other — the clean way to keep two
        // independently-filtered crossfade channels.
        private (AudioSource source, AudioLowPassFilter filter) BuildMusicSource(string childName)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);

            var source = child.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;

            var filter = child.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = OpenCutoffHz;
            return (source, filter);
        }

        /// <summary>One-shot sound effect. Null clip is ignored (no art = no crash).</summary>
        public void PlaySfx(AudioClip clip, float volume = 1f)
        {
            if (clip != null)
            {
                _sfxSource.PlayOneShot(clip, volume);
            }
        }

        /// <summary>
        /// A musical stinger (e.g. a victory fanfare) played over the current
        /// music as a one-shot. Use this for the win sound before crossfading
        /// back to the menu track. Null clip is ignored.
        /// </summary>
        public void PlayStinger(AudioClip clip, float volume = 1f)
        {
            if (clip != null)
            {
                _sfxSource.PlayOneShot(clip, volume);
            }
        }

        /// <summary>
        /// Crossfade the music bed to <paramref name="clip"/>. The incoming track
        /// fades up on the idle source while the current one fades out, so there
        /// is no silent gap. Passing the clip that is already playing is a no-op.
        /// Null clip fades the music out to silence.
        /// </summary>
        public void CrossfadeTo(AudioClip clip, float duration = -1f)
        {
            if (duration < 0f)
            {
                duration = _crossfadeSeconds;
            }

            if (clip != null && _activeMusic.isPlaying && _activeMusic.clip == clip)
            {
                return;
            }

            if (_crossfadeRoutine != null)
            {
                StopCoroutine(_crossfadeRoutine);
            }

            _crossfadeRoutine = StartCoroutine(CrossfadeRoutine(clip, duration));
        }

        private IEnumerator CrossfadeRoutine(AudioClip clip, float duration)
        {
            AudioSource outgoing = _activeMusic;
            AudioLowPassFilter outgoingFilter = _activeLowPass;
            AudioSource incoming = outgoing == _musicA ? _musicB : _musicA;
            AudioLowPassFilter incomingFilter = incoming == _musicA ? _lowPassA : _lowPassB;

            // The new track always comes in clean (un-muffled). A crossfade
            // implicitly resets the muffled state.
            _muffled = false;
            incomingFilter.cutoffFrequency = OpenCutoffHz;

            float startOutVolume = outgoing.volume;
            float targetInVolume = clip != null ? _musicVolume : 0f;

            if (clip != null)
            {
                incoming.clip = clip;
                incoming.volume = 0f;
                incoming.Play();
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                outgoing.volume = Mathf.Lerp(startOutVolume, 0f, k);
                if (clip != null)
                {
                    incoming.volume = Mathf.Lerp(0f, targetInVolume, k);
                }
                yield return null;
            }

            outgoing.Stop();
            outgoing.volume = 0f;
            outgoingFilter.cutoffFrequency = OpenCutoffHz;

            if (clip != null)
            {
                incoming.volume = targetInVolume;
                _activeMusic = incoming;
                _activeLowPass = incomingFilter;
            }

            _crossfadeRoutine = null;
        }

        /// <summary>
        /// Muffle the active music as if heard through a closed door: low-pass the
        /// highs out and duck the volume, both eased in. Idempotent.
        /// </summary>
        public void MuffleMusic()
        {
            if (_muffled)
            {
                return;
            }

            _muffled = true;
            StartMuffleFade(_muffledCutoffHz, _musicVolume * _muffledVolumeScale);
        }

        /// <summary>Restore the active music to full, clear playback. Idempotent.</summary>
        public void UnmuffleMusic()
        {
            if (!_muffled)
            {
                return;
            }

            _muffled = false;
            StartMuffleFade(OpenCutoffHz, _musicVolume);
        }

        private void StartMuffleFade(float targetCutoff, float targetVolume)
        {
            if (_muffleRoutine != null)
            {
                StopCoroutine(_muffleRoutine);
            }

            _muffleRoutine = StartCoroutine(MuffleRoutine(targetCutoff, targetVolume));
        }

        private IEnumerator MuffleRoutine(float targetCutoff, float targetVolume)
        {
            // Capture the source we are muffling now; a crossfade mid-muffle would
            // swap _activeMusic, but this routine keeps acting on the source it
            // started with (the crossfade resets muffle state on its own).
            AudioSource source = _activeMusic;
            AudioLowPassFilter filter = _activeLowPass;

            float startCutoff = filter.cutoffFrequency;
            float startVolume = source.volume;

            float t = 0f;
            while (t < _muffleSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / _muffleSeconds);
                filter.cutoffFrequency = Mathf.Lerp(startCutoff, targetCutoff, k);
                source.volume = Mathf.Lerp(startVolume, targetVolume, k);
                yield return null;
            }

            filter.cutoffFrequency = targetCutoff;
            source.volume = targetVolume;
            _muffleRoutine = null;
        }
    }
}
