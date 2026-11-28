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
    private AudioSource uiSource, footstepSource, looseFootstepSource, landingSource;
    private readonly Dictionary<AudioClip, AudioClip> uiEchoClips = new();
    private readonly List<AudioClip> ownedUICues = new();
    private float cachedEchoInterval = -1f;
    private readonly List<AudioSource> ambienceSources = new();
    private GameSoundMap currentMap;
    private bool currentBase;
    private bool cutsceneRoutingHeld;
    private GameSoundMap lastFieldMap;
    private int lastFieldSceneHandle;
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
        footstepSource = CreateSource("Footsteps", 96, false);
        looseFootstepSource = CreateSource("Footstep Accents", 96, false);
        landingSource = CreateSource("Landing", 96, false);
        // Warm the short cues before input is enabled, without changing WAV import settings.
        var clips = new HashSet<AudioClip> { library.mainButton, library.inventoryOpen, library.inventorySlide };
        foreach (var group in new[] { library.dirtSteps, library.dirtLooseSteps, library.concreteSteps,
            library.concreteLooseSteps, library.concreteLandings })
            if (group != null) foreach (var clip in group) clips.Add(clip);
        foreach (var clip in clips) Load(clip);
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
        || sceneName == "TreatmentScene" || sceneName == "ScareCrowScene";

    private void Update()
    {
        if (library == null) return;
        if (CutsceneVideoSequence.IsAnyVisible) cutsceneRoutingHeld = true;
        else if (!SceneFadeTransition.IsRunning) cutsceneRoutingHeld = false;
        var nextMap = CurrentMap;
        bool nextBase = IsBaseScene(SceneManager.GetActiveScene().name);
        if (nextMap != GameSoundMap.None)
        {
            lastFieldMap = nextMap;
            lastFieldSceneHandle = SceneManager.GetActiveScene().handle;
        }
        if (nextMap == currentMap && nextBase == currentBase) return;
        currentMap = nextMap;
        currentBase = nextBase;
        if (ambienceStart != null) StopCoroutine(ambienceStart);
        footstepSource.Stop();
        looseFootstepSource.Stop();
        ambienceStart = StartCoroutine(TransitionBackground(nextMap, nextBase));
    }

    private IEnumerator TransitionBackground(GameSoundMap map, bool baseArea)
    {
        var clips = baseArea && library.baseBgm != null ? new[] { library.baseBgm }
            : baseArea ? Array.Empty<AudioClip>() : library.Ambience(map);
        clips ??= Array.Empty<AudioClip>();
        foreach (var clip in clips) Load(clip);
        bool loading;
        do
        {
            loading = false;
            foreach (var clip in clips) if (clip != null && clip.loadState == AudioDataLoadState.Loading) loading = true;
            if (loading) yield return null;
        } while (loading);

        // A base departure finishes its music fade before the field ambience begins.
        bool leavingBase = !baseArea && ambienceSources.Exists(s => s != null && s.clip == library.baseBgm);
        if (leavingBase)
        {
            yield return FadeBackground(Array.Empty<AudioClip>(), 0, library.baseMusicFadeDuration);
            RetainBackground(Array.Empty<AudioClip>());
        }
        double start = AudioSettings.dspTime + .05;
        bool scheduled = false;
        foreach (var clip in clips)
        {
            if (clip == null || clip.loadState == AudioDataLoadState.Failed) continue;
            // Reuse a playing layer on a rapid return instead of restarting its loop.
            if (ambienceSources.Exists(s => s != null && s.clip == clip)) continue;
            var source = CreateSource((baseArea ? "Base Music " : "Ambience ") + clip.name, 180, false);
            source.clip = clip;
            source.volume = 0;
            source.loop = true;
            ambienceSources.Add(source);
            source.PlayScheduled(start);
            scheduled = true;
        }
        if (scheduled) yield return new WaitForSecondsRealtime(.05f);
        yield return FadeBackground(clips, baseArea ? library.baseMusicVolume : library.ambienceVolume,
            baseArea ? library.baseMusicFadeDuration : library.ambienceCrossfadeDuration);
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

    public static void PlayMenuButton()
    {
        var service = Ensure();
        if (service == null || service.library == null) return;
        var clip = service.library.mainButton;
        service.PlayUI(clip);
        if (clip != null) service.menuEchoEndsAt = Time.realtimeSinceStartupAsDouble + clip.length + service.library.uiEchoInterval * 3;
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
        if (uiSource != null) uiSource.Stop();
        foreach (var cue in ownedUICues) if (cue != null) Destroy(cue);
        if (instance == this) instance = null;
    }
}
