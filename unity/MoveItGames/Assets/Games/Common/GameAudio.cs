using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>
    /// Music and sound effects, loaded by NAME from <c>Assets/Resources/Music</c> and <c>Assets/Resources/Sfx</c>.
    /// To change a track, replace the file with any audio file of the same name (ogg, wav or mp3): no code changes.
    /// The sources (editable MIDI + the synth that renders them) live in <c>music/</c>; see music/README.md.
    ///
    /// Music crossfades between tracks and loops; jingles play once over a ducked track; sound effects come from a
    /// small pool. Volumes are remembered (PlayerPrefs). Missing files are skipped with one warning.
    /// </summary>
    public static class GameAudio
    {
        const string MusicKey = "audio.music", SfxKey = "audio.sfx";

        static AudioRig _rig;

        static AudioRig Rig
        {
            get
            {
                if (_rig == null)
                {
                    var go = new GameObject("GameAudio") { hideFlags = HideFlags.DontSave };
                    Object.DontDestroyOnLoad(go);
                    _rig = go.AddComponent<AudioRig>();
                }
                return _rig;
            }
        }

        /// <summary>0..1, remembered.</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, 0.7f);
            set
            {
                PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value));
                PlayerPrefs.Save();
            }
        }

        /// <summary>0..1, remembered.</summary>
        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(SfxKey, 1f);
            set
            {
                PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value));
                PlayerPrefs.Save();
            }
        }

        /// <summary>Plays Resources/Music/&lt;track&gt; in a loop (crossfading from the current one). Same track: nothing.</summary>
        public static void PlayMusic(string track, float fadeSeconds = 1f) => Rig.PlayMusic(track, fadeSeconds);

        public static void StopMusic(float fadeSeconds = 0.6f) => Rig.PlayMusic(null, fadeSeconds);

        /// <summary>Plays a short tune once (Resources/Music/&lt;name&gt;), the music ducks underneath it.</summary>
        public static void Jingle(string name, float duckTo = 0.2f) => Rig.Jingle(name, duckTo);

        /// <summary>Plays Resources/Sfx/&lt;name&gt;. vary: random pitch spread (0.05 = ±5%) so repeats don't sound robotic.</summary>
        public static void Sfx(string name, float volume = 1f, float pitch = 1f, float vary = 0.04f) =>
            Rig.Sfx(name, volume, pitch * (1f + Random.Range(-vary, vary)));
    }

    sealed class AudioRig : MonoBehaviour
    {
        const int SfxVoices = 10;

        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly HashSet<string> _missing = new HashSet<string>();
        AudioSource[] _music;
        AudioSource _jingle;
        AudioSource[] _sfx;
        int _active, _nextVoice;
        string _track;
        float _fade = 1f, _fadeSpeed = 1f, _duck = 1f;

        void Awake()
        {
            _music = new[] { NewSource(true), NewSource(true) };
            _jingle = NewSource(false);
            _sfx = new AudioSource[SfxVoices];
            for (int i = 0; i < SfxVoices; i++) _sfx[i] = NewSource(false);
        }

        AudioSource NewSource(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            return s;
        }

        AudioClip Clip(string folder, string name)
        {
            string key = folder + "/" + name;
            if (_clips.TryGetValue(key, out var clip)) return clip;
            clip = Resources.Load<AudioClip>(key);
            if (clip == null && _missing.Add(key)) Debug.LogWarning($"[GameAudio] No sound at Resources/{key}");
            _clips[key] = clip;
            return clip;
        }

        public void PlayMusic(string track, float fadeSeconds)
        {
            if (track == _track) return;
            _track = track;
            var clip = track != null ? Clip("Music", track) : null;
            _active = 1 - _active;                 // the old source fades out, the new one fades in
            var next = _music[_active];
            next.Stop();
            next.clip = clip;
            if (clip != null) next.Play();
            _fade = 0f;
            _fadeSpeed = 1f / Mathf.Max(0.05f, fadeSeconds);
        }

        public void Jingle(string name, float duckTo)
        {
            var clip = Clip("Music", name);
            if (clip == null) return;
            _jingle.clip = clip;
            _jingle.volume = GameAudio.MusicVolume;
            _jingle.Play();
            _duck = duckTo;
        }

        public void Sfx(string name, float volume, float pitch)
        {
            var clip = Clip("Sfx", name);
            if (clip == null) return;
            var voice = _sfx[_nextVoice];
            _nextVoice = (_nextVoice + 1) % SfxVoices;
            voice.pitch = pitch;
            voice.PlayOneShot(clip, volume * GameAudio.SfxVolume);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _fade = Mathf.MoveTowards(_fade, 1f, _fadeSpeed * dt);
            if (!_jingle.isPlaying) _duck = Mathf.MoveTowards(_duck, 1f, dt * 1.5f);
            float level = GameAudio.MusicVolume * _duck;
            _music[_active].volume = level * _fade;
            var old = _music[1 - _active];
            old.volume = level * (1f - _fade);
            if (_fade >= 1f && old.isPlaying) old.Stop();
        }
    }
}
