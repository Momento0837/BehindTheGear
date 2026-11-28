using System;
using UnityEngine;

public enum GameSoundMap { None, Forest, Garbage, MarketHouse }

/// <summary>Build-safe references to the authored WAV files in Assets/!Sounds.</summary>
[CreateAssetMenu(menuName = "Behind The Gear/Game Sounds")]
public sealed class GameSoundLibrary : ScriptableObject
{
    public AudioClip[] forestAmbience = Array.Empty<AudioClip>();
    public AudioClip[] garbageAmbience = Array.Empty<AudioClip>();
    public AudioClip[] marketHouseAmbience = Array.Empty<AudioClip>();
    public AudioClip[] dirtSteps = Array.Empty<AudioClip>();
    public AudioClip[] dirtLooseSteps = Array.Empty<AudioClip>();
    public AudioClip[] concreteSteps = Array.Empty<AudioClip>();
    [Tooltip("Concrete direction-change and jump sounds: slide (or loose) clips.")]
    public AudioClip[] concreteLooseSteps = Array.Empty<AudioClip>();
    public AudioClip[] concreteLandings = Array.Empty<AudioClip>();
    public AudioClip mainButton, inventoryOpen, inventorySlide, reservedMetallicButton;
    public AudioClip baseBgm;
    [Min(.01f)] public float baseMusicFadeDuration = 2f;
    [Min(.01f)] public float ambienceCrossfadeDuration = 2f;
    [Range(0, 1)] public float baseMusicVolume = 1f;
    [Min(.01f)] public float uiEchoInterval = .15f;
    [Range(0, 1)] public float ambienceVolume = 1f;
    [Range(0, 1)] public float footstepsVolume = 1f;
    [Range(0, 1)] public float uiVolume = 1f;

    public AudioClip[] Ambience(GameSoundMap map) => map switch
    {
        GameSoundMap.Forest => forestAmbience,
        GameSoundMap.Garbage => garbageAmbience,
        GameSoundMap.MarketHouse => marketHouseAmbience,
        _ => Array.Empty<AudioClip>()
    };

    public AudioClip Footstep(GameSoundMap map)
    {
        if (map == GameSoundMap.None) return null;
        return Pick(map == GameSoundMap.Forest ? dirtSteps : concreteSteps);
    }

    // Dirt loose / concrete slide clips are accents for turns and takeoff, never ordinary steps.
    // An empty map-specific group stays silent rather than substituting another surface.
    public AudioClip LooseFootstep(GameSoundMap map) => map == GameSoundMap.None ? null
        : Pick(map == GameSoundMap.Forest ? dirtLooseSteps : concreteLooseSteps);

    public static AudioClip Pick(AudioClip[] clips) => clips != null && clips.Length > 0
        ? clips[UnityEngine.Random.Range(0, clips.Length)] : null;
}
