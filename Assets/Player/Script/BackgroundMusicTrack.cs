using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Marks music explicitly so scene fades never change ambience, footsteps or UI cues.</summary>
public sealed class BackgroundMusicTrack : MonoBehaviour
{
    private static readonly HashSet<BackgroundMusicTrack> Tracks = new();
    private static readonly HashSet<ScreenFadeLease> ActiveFades = new();
    private AudioSource source;
    private Coroutine fadeIn;
    private bool pendingStart;

    public AudioClip Clip => source != null ? source.clip : null;
    public bool IsPlaying => source != null && (pendingStart || source.isPlaying);
    public static bool IsTransitionActive => ActiveFades.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Tracks.Clear();
        ActiveFades.Clear();
    }

    public static BackgroundMusicTrack Attach(AudioSource audioSource)
    {
        if (audioSource == null) return null;
        foreach (var existing in audioSource.GetComponents<BackgroundMusicTrack>())
        {
            if (existing.source != audioSource) continue;
            Tracks.Add(existing);
            return existing;
        }
        var track = audioSource.gameObject.AddComponent<BackgroundMusicTrack>();
        track.source = audioSource;
        Tracks.Add(track);
        return track;
    }

    private void OnEnable() { if (source != null) Tracks.Add(this); }
    private void OnDisable() { CancelFadeIn(); Tracks.Remove(this); }
    private void OnDestroy() { CancelFadeIn(); Tracks.Remove(this); }

    public void PlayLoop(AudioClip clip, float fadeInSeconds, float targetVolume = 1f)
    {
        if (source == null || clip == null || IsTransitionActive) return;
        if (source.clip == clip && IsPlaying) return;
        CancelFadeIn();
        source.Stop();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;
        pendingStart = true;
        fadeIn = StartCoroutine(PlayWhenLoaded(clip, fadeInSeconds, Mathf.Clamp01(targetVolume)));
    }

    private IEnumerator PlayWhenLoaded(AudioClip clip, float duration, float volume)
    {
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading) yield return null;
        if (source == null || clip.loadState != AudioDataLoadState.Loaded || IsTransitionActive)
        {
            pendingStart = false;
            fadeIn = null;
            yield break;
        }
        source.Play();
        pendingStart = false;
        float safeDuration = Mathf.Max(0f, duration);
        for (float elapsed = 0f; elapsed < safeDuration; elapsed += Time.unscaledDeltaTime)
        {
            if (source == null) yield break;
            source.volume = volume * Mathf.SmoothStep(0f, 1f, elapsed / safeDuration);
            yield return null;
        }
        if (source != null) source.volume = volume;
        fadeIn = null;
    }

    private void CancelFadeIn()
    {
        if (fadeIn != null) StopCoroutine(fadeIn);
        fadeIn = null;
        pendingStart = false;
    }

    public void Stop()
    {
        CancelFadeIn();
        if (source == null) return;
        source.volume = 0f;
        source.Stop();
    }

    public static ScreenFadeLease BeginScreenFade() => new ScreenFadeLease();

    /// <summary>Owns the outgoing music until the screen transition has finished.</summary>
    public sealed class ScreenFadeLease : IDisposable
    {
        private readonly List<AudioSource> sources = new();
        private readonly List<float> startVolumes = new();
        private bool disposed;

        internal ScreenFadeLease()
        {
            Tracks.RemoveWhere(track => track == null || track.source == null);
            foreach (var track in Tracks)
            {
                track.CancelFadeIn();
                sources.Add(track.source);
                startVolumes.Add(track.source.volume);
            }
            ActiveFades.Add(this);
        }

        public void SetProgress(float progress)
        {
            if (disposed) return;
            float remaining = 1f - Mathf.Clamp01(progress);
            for (int i = 0; i < sources.Count; i++)
            {
                var audioSource = sources[i];
                if (audioSource == null) continue;
                audioSource.volume = startVolumes[i] * remaining;
                if (remaining <= 0f) audioSource.Stop();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ActiveFades.Remove(this);
        }
    }
}
