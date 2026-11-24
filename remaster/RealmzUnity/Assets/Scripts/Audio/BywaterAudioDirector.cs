using System;
using System.Collections.Generic;
using RealmzCore.Serialization;
using UnityEngine;

namespace Realmz.Audio
{
    /// <summary>
    /// Audio director for Realmz Remaster: loads RealmzAudio/audio_manifest.json,
    /// manages 2 crossfading music channels across all 11 suites, and pools an 8-voice SFX ring buffer.
    /// </summary>
    public sealed class BywaterAudioDirector : MonoBehaviour
    {
        public const string ManifestResourcePath = "RealmzAudio/audio_manifest";
        public const string AudioResourceRoot = "RealmzAudio/";
        public const int SfxVoiceCount = 8;

        private const int DefaultFootstepOverworldId = 82;
        private const int DefaultFootstepDungeonId = 83;
        private const int DefaultUiClickId = 10129;
        private const int FallbackUiClickId = 147;
        private const int DefaultCoinId = 10051;
        private const int FallbackCoinId = 20;
        private const int MeleeHitId = 637;
        private const int MeleeCritId = 635;
        private const int RangedAttackId = 643;
        private const int RangedCritId = 619;
        private const int DefaultSpellId = 20002;
        private const int FrogCroakId = 662;
        private const int FallbackFrogCroakId = 681;
        private const int ShoveId = 134;
        private const int FallbackShoveId = 630;

        private readonly Dictionary<int, string> _sfxPathsById = new Dictionary<int, string>();
        private readonly Dictionary<string, string> _musicPathsByKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AudioClip> _clipCache =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        private readonly AudioSource[] _musicSources = new AudioSource[2];
        private readonly float[] _musicWeights = new float[2];
        private readonly float[] _musicTargetWeights = new float[2];
        private readonly AudioSource[] _sfxVoices = new AudioSource[SfxVoiceCount];

        private bool _manifestAttempted;
        private bool _hasLoadedManifest;
        private bool _sourcesInitialized;
        private int _activeMusicChannel;
        private float _musicFadeRate = 1f;
        private float _musicVolume = 0.55f;
        private float _sfxVolume = 0.85f;
        private int _sfxVoiceCursor;
        private string _currentMusicKey = string.Empty;

        public bool HasLoadedManifest
        {
            get
            {
                EnsureInitialized();
                return _hasLoadedManifest;
            }
        }

        public int MusicTrackCount
        {
            get
            {
                EnsureInitialized();
                return _musicPathsByKey.Count;
            }
        }

        public int SfxCount
        {
            get
            {
                EnsureInitialized();
                return _sfxPathsById.Count;
            }
        }

        public string CurrentMusicKey => _currentMusicKey;

        public float MusicVolume
        {
            get => _musicVolume;
            set
            {
                _musicVolume = Mathf.Clamp01(value);
                ApplyMusicVolumes();
            }
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set => _sfxVolume = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            if (!_sourcesInitialized)
            {
                return;
            }

            bool changed = false;
            float step = _musicFadeRate * Time.unscaledDeltaTime;

            for (int i = 0; i < 2; i++)
            {
                float current = _musicWeights[i];
                float target = _musicTargetWeights[i];
                if (!Mathf.Approximately(current, target))
                {
                    _musicWeights[i] = Mathf.MoveTowards(current, target, step);
                    changed = true;
                }

                AudioSource src = _musicSources[i];
                if (src != null && _musicWeights[i] <= 0.0001f && target <= 0f && src.isPlaying)
                {
                    StopSafe(src);
                }
            }

            if (changed)
            {
                ApplyMusicVolumes();
            }
        }

        public void EnsureInitialized()
        {
            if (!_manifestAttempted)
            {
                LoadManifest();
            }

            if (!_sourcesInitialized)
            {
                EnsureAudioSources();
            }
        }

        public bool TryGetSfxResourcePath(int soundId, out string path)
        {
            EnsureInitialized();
            return _sfxPathsById.TryGetValue(soundId, out path);
        }

        public bool TryGetMusicResourcePath(string key, out string path)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(key))
            {
                path = string.Empty;
                return false;
            }

