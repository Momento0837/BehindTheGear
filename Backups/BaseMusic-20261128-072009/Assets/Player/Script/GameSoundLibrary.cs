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
    public AudioClip[] concreteLooseSteps = Array.Empty<AudioClip>();
    public AudioClip[] concreteLandings = Array.Empty<AudioClip>();
    public AudioClip mainButton, inventoryOpen, inventorySlide, reservedMetallicButton;
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
        var regular = map == GameSoundMap.Forest ? dirtSteps : concreteSteps;
        var loose = map == GameSoundMap.Forest ? dirtLooseSteps : concreteLooseSteps;
        // Weight categories, not individual files: adding clips must not change 70/30.
        bool hasRegular = regular != null && regular.Length > 0;
        bool hasLoose = loose != null && loose.Length > 0;
        return Pick(!hasRegular || (hasLoose && UnityEngine.Random.value < .3f) ? loose : regular);
    }

    public static AudioClip Pick(AudioClip[] clips) => clips != null && clips.Length > 0
        ? clips[UnityEngine.Random.Range(0, clips.Length)] : null;
}
