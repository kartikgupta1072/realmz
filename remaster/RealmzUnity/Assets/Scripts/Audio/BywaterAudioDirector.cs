using System;
using System.Collections.Generic;
using RealmzCore.Serialization;
using UnityEngine;
using UnityEngine.Audio;

namespace Realmz.Audio
{
    /// <summary>
    /// Audio director for Realmz Remaster: loads RealmzAudio/audio_manifest.json,
    /// manages 2 crossfading music channels across all 11 suites, pools an 8-voice SFX ring buffer,
    /// routes AudioMixer groups, and coordinates BywaterAmbientAudio beds.
    /// </summary>
    public sealed class BywaterAudioDirector : MonoBehaviour
    {
        public const string ManifestResourcePath = "RealmzAudio/audio_manifest";
        public const string AudioResourceRoot = "RealmzAudio/";
        public const int SfxVoiceCount = 8;
        public const float DefaultMusicCrossfadeSeconds = 1.5f;

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

        private const int LevelUpId = 3002;
        private const int BigExplodeId = 642;
        private const int SmallExplodeId = 645;
        private const int EarthShakeId = 10090;
        private const int LightningId = 615;
        private const int ElectricEnergizeId = 686;
        private const int DoorSlamId = 690;
        private const int TakeItemId = 6002;

        private readonly Dictionary<int, string> _sfxPathsById = new Dictionary<int, string>();
        private readonly Dictionary<string, string> _musicPathsByKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AudioClip> _clipCache =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        private readonly AudioSource[] _musicSources = new AudioSource[2];
        private readonly float[] _musicWeights = new float[2];
        private readonly float[] _musicTargetWeights = new float[2];
        private readonly AudioSource[] _sfxVoices = new AudioSource[SfxVoiceCount];

        private AudioMixer _masterMixer;
        private AudioMixerGroup _musicMixerGroup;
        private AudioMixerGroup _sfxMixerGroup;
        private AudioMixerGroup _ambienceMixerGroup;
        private AudioMixerGroup _uiMixerGroup;
        private BywaterAmbientAudio _ambientAudio;

        private bool _manifestAttempted;
        private bool _hasLoadedManifest;
        private bool _sourcesInitialized;
        private int _activeMusicChannel;
        private float _musicFadeRate = 1f;
        private float _musicVolume = 0.55f;
        private float _sfxVolume = 0.85f;
        private float _ambienceVolume = 0.70f;
        private float _uiVolume = 0.85f;
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
        public AudioMixer MasterMixer => _masterMixer;
        public float DefaultMusicFadeSeconds => DefaultMusicCrossfadeSeconds;
        public BywaterAmbientAudio AmbientAudio => _ambientAudio;

        public float MusicVolume
        {
            get => _musicVolume;
            set
            {
                _musicVolume = Mathf.Clamp01(value);
                ApplyMusicVolumes();
                ApplyMixerVolumes();
            }
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set
            {
                _sfxVolume = Mathf.Clamp01(value);
                ApplyMixerVolumes();
            }
        }

        public float AmbienceVolume
        {
            get => _ambienceVolume;
            set
            {
                _ambienceVolume = Mathf.Clamp01(value);
                if (_ambientAudio != null)
                {
                    _ambientAudio.SetAmbienceVolume(_ambienceVolume);
                }

                ApplyMixerVolumes();
            }
        }

