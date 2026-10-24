using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TheOtherSide
{
    /// <summary>
    /// Single audio authority for the horror loop. It owns every looping bed
    /// (ambient, chase music, heartbeat) and one-shot SFX source, and derives its
    /// state from the existing managers rather than owning gameplay state.
    ///
    /// Clip mapping (files in Assets/Resources/Audio):
    ///   Horror bg game audio -> ambient bed
    ///   Chasing              -> chase music (crossfaded)
    ///   Heartbeat raising    -> heartbeat bed (volume/pitch follow danger)
    ///   enemy hit            -> player damage / monster hit
    ///   door opeaning        -> door open (future-callable)
    ///   DoorOpening1         -> door open variant (future-callable)
    ///   You Lost             -> game over
    ///
    /// Clips that do not exist in the project (relic pickup, generic interact,
    /// dimension switch, freeze start/end, respawn, restart, door close) are
    /// handled by optional lightweight synthesized cues. They are generated in
    /// memory at runtime and can be disabled in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-80)]
    public class HorrorAudioManager : MonoBehaviour
    {
        public static HorrorAudioManager Instance { get; private set; }

        [Header("Clips (optional — loaded from Resources/Audio when empty)")]
        [SerializeField] AudioClip ambientClip;
        [SerializeField] AudioClip chaseClip;
        [SerializeField] AudioClip heartbeatClip;
        [SerializeField] AudioClip damageClip;
        [SerializeField] AudioClip doorOpenClip;
        [SerializeField] AudioClip doorOpenAltClip;
        [SerializeField] AudioClip gameOverClip;

        [Header("Mix")]
        [Range(0f, 1f)] [SerializeField] float musicVolume = 0.32f;
        [Range(0f, 1f)] [SerializeField] float ambienceVolume = 0.22f;
        [Range(0f, 1f)] [SerializeField] float sfxVolume = 0.65f;
        [Range(0f, 1f)] [SerializeField] float heartbeatVolume = 0.55f;

        [Header("Music")]
        [SerializeField] float musicCrossfade = 1.4f;
        [Tooltip("Chase music fades out this long after the ghost stops chasing.")]
        [SerializeField] float chaseReleaseDelay = 0.6f;

        [Header("Heartbeat")]
        [Tooltip("Player/ghost distance at which the heartbeat starts to rise.")]
        [SerializeField] float heartbeatRange = 14f;
        [Tooltip("Below this danger value the heartbeat is silent.")]
        [Range(0f, 0.6f)] [SerializeField] float heartbeatThreshold = 0.16f;
        [SerializeField] float heartbeatMinPitch = 0.85f;
        [SerializeField] float heartbeatMaxPitch = 1.35f;

        [Header("Missing cues")]
        [Tooltip("Generate subtle in-memory cues for events that have no authored clip.")]
        [SerializeField] bool synthesizeMissingCues = true;

        AudioSource _ambience;
        AudioSource _musicA;
        AudioSource _musicB;
        AudioSource _heartbeat;
        AudioSource _sfx;

        AudioClip _pickupCue;
        AudioClip _interactCue;
        AudioClip _switchCue;
        AudioClip _freezeStartCue;
        AudioClip _freezeEndCue;
        AudioClip _respawnCue;
        AudioClip _restartCue;
        AudioClip _doorCloseCue;

        GhostAI _ghost;
        PlayerHealth _health;
        Transform _player;

        bool _chaseActive;
        bool _musicOnB;
        bool _gameOver;
        float _danger;
        float _lastGhostResolve = -10f;
        float _lastHealthResolve = -10f;
        Coroutine _musicFade;
        Coroutine _ambienceFade;
        bool _bound;
        bool _boundGameManager;

        public float Danger => _danger;
        public bool IsChaseMusicActive => _chaseActive;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveClips();
            BuildSources();
            BuildMissingCues();
        }

        void OnEnable()
        {
            Bind();
        }

        void OnDisable()
        {
            Unbind();
        }

        void OnDestroy()
        {
            Unbind();
            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            if (!_gameOver)
                PlayAmbient();
        }

        void Update()
        {
            ResolveReferences();
            UpdateDanger();
            UpdateHeartbeat();
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Crossfades the chase bed in/out. Safe to call every frame.</summary>
        public void SetChase(bool active)
        {
            if (_gameOver || _chaseActive == active)
                return;

            _chaseActive = active;
            StartChaseMusic(active);
        }

        public void PlayRelicPickup()
        {
            PlaySfx(_pickupCue, 0.9f);
        }

        public void PlayInteraction()
        {
            PlaySfx(_interactCue, 0.6f);
        }

        /// <summary>Q — dimension switch. Pitch differs between reality and the void.</summary>
        public void PlayDimensionSwitch(bool toVoid)
        {
            if (_switchCue == null)
                return;

            PlaySfx(_switchCue, 0.8f, toVoid ? 0.82f : 1.06f);
        }

        public void PlayFreezeStart()
        {
            PlaySfx(_freezeStartCue, 0.85f);
        }

        public void PlayFreezeEnd()
        {
            PlaySfx(_freezeEndCue, 0.7f);
        }

        public void PlayMonsterAttack()
        {
            PlaySfx(damageClip, 0.9f);
        }

        public void PlayDamage()
        {
            PlaySfx(damageClip, 1f);
        }

        public void PlayDoorOpen()
        {
            PlaySfx(doorOpenClip != null ? doorOpenClip : doorOpenAltClip, 0.9f);
        }

        public void PlayDoorClose()
        {
            PlaySfx(_doorCloseCue, 0.7f);
        }

        public void PlayDeath()
        {
            StopHeartbeat();
            SetChase(false);
        }

        public void PlayRespawn()
        {
            PlaySfx(_respawnCue, 0.7f);
            _danger = 0f;
            UpdateHeartbeat();
        }

        public void PlayGameOver()
        {
            _gameOver = true;
            _chaseActive = false;
            StopMusicFade();
            FadeOut(_musicA, 0.6f);
            FadeOut(_musicB, 0.6f);
            StopHeartbeat();
            FadeOut(_ambience, 1.2f);
            PlaySfx(gameOverClip, 1f);
        }

        public void PlayRestart()
        {
            _gameOver = false;
            _chaseActive = false;
            _danger = 0f;
            PlaySfx(_restartCue, 0.7f);
            PlayAmbient();
        }

        /// <summary>Clears every bed and transient state. Used on restart/respawn resets.</summary>
        public void ResetAll()
        {
            _gameOver = false;
            _chaseActive = false;
            _danger = 0f;
            StopMusicFade();
            StopCoroutineSafe(ref _ambienceFade);

            if (_musicA != null) { _musicA.Stop(); _musicA.volume = 0f; }
            if (_musicB != null) { _musicB.Stop(); _musicB.volume = 0f; }
            if (_sfx != null) _sfx.Stop();
            StopHeartbeat();

            if (_ambience != null)
            {
                _ambience.Stop();
                _ambience.volume = 0f;
            }
        }

        // ---------------------------------------------------------------- setup

        void ResolveClips()
        {
            if (ambientClip == null) ambientClip = Load("Horror bg game audio");
            if (chaseClip == null) chaseClip = Load("Chasing");
            if (heartbeatClip == null) heartbeatClip = Load("Heartbeat raising");
            if (damageClip == null) damageClip = Load("enemy hit");
            if (doorOpenClip == null) doorOpenClip = Load("door opeaning");
            if (doorOpenAltClip == null) doorOpenAltClip = Load("DoorOpening1");
            if (gameOverClip == null) gameOverClip = Load("You Lost");
        }

        static AudioClip Load(string name)
        {
            return Resources.Load<AudioClip>("Audio/" + name);
        }

        void BuildSources()
        {
            _ambience = CreateSource("Ambience", true, 0f);
            _musicA = CreateSource("MusicA", true, 0f);
            _musicB = CreateSource("MusicB", true, 0f);
            _heartbeat = CreateSource("Heartbeat", true, 0f);
            _sfx = CreateSource("Sfx", false, sfxVolume);

            if (_ambience != null && ambientClip != null) _ambience.clip = ambientClip;
            if (_musicA != null && chaseClip != null) _musicA.clip = chaseClip;
            if (_musicB != null && chaseClip != null) _musicB.clip = chaseClip;
            if (_heartbeat != null && heartbeatClip != null) _heartbeat.clip = heartbeatClip;
        }

        AudioSource CreateSource(string name, bool loop, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.volume = volume;
            source.dopplerLevel = 0f;
            return source;
        }

        void BuildMissingCues()
        {
            if (!synthesizeMissingCues)
                return;

            _pickupCue = Synth("Cue_Pickup", 0.26f, t => Bell(t, 660f, 990f));
            _interactCue = Synth("Cue_Interact", 0.09f, t => Mathf.Sin(2f * Mathf.PI * 520f * t) * Envelope(t, 0.09f, 0.02f));
            _switchCue = Synth("Cue_Switch", 0.42f, t => Noise(t) * 0.35f * Envelope(t, 0.42f, 0.12f) + Mathf.Sin(2f * Mathf.PI * (180f + 520f * t) * t) * 0.35f * Envelope(t, 0.42f, 0.1f));
            _freezeStartCue = Synth("Cue_FreezeStart", 0.55f, t => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(880f, 240f, t / 0.55f) * t) * 0.6f * Envelope(t, 0.55f, 0.08f));
            _freezeEndCue = Synth("Cue_FreezeEnd", 0.4f, t => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(240f, 760f, t / 0.4f) * t) * 0.45f * Envelope(t, 0.4f, 0.06f));
            _respawnCue = Synth("Cue_Respawn", 0.6f, t => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 440f, t / 0.6f) * t) * 0.4f * Envelope(t, 0.6f, 0.12f));
            _restartCue = Synth("Cue_Restart", 0.7f, t => Bell(t, 392f, 523f));
            _doorCloseCue = Synth("Cue_DoorClose", 0.22f, t => Noise(t) * 0.5f * Envelope(t, 0.22f, 0.04f));
        }

        static float Bell(float t, float f0, float f1)
        {
            float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / 0.26f));
            return Mathf.Sin(2f * Mathf.PI * f * t) * 0.45f * Envelope(t, 0.26f, 0.05f);
        }

        static float Envelope(float t, float length, float attack)
        {
            float a = Mathf.Clamp01(t / Mathf.Max(0.0001f, attack));
            float d = Mathf.Clamp01(1f - t / Mathf.Max(0.0001f, length));
            return a * d * d;
        }

        static float Noise(float t)
        {
            // Deterministic pseudo-noise; avoids UnityEngine.Random state coupling.
            float x = Mathf.Sin(t * 12345.678f) * 43758.5453f;
            return (x - Mathf.Floor(x)) * 2f - 1f;
        }

        static AudioClip Synth(string name, float length, Func<float, float> sample)
        {
            const int rate = 44100;
            int count = Mathf.Max(1, Mathf.RoundToInt(length * rate));
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                data[i] = Mathf.Clamp(sample(t), -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---------------------------------------------------------------- binding

        void Bind()
        {
            if (_bound)
                return;

            BindGameManager();

            if (PlayerHealth.Instance != null)
                BindHealth(PlayerHealth.Instance);

            _bound = true;
        }

        /// <summary>
        /// Subscribes to run state changes. Called again from ResolveReferences()
        /// because this component's OnEnable can run before GameManager.Awake has
        /// assigned its singleton, which silently lost the game-over reaction.
        /// </summary>
        void BindGameManager()
        {
            if (_boundGameManager || GameManager.Instance == null)
                return;

            GameManager.Instance.StateChanged += OnGameStateChanged;
            _boundGameManager = true;

            // If the manager appears while the run is already over, catch up once.
            if (GameManager.Instance.State == GameState.GameOver && !_gameOver)
                PlayGameOver();
        }

        void Unbind()
        {
            if (!_bound)
                return;

            if (_boundGameManager && GameManager.Instance != null)
                GameManager.Instance.StateChanged -= OnGameStateChanged;

            _boundGameManager = false;

            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
                _health.Respawned -= OnRespawned;
            }

            _bound = false;
        }

        void BindHealth(PlayerHealth health)
        {
            if (health == null || health == _health)
                return;

            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
                _health.Respawned -= OnRespawned;
            }

            _health = health;
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
            _health.Respawned += OnRespawned;
        }

        void ResolveReferences()
        {
            BindGameManager();

            if (_health == null && Time.unscaledTime - _lastHealthResolve > 0.5f)
            {
                _lastHealthResolve = Time.unscaledTime;
                if (PlayerHealth.Instance != null)
                    BindHealth(PlayerHealth.Instance);
            }

            if (_ghost == null && Time.unscaledTime - _lastGhostResolve > 1f)
            {
                _lastGhostResolve = Time.unscaledTime;
                _ghost = FindAnyObjectByType<GhostAI>();
            }

            if (_player == null)
            {
                if (_health != null)
                    _player = _health.transform;
                else if (PlayerHealth.Instance != null)
                    _player = PlayerHealth.Instance.transform;
            }
        }

        // ---------------------------------------------------------------- reactions

        void OnGameStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.GameOver:
                    PlayGameOver();
                    break;
                case GameState.Won:
                    _gameOver = true;
                    if (_chaseActive)
                    {
                        _chaseActive = false;
                        StartChaseMusic(false);
                    }
                    StopHeartbeat();
                    FadeOut(_ambience, 1f);
                    break;
                case GameState.Playing:
                    if (_gameOver)
                        PlayRestart();
                    break;
            }
        }

        void OnDamaged(int current, int max)
        {
            PlayDamage();
        }

        void OnDied()
        {
            PlayDeath();
        }

        void OnRespawned()
        {
            PlayRespawn();
        }

        // ---------------------------------------------------------------- danger / heartbeat

        void UpdateDanger()
        {
            if (_gameOver)
            {
                _danger = 0f;
                return;
            }

            float proximity = 0f;
            float behavior = 0f;

            if (_ghost != null && _player != null)
            {
                float distance = Vector3.Distance(_ghost.transform.position, _player.position);
                proximity = Mathf.Clamp01(1f - distance / Mathf.Max(1f, heartbeatRange));

                if (_ghost.CurrentBehavior == GhostBehavior.Chasing)
                    behavior = 1f;
                else if (_ghost.CurrentBehavior == GhostBehavior.Investigating)
                    behavior = 0.45f;
            }

            float health = 0f;
            if (_health != null)
                health = 1f - (float)_health.CurrentHealth / Mathf.Max(1, _health.MaxHealth);

            float danger = Mathf.Max(proximity, behavior * 0.92f, health * 0.85f);

            // Critical health keeps a floor under the tension even when the ghost is far.
            if (_health != null && _health.IsAlive && _health.CurrentHealth <= 1)
                danger = Mathf.Max(danger, 0.78f);

            _danger = Mathf.Clamp01(danger);

            SetChase(_ghost != null && _ghost.CurrentBehavior == GhostBehavior.Chasing);
        }

        void UpdateHeartbeat()
        {
            if (_heartbeat == null || heartbeatClip == null)
                return;

            if (_gameOver || _danger < heartbeatThreshold)
            {
                StopHeartbeat();
                return;
            }

            float t = Mathf.InverseLerp(heartbeatThreshold, 1f, _danger);
            _heartbeat.volume = heartbeatVolume * Mathf.Lerp(0.25f, 1f, t);
            _heartbeat.pitch = Mathf.Lerp(heartbeatMinPitch, heartbeatMaxPitch, t);

            if (!_heartbeat.isPlaying)
                _heartbeat.Play();
        }

        void StopHeartbeat()
        {
            if (_heartbeat == null)
                return;

            _heartbeat.Stop();
            _heartbeat.volume = 0f;
        }

        // ---------------------------------------------------------------- music

        void StartChaseMusic(bool active)
        {
            if (chaseClip == null)
                return;

            AudioSource from = _musicOnB ? _musicB : _musicA;
            AudioSource to = _musicOnB ? _musicA : _musicB;

            if (active)
            {
                _musicOnB = !_musicOnB;

                if (to.clip != chaseClip)
                    to.clip = chaseClip;

                if (!to.isPlaying)
                {
                    to.volume = 0f;
                    to.Play();
                }

                Crossfade(to, from, musicVolume, 0f, musicCrossfade);
            }
            else
            {
                // Fade the audible bed out; leave the other silent and ready.
                Crossfade(from, null, 0f, 0f, Mathf.Min(musicCrossfade, chaseReleaseDelay + 1f));
            }
        }

        void Crossfade(AudioSource fadeIn, AudioSource fadeOut, float to, float from, float duration)
        {
            StopMusicFade();
            _musicFade = StartCoroutine(CrossfadeRoutine(fadeIn, fadeOut, to, from, Mathf.Max(0.01f, duration)));
        }

        IEnumerator CrossfadeRoutine(AudioSource fadeIn, AudioSource fadeOut, float to, float from, float duration)
        {
            float startIn = fadeIn != null ? fadeIn.volume : 0f;
            float startOut = fadeOut != null ? fadeOut.volume : 0f;
            float t = 0f;

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);

                if (fadeIn != null)
                    fadeIn.volume = Mathf.Lerp(startIn, to, k);

                if (fadeOut != null)
                    fadeOut.volume = Mathf.Lerp(startOut, from, k);

                yield return null;
            }

            if (fadeIn != null)
                fadeIn.volume = to;

            if (fadeOut != null)
            {
                fadeOut.volume = from;
                if (from <= 0.0001f)
                    fadeOut.Stop();
            }

            _musicFade = null;
        }

        void StopMusicFade()
        {
            if (_musicFade != null)
            {
                StopCoroutine(_musicFade);
                _musicFade = null;
            }
        }

        void PlayAmbient()
        {
            if (_ambience == null || ambientClip == null)
                return;

            if (_ambience.clip != ambientClip)
                _ambience.clip = ambientClip;

            if (!_ambience.isPlaying)
                _ambience.Play();

            StopCoroutineSafe(ref _ambienceFade);
            _ambienceFade = StartCoroutine(FadeRoutine(_ambience, ambienceVolume, 1.2f));
        }

        void FadeOut(AudioSource source, float duration)
        {
            if (source == null)
                return;

            if (source == _ambience)
            {
                StopCoroutineSafe(ref _ambienceFade);
                _ambienceFade = StartCoroutine(FadeRoutine(source, 0f, duration, true));
                return;
            }

            StartCoroutine(FadeRoutine(source, 0f, duration, true));
        }

        void StopCoroutineSafe(ref Coroutine routine)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
        }

        IEnumerator FadeRoutine(AudioSource source, float target, float duration, bool stopAtEnd = false)
        {
            float start = source.volume;
            float t = 0f;
            duration = Mathf.Max(0.01f, duration);

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
                yield return null;
            }

            source.volume = target;
            if (stopAtEnd && target <= 0.0001f)
                source.Stop();
        }

        // ---------------------------------------------------------------- sfx

        void PlaySfx(AudioClip clip, float gain, float pitch = 1f)
        {
            if (clip == null || _sfx == null)
                return;

            _sfx.pitch = pitch;
            _sfx.PlayOneShot(clip, Mathf.Clamp01(sfxVolume * gain));
        }
    }
}
