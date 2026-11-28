using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Persistent 2D sound playback; UI echoes survive menu and scene changes.</summary>
public sealed class GameSoundService : MonoBehaviour
{
    private static GameSoundService instance;
    private static readonly float[] EchoGains = { 1f, .25f, .1f, .03f };
    private GameSoundLibrary library;
    private AudioSource uiSource, footstepSource, looseFootstepSource, landingSource, punchSource, attackSource;
    private AudioSource menuButtonSource;
    private AudioSource playerDamageSource;
    private AudioSource openingQuestSource;
    private readonly Dictionary<AudioClip, AudioClip> openingQuestOctaves = new();
    private readonly Dictionary<AudioClip, AudioClip> uiEchoClips = new();
    private readonly List<AudioClip> ownedUICues = new();
    private float cachedEchoInterval = -1f;
    private readonly List<AudioSource> ambienceSources = new();
    private BackgroundMusicTrack musicTrack;
    private readonly HashSet<int> marketMusicScenes = new();
    private GameSoundMap currentMap;
    private bool currentBase;
    private bool cutsceneRoutingHeld;
    private GameSoundMap lastFieldMap;
    private int lastFieldSceneHandle;
    private string lastFieldRoom;
    private int? openingForestAmbienceScene;
    private Coroutine ambienceStart;
    private int inventoryGeneration;
    private double menuEchoEndsAt;
    private bool quitRequested;