        public float UiVolume
        {
            get => _uiVolume;
            set
            {
                _uiVolume = Mathf.Clamp01(value);
                ApplyMixerVolumes();
            }
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

        public void BindMixer(AudioMixer mixer)
        {
            EnsureInitialized();
            _masterMixer = mixer;

            if (mixer == null)
            {
                _musicMixerGroup = null;
                _sfxMixerGroup = null;
                _ambienceMixerGroup = null;
                _uiMixerGroup = null;
            }
            else
            {
                _musicMixerGroup = ResolveMixerGroup(mixer, "Music");
                _sfxMixerGroup = ResolveMixerGroup(mixer, "SFX");
                _ambienceMixerGroup = ResolveMixerGroup(mixer, "Ambience");
                _uiMixerGroup = ResolveMixerGroup(mixer, "UI");
            }

            for (int i = 0; i < 2; i++)
            {
                if (_musicSources[i] != null)
                {
                    _musicSources[i].outputAudioMixerGroup = _musicMixerGroup;
                }
            }

            for (int i = 0; i < SfxVoiceCount; i++)
            {
                if (_sfxVoices[i] != null)
                {
                    _sfxVoices[i].outputAudioMixerGroup = _sfxMixerGroup;
                }
            }

            BywaterAmbientAudio ambient = EnsureAmbientAudio();
            if (ambient != null)
            {
                ambient.BindMixerGroup(_ambienceMixerGroup);
            }

            ApplyMixerVolumes();
            ApplyEnvironmentSnapshot(false, 0f);
        }

        public void ApplyEnvironmentSnapshot(bool isDungeonOrIndoor, float fadeSeconds = 0.6f)
        {
            if (_masterMixer == null)
            {
                return;
            }

            try
            {
                AudioMixerSnapshot snapshot = _masterMixer.FindSnapshot(isDungeonOrIndoor ? "Dungeon" : "Overworld");
                if (snapshot == null && isDungeonOrIndoor)
                {
                    snapshot = _masterMixer.FindSnapshot("Indoor");
                }
                snapshot?.TransitionTo(Mathf.Max(0f, fadeSeconds));
            }
            catch (Exception)
            {
                // Ignore headless or uninitialized snapshot transitions safely.
            }
        }

        public void UpdateAmbientContext(
            Vector3 listenerWorldPos,
            Vector3? nearestRiverWorldPos = null,
            Vector3? nearestForgeWorldPos = null,
            Vector3? nearestFireWorldPos = null,
            bool isIndoorOrDungeon = false,
            bool isDayOrDawn = true,
            string weatherLabel = null,
            bool isCombatActive = false)
        {
            EnsureInitialized();
            BywaterAmbientAudio ambient = EnsureAmbientAudio();
            if (ambient == null)
            {
                return;
            }

            ambient.SetAmbienceVolume(_ambienceVolume);
            ambient.UpdateAmbientContext(
                listenerWorldPos,
                nearestRiverWorldPos,
                nearestForgeWorldPos,
                nearestFireWorldPos,
                isIndoorOrDungeon,
                isDayOrDawn,
                weatherLabel,
                isCombatActive);
        }

        public void PlayMusic(string key, float fadeSeconds = 1.5f)
        {
            EnsureInitialized();

            float effectiveFadeSeconds = fadeSeconds > 0f ? fadeSeconds : DefaultMusicCrossfadeSeconds;

            if (string.IsNullOrWhiteSpace(key))
            {
                StopMusic(effectiveFadeSeconds);
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
                _musicFadeRate = 1f / Mathf.Max(0.01f, effectiveFadeSeconds);
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

            if (effectiveFadeSeconds <= 0.01f || !Application.isPlaying || !gameObject.activeInHierarchy)
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
            _musicFadeRate = 1f / Mathf.Max(0.01f, effectiveFadeSeconds);
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
            PlaySfxInternal(soundId, volume, pitchJitter, null, 0f);
        }

        public void PlaySfxResource(string resourceSubPath, float volume = 0.85f, float pitchJitter = 0.06f)
        {
            PlaySfxResourceInternal(resourceSubPath, volume, pitchJitter, null, 0f);
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
            AudioMixerGroup uiGroup = _uiMixerGroup != null ? _uiMixerGroup : _sfxMixerGroup;
            PlaySfxInternal(id, volume: 0.65f, pitchJitter: 0.03f, mixerGroupOverride: uiGroup, delaySeconds: 0f);
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
            PlaySfx(id, volume: vol, pitchJitter: 0.08f);
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

        public void PlayLevelUpFanfare()
        {
            EnsureInitialized();
            PlaySfx(LevelUpId, volume: 0.90f, pitchJitter: 0.02f);
        }

        public void PlayExplosion(bool big)
        {
            EnsureInitialized();
            int primaryId = big ? BigExplodeId : SmallExplodeId;
            float primaryVol = big ? 0.95f : 0.85f;
            float shakeVol = big ? 0.80f : 0.62f;
            float shakeDelay = big ? 0.06f : 0.04f;

            PlaySfxInternal(primaryId, primaryVol, 0.05f, null, 0f);
            PlaySfxInternal(EarthShakeId, shakeVol, 0.04f, null, shakeDelay);
        }

        public void PlayLightningChain()
        {
            EnsureInitialized();
            PlaySfxInternal(LightningId, 0.90f, 0.06f, null, 0f);
            PlaySfxInternal(ElectricEnergizeId, 0.78f, 0.05f, null, 0.05f);
        }

        public void PlayDoorInteract()
        {
            EnsureInitialized();
            PlaySfx(DoorSlamId, volume: 0.80f, pitchJitter: 0.06f);
        }

        public void PlayTakeItem()
        {
            EnsureInitialized();
            PlaySfx(TakeItemId, volume: 0.82f, pitchJitter: 0.04f);
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
                    src.outputAudioMixerGroup = _musicMixerGroup;
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
                    voice.outputAudioMixerGroup = _sfxMixerGroup;
                    _sfxVoices[i] = voice;
                }
            }

            EnsureAmbientAudio();
        }

        private BywaterAmbientAudio EnsureAmbientAudio()
        {
            if (_ambientAudio != null)
            {
                return _ambientAudio;
            }

            _ambientAudio = GetComponentInChildren<BywaterAmbientAudio>(true);
            if (_ambientAudio == null)
            {
                GameObject child = new GameObject("BywaterAmbientAudio");
                child.transform.SetParent(transform, false);
                _ambientAudio = child.AddComponent<BywaterAmbientAudio>();
            }

            _ambientAudio.Initialize(LoadClipBySfxId);
            _ambientAudio.SetAmbienceVolume(_ambienceVolume);
            if (_ambienceMixerGroup != null)
            {
                _ambientAudio.BindMixerGroup(_ambienceMixerGroup);
            }

            return _ambientAudio;
        }

        private AudioSource AcquireNextSfxVoice()
        {
            EnsureAudioSources();
            AudioSource voice = _sfxVoices[_sfxVoiceCursor];
            _sfxVoiceCursor = (_sfxVoiceCursor + 1) % SfxVoiceCount;
            return voice;
        }

        private void PlaySfxInternal(
            int soundId,
            float volume,
            float pitchJitter,
            AudioMixerGroup mixerGroupOverride,
            float delaySeconds)
        {
            EnsureInitialized();
            if (!_sfxPathsById.TryGetValue(soundId, out string subPath))
            {
                return;
            }

            PlaySfxResourceInternal(subPath, volume, pitchJitter, mixerGroupOverride, delaySeconds);
        }

        private void PlaySfxResourceInternal(
            string resourceSubPath,
            float volume,
            float pitchJitter,
            AudioMixerGroup mixerGroupOverride,
            float delaySeconds)
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

            voice.outputAudioMixerGroup = mixerGroupOverride != null ? mixerGroupOverride : _sfxMixerGroup;
            voice.clip = clip;
            voice.loop = false;
            voice.volume = finalVolume;
            voice.pitch = Mathf.Clamp(pitch, 0.5f, 1.8f);
            PlaySafe(voice, delaySeconds);
        }

