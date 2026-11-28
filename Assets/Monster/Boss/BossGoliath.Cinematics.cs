using System.Collections;
using UnityEngine;

/// <summary>
/// Staged moments of the boss: the pieces the entrance is built from (ForestBossEncounter directs them) and the
/// defeat. Real time is used wherever the game clock is frozen or slowed on purpose.
/// </summary>
public sealed partial class BossGoliath
{
    public BossAudio Audio { get; private set; }
    public BossCameraDirector CameraDirector { get; private set; }
    /// <summary>True once the defeat has played out and the rewards are on the ground.</summary>
    public bool DefeatFinished { get; private set; }

    private BossHealthBarUI healthBar;
    private BossTitleCard overlay;
    private bool dormant;      // before it "switches on" in the entrance: no fire, gear still
    private bool slowMotion;   // this boss currently owns Time.timeScale

    /// <summary>Letterbox, title card and flash (made the first time it is needed).</summary>
    public BossTitleCard Overlay => overlay != null ? overlay : overlay = BossTitleCard.Create(transform);

    public void ShowHealthBar()
    {
        if (healthBar == null) healthBar = BossHealthBarUI.Show(this);
    }

    /// <summary>Cold machine (furnace out, gear still) or running.</summary>
    public void SetDormant(bool value) => dormant = value;

    /// <summary>Entrance beat: the fists run out of their drums and pound the ground once. Harmless.</summary>
    public IEnumerator EntranceSlam()
    {
        float groundY = Rig.GroundPixelY - BossSpriteLayout.FistSize.y;
        pilotControlled = true; pilotPose = BossSpriteLayout.Pilot.Roar0;
        Audio.Play(Profile.sfxChain);
        for (int side = 0; side < 2; side++)
        {
            fistControlled[side] = true;
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
        }
        Run(DropFist(BossGoliathRig.Left, groundY, -1, 0));
        yield return DropFist(BossGoliathRig.Right, groundY, -1, 0);
        for (int side = 0; side < 2; side++) Impact(side, .3f);
        pilotPose = BossSpriteLayout.Pilot.Roar1;
        yield return new WaitForSeconds(.55f);
        yield return ReturnFists(.5f);
        ReleaseParts();
        helpers.Clear();
    }