    public static GameSoundMap CurrentMap
    {
        get
        {
            if (CutsceneVideoSequence.IsAnyVisible
                || (instance != null && instance.cutsceneRoutingHeld && SceneFadeTransition.IsRunning))
                return GameSoundMap.None;
            var scene = SceneManager.GetActiveScene();
            var map = ResolveMap(scene.name, scene.path, RoomDropCache.CurrentRoom);
            // Additive base visits clear RoomDropCache while retaining the original field scene.
            if (map == GameSoundMap.None && scene.name == "Market" && string.IsNullOrEmpty(RoomDropCache.CurrentRoom)
                && instance != null && scene.handle == instance.lastFieldSceneHandle)
                return instance.lastFieldMap;
            return map;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap() => Ensure();

    private static GameSoundService Ensure()
    {
        if (instance == null && Application.isPlaying)
            new GameObject("Game Sounds").AddComponent<GameSoundService>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        library = Resources.Load<GameSoundLibrary>("Audio/GameSoundLibrary");
        if (library == null) { Debug.LogError("[Sounds] Missing Resources/Audio/GameSoundLibrary."); return; }
        uiSource = CreateSource("UI", 64, true);
        menuButtonSource = CreateSource("Menu Buttons", 64, true);
        footstepSource = CreateSource("Footsteps", 96, false);
        looseFootstepSource = CreateSource("Footstep Accents", 96, false);
        landingSource = CreateSource("Landing", 96, false);
        punchSource = CreateSource("Melee Impacts", 80, false);
        playerDamageSource = CreateSource("Player Damage", 80, false);
        attackSource = CreateSource("Attack Swings", 80, false);
        openingQuestSource = CreateSource("Opening Quest", 64, true);
        musicTrack = BackgroundMusicTrack.Attach(CreateSource("Background Music", 160, false));
        SceneManager.sceneUnloaded += ForgetSceneMusic;
        // Warm the short cues before input is enabled, without changing WAV import settings.
        var clips = new HashSet<AudioClip> { library.mainButton, library.inventoryOpen, library.inventorySlide,
            library.playerAttack13, library.playerAttack2, library.gangBAttack, library.openingQuest };
        foreach (var group in new[] { library.dirtSteps, library.dirtLooseSteps, library.concreteSteps,
            library.concreteLooseSteps, library.concreteLandings, library.concreteDeathLandings, library.punchHits })
            if (group != null) foreach (var clip in group) clips.Add(clip);
        foreach (var clip in clips) Load(clip);
        OpeningQuestClip();
        // Prepare once, before a button press. Echo timing then runs on the audio thread,
        // including while loading a scene stalls the main thread.
        UIEchoClip(library.mainButton);
        UIEchoClip(library.inventoryOpen);
        UIEchoClip(library.inventorySlide);
    }

    private AudioSource CreateSource(string label, int priority, bool ignorePause)
    {
        var child = new GameObject(label);
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0;
        source.priority = priority;
        source.ignoreListenerPause = ignorePause;
        return source;
    }

    private static void Load(AudioClip clip)
    {
        if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
    }

    // Garbage, Market and the neighborhood share Market.unity. Use the story room ID.
    public static GameSoundMap ResolveMap(string sceneName, string scenePath, string room)
    {
        if (sceneName == "Forest") return GameSoundMap.Forest;
        if (sceneName != "Market" || string.IsNullOrEmpty(room)
            || string.IsNullOrEmpty(scenePath) || !room.StartsWith(scenePath, StringComparison.Ordinal))
            return GameSoundMap.None;
        if (room.EndsWith("#market", StringComparison.Ordinal)
            || room.EndsWith("#5_Neighborhood", StringComparison.Ordinal)
            || room.EndsWith("#4_Neighborhood", StringComparison.Ordinal)) return GameSoundMap.MarketHouse;
        return GameSoundMap.Garbage;
    }

    public static bool IsBaseScene(string sceneName) => sceneName == "Base_Test" || sceneName == "HideOutSystemScene"
        || sceneName == "TreatmentScene" || sceneName == "ScareCrowScene" || sceneName == "BedScene";

    public static bool IsMarketRoom(string sceneName, string scenePath, string room)
        => sceneName == "Market" && !string.IsNullOrEmpty(scenePath)
            && string.Equals(room, scenePath + "#market", StringComparison.Ordinal);

    private bool IsCurrentMarketRoom()
    {
        var scene = SceneManager.GetActiveScene();
        string room = RoomDropCache.CurrentRoom;
        if (string.IsNullOrEmpty(room) && scene.handle == lastFieldSceneHandle) room = lastFieldRoom;
        return IsMarketRoom(scene.name, scene.path, room);
    }

    public static void StartMarketMusic()
    {
        var service = Ensure();
        if (service == null || service.library == null || !service.IsCurrentMarketRoom()) return;
        service.marketMusicScenes.Add(SceneManager.GetActiveScene().handle);
        service.UpdateMusic(false);
    }

    public static void PrepareOpeningForestAmbience()
    {
        var service = Ensure();
        var scene = SceneManager.GetActiveScene();
        if (service != null && scene.name == "Forest" && CutsceneVideoSequence.IsAnyVisible)
            service.openingForestAmbienceScene = scene.handle;
    }

    private void ForgetSceneMusic(Scene scene)
    {
        marketMusicScenes.Remove(scene.handle);
        if (openingForestAmbienceScene == scene.handle) openingForestAmbienceScene = null;
    }

    private void UpdateMusic(bool baseArea)
    {
        // A departing song is owned by the screen fade until reveal finishes. This also
        // prevents a base room change from restarting its song under the black cover.
        if (SceneFadeTransition.IsRunning || BackgroundMusicTrack.IsTransitionActive) return;
        AudioClip wanted = null;
        if (!CutsceneVideoSequence.IsAnyVisible && !cutsceneRoutingHeld)
        {
            if (baseArea) wanted = library.baseBgm;
            else if (IsCurrentMarketRoom() && marketMusicScenes.Contains(SceneManager.GetActiveScene().handle))
                wanted = library.marketBgm;
        }
        if (wanted == null) { musicTrack.Stop(); return; }
        if (musicTrack.Clip == wanted && musicTrack.IsPlaying) return;
        musicTrack.PlayLoop(wanted, baseArea ? library.baseMusicFadeDuration : library.marketMusicFadeDuration,
            baseArea ? library.baseMusicVolume : library.marketMusicVolume);
    }

    private void Update()
    {
        if (library == null) return;
        if (CutsceneVideoSequence.IsAnyVisible) cutsceneRoutingHeld = true;
        else if (!SceneFadeTransition.IsRunning) cutsceneRoutingHeld = false;
        var nextMap = CurrentMap;
        bool nextBase = IsBaseScene(SceneManager.GetActiveScene().name);
        if (openingForestAmbienceScene.HasValue
            && openingForestAmbienceScene.Value != SceneManager.GetActiveScene().handle)
            openingForestAmbienceScene = null;
        if (nextMap != GameSoundMap.None)
        {
            lastFieldMap = nextMap;
            lastFieldSceneHandle = SceneManager.GetActiveScene().handle;
            if (!string.IsNullOrEmpty(RoomDropCache.CurrentRoom)) lastFieldRoom = RoomDropCache.CurrentRoom;
        }
        // Preload while the arrival bubbles are playing, before the Ethan dialogue cue.
        if (IsCurrentMarketRoom()) Load(library.marketBgm);
        UpdateMusic(nextBase);
        if (nextMap == currentMap && nextBase == currentBase) return;
        currentMap = nextMap;
        currentBase = nextBase;
        if (ambienceStart != null) StopCoroutine(ambienceStart);
        footstepSource.Stop();
        looseFootstepSource.Stop();
        float ambienceDuration = library.ambienceCrossfadeDuration;
        if (nextMap == GameSoundMap.Forest && !nextBase && openingForestAmbienceScene.HasValue)
        {
            // Only the reveal after the first movie gets a longer entrance, once.
            ambienceDuration *= 2f;
            openingForestAmbienceScene = null;
        }
        ambienceStart = StartCoroutine(TransitionBackground(nextMap, nextBase, ambienceDuration));
    }

    private IEnumerator TransitionBackground(GameSoundMap map, bool baseArea, float duration)
    {
        // Ambience has its own crossfade and is never registered with the music screen fade.
        var clips = baseArea ? Array.Empty<AudioClip>() : library.Ambience(map);
        clips ??= Array.Empty<AudioClip>();
        foreach (var clip in clips) Load(clip);
        bool loading;
        do
        {
            loading = false;
            foreach (var clip in clips) if (clip != null && clip.loadState == AudioDataLoadState.Loading) loading = true;
            if (loading) yield return null;
        } while (loading);

        double start = AudioSettings.dspTime + .05;
        bool scheduled = false;
        foreach (var clip in clips)
        {
            if (clip == null || clip.loadState == AudioDataLoadState.Failed) continue;
            // Reuse a playing layer on a rapid return instead of restarting its loop.
            if (ambienceSources.Exists(s => s != null && s.clip == clip)) continue;
            var source = CreateSource("Ambience " + clip.name, 180, false);
            source.clip = clip;
            source.volume = 0;
            source.loop = true;
            ambienceSources.Add(source);
            source.PlayScheduled(start);
            scheduled = true;
        }
        if (scheduled) yield return new WaitForSecondsRealtime(.05f);
        yield return FadeBackground(clips, library.ambienceVolume, duration);
        RetainBackground(clips);
        ambienceStart = null;
    }

    private IEnumerator FadeBackground(AudioClip[] retained, float targetVolume, float duration)
    {
        var sources = ambienceSources.ToArray();
        var from = new float[sources.Length];
        var to = new float[sources.Length];
        var targets = new HashSet<AudioClip>(retained);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null) continue;
            from[i] = sources[i].volume;
            to[i] = targets.Contains(sources[i].clip) ? targetVolume : 0;
        }
        duration = Mathf.Max(.01f, duration);
        for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float blend = Mathf.SmoothStep(0, 1, elapsed / duration);
            for (int i = 0; i < sources.Length; i++)
                if (sources[i] != null) sources[i].volume = Mathf.Lerp(from[i], to[i], blend);
            yield return null;
        }
        for (int i = 0; i < sources.Length; i++) if (sources[i] != null) sources[i].volume = to[i];
    }

    private void RetainBackground(AudioClip[] retained)
    {
        var targets = new HashSet<AudioClip>(retained);
        for (int i = ambienceSources.Count - 1; i >= 0; i--)
        {
            var source = ambienceSources[i];
            if (source != null && targets.Contains(source.clip)) continue;
            if (source != null) { source.Stop(); Destroy(source.gameObject); }
            ambienceSources.RemoveAt(i);
        }
    }

    public static AudioClip PlayFootstep()
    {
        var service = Ensure();
        if (service == null || service.library == null) return null;
        var clip = service.library.Footstep(CurrentMap);
        if (clip == null) return null;
        service.footstepSource.clip = clip;
        service.footstepSource.volume = service.library.footstepsVolume;
        service.footstepSource.Play();
        return clip;
    }

    public static void StopFootstep()
    {
        if (instance != null && instance.footstepSource != null) instance.footstepSource.Stop();
    }

    public static void PlayLooseFootstep()
    {
        var service = Ensure();
        if (service == null || service.library == null) return;
        var clip = service.library.LooseFootstep(CurrentMap);
        if (clip != null) service.looseFootstepSource.PlayOneShot(clip, service.library.footstepsVolume);
    }

    public static void PlayGarbageLanding()
    {
        var service = Ensure();
        if (service == null || service.library == null || CurrentMap != GameSoundMap.Garbage) return;
        var clip = GameSoundLibrary.Pick(service.library.concreteLandings);
        if (clip != null) service.landingSource.PlayOneShot(clip, service.library.footstepsVolume);
    }

    public static void PlayMonsterFootstep(AudioSource source, bool loose, float distanceGain)
    {
        if (source == null || distanceGain <= 0f) return;
        var service = Ensure();
        if (service == null || service.library == null) return;
        var clip = loose ? service.library.LooseFootstep(CurrentMap) : service.library.Footstep(CurrentMap);
        if (clip != null) source.PlayOneShot(clip, service.library.footstepsVolume * .5f * Mathf.Clamp01(distanceGain));
    }

    public static void PlayDeathLanding(float volume)
    {
        var service = Ensure();
        if (service == null || service.library == null) return;
        var clip = GameSoundLibrary.Pick(service.library.concreteDeathLandings);
        // Death contacts use their authored 50/100% levels, independent of quieter footsteps.
        if (clip != null) service.landingSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    public static void PlayPlayerAttack(int attackIndex)
    {
        var service = Ensure();
        if (service == null || service.library == null) return;
        var clip = service.library.PlayerAttack(attackIndex);
        if (clip != null) service.attackSource.PlayOneShot(clip, service.library.attackVolume);
    }

    public static void PlayGangBAttack()
    {
        var service = Ensure();
        if (service == null || service.library == null || service.library.gangBAttack == null) return;
        service.attackSource.PlayOneShot(service.library.gangBAttack, service.library.attackVolume * .5f);
    }

    public static void PlayPunchHit()
    {
        var service = Ensure();
        if (service == null || service.library == null) return;
        var clip = GameSoundLibrary.Pick(service.library.punchHits);
        if (clip != null) service.punchSource.PlayOneShot(clip, service.library.punchVolume);
    }

    public static void PlayPlayerDamage()
    {
        var service = Ensure();
        if (service == null || service.library == null) return;
        var clip = GameSoundLibrary.Pick(service.library.punchHits);
        if (clip == null) return;
        // Incoming hits have their own pitch/level, independent of simultaneous melee impacts.
        service.playerDamageSource.pitch = Mathf.Clamp(service.library.PlayerDamagePitch, .5f, 2f);
        service.playerDamageSource.PlayOneShot(clip, service.library.playerDamageVolume);
    }

    /// <summary>Schedules the whole WAV and returns the realtime deadline for its first quest glyph.</summary>
    public static double ScheduleOpeningQuest()
    {
        var service = Ensure();
        if (service == null || service.library == null || service.library.openingQuest == null) return 0d;
        var clip = service.OpeningQuestClip();
        if (clip == null) return 0d;
        Load(clip);
        if (clip.loadState != AudioDataLoadState.Loaded) return 0d;
        var source = service.openingQuestSource;
        source.Stop();
        source.clip = clip;
        source.pitch = service.library.OpeningQuestPitch * service.library.openingQuest.frequency / clip.frequency;
        source.volume = service.library.openingQuestVolume;
        source.timeSamples = 0;
        // Keep the quiet beginning intact. The glyph waits for the audible attack,
        // accounting for the shorter lead-in produced by the higher pitch.
        const double schedulingLead = .05d;
        double dspStart = AudioSettings.dspTime + schedulingLead;
        double firstGlyphAt = Time.realtimeSinceStartupAsDouble + schedulingLead + service.library.OpeningQuestOnsetDelay;
        source.PlayScheduled(dspStart);
        return firstGlyphAt;
    }

    private AudioClip OpeningQuestClip()
    {
        var original = library.openingQuest;
        if (original == null || library.OpeningQuestPitch <= 3f) return original;
        if (openingQuestOctaves.TryGetValue(original, out var cached)) return cached;
        var cue = CreateOpeningQuestOctave(original);
        if (cue != null) openingQuestOctaves.Add(original, cue);
        return cue;
    }

    /// <summary>Keeps every PCM sample, playing one octave higher without modifying the source asset.</summary>
    public static AudioClip CreateOpeningQuestOctave(AudioClip original)
    {
        if (original == null || original.loadType != AudioClipLoadType.DecompressOnLoad) return null;
        Load(original);
        if (original.loadState != AudioDataLoadState.Loaded) return null;
        var samples = new float[checked(original.samples * original.channels)];
        if (!original.GetData(samples, 0)) return null;
        // AudioSource pitch caps at 3. Put +12 semitones in the transient clip's
        // playback rate, then apply the remainder on the source (+13 for +25 total).
        var cue = AudioClip.Create(original.name + " (Quest Pitch Octave)", original.samples,
            original.channels, checked(original.frequency * 2), false);
        cue.hideFlags = HideFlags.HideAndDontSave;
        if (cue.SetData(samples, 0)) return cue;
        if (Application.isPlaying) Destroy(cue);
        else DestroyImmediate(cue);
        return null;
    }

    public static void PlayMenuButton()
    {
        var service = Ensure();
        if (service == null || service.library == null) return;
        var clip = service.library.mainButton;
        if (clip == null) return;
        var cue = service.UIEchoClip(clip) ?? clip;
        // Keep inventory cues on their original source/pitch, even when a click overlaps them.
        float pitch = Mathf.Clamp(service.library.MenuButtonPitch, .5f, 2f);
        service.menuButtonSource.pitch = pitch;
        service.menuButtonSource.PlayOneShot(cue, service.library.uiVolume);
        service.menuEchoEndsAt = Time.realtimeSinceStartupAsDouble + cue.length / pitch;
    }

    public static void PlayInventoryOpen()
    {
        var service = Ensure();
        if (service == null || service.library == null) return;
        int generation = ++service.inventoryGeneration;
        service.PlayUI(service.library.inventoryOpen);
        service.StartCoroutine(service.InventorySecondCue(generation));
    }

    private IEnumerator InventorySecondCue(int generation)
    {
        float delay = library.inventoryOpen != null ? library.inventoryOpen.length * .7f : 0;
        if (delay > 0) yield return new WaitForSecondsRealtime(delay);
        if (generation == inventoryGeneration) PlayUI(library.inventorySlide);
    }

    public static void CancelPendingInventoryOpen()
    {
        if (instance != null) ++instance.inventoryGeneration;
    }

    public static void PlayInventoryClose()
    {
        var service = Ensure();
        if (service == null || service.library == null) return;
        ++service.inventoryGeneration;
        service.PlayUI(service.library.inventorySlide);
    }

    private void PlayUI(AudioClip clip)
    {
        if (clip == null) return;
        // One cue contains the original and three attenuated copies. No later frame can
        // bunch the copies together or change their relative gain.
        var cue = UIEchoClip(clip);
        uiSource.PlayOneShot(cue != null ? cue : clip, library.uiVolume);
    }

    private AudioClip UIEchoClip(AudioClip clip)
    {
        if (clip == null) return null;
        float interval = Mathf.Max(.01f, library.uiEchoInterval);
        if (!Mathf.Approximately(cachedEchoInterval, interval))
        {
            // Keep old cues alive until this service is destroyed: a previous click
            // may still be sounding while the library is edited during Play mode.
            uiEchoClips.Clear();
            cachedEchoInterval = interval;
        }
        if (uiEchoClips.TryGetValue(clip, out var cached)) return cached;
        var cue = CreateUIEchoClip(clip, interval);
        if (cue != null)
        {
            uiEchoClips.Add(clip, cue);
            ownedUICues.Add(cue);
        }
        else Debug.LogWarning("[Sounds] Could not prepare UI echoes for " + clip.name
            + ". UI clips must use Decompress On Load; playing the original cue.");
        return cue;
    }

    /// <summary>Caller owns the transient result. Does not modify or normalize the source clip.</summary>
    public static AudioClip CreateUIEchoClip(AudioClip original, float interval)
    {
        if (original == null || original.loadType != AudioClipLoadType.DecompressOnLoad) return null;
        Load(original);
        if (original.loadState != AudioDataLoadState.Loaded) return null;
        int delayFrames = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(.01f, interval) * original.frequency));
        var dry = new float[original.samples * original.channels];
        if (!original.GetData(dry, 0)) return null;
        int totalFrames = original.samples + delayFrames * (EchoGains.Length - 1);
        var wet = new float[totalFrames * original.channels];
        for (int tap = 0; tap < EchoGains.Length; tap++)
        {
            int offset = tap * delayFrames * original.channels;
            for (int sample = 0; sample < dry.Length; sample++)
                wet[offset + sample] += dry[sample] * EchoGains[tap];
        }
        var result = AudioClip.Create(original.name + " (UI Echo)", totalFrames, original.channels,
            original.frequency, false);
        result.hideFlags = HideFlags.HideAndDontSave;
        if (result.SetData(wet, 0)) return result;
        if (Application.isPlaying) Destroy(result);
        else DestroyImmediate(result);
        return null;
    }

    public static void QuitAfterMenuSound()
    {
        var service = Ensure();
        if (service == null) { Application.Quit(); return; }
        if (!service.quitRequested) { service.quitRequested = true; service.StartCoroutine(service.QuitWhenQuiet()); }
    }

    private IEnumerator QuitWhenQuiet()
    {
        // Button.OnSubmit may dispatch before MenuButtonSound.OnSubmit on this object.
        yield return null;
        while (Time.realtimeSinceStartupAsDouble < menuEchoEndsAt) yield return null;
        Application.Quit();
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= ForgetSceneMusic;
        if (uiSource != null) uiSource.Stop();
        if (menuButtonSource != null) menuButtonSource.Stop();
        if (openingQuestSource != null) openingQuestSource.Stop();
        foreach (var cue in openingQuestOctaves.Values) if (cue != null) Destroy(cue);
        foreach (var cue in ownedUICues) if (cue != null) Destroy(cue);
        if (instance == this) instance = null;
    }
}
