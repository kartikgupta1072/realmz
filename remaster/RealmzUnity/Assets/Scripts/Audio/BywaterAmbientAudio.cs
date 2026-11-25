using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Realmz.Audio
{
    /// <summary>
    /// Spatial and environmental ambient audio controller managing 3 pooled AudioSources:
    /// River Water (spatialized loop), Blacksmith Anvil (daytime rhythmic clinks),
    /// and Weather/Campfire (wind and fire presence bed).
    /// </summary>
    public sealed class BywaterAmbientAudio : MonoBehaviour
    {
        private const int RiverPoolSfxId = 128;
        private const int RiverWalkSfxId = 88;
        private const int ForgeMetalHitSfxId = 636;
        private const int ForgeChaclunkSfxId = 694;
        private const int WindSfxId = 699;

        private const int RiverBedIndex = 0;
        private const int ForgeBedIndex = 1;
        private const int WeatherBedIndex = 2;
        private const int BedCount = 3;

        private const float RiverAudibleRange = 24f;
        private const float ForgeAudibleRange = 20f;
        private const float FireWarmthRange = 16f;

        private readonly AudioSource[] _sources = new AudioSource[BedCount];

        private Func<int, AudioClip> _clipLoader;
        private AudioMixerGroup _mixerGroup;
        private bool _sourcesInitialized;
        private float _ambienceVolume = 0.70f;

        private bool _forgeActive;
        private float _forgeGain;
        private float _nextForgeClinkTime;
        private int _forgeStrikeParity;

        private float _riverTargetVolume;
        private float _weatherTargetVolume;
        private float _weatherTargetPitch = 1f;
        private float _weatherTargetPan;
        private bool _hasListenerCached;
        private float _nextListenerCheckTime = -1f;

        public float AmbienceVolume => _ambienceVolume;
        public bool HasInitializedSources => _sourcesInitialized;
        public AudioMixerGroup BoundMixerGroup => _mixerGroup;

        public void Initialize(Func<int, AudioClip> clipLoader)
        {
            _clipLoader = clipLoader;
        }

        public void BindMixerGroup(AudioMixerGroup group)
        {
            _mixerGroup = group;
            if (!_sourcesInitialized)
            {
                return;
            }

            for (int i = 0; i < BedCount; i++)
            {
                if (_sources[i] != null)
                {
                    _sources[i].outputAudioMixerGroup = group;
                }
            }
        }

        public void SetAmbienceVolume(float volume)
        {
            _ambienceVolume = Mathf.Clamp01(volume);
            ApplyCurrentBedVolumes();
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
            EnsureSources();

            bool canPlay = CanPlayAudio();
            float indoorDamper = isIndoorOrDungeon ? 0.25f : 1f;
            float combatDamper = isCombatActive ? 0.65f : 1f;

            // 1. River Water bed (spatialBlend = 0.85f, loop = true)
            AudioSource riverSource = _sources[RiverBedIndex];
            if (riverSource != null)
            {
                if (nearestRiverWorldPos.HasValue)
                {
                    Vector3 riverPos = nearestRiverWorldPos.Value;
                    riverSource.transform.position = riverPos;
                    float dist = Vector3.Distance(listenerWorldPos, riverPos);
                    float distAtten = Mathf.Clamp01(1f - (dist / RiverAudibleRange));
                    distAtten *= distAtten;
                    _riverTargetVolume = Mathf.Clamp01(0.45f * distAtten * indoorDamper * combatDamper);
                }
                else
                {
                    _riverTargetVolume = 0f;
                }

                UpdateLoopingBed(
                    riverSource,
                    LoadRiverClip(),
                    _riverTargetVolume * _ambienceVolume,
                    1f,
                    0f,
                    canPlay);
            }

            // 2. Blacksmith Anvil bed (spatialBlend = 0.85f, daytime rhythmic clinks outside combat)
            AudioSource forgeSource = _sources[ForgeBedIndex];
            if (forgeSource != null)
            {
                _forgeActive = false;
                _forgeGain = 0f;

                if (nearestForgeWorldPos.HasValue && isDayOrDawn && !isCombatActive && !isIndoorOrDungeon)
                {
                    Vector3 forgePos = nearestForgeWorldPos.Value;
                    forgeSource.transform.position = forgePos;
                    float dist = Vector3.Distance(listenerWorldPos, forgePos);
                    if (dist <= ForgeAudibleRange)
                    {
                        float distAtten = Mathf.Clamp01(1f - (dist / ForgeAudibleRange));
                        _forgeGain = Mathf.Clamp01(0.55f * distAtten);
                        _forgeActive = _forgeGain > 0.005f;
                    }
                }

                if (!_forgeActive || !canPlay || _ambienceVolume <= 0.0001f)
                {
                    StopSafe(forgeSource);
                    forgeSource.mute = !canPlay;
                }
                else
                {
                    forgeSource.mute = false;
                    if (_nextForgeClinkTime <= 0f)
                    {
                        _nextForgeClinkTime = Time.unscaledTime + 0.4f;
                    }
                }
            }

            // 3. Weather + Campfire bed (spatialBlend = 0f with gentle pan near fire, loop = true)
            AudioSource weatherSource = _sources[WeatherBedIndex];
            if (weatherSource != null)
            {
                ComputeWeatherAndFireParams(
                    listenerWorldPos,
                    nearestFireWorldPos,
                    isIndoorOrDungeon,
                    weatherLabel,
                    combatDamper,
                    out _weatherTargetVolume,
                    out _weatherTargetPitch,
                    out _weatherTargetPan);

                UpdateLoopingBed(
                    weatherSource,
                    LoadClipSafe(WindSfxId),
                    _weatherTargetVolume * _ambienceVolume,
                    _weatherTargetPitch,
                    _weatherTargetPan,
                    canPlay);
            }
        }

        public void StopAll()
        {
            _forgeActive = false;
            _forgeGain = 0f;
            _riverTargetVolume = 0f;
            _weatherTargetVolume = 0f;
            _nextForgeClinkTime = 0f;

            if (!_sourcesInitialized)
            {
                return;
            }

            for (int i = 0; i < BedCount; i++)
            {
                AudioSource src = _sources[i];
                if (src != null)
                {
                    StopSafe(src);
                    src.volume = 0f;
                }
            }
        }

        private void Update()
        {
            if (!_sourcesInitialized)
            {
                return;
            }

            bool canPlay = CanPlayAudio();
            if (!canPlay)
            {
                for (int i = 0; i < BedCount; i++)
                {
                    AudioSource src = _sources[i];
                    if (src != null)
                    {
                        src.mute = true;
                        if (src.isPlaying)
                        {
                            PauseOrStopSafe(src);
                        }
                    }
                }
                return;
            }

            if (_forgeActive && _ambienceVolume > 0.0001f && Time.unscaledTime >= _nextForgeClinkTime)
            {
                TriggerForgeClink();
            }
        }

        private void OnDestroy()
        {
            StopAll();
            for (int i = 0; i < BedCount; i++)
            {
                AudioSource src = _sources[i];
                if (src != null)
                {
                    src.clip = null;
                    src.outputAudioMixerGroup = null;
                    GameObject child = src.gameObject;
                    if (child != null && child != gameObject)
                    {
                        if (Application.isPlaying)
                        {
                            Destroy(child);
                        }
                        else
                        {
                            DestroyImmediate(child);
                        }
                    }
                }

                _sources[i] = null;
            }

            _sourcesInitialized = false;
        }

        private void EnsureSources()
        {
            if (_sourcesInitialized)
            {
                return;
            }

            _sourcesInitialized = true;
            _sources[RiverBedIndex] = CreatePooledSource("Ambient_RiverWater", spatialBlend: 0.85f, loop: true, minDist: 2f, maxDist: RiverAudibleRange);
            _sources[ForgeBedIndex] = CreatePooledSource("Ambient_BlacksmithAnvil", spatialBlend: 0.85f, loop: false, minDist: 2f, maxDist: ForgeAudibleRange);
            _sources[WeatherBedIndex] = CreatePooledSource("Ambient_WeatherFire", spatialBlend: 0f, loop: true, minDist: 1f, maxDist: 30f);
        }

        private AudioSource CreatePooledSource(string childName, float spatialBlend, bool loop, float minDist, float maxDist)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(transform, false);

            AudioSource src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = spatialBlend;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = minDist;
            src.maxDistance = maxDist;
            src.volume = 0f;
            src.priority = 160;
            src.outputAudioMixerGroup = _mixerGroup;
            src.mute = !CanPlayAudio();
            return src;
        }

        private void ApplyCurrentBedVolumes()
        {
            if (!_sourcesInitialized)
            {
                return;
            }

            bool canPlay = CanPlayAudio();

            AudioSource river = _sources[RiverBedIndex];
            if (river != null)
            {
                float vol = Mathf.Clamp01(_riverTargetVolume * _ambienceVolume);
                river.volume = vol;
                river.mute = !canPlay;
                if (vol <= 0.0001f || !canPlay)
                {
                    StopSafe(river);
                }
            }

            AudioSource forge = _sources[ForgeBedIndex];
            if (forge != null)
            {
                forge.volume = Mathf.Clamp01(_forgeGain * _ambienceVolume);
                forge.mute = !canPlay;
                if (_ambienceVolume <= 0.0001f || !canPlay)
                {
                    StopSafe(forge);
                }
            }

            AudioSource weather = _sources[WeatherBedIndex];
            if (weather != null)
            {
                float vol = Mathf.Clamp01(_weatherTargetVolume * _ambienceVolume);
                weather.volume = vol;
                weather.mute = !canPlay;
                if (vol <= 0.0001f || !canPlay)
                {
                    StopSafe(weather);
                }
            }
        }

        private void UpdateLoopingBed(
            AudioSource source,
            AudioClip clip,
            float targetVolume,
            float pitch,
            float panStereo,
            bool canPlay)
        {
            if (source == null)
            {
                return;
            }

            float clampedVol = Mathf.Clamp01(targetVolume);
            source.volume = clampedVol;
            source.pitch = Mathf.Clamp(pitch, 0.5f, 1.5f);
            source.panStereo = Mathf.Clamp(panStereo, -1f, 1f);
            source.mute = !canPlay;

            if (clip != null && source.clip != clip)
            {
                source.clip = clip;
            }

            if (!canPlay || clip == null || clampedVol <= 0.0001f)
            {
                StopSafe(source);
                return;
            }

            if (!source.isPlaying)
            {
                PlaySafe(source);
            }
        }

        private void TriggerForgeClink()
        {
            AudioSource forge = _sources[ForgeBedIndex];
            if (forge == null || !_forgeActive || !CanPlayAudio())
            {
                return;
            }

            _forgeStrikeParity++;
            bool useChaclunk = (_forgeStrikeParity % 3) == 0;
            AudioClip clip = LoadClipSafe(useChaclunk ? ForgeChaclunkSfxId : ForgeMetalHitSfxId);
            if (clip == null)
            {
                clip = LoadClipSafe(useChaclunk ? ForgeMetalHitSfxId : ForgeChaclunkSfxId);
            }

            if (clip != null)
            {
                forge.mute = false;
                forge.clip = clip;
                forge.volume = Mathf.Clamp01(_forgeGain * _ambienceVolume);
                forge.pitch = UnityEngine.Random.Range(0.93f, 1.07f);
                PlaySafe(forge);
            }

            // Cadence: pair of rapid hammer strikes followed by a short pause.
            bool quickFollowUp = (_forgeStrikeParity % 2) == 1;
            _nextForgeClinkTime = Time.unscaledTime + (quickFollowUp
                ? UnityEngine.Random.Range(0.45f, 0.75f)
                : UnityEngine.Random.Range(1.9f, 3.4f));
        }

        private AudioClip LoadRiverClip()
        {
            AudioClip pool = LoadClipSafe(RiverPoolSfxId);
            return pool != null ? pool : LoadClipSafe(RiverWalkSfxId);
        }

        private AudioClip LoadClipSafe(int soundId)
        {
            if (_clipLoader == null)
            {
                return null;
            }

            try
            {
                return _clipLoader(soundId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void ComputeWeatherAndFireParams(
            Vector3 listenerWorldPos,
            Vector3? nearestFireWorldPos,
            bool isIndoorOrDungeon,
            string weatherLabel,
            float combatDamper,
            out float volume,
            out float pitch,
            out float pan)
        {
            string normalized = string.IsNullOrWhiteSpace(weatherLabel)
                ? string.Empty
                : weatherLabel.Trim().ToLowerInvariant();

            float windGain = 0.10f;
            pitch = 0.96f;

            if (normalized.Contains("storm") || normalized.Contains("thunder") || normalized.Contains("tempest"))
            {
                windGain = 0.52f;
                pitch = 1.08f;
            }
            else if (normalized.Contains("blizzard") || normalized.Contains("gale") || normalized.Contains("wind"))
            {
                windGain = 0.42f;
                pitch = 1.03f;
            }
            else if (normalized.Contains("rain") || normalized.Contains("shower") || normalized.Contains("drizzle"))
            {
                windGain = 0.34f;
                pitch = 0.98f;
            }
            else if (normalized.Contains("snow") || normalized.Contains("fog") || normalized.Contains("mist"))
            {
                windGain = 0.22f;
                pitch = 0.92f;
            }

            if (isIndoorOrDungeon)
            {
                windGain *= 0.20f;
            }

            float fireBoost = 0f;
            pan = 0f;
            if (nearestFireWorldPos.HasValue)
            {
                Vector3 firePos = nearestFireWorldPos.Value;
                float dist = Vector3.Distance(listenerWorldPos, firePos);
                float fireFactor = Mathf.Clamp01(1f - (dist / FireWarmthRange));
                fireBoost = 0.20f * fireFactor * fireFactor;
                pan = Mathf.Clamp((firePos.x - listenerWorldPos.x) / 10f, -0.45f, 0.45f) * fireFactor;
            }

            volume = Mathf.Clamp01((windGain + fireBoost) * combatDamper);
        }

        private bool CanPlayAudio()
        {
            return Application.isPlaying &&
                   !Application.isBatchMode &&
                   isActiveAndEnabled &&
                   gameObject.activeInHierarchy &&
                   HasActiveAudioListener();
        }

        private void PlaySafe(AudioSource source)
        {
            if (source == null || source.clip == null || !CanPlayAudio())
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

        private static void PauseOrStopSafe(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                if (source.isPlaying)
                {
                    source.Pause();
                }
            }
            catch (Exception)
            {
                // Ignore headless or destroyed-source errors safely.
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

        private bool HasActiveAudioListener()
        {
            float now = Time.unscaledTime;
            if (_nextListenerCheckTime >= 0f && now < _nextListenerCheckTime)
            {
                return _hasListenerCached;
            }

            _nextListenerCheckTime = now + 0.5f;
            try
            {
                AudioListener listener = FindFirstObjectByType<AudioListener>(FindObjectsInactive.Exclude);
                _hasListenerCached = listener != null && listener.enabled;
            }
            catch (Exception)
            {
                _hasListenerCached = false;
            }

            return _hasListenerCached;
        }
    }
}