    /// <summary>
    /// Walks to `worldX` with alternating steps: one foot up, down (footfall), the other foot up, down. The whole
    /// boss moves with it: parts, fists and chains hang off the body, so they follow by themselves.
    /// </summary>
    public IEnumerator WalkTo(float worldX, float speed, float maxSeconds = float.MaxValue, float footfall = .5f)
    {
        if (speed <= 0f) yield break;
        bool wasControlled = bodyControlled;
        bodyControlled = true;
        float step = Profile.walkStepSeconds, elapsed = 0f;
        int phase = 0; // 0 left foot up, 1 both down, 2 right foot up, 3 both down
        Vector3 position = transform.position;
        while (!Mathf.Approximately(position.x, worldX) && elapsed < maxSeconds)
        {
            Rig.BodyFrame = WalkFrame(phase);
            for (float t = 0f; t < step && !Mathf.Approximately(position.x, worldX) && elapsed < maxSeconds; t += Time.deltaTime)
            {
                // The body travels while a foot is in the air and settles while both are down.
                float pace = phase % 2 == 0 ? 1.5f : .5f;
                position.x = Mathf.MoveTowards(position.x, worldX, speed * pace * Time.deltaTime);
                transform.position = Snap(position);
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (phase % 2 == 0) Footfall(footfall); // that foot comes down
            phase = (phase + 1) % 4;
        }
        transform.position = Snap(position);
        bodyControlled = wasControlled;
    }

    private BossSpriteLayout.Body WalkFrame(int phase)
    {
        bool rage = Phase >= 2;
        switch (phase)
        {
            case 0: return rage ? BossSpriteLayout.Body.Rage1 : BossSpriteLayout.Body.WalkLeftUp; // phase 2 has no legs: it rolls (UpdateRoll)
            case 2: return rage ? BossSpriteLayout.Body.Rage0 : BossSpriteLayout.Body.WalkRightUp;
            case 1: return rage ? BossSpriteLayout.Body.Rage1 : BossSpriteLayout.Body.Idle1;
            default: return rage ? BossSpriteLayout.Body.Rage0 : BossSpriteLayout.Body.Idle0;
        }
    }

    /// <summary>One heavy footfall of the walking machine.</summary>
    public void Footfall(float strength)
    {
        Audio.Play(Profile.sfxThud, .6f + .4f * strength);
        CameraDirector.Shake(.06f + .12f * strength, .35f);
        Dust(Rig.PixelToWorld(20, Rig.GroundPixelY), -1f);
        Dust(Rig.PixelToWorld(92, Rig.GroundPixelY), 1f);
    }

    // ------------------------------------------------------------------ defeat
    private void Die()
    {
        if (IsDead) return;
        IsDead = true;
        Invulnerable = true;
        StopFight();
        SetOverheated(false);
        StartCoroutine(Defeat());
    }

    private IEnumerator Defeat()
    {
        var profile = Profile;
        // The finishing blow: freeze, one white frame, then a stretch of slow motion while the boiler bursts.
        slowMotion = true;
        try
        {
            Time.timeScale = 0f;
            Rig.PilotFrame = BossSpriteLayout.Pilot.Hit;
            yield return new WaitForSecondsRealtime(profile.defeatHitStopSeconds);
            StartCoroutine(Overlay.Flash(.06f));
            Time.timeScale = profile.defeatSlowScale;
            int bursts = Mathf.Max(1, profile.defeatExplosions);
            for (int i = 0; i < bursts; i++)
            {
                Vector3 point = Rig.PixelToWorld(Random.Range(22f, 90f), Random.Range(34f, 80f));
                BossFx.Play(Rig, Rig.Explosion, point, .07f, profile.fxSortingOrder);
                Audio.Play(profile.sfxExplosion, .75f);
                CameraDirector.Shake(.16f, .25f);
                yield return new WaitForSecondsRealtime(profile.defeatSlowSeconds / bursts);
            }
        }
        finally
        {
            Time.timeScale = 1f;
            slowMotion = false;
        }
        Audio.StopMusic(2f);

        // The machine goes dark and drops to its knees...
        float groundY = Rig.GroundPixelY - BossSpriteLayout.FistSize.y;
        Rig.FurnaceStage = -1;
        Rig.BodyFrame = BossSpriteLayout.Body.Wreck0;
        Rig.PilotFrame = BossSpriteLayout.Pilot.Stun;
        Rig.PilotDrop = 0;
        Rig.PilotNudge = Vector2Int.zero;
        for (int side = 0; side < 2; side++)
        {
            Rig.SetFistPlatform(side, false);
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Slam;
            Rig.FistPixel[side] = new Vector2(Rig.FistPixel[side].x, groundY);
        }
        Footfall(.8f);
        yield return new WaitForSeconds(.7f);

        // ...then falls on its face. The bucket, with Bartol out cold in it, slides off and lands in front.
        Rig.BodyFrame = BossSpriteLayout.Body.Wreck1;
        Rig.GearVisible = false;
        Audio.Play(profile.sfxSlam);
        CameraDirector.Shake(.32f, .5f);
        Dust(Rig.PixelToWorld(8, Rig.GroundPixelY), -1.4f);
        Dust(Rig.PixelToWorld(104, Rig.GroundPixelY), 1.4f);
        int fall = (int)Rig.GroundPixelY - BossSpriteLayout.PilotSize.y - Rig.Anchors.Pilot.y;
        for (float t = 0f; t < .4f; t += Time.deltaTime)
        {
            float u = t / .4f;
            Rig.PilotDrop = Mathf.RoundToInt(fall * u * u);
            Rig.PilotNudge = new Vector2Int(Mathf.RoundToInt(-14f * u), 0);
            yield return null;
        }
        Rig.PilotDrop = fall;
        Rig.PilotNudge = new Vector2Int(-14, 0);
        Audio.Play(profile.sfxThud, .7f);
        yield return new WaitForSeconds(.5f);

        // Boss-rank rewards burst out where he landed (coins, base parts, AP, EXP: the existing reward table).
        MonsterRewards.Drop(Rig.PilotTransform.position + Vector3.up * .5f, MonsterRank.Boss);
        yield return new WaitForSeconds(1.2f);
        DefeatFinished = true;
        Died?.Invoke();
    }

    private void RestoreTimeIfOwned()
    {
        if (!slowMotion) return;
        slowMotion = false;
        Time.timeScale = 1f;
    }
}
