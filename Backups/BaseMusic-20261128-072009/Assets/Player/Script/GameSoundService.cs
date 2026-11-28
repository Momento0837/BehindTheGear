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
    private Coroutine ambienceStart;
    private int inventoryGeneration;
    private double menuEchoEndsAt;
    private bool quitRequested;

    public static GameSoundMap CurrentMap => CutsceneVideoSequence.IsAnyVisible ? GameSoundMap.None
        : ResolveMap(SceneManager.GetActiveScene().name, SceneManager.GetActiveScene().path, RoomDropCache.CurrentRoom);

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

    private void Update()
    {
        if (library == null) return;
        var nextMap = CurrentMap;
        if (nextMap == currentMap) return;
        currentMap = nextMap;
        if (ambienceStart != null) StopCoroutine(ambienceStart);
        ambienceStart = null;
        foreach (var source in ambienceSources) if (source != null) Destroy(source.gameObject);
        ambienceSources.Clear();
        footstepSource.Stop();
        var clips = library.Ambience(currentMap);
        if (clips != null && clips.Length > 0) ambienceStart = StartCoroutine(StartAmbience(clips));
    }

    private IEnumerator StartAmbience(AudioClip[] clips)
    {
        foreach (var clip in clips) Load(clip);
        bool loading;
        do
        {
            loading = false;
            foreach (var clip in clips) if (clip != null && clip.loadState == AudioDataLoadState.Loading) loading = true;
            if (loading) yield return null;
        } while (loading);
        double start = AudioSettings.dspTime + .05;
        foreach (var clip in clips)
        {
            if (clip == null || clip.loadState == AudioDataLoadState.Failed) continue;
            var source = CreateSource("Ambience " + clip.name, 180, false);
            source.clip = clip;
            source.volume = library.ambienceVolume;
            source.loop = true;
            ambienceSources.Add(source);
            source.PlayScheduled(start);
        }
        ambienceStart = null;
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
