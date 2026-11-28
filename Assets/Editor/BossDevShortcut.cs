using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Editor-only keys for working on the Forest boss while in Play Mode (never part of a build):
///   F4  the whole encounter: entrance, fight, defeat
///   F6  the fight without the entrance (again = a fresh one)
///   F8  deal 10 % of its maximum HP
///   F9 / F10 / F11  next pattern: hammer slam / crane sweep / furnace shot
///   F12  next pattern: overheat (bucket down, weak point reachable from the ground)
///   F5  next pattern: triple quake (the phase-2 pattern; works in phase 1 too, for testing)
///   F7  player invincibility on/off (testing only; PlayerHealth itself is not changed)
///   0   skip the boss: ends the running encounter at once, the story carries on as if it had been beaten
/// Plain function keys: Ctrl+Shift+B and friends are taken by the Unity editor itself.
/// Also available under Tools/Boss. Needs no object in the scene.
/// The keys are read right after the game's own input update: while the Game view has focus the Input System keeps
/// keyboard state in the player buffers, and reading it from EditorApplication.update sees the editor buffers instead
/// (keys never looked pressed there, only the menu items worked).
/// </summary>
[InitializeOnLoad]
public static class BossDevShortcut
{
    private static readonly Key[] Keys = { Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12, Key.F5, Key.F4, Key.Digit0, Key.Numpad0 };
    private static int held;
    private static bool invincible;
    private static readonly System.Reflection.FieldInfo InvincibleUntil = typeof(PlayerHealth).GetField("invincibilityEndTime",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    static BossDevShortcut()
    {
        EditorApplication.update -= KeepInvincible;
        EditorApplication.update += KeepInvincible;
        InputSystem.onAfterUpdate -= ReadKeys;
        InputSystem.onAfterUpdate += ReadKeys;
    }

    private static void KeepInvincible()
    {
        if (invincible && EditorApplication.isPlaying) HoldInvincibility(float.MaxValue);
    }

    private static void ReadKeys()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) { held = 0; return; }
        var update = InputState.currentUpdateType;
        if (update != InputUpdateType.Dynamic && update != InputUpdateType.Fixed && update != InputUpdateType.Manual) return;
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        // Own edge detection on isPressed, so a key held across several input updates fires once.
        int now = 0;
        for (int i = 0; i < Keys.Length; i++) if (keyboard[Keys[i]].isPressed) now |= 1 << i;
        int pressed = now & ~held;
        held = now;
        if (pressed == 0) return;
        if ((pressed & 1) != 0) Spawn();
        if ((pressed & 2) != 0) ToggleInvincible();
        if ((pressed & 4) != 0) Hurt();
        if ((pressed & 8) != 0) Force(BossGoliath.Pattern.Slam);
        if ((pressed & 16) != 0) Force(BossGoliath.Pattern.Sweep);
        if ((pressed & 32) != 0) Force(BossGoliath.Pattern.Slag);
        if ((pressed & 64) != 0) Force(BossGoliath.Pattern.Overheat);
        if ((pressed & 128) != 0) Force(BossGoliath.Pattern.Quake);
        if ((pressed & 256) != 0) SpawnWithEntrance();
        if ((pressed & (512 | 1024)) != 0) SkipBoss();
    }

    [MenuItem("Tools/Boss/Skip boss (0)")]
    private static void SkipBoss()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Boss] Enter Play Mode first."); return; }
        Notify(ForestBossEncounter.RequestSkip() ? "Boss skipped" : "No boss encounter running");
    }

    // What just happened, shown on the Game view itself (the Console is often not visible while testing).
    private static void Notify(string message)
    {
        Debug.Log("[Boss] " + message);
        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if (window.GetType().Name == "GameView") window.ShowNotification(new GUIContent(message), 1.2);
    }

    [MenuItem("Tools/Boss/Spawn in arena (F6)")]
    private static void Spawn()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Boss] Enter Play Mode first."); return; }
        ForestBossEncounter.PlayStandalone(ForestBossEncounter.EntranceKind.None);
        Notify("Boss fight started (no entrance)");
    }

    [MenuItem("Tools/Boss/Full encounter with entrance (F4)")]
    private static void SpawnWithEntrance()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Boss] Enter Play Mode first."); return; }
        ForestBossEncounter.PlayStandalone(ForestBossEncounter.EntranceKind.Full);
        Notify("Boss entrance");
    }

    [MenuItem("Tools/Boss/Deal 10 percent damage (F8)")]
    private static void Hurt()
    {
        var boss = BossGoliath.Active;
        if (boss == null) { Notify("No boss in the scene (F6 spawns one)"); return; }
        boss.TakeHit(Mathf.Max(1, boss.MaxHealth / 10));
        Notify($"Boss HP {boss.CurrentHealth}/{boss.MaxHealth}");
    }

    [MenuItem("Tools/Boss/Next pattern: Hammer slam (F9)")]
    private static void ForceSlam() => Force(BossGoliath.Pattern.Slam);
    [MenuItem("Tools/Boss/Next pattern: Crane sweep (F10)")]
    private static void ForceSweep() => Force(BossGoliath.Pattern.Sweep);
    [MenuItem("Tools/Boss/Next pattern: Furnace shot (F11)")]
    private static void ForceSlag() => Force(BossGoliath.Pattern.Slag);
    [MenuItem("Tools/Boss/Next pattern: Triple quake (F5)")]
    private static void ForceQuake() => Force(BossGoliath.Pattern.Quake);
    [MenuItem("Tools/Boss/Next pattern: Overheat (F12)")]
    private static void ForceOverheat() => Force(BossGoliath.Pattern.Overheat);

    private const string InvincibleMenu = "Tools/Boss/Player invincible (F7)";

    [MenuItem(InvincibleMenu)]
    private static void ToggleInvincible()
    {
        invincible = !invincible;
        if (!invincible) HoldInvincibility(0f); // hand the normal 0.2 s invincibility window back to PlayerHealth
        Notify("Player invincibility " + (invincible ? "ON" : "OFF"));
    }

    [MenuItem(InvincibleMenu, true)]
    private static bool ToggleInvincibleState()
    {
        Menu.SetChecked(InvincibleMenu, invincible);
        return true;
    }

    // PlayerHealth ignores damage while Time.time is before its private invincibility end time: push that time out.
    private static void HoldInvincibility(float until)
    {
        if (InvincibleUntil == null || !Application.isPlaying) return;
        foreach (var health in Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None)) InvincibleUntil.SetValue(health, until);
    }

    // Takes effect when the pattern that is running now has finished.
    private static void Force(BossGoliath.Pattern pattern)
    {
        var boss = BossGoliath.Active;
        if (boss == null) { Notify("No boss in the scene (F6 spawns one)"); return; }
        boss.ForcePattern(pattern);
        Notify("Next pattern: " + pattern);
    }
}
