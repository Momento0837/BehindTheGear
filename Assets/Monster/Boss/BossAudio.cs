using UnityEngine;

/// <summary>
/// The boss's own sound: its looping battle theme and its effect sounds (clips are set on BossProfile; they are the
/// synthesised files in Assets/!Sounds/Boss made by Tools/BossArt/boss_audio.py).
/// Kept separate from GameSoundService: that service routes the map ambience and UI sounds, and is left untouched.
/// The forest ambience keeps playing quietly under the theme.
/// </summary>
public sealed class BossAudio : MonoBehaviour
{
    private BossProfile profile;
    private AudioSource music, effects;
    private float musicLevel, musicTarget, musicFadeSeconds = 1f;

    public static BossAudio Create(BossGoliath boss)
    {
        var audio = boss.gameObject.AddComponent<BossAudio>();
        audio.profile = boss.Profile;
        audio.music = audio.Source("Boss Music", true);
        audio.effects = audio.Source("Boss Effects", false);
        return audio;
    }

    private AudioSource Source(string sourceName, bool loop)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f; // a boss fills the screen: plain stereo, no distance falloff
        return source;
    }

    /// <summary>Plays an effect once. A missing clip is simply silent.</summary>
    public void Play(AudioClip clip, float volume = 1f)
    {
        if (clip == null || effects == null) return;
        effects.PlayOneShot(clip, Mathf.Clamp01(volume * profile.effectsVolume));
    }

    public void StartMusic(float fadeSeconds = 1f)
    {
        if (profile.bgm == null) return;
        if (music.clip != profile.bgm) { music.clip = profile.bgm; music.volume = 0f; musicLevel = 0f; }
        if (!music.isPlaying) music.Play();
        musicTarget = 1f;
        musicFadeSeconds = Mathf.Max(.01f, fadeSeconds);
    }

    public void StopMusic(float fadeSeconds = 1.5f)
    {
        musicTarget = 0f;
        musicFadeSeconds = Mathf.Max(.01f, fadeSeconds);
    }

    private void Update()
    {
        if (music == null) return;
        // Unscaled: the theme keeps its level through hit-stop and the slow-motion finish.
        musicLevel = Mathf.MoveTowards(musicLevel, musicTarget, Time.unscaledDeltaTime / musicFadeSeconds);
        music.volume = musicLevel * profile.musicVolume;
        if (musicLevel <= 0f && musicTarget <= 0f && music.isPlaying) music.Stop();
    }
}
