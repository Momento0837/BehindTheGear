using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Persistent 2D sound playback; UI echoes survive menu and scene changes.</summary>
public sealed class GameSoundService : MonoBehaviour
{
    private static GameSoundService instance;
    private static readonly float[] EchoGains = { 1f, .4f, .2f, .07f };
    private GameSoundLibrary library;
    private AudioSource uiSource, footstepSource, landingSource;
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
        landingSource = CreateSource("Landing", 96, false);
        // Warm the short cues before input is enabled, without changing WAV import settings.
        var clips = new HashSet<AudioClip> { library.mainButton, library.inventoryOpen, library.inventorySlide };
        foreach (var group in new[] { library.dirtSteps, library.dirtLooseSteps, library.concreteSteps,
            library.concreteLooseSteps, library.concreteLandings })
            if (group != null) foreach (var clip in group) clips.Add(clip);
        foreach (var clip in clips) Load(clip);
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
        // The first sound runs synchronously in the input handler, never after a UI animation.
        uiSource.PlayOneShot(clip, library.uiVolume * EchoGains[0]);
        StartCoroutine(Echoes(clip));
    }

    private IEnumerator Echoes(AudioClip clip)
    {
        double began = Time.realtimeSinceStartupAsDouble;
        for (int i = 1; i < EchoGains.Length; i++)
        {
            double due = began + library.uiEchoInterval * i;
            while (Time.realtimeSinceStartupAsDouble < due) yield return null;
            uiSource.PlayOneShot(clip, library.uiVolume * EchoGains[i]);
        }
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

    private void OnDestroy() { if (instance == this) instance = null; }
}
