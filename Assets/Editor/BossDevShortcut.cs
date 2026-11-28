using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Editor-only keys for working on the Forest boss while in Play Mode (never part of a build):
///   F4  the whole encounter: entrance, fight, defeat
///   F6  the fight without the entrance (again = a fresh one)
///   F8  deal 10 % of its maximum HP
///   F9 / F10 / F11  next pattern: hammer slam / crane sweep / furnace shot
///   F12  next pattern: overheat (bucket down, weak point reachable from the ground)
///   F5  next pattern: triple quake (the phase-2 pattern; works in phase 1 too, for testing)
///   F7  player invincibility on/off (testing only; PlayerHealth itself is not changed)
/// Plain function keys: Ctrl+Shift+B and friends are taken by the Unity editor itself.
/// Also available under Tools/Boss. Lives in the editor loop, so it needs no object in the scene.
/// </summary>
[InitializeOnLoad]
public static class BossDevShortcut
{
    private static readonly Key[] Keys = { Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12, Key.F5, Key.F4 };
    private static int held;
    private static bool invincible;
    private static readonly System.Reflection.FieldInfo InvincibleUntil = typeof(PlayerHealth).GetField("invincibilityEndTime",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    static BossDevShortcut()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) { held = 0; return; }
        if (invincible) HoldInvincibility(float.MaxValue);
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        // Own edge detection on isPressed: the editor loop does not tick in step with the input system's frames, so
        // wasPressedThisFrame can be missed (or seen twice) from here.
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