            return _musicPathsByKey.TryGetValue(key.Trim(), out path);
        }

        public void PlayMusic(string key, float fadeSeconds = 1.0f)
        {
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(key))
            {
                StopMusic(fadeSeconds);
                return;
            }

            string normalizedKey = key.Trim().ToLowerInvariant();
            if (!TryGetMusicResourcePath(normalizedKey, out string subPath))
            {
                return;
            }

            AudioSource currentActive = _musicSources[_activeMusicChannel];
            if (string.Equals(_currentMusicKey, normalizedKey, StringComparison.OrdinalIgnoreCase) &&
                currentActive != null &&
                currentActive.clip != null)
            {
                _musicTargetWeights[_activeMusicChannel] = 1f;
                _musicTargetWeights[1 - _activeMusicChannel] = 0f;
                _musicFadeRate = 1f / Mathf.Max(0.01f, fadeSeconds);
                if (!currentActive.isPlaying)
                {
                    PlaySafe(currentActive);
                }

                return;
            }

            AudioClip clip = LoadClipCached(subPath);
            if (clip == null)
            {
                return;
            }

            int nextChannel = 1 - _activeMusicChannel;
            int prevChannel = _activeMusicChannel;
            _activeMusicChannel = nextChannel;
            _currentMusicKey = normalizedKey;

            AudioSource incoming = _musicSources[nextChannel];
            AudioSource outgoing = _musicSources[prevChannel];
            if (incoming == null)
            {
                return;
            }

            incoming.clip = clip;
            incoming.loop = true;
            incoming.time = 0f;

            if (fadeSeconds <= 0.01f || !Application.isPlaying || !gameObject.activeInHierarchy)
            {
                _musicWeights[nextChannel] = 1f;
                _musicTargetWeights[nextChannel] = 1f;
                _musicWeights[prevChannel] = 0f;
                _musicTargetWeights[prevChannel] = 0f;
                ApplyMusicVolumes();
                StopSafe(outgoing);
                PlaySafe(incoming);
                return;
            }

            _musicWeights[nextChannel] = 0f;
            _musicTargetWeights[nextChannel] = 1f;
            _musicTargetWeights[prevChannel] = 0f;
            _musicFadeRate = 1f / Mathf.Max(0.01f, fadeSeconds);
            ApplyMusicVolumes();
            PlaySafe(incoming);
        }

        public void StopMusic(float fadeSeconds = 0.6f)
        {
            EnsureInitialized();
            _currentMusicKey = string.Empty;

            if (fadeSeconds <= 0.01f || !Application.isPlaying || !gameObject.activeInHierarchy)
            {
                _musicWeights[0] = 0f;
                _musicWeights[1] = 0f;
                _musicTargetWeights[0] = 0f;
                _musicTargetWeights[1] = 0f;
                ApplyMusicVolumes();
                StopSafe(_musicSources[0]);
                StopSafe(_musicSources[1]);
                return;
            }

            _musicTargetWeights[0] = 0f;
            _musicTargetWeights[1] = 0f;
            _musicFadeRate = 1f / Mathf.Max(0.01f, fadeSeconds);
        }

        public void PlaySfx(int soundId, float volume = 0.85f, float pitchJitter = 0.06f)
        {
            EnsureInitialized();
            if (!_sfxPathsById.TryGetValue(soundId, out string subPath))
            {
                return;
            }

            PlaySfxResource(subPath, volume, pitchJitter);
        }

        public void PlaySfxResource(string resourceSubPath, float volume = 0.85f, float pitchJitter = 0.06f)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(resourceSubPath))
            {
                return;
            }

            float finalVolume = Mathf.Clamp01(volume * _sfxVolume);
            if (finalVolume <= 0.0001f)
            {
                return;
            }

            string normalized = StripAudioExtension(resourceSubPath);
            AudioClip clip = LoadClipCached(normalized);
            if (clip == null)
            {
                return;
            }

            AudioSource voice = AcquireNextSfxVoice();
            if (voice == null)
            {
                return;
            }

            float jitter = Mathf.Clamp(pitchJitter, 0f, 0.5f);
            float pitch = jitter > 0.0001f
                ? 1f + UnityEngine.Random.Range(-jitter, jitter)
                : 1f;

            voice.clip = clip;
            voice.loop = false;
            voice.volume = finalVolume;
            voice.pitch = Mathf.Clamp(pitch, 0.5f, 1.8f);
            PlaySafe(voice);
        }

        public void PlayFootstep(int soundId, bool isDungeon)
        {
            EnsureInitialized();
            if (soundId > 0 && _sfxPathsById.ContainsKey(soundId))
            {
                PlaySfx(soundId, volume: 0.52f, pitchJitter: 0.08f);
                return;
            }

            int fallbackId = isDungeon ? DefaultFootstepDungeonId : DefaultFootstepOverworldId;
            if (_sfxPathsById.ContainsKey(fallbackId))
            {
                PlaySfx(fallbackId, volume: 0.52f, pitchJitter: 0.08f);
            }
        }

        public void PlayUiClick()
        {
            EnsureInitialized();
            int id = _sfxPathsById.ContainsKey(DefaultUiClickId) ? DefaultUiClickId : FallbackUiClickId;
            PlaySfx(id, volume: 0.65f, pitchJitter: 0.03f);
        }

        public void PlayCoin()
        {
            EnsureInitialized();
            int id = _sfxPathsById.ContainsKey(DefaultCoinId) ? DefaultCoinId : FallbackCoinId;
            PlaySfx(id, volume: 0.80f, pitchJitter: 0.05f);
        }

        public void PlayAttack(bool isRanged, bool isCrit)
        {
            EnsureInitialized();
            int id;
            if (isRanged)
            {
                id = isCrit ? RangedCritId : RangedAttackId;
            }
            else
            {
                id = isCrit ? MeleeCritId : MeleeHitId;
            }

            float vol = isCrit ? 0.95f : 0.82f;
            PlaySfx(id, volume: vol, pitchJitter: 0.07f);
        }

        public void PlaySpell(string effectKind)
        {
            EnsureInitialized();
            int id = ResolveSpellSoundId(effectKind);
            PlaySfx(id, volume: 0.85f, pitchJitter: 0.05f);
        }

        public void PlayFrogCroak()
        {
            EnsureInitialized();
            int id = _sfxPathsById.ContainsKey(FrogCroakId) ? FrogCroakId : FallbackFrogCroakId;
            PlaySfx(id, volume: 0.80f, pitchJitter: 0.09f);
        }

        public void PlayShove()
        {
            EnsureInitialized();
            int id = _sfxPathsById.ContainsKey(ShoveId) ? ShoveId : FallbackShoveId;
            PlaySfx(id, volume: 0.82f, pitchJitter: 0.06f);
        }

        private int ResolveSpellSoundId(string effectKind)
        {
            if (string.IsNullOrWhiteSpace(effectKind))
            {
                return DefaultSpellId;
            }

            string normalized = effectKind.Trim().ToLowerInvariant();
            if (normalized.Contains("heal") || normalized.Contains("cure") || normalized.Contains("restore"))
            {
                return _sfxPathsById.ContainsKey(695) ? 695 : 10105;
            }

            if (normalized.Contains("light") || normalized.Contains("electric") || normalized.Contains("shock"))
            {
                return _sfxPathsById.ContainsKey(615) ? 615 : 686;
            }

            if (normalized.Contains("fire") || normalized.Contains("blast") || normalized.Contains("explode") || normalized.Contains("burn"))
            {
                return _sfxPathsById.ContainsKey(645) ? 645 : 675;
            }

            if (normalized.Contains("teleport") || normalized.Contains("warp") || normalized.Contains("blink"))
            {
                return _sfxPathsById.ContainsKey(659) ? 659 : DefaultSpellId;
            }

            if (normalized.Contains("shield") || normalized.Contains("protect") || normalized.Contains("ward") || normalized.Contains("buff"))
            {
                return _sfxPathsById.ContainsKey(664) ? 664 : DefaultSpellId;
            }

            if (normalized.Contains("poison") || normalized.Contains("acid") || normalized.Contains("slime"))
            {
                return _sfxPathsById.ContainsKey(640) ? 640 : DefaultSpellId;
            }

            if (normalized.Contains("cold") || normalized.Contains("ice") || normalized.Contains("wind"))
            {
                return _sfxPathsById.ContainsKey(699) ? 699 : DefaultSpellId;
            }

            return _sfxPathsById.ContainsKey(DefaultSpellId) ? DefaultSpellId : 600;
        }

        private void LoadManifest()
        {
            _manifestAttempted = true;
            _hasLoadedManifest = false;
            _sfxPathsById.Clear();
            _musicPathsByKey.Clear();

            TextAsset manifestAsset = Resources.Load<TextAsset>(ManifestResourcePath);
            if (manifestAsset == null || string.IsNullOrWhiteSpace(manifestAsset.text))
            {
                return;
            }

            try
            {
                IReadOnlyDictionary<string, object> root = MiniJson.AsObject(MiniJson.Parse(manifestAsset.text));

                ParseSfxArray(root, "sfx");
                ParseSfxArray(root, "scenario_sfx");

                if (MiniJson.TryGetValue(root, "music_by_key", out object rawMusic) && rawMusic != null)
                {
                    IReadOnlyDictionary<string, object> musicMap = MiniJson.AsObject(rawMusic);
                    foreach (KeyValuePair<string, object> kvp in musicMap)
                    {
                        if (kvp.Value == null || string.IsNullOrWhiteSpace(kvp.Key))
                        {
                            continue;
                        }

                        IReadOnlyDictionary<string, object> entry = MiniJson.AsObject(kvp.Value);
                        string key = MiniJson.GetString(entry, "key", kvp.Key);
                        string relPath = MiniJson.GetString(entry, "relative_path", string.Empty);
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(relPath))
                        {
                            _musicPathsByKey[key.Trim()] = StripAudioExtension(relPath);
                        }
                    }
                }

                _hasLoadedManifest = _sfxPathsById.Count > 0 || _musicPathsByKey.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BywaterAudioDirector] Failed to parse audio manifest: " + ex.Message);
                _hasLoadedManifest = false;
            }
        }

        private void ParseSfxArray(IReadOnlyDictionary<string, object> root, string arrayKey)
        {
            if (!MiniJson.TryGetValue(root, arrayKey, out object rawSfx) || rawSfx == null)
            {
                return;
            }

            IReadOnlyList<object> sfxArray = MiniJson.AsArray(rawSfx);
            for (int i = 0; i < sfxArray.Count; i++)
            {
                if (sfxArray[i] == null)
                {
                    continue;
                }

                IReadOnlyDictionary<string, object> entry = MiniJson.AsObject(sfxArray[i]);
                int soundId = MiniJson.GetInt(entry, "sound_id", -1);
                string relPath = MiniJson.GetString(entry, "relative_path", string.Empty);
                if (soundId >= 0 && !string.IsNullOrWhiteSpace(relPath))
                {
                    _sfxPathsById[soundId] = StripAudioExtension(relPath);
                }
            }
        }

        private void EnsureAudioSources()
        {
            _sourcesInitialized = true;

            for (int i = 0; i < 2; i++)
            {
                if (_musicSources[i] == null)
                {
                    AudioSource src = gameObject.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.loop = true;
                    src.spatialBlend = 0f;
                    src.volume = 0f;
                    src.priority = 64;
                    _musicSources[i] = src;
                }
            }

            for (int i = 0; i < SfxVoiceCount; i++)
            {
                if (_sfxVoices[i] == null)
                {
                    AudioSource voice = gameObject.AddComponent<AudioSource>();
                    voice.playOnAwake = false;
                    voice.loop = false;
                    voice.spatialBlend = 0f;
                    voice.volume = _sfxVolume;
                    voice.priority = 128;
                    _sfxVoices[i] = voice;
                }
            }
        }

        private AudioSource AcquireNextSfxVoice()
        {
            EnsureAudioSources();
            AudioSource voice = _sfxVoices[_sfxVoiceCursor];
            _sfxVoiceCursor = (_sfxVoiceCursor + 1) % SfxVoiceCount;
            return voice;
        }

        private AudioClip LoadClipCached(string resourceSubPath)
        {
            if (string.IsNullOrWhiteSpace(resourceSubPath))
            {
                return null;
            }

            if (_clipCache.TryGetValue(resourceSubPath, out AudioClip cached))
            {
                return cached;
            }

            try
            {
                AudioClip loaded = Resources.Load<AudioClip>(AudioResourceRoot + resourceSubPath);
                _clipCache[resourceSubPath] = loaded;
                return loaded;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ApplyMusicVolumes()
        {
            for (int i = 0; i < 2; i++)
            {
                if (_musicSources[i] != null)
                {
                    _musicSources[i].volume = Mathf.Clamp01(_musicWeights[i] * _musicVolume);
                }
            }
        }

        private void PlaySafe(AudioSource source)
        {
            if (source == null || source.clip == null)
            {
                return;
            }

            if (!Application.isPlaying || Application.isBatchMode)
            {
                return;
            }

            if (!isActiveAndEnabled || !source.enabled || !source.gameObject.activeInHierarchy)
            {
                return;
            }

            if (!HasActiveAudioListener())
            {
                return;
            }

            try
            {
                source.Play();
            }
            catch (Exception)
            {
                // Ignore headless or audio-device errors safely.
            }
        }

        private static void StopSafe(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                if (source.isPlaying)
                {
                    source.Stop();
                }
            }
            catch (Exception)
            {
                // Ignore headless or destroyed-source errors safely.
            }
        }

        private static bool HasActiveAudioListener()
        {
            try
            {
                AudioListener listener = FindFirstObjectByType<AudioListener>(FindObjectsInactive.Exclude);
                return listener != null && listener.enabled;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string StripAudioExtension(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/').Trim();
            if (normalized.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(0, normalized.Length - 4);
            }

            return normalized;
        }
    }
}