        private AudioClip LoadClipBySfxId(int soundId)
        {
            EnsureInitialized();
            if (!_sfxPathsById.TryGetValue(soundId, out string subPath))
            {
                return null;
            }

            return LoadClipCached(subPath);
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

        private void ApplyMixerVolumes()
        {
            if (_masterMixer == null)
            {
                return;
            }

            SetMixerVolumeSafe(_masterMixer, "MusicVolumeDb", _musicVolume);
            SetMixerVolumeSafe(_masterMixer, "SfxVolumeDb", _sfxVolume);
            SetMixerVolumeSafe(_masterMixer, "AmbienceVolumeDb", _ambienceVolume);
            SetMixerVolumeSafe(_masterMixer, "UiVolumeDb", _uiVolume);
        }

        private static AudioMixerGroup ResolveMixerGroup(AudioMixer mixer, string groupName)
        {
            if (mixer == null || string.IsNullOrWhiteSpace(groupName))
            {
                return null;
            }

            try
            {
                AudioMixerGroup[] matches = mixer.FindMatchingGroups(groupName);
                if (matches == null || matches.Length == 0)
                {
                    return null;
                }

                for (int i = 0; i < matches.Length; i++)
                {
                    if (matches[i] != null &&
                        string.Equals(matches[i].name, groupName, StringComparison.OrdinalIgnoreCase))
                    {
                        return matches[i];
                    }
                }

                return matches[0];
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void SetMixerVolumeSafe(AudioMixer mixer, string paramName, float linearVolume)
        {
            if (mixer == null || string.IsNullOrEmpty(paramName))
            {
                return;
            }

            float db = Mathf.Log10(Mathf.Max(0.0001f, linearVolume)) * 20f;
            try
            {
                mixer.SetFloat(paramName, db);
            }
            catch (Exception)
            {
                // Ignore unexposed parameters or headless mixer errors safely.
            }
        }

        private void PlaySafe(AudioSource source, float delaySeconds = 0f)
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
                if (delaySeconds > 0.001f)
                {
                    source.PlayDelayed(delaySeconds);
                }
                else
                {
                    source.Play();
                }
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
