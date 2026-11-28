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
    public AudioClip[] concreteDeathLandings = Array.Empty<AudioClip>();
    public AudioClip[] punchHits = Array.Empty<AudioClip>();
    public AudioClip openingQuest;
    [Range(0, 1)] public float openingQuestVolume = .3f;
    [Range(-12f, 18f)] public float openingQuestPitchSemitones = 15f;
    [Tooltip("Audible attack position in the original WAV. Used only to time the first quest glyph; the entire clip plays from sample zero.")]
    [Min(0f)] public float openingQuestAudibleOnsetSeconds = 686f / 48000f;
    public float OpeningQuestPitch => Mathf.Pow(2f, openingQuestPitchSemitones / 12f);
    public float OpeningQuestOnsetDelay => Mathf.Max(0f, openingQuestAudibleOnsetSeconds) / OpeningQuestPitch;
    public AudioClip playerAttack13, playerAttack2, gangBAttack;
    [Range(0, 1)] public float attackVolume = 1f;
    public AudioClip mainButton, inventoryOpen, inventorySlide, reservedMetallicButton;
    public AudioClip baseBgm;
    public AudioClip marketBgm;
    [Min(.01f)] public float marketMusicFadeDuration = 2f;
    [Range(0, 1)] public float marketMusicVolume = 1f;
    [Min(.01f)] public float baseMusicFadeDuration = 2f;
    [Min(.01f)] public float ambienceCrossfadeDuration = 2f;
    [Range(0, 1)] public float baseMusicVolume = 1f;
    [Min(.01f)] public float uiEchoInterval = .15f;
    [Range(0, 1)] public float ambienceVolume = 1f;
    [Range(0, 1)] public float footstepsVolume = .8f;
    [Range(0, 2)] public float punchVolume = 1.1f;
    [Range(0, 1)] public float playerDamageVolume = .7f;
    [Range(-12f, 12f)] public float playerDamagePitchSemitones = -5f;
    public float PlayerDamagePitch => Mathf.Pow(2f, playerDamagePitchSemitones / 12f);
    [Range(0, 1)] public float uiVolume = 1f;
    [Tooltip("Pitch offset in semitones for main/save menu button clicks, including their echoes.")]
    [Range(-12f, 12f)] public float menuButtonPitchSemitones = -2f;
    public float MenuButtonPitch => Mathf.Pow(2f, menuButtonPitchSemitones / 12f);

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

    public AudioClip PlayerAttack(int attackIndex) => attackIndex switch
    {
        1 or 3 => playerAttack13,
        2 => playerAttack2,
        _ => null
    };

    // Dirt loose / concrete slide clips are accents for turns and takeoff, never ordinary steps.
    // An empty map-specific group stays silent rather than substituting another surface.
    public AudioClip LooseFootstep(GameSoundMap map) => map == GameSoundMap.None ? null
        : Pick(map == GameSoundMap.Forest ? dirtLooseSteps : concreteLooseSteps);

    public static AudioClip Pick(AudioClip[] clips) => clips != null && clips.Length > 0
        ? clips[UnityEngine.Random.Range(0, clips.Length)] : null;
}
