using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HoMM5.Core;
using HoMM5.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace HoMM5.Audio
{
    /// <summary>
    /// Music and sound effects for the adventure map, the town screen and battles.
    ///
    /// Music follows HoMM5's rules as far as the catalogue describes them: a theme
    /// loops, waits its own pause between plays (minPause..maxPause), and crossfades
    /// into whatever is asked for next. A stinger (victory, defeat) plays once and
    /// then the theme that was running resumes.
    ///
    /// Effects are loaded on first use and cached. Music is not cached: only the
    /// track that is playing stays decoded, because a theme can run for minutes.
    ///
    /// Everything is keyed by the names in sounds.json, so callers never handle
    /// files. Missing keys log once and then stay silent.
    /// </summary>
    public class H5Audio : MonoBehaviour
    {
        public const float DefaultMusicVolume = 0.6f;
        public const float DefaultSfxVolume = 0.8f;

        const int VoiceCount = 16;
        const float DestroyOldMusicAfter = 6f;   // longer than any crossfade in the data
        const string MusicPref = "h5.audio.music";
        const string SfxPref = "h5.audio.sfx";
        const string MutePref = "h5.audio.mute";

        /// <summary>The live instance, or null before the bootstrap has created one.</summary>
        public static H5Audio Instance { get; private set; }

        GameDatabase _db;

        // Two music sources, so one track can fade out while the next fades in.
        readonly AudioSource[] _music = new AudioSource[2];
        readonly Coroutine[] _fades = new Coroutine[2];
        int _current;

        readonly AudioSource[] _voices = new AudioSource[VoiceCount];
        int _nextVoice;

        readonly Dictionary<string, AudioClip> _sfxClips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _sfxLoading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _missingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AudioClip _musicClip;
        string _musicFile;

        Coroutine _musicRoutine;
        int _musicGen;
        string _theme;

        float _musicVolume = DefaultMusicVolume;
        float _sfxVolume = DefaultSfxVolume;
        bool _muted;

        public string CurrentTheme => _theme;
        public float MusicVolume => _musicVolume;
        public float SfxVolume => _sfxVolume;
        public bool Muted => _muted;
        /// <summary>Distinct effects decoded so far (a count for the HUD and tests).</summary>
        public int EffectsLoaded => _sfxClips.Count;
        /// <summary>Files the catalogue names that are not on disk, or failed to decode.</summary>
        public int FilesMissing => _missingFiles.Count;

        float MusicLevel => _muted ? 0f : _musicVolume;

        /// <summary>
        /// Attach to a host (or reuse the one that has it) and bind it to a loaded
        /// catalogue. Call once, after GameDatabase.Load.
        /// </summary>
        public static H5Audio Create(GameObject host, GameDatabase db)
        {
            var audio = host.GetComponent<H5Audio>();
            if (audio == null) audio = host.AddComponent<H5Audio>();
            audio.Init(db);
            return audio;
        }

        /// <summary>Play a one-shot effect by key. Nothing happens when muted or unknown.</summary>
        public static void Play(string key)
        {
            if (Instance != null) Instance.PlaySound(key);
        }

        /// <summary>The menu and screen click.</summary>
        public static void Click()
        {
            if (Instance != null) Instance.PlaySound(H5Cues.Click);
        }

        /// <summary>Start the first candidate track the catalogue has. Silent when it has none.</summary>
        public static void Theme(params string[] candidates)
        {
            if (Instance == null) return;
            var key = Instance.FirstTrack(candidates);
            if (key != null) Instance.PlayTheme(key);
        }

        /// <summary>
        /// Play the first candidate stinger the catalogue has, then <paramref name="thenTheme"/>
        /// (may be null for silence). With no stinger at all, the theme starts at once.
        /// </summary>
        public static void Stinger(string[] candidates, string thenTheme)
        {
            if (Instance == null) return;
            var key = Instance.FirstTrack(candidates);
            if (key != null) Instance.PlayStinger(key, thenTheme);
            else if (!string.IsNullOrEmpty(thenTheme)) Instance.PlayTheme(thenTheme);
        }

        void Init(GameDatabase db)
        {
            _db = db;
            Instance = this;
            _musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicPref, DefaultMusicVolume));
            _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxPref, DefaultSfxVolume));
            _muted = PlayerPrefs.GetInt(MutePref, 0) == 1;

            if (_music[0] != null) return;   // already built: Create was called again
            for (var i = 0; i < _music.Length; i++) _music[i] = NewSource();
            for (var i = 0; i < _voices.Length; i++) _voices[i] = NewSource();
        }

        AudioSource NewSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;   // all of this is 2D: menus, the HUD, the battlefield
            source.volume = 0f;
            return source;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // -- music ---------------------------------------------------------

        /// <summary>
        /// Start a theme. A theme that is already playing keeps playing, so the town
        /// screen opening and closing over the same terrain does not restart the music.
        /// </summary>
        public void PlayTheme(string key)
        {
            if (_db == null) return;
            if (string.Equals(key, _theme, StringComparison.OrdinalIgnoreCase)) return;
            StartTheme(key);
        }

        /// <summary>
        /// Play a one-shot track (victory, defeat, retreat), then resume the theme.
        /// The resume target is set first, so a PlayTheme for it during the stinger
        /// is a no-op and the theme comes back on its own.
        /// </summary>
        public void PlayStinger(string key, string thenTheme)
        {
            if (_db == null) return;
            _theme = thenTheme;
            var gen = ++_musicGen;
            StopMusicRoutine();
            _musicRoutine = StartCoroutine(StingerRoutine(key, thenTheme, gen));
        }

        /// <summary>Fade the music out and stop the theme.</summary>
        public void StopMusic(float fadeSeconds)
        {
            _theme = null;
            ++_musicGen;
            StopMusicRoutine();
            FadeMusic(_current, fadeSeconds, false, true);
        }

        /// <summary>The first of the candidate track keys the catalogue has, or null (and a warning, once).</summary>
        public string FirstTrack(params string[] keys)
        {
            if (_db == null || keys == null || keys.Length == 0) return null;
            foreach (var key in keys)
                if (_db.GetMusic(key) != null) return key;
            Warn("music", keys[0]);
            return null;
        }

        void StartTheme(string key)
        {
            _theme = key;
            var gen = ++_musicGen;
            StopMusicRoutine();
            if (string.IsNullOrEmpty(key)) return;
            _musicRoutine = StartCoroutine(ThemeRoutine(key, gen));
        }

        void StopMusicRoutine()
        {
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = null;
        }

        IEnumerator ThemeRoutine(string key, int gen)
        {
            var def = _db.GetMusic(key);
            if (def == null)
            {
                Warn("music", key);
                yield break;
            }

            while (gen == _musicGen)
            {
                var holder = new ClipHolder();
                yield return LoadMusic(def.file, holder);
                if (gen != _musicGen || holder.Clip == null) yield break;

                var source = StartTrack(holder.Clip, def.fadeIn, def.fadeOut);
                yield return new WaitUntil(() => gen != _musicGen || !source.isPlaying);
                if (gen != _musicGen) yield break;

                // The gap between plays is part of the theme, not a glitch.
                var lo = Mathf.Max(0f, def.minPause);
                var hi = Mathf.Max(lo, def.maxPause);
                var pause = UnityEngine.Random.Range(lo, hi) / 1000f;
                if (pause > 0f) yield return new WaitForSecondsRealtime(pause);
            }
        }

        IEnumerator StingerRoutine(string key, string thenTheme, int gen)
        {
            var def = _db.GetMusic(key);
            if (def != null)
            {
                var holder = new ClipHolder();
                yield return LoadMusic(def.file, holder);
                if (gen != _musicGen) yield break;
                if (holder.Clip != null)
                {
                    var source = StartTrack(holder.Clip, 0f, def.fadeOut);
                    yield return new WaitUntil(() => gen != _musicGen || !source.isPlaying);
                    if (gen != _musicGen) yield break;
                }
                else
                {
                    Warn("music", key);
                }
            }
            else
            {
                Warn("music", key);
            }

            if (string.IsNullOrEmpty(thenTheme)) yield break;
            _musicRoutine = StartCoroutine(ThemeRoutine(thenTheme, ++_musicGen));
        }

        /// <summary>
        /// Move the music to a new clip. The old one fades out on its own source and
        /// the new one fades in on the other, so the two overlap for the crossfade.
        /// </summary>
        AudioSource StartTrack(AudioClip clip, float fadeInMs, float fadeOutMs)
        {
            var previous = _current;
            _current = 1 - _current;
            var source = _music[_current];
            source.clip = clip;
            source.volume = 0f;
            source.Play();

            FadeMusic(previous, fadeOutMs / 1000f, false, true);
            FadeMusic(_current, fadeInMs / 1000f, true, false);
            return source;
        }

        void FadeMusic(int slot, float seconds, bool audible, bool stopAtEnd)
        {
            if (_fades[slot] != null) StopCoroutine(_fades[slot]);
            _fades[slot] = null;
            var source = _music[slot];
            if (seconds <= 0f)
            {
                source.volume = audible ? MusicLevel : 0f;
                if (stopAtEnd) StopSource(source);
                return;
            }
            _fades[slot] = StartCoroutine(FadeRoutine(slot, seconds, audible, stopAtEnd));
        }

        IEnumerator FadeRoutine(int slot, float seconds, bool audible, bool stopAtEnd)
        {
            var source = _music[slot];
            var from = source.volume;
            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                var target = audible ? MusicLevel : 0f;
                source.volume = Mathf.Lerp(from, target, t / seconds);
                yield return null;
            }
            source.volume = audible ? MusicLevel : 0f;
            if (stopAtEnd) StopSource(source);
            _fades[slot] = null;
        }

        static void StopSource(AudioSource source)
        {
            source.Stop();
            source.clip = null;
        }

        /// <summary>
        /// The track for a file, from the cache when it is the one already loaded.
        /// Loading a different track schedules the previous clip for destruction
        /// after the crossfade; the source that still plays it has stopped by then.
        /// </summary>
        IEnumerator LoadMusic(string file, ClipHolder holder)
        {
            if (_musicClip != null && string.Equals(file, _musicFile, StringComparison.OrdinalIgnoreCase))
            {
                holder.Clip = _musicClip;
                yield break;
            }
            if (_musicClip != null) Destroy(_musicClip, DestroyOldMusicAfter);
            _musicClip = null;
            _musicFile = null;

            yield return ReadClip(file, holder);
            _musicClip = holder.Clip;
            _musicFile = holder.Clip != null ? file : null;
        }

        // -- volume and mute -----------------------------------------------

        public void SetMusicVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(MusicPref, _musicVolume);
            ApplyMusicLevel();
        }

        public void SetSfxVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(SfxPref, _sfxVolume);
        }

        public void ToggleMute() => SetMuted(!_muted);

        public void SetMuted(bool muted)
        {
            _muted = muted;
            PlayerPrefs.SetInt(MutePref, muted ? 1 : 0);
            if (muted)
                foreach (var voice in _voices) voice.Stop();
            ApplyMusicLevel();
        }

        void ApplyMusicLevel()
        {
            // A fade in progress picks up the level itself on its next frame.
            if (_fades[_current] == null) _music[_current].volume = MusicLevel;
        }

        // -- effects -------------------------------------------------------

        /// <summary>Play a catalogue effect. The first use of a key loads it, the next plays at once.</summary>
        public void PlaySound(string key)
        {
            if (_db == null || _muted || _sfxVolume <= 0f || string.IsNullOrEmpty(key)) return;
            var def = _db.GetSound(key);
            if (def == null)
            {
                Warn("sound", key);
                return;
            }

            AudioClip clip;
            if (_sfxClips.TryGetValue(def.file, out clip))
            {
                Voice(clip, def.volume);
                return;
            }
            if (_sfxLoading.Contains(def.file) || _missingFiles.Contains(def.file)) return;
            StartCoroutine(LoadEffect(def));
        }

        IEnumerator LoadEffect(SoundDef def)
        {
            _sfxLoading.Add(def.file);
            var holder = new ClipHolder();
            yield return ReadClip(def.file, holder);
            _sfxLoading.Remove(def.file);

            if (holder.Clip == null)
            {
                _missingFiles.Add(def.file);
                yield break;
            }
            _sfxClips[def.file] = holder.Clip;
            Voice(holder.Clip, def.volume);
        }

        /// <summary>
        /// Plays on an idle voice when there is one, otherwise steals the next one in
        /// rotation, so a burst of hits never drops the strike that matters.
        /// </summary>
        void Voice(AudioClip clip, float volume)
        {
            if (_muted || clip == null) return;
            AudioSource pick = null;
            for (var i = 0; i < _voices.Length; i++)
            {
                if (!_voices[i].isPlaying)
                {
                    pick = _voices[i];
                    break;
                }
            }
            if (pick == null)
            {
                pick = _voices[_nextVoice];
                _nextVoice = (_nextVoice + 1) % _voices.Length;
            }
            pick.clip = clip;
            pick.volume = Mathf.Clamp01(volume) * _sfxVolume;
            pick.Play();
        }

        // -- decoding --------------------------------------------------------

        sealed class ClipHolder
        {
            public AudioClip Clip;
        }

        /// <summary>
        /// Read one catalogue file into a clip. WAV is decoded in place (it is a few
        /// milliseconds for an effect); Ogg Vorbis goes through Unity's own decoder.
        /// </summary>
        IEnumerator ReadClip(string file, ClipHolder holder)
        {
            var path = H5Paths.Import(file);
            if (!File.Exists(path))
            {
                MissingOnce(file);
                yield break;
            }

            if (file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                holder.Clip = DecodeWav(file, path);
                if (holder.Clip == null) MissingOnce(file);
                yield break;
            }

            var uri = new Uri(path).AbsoluteUri;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    holder.Clip = DownloadHandlerAudioClip.GetContent(request);
                }
                else
                {
                    Debug.LogWarning($"[HoMM5] could not decode {file}: {request.error}");
                    MissingOnce(file);
                }
            }
        }

        static AudioClip DecodeWav(string file, string path)
        {
            try
            {
                var decoded = H5Wav.Decode(File.ReadAllBytes(path));
                if (decoded == null || decoded.Frames == 0)
                {
                    Debug.LogWarning($"[HoMM5] unsupported WAV {file}");
                    return null;
                }
                var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(file), decoded.Frames,
                                            decoded.Channels, decoded.SampleRate, false);
                clip.SetData(decoded.Samples, 0);
                return clip;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HoMM5] could not read {file}: {e.Message}");
                return null;
            }
        }

        void MissingOnce(string file)
        {
            if (_missingFiles.Add(file))
                Debug.LogWarning($"[HoMM5] audio file missing or unreadable: {file}");
        }

        void Warn(string kind, string key)
        {
            if (_warned.Add(kind + ":" + key))
                Debug.LogWarning($"[HoMM5] no {kind} named '{key}' in sounds.json");
        }
    }
}
