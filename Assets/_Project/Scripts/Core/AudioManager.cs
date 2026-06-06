using UnityEngine;

namespace Ngj10.Core
{
    /// <summary>
    /// Persistent audio hub. Survives scene loads (created from Boot, same pattern
    /// as <see cref="GameManager"/>). Holds two AudioSources built in code: one
    /// looped music bed, one fire-and-forget SFX channel.
    ///
    /// WebGL note: browsers block audio until the first user gesture. SFX fired
    /// from a click (e.g. popping a balloon) is already a gesture, so it plays.
    /// Music started before any input may stay silent until the first click —
    /// call <see cref="PlayMusic"/> from a gesture, or accept the first-click delay.
    /// SFX plays on this persistent object, so a clip outlives the GameObject that
    /// triggered it (a balloon destroyed on click won't cut its own pop short).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
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

        private AudioSource _musicSource;
        private AudioSource _sfxSource;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
        }

        /// <summary>One-shot sound effect. Null clip is ignored (no art = no crash).</summary>
        public void PlaySfx(AudioClip clip, float volume = 1f)
        {
            if (clip != null)
            {
                _sfxSource.PlayOneShot(clip, volume);
            }
        }

        /// <summary>Start (or swap) the looping music bed. Null clip stops music.</summary>
        public void PlayMusic(AudioClip clip)
        {
            if (clip == null)
            {
                _musicSource.Stop();
                return;
            }

            _musicSource.clip = clip;
            _musicSource.Play();
        }

        public void SetMusicVolume(float volume) => _musicSource.volume = volume;

        public void SetSfxVolume(float volume) => _sfxSource.volume = volume;
    }
}
