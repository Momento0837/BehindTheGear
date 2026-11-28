using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The fight. Every pattern is: telegraph -> hit -> recovery, with all times and sizes read from BossProfile.
///   A  Hammer slam  : a fist follows the player from above, drops, and stays stuck as a platform.
///   B  Crane sweep  : the body leans to the player's side, then a fist drags along the ground. Jump it.
///   C  Furnace shot : globs of molten metal rain on marked spots; afterwards the machine overheats (punish window).
///   D  Triple quake : (phase 2) both fists pound the ground three times; each time ground waves run out. Jump them.
/// Phase 1 round: A and B in random order, then C. Phase 2 round: A, B and D in random order, then C; A, B and C
/// are faster / denser (see the "Phase 2" block of BossProfile).
/// Positions are in body pixels (see BossGoliathRig). Scaled time throughout: the fight stops with the game.
/// </summary>
public sealed partial class BossGoliath
{
    public enum Pattern { Slam, Sweep, Slag, Overheat, Quake }

    private Coroutine fight;
    private readonly List<Coroutine> helpers = new();
    private readonly List<BossFx> heldFx = new();
    private BossSpriteLayout.Pilot pilotPose;
    private Pattern? forced;
    private int attackSerial, lastAttackThatHit = -1;

    public bool Fighting => fight != null;
    private bool Raging => Phase >= 2;

    /// <summary>Starts the attack loop (after the entrance, or straight away from the editor shortcut).</summary>
    public void BeginFight()
    {
        if (IsDead || fight != null) return;
        fight = StartCoroutine(FightLoop());
    }

    /// <summary>Editor/testing: the next pattern will be this one.</summary>
    public void ForcePattern(Pattern pattern)
    {
        forced = pattern;
        BeginFight();
    }

    private void StopFight()
    {
        if (fight != null) StopCoroutine(fight);
        fight = null;
        foreach (var helper in helpers) if (helper != null) StopCoroutine(helper);
        helpers.Clear();
        foreach (var fx in heldFx) if (fx != null) fx.Stop();
        heldFx.Clear();
        ReleaseParts();
    }

    private void ReleaseParts()
    {
        bodyControlled = pilotControlled = furnaceControlled = false;
        fistControlled[0] = fistControlled[1] = false;
        gearSpeed = 1f;
        brake = false;
        Rig.PilotDrop = 0;
        for (int side = 0; side < 2; side++) Rig.SetFistPlatform(side, false);
    }

    private IEnumerator FightLoop()
    {
        yield return new WaitForSeconds(Profile.firstPatternDelay);
        var round = new List<Pattern>();
        while (!IsDead)
        {
            if (round.Count == 0)
            {
                round.Add(Pattern.Slam); round.Add(Pattern.Sweep);
                if (Raging) round.Add(Pattern.Quake);
                for (int i = round.Count - 1; i > 0; i--) // shuffle
                {
                    int j = Random.Range(0, i + 1);
                    (round[i], round[j]) = (round[j], round[i]);
                }
                round.Add(Pattern.Slag); // always last: the overheat window comes round reliably
            }
            if (!PlayerAlive) { yield return null; continue; } // the death restart owns the scene now

            Pattern next;
            if (forced.HasValue) { next = forced.Value; forced = null; }
            else { next = round[0]; round.RemoveAt(0); }
            switch (next)
            {
                case Pattern.Slam: yield return Slam(); break;
                case Pattern.Sweep: yield return Sweep(); break;
                case Pattern.Slag: yield return SlagShot(); break;
                case Pattern.Overheat: yield return Overheat(Raging ? Profile.rageOverheatSeconds : Profile.overheatSeconds); break;
                case Pattern.Quake: yield return Quake(); break;
            }
            ReleaseParts();
            helpers.Clear(); // every helper of the finished pattern has run out
            yield return new WaitForSeconds(Profile.patternGap);
        }
    }

    // ------------------------------------------------------------------ phase change (HP at or below the threshold)
    private void BeginPhaseTwo()
    {
        Phase = 2;
        Invulnerable = true;
        StopFight();         // whatever it was doing (usually sagging in the overheat window) is cut short
        SetOverheated(false);
        fight = StartCoroutine(PhaseChange());
    }

    private IEnumerator PhaseChange()
    {
        var profile = Profile;
        PhaseChanged?.Invoke(Phase);
        // The furnace door blows off, both stacks vent, Bartol pulls his visor down and roars with both fists up.
        bodyControlled = false; // Update now shows the phase-2 body
        furnaceControlled = true; Rig.FurnaceStage = (int)BossSpriteLayout.Furnace.Overheat;
        pilotControlled = true;
        var anchors = Rig.Anchors;
        Vector3 mouth = Rig.PixelToWorld(anchors.Furnace.x + BossSpriteLayout.FurnaceSize.x * .5f, anchors.Furnace.y + 5);
        BossFx.Play(Rig, Rig.Explosion, mouth, .08f, Profile.fxSortingOrder);
        // The legs fold away and the hull drops onto its crawler tracks (the body frames of phase 2 show them).
        Audio.Play(profile.sfxTread);
        Dust(Rig.PixelToWorld(14, Rig.GroundPixelY), -1.6f);
        Dust(Rig.PixelToWorld(98, Rig.GroundPixelY), 1.6f);
        CameraDirector.ZoomTo(profile.rageZoom, 1.4f);
        CameraDirector.TrackX = () => (transform.position.x + PlayerX) * .5f;
        Audio.Play(profile.sfxExplosion);
        Audio.Play(profile.sfxRoar);
        CameraDirector.Shake(.3f, .7f);
        float raisedY = -profile.slamRaisePixels;
        for (int side = 0; side < 2; side++)
        {
            fistControlled[side] = true;
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
            Run(MoveFist(side, new Vector2(Rig.RestFist(side).x, raisedY), .35f, EaseOut));
        }
        for (float t = 0f; t < profile.phaseChangeSeconds; t += .15f)
        {
            int beat = Mathf.FloorToInt(t / .15f);
            pilotPose = beat % 2 == 0 ? BossSpriteLayout.Pilot.Roar0 : BossSpriteLayout.Pilot.Roar1;
            if (beat % 2 == 0) StackSteam(beat % 4 == 0);
            yield return new WaitForSeconds(Mathf.Min(.15f, profile.phaseChangeSeconds - t));
        }
        yield return ReturnFists(.35f);
        ReleaseParts();
        helpers.Clear();
        Invulnerable = false;
        fight = null;
        BeginFight();
    }

    // ------------------------------------------------------------------ A. hammer slam
    private IEnumerator Slam()
    {
        var profile = Profile;
        float raisedY = -profile.slamRaisePixels;
        float groundY = Rig.GroundPixelY - BossSpriteLayout.FistSize.y;
        float shadowSeconds = Raging ? profile.rageSlamShadowSeconds : profile.slamShadowSeconds;
        int sets = Raging ? profile.rageSlamSets : 1;
        pilotControlled = true; pilotPose = BossSpriteLayout.Pilot.Pull0;
        Audio.Play(profile.sfxChain, .7f);
        for (int side = 0; side < 2; side++)
        {
            fistControlled[side] = true;
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
            Run(MoveFist(side, new Vector2(Rig.FistPixel[side].x, raisedY), profile.slamRaiseSeconds, EaseOut));
        }
        yield return new WaitForSeconds(profile.slamRaiseSeconds);

        for (int i = 0; i < profile.slamCount * sets && PlayerAlive; i++)
        {
            if (i > 0 && i % profile.slamCount == 0) yield return new WaitForSeconds(profile.rageSlamSetGap);
            int side = FistNearest(PlayerPixelX);
            int attack = ++attackSerial;

            // Telegraph: the fist follows from above while its shadow on the ground darkens.
            var shadow = Hold(Rig.Shadow, Vector3.zero);
            float followUntil = shadowSeconds - profile.slamLockSeconds;
            for (float t = 0f; t < shadowSeconds; t += Time.deltaTime)
            {
                if (t < followUntil)
                {
                    float target = Mathf.Clamp(PlayerPixelX - BossSpriteLayout.FistSize.x * .5f, ArenaLeftPixel, ArenaRightPixel - BossSpriteLayout.FistSize.x);
                    Rig.FistPixel[side].x = Mathf.MoveTowards(Rig.FistPixel[side].x, target, profile.slamTrackSpeed * Time.deltaTime);
                }
                Rig.FistPixel[side].y = raisedY;
                if (shadow != null)
                {
                    shadow.SetFrame(t > shadowSeconds * .5f ? 1 : 0);
                    shadow.MoveTo(Rig.PixelToWorld(Mathf.Round(Rig.FistPixel[side].x) + BossSpriteLayout.FistSize.x * .5f, Rig.GroundPixelY - 2));
                }
                yield return null;
            }

            // Drop. Jumping into it only means being hit sooner.
            pilotPose = BossSpriteLayout.Pilot.Pull1;
            brake = true; // a fist stuck in the ground must not be dragged along by the tracks
            yield return DropFist(side, groundY, attack, profile.slamDamage);
            Release(shadow);
            Landing(side, attack);

            // Stuck: a platform to reach Bartol from.
            Rig.SetFistPlatform(side, true);
            yield return new WaitForSeconds(Mathf.Min(.2f, profile.slamStuckSeconds));
            pilotPose = BossSpriteLayout.Pilot.Pull0;
            yield return new WaitForSeconds(Mathf.Max(0f, profile.slamStuckSeconds - .2f));
            Rig.SetFistPlatform(side, false);
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
            brake = false;
            yield return MoveFist(side, new Vector2(Rig.FistPixel[side].x, raisedY), .3f, EaseOut);
        }
        yield return ReturnFists(.45f);
    }

    private IEnumerator DropFist(int side, float groundY, int attack, int damage)
    {
        Vector2 from = Rig.FistPixel[side];
        for (float t = 0f; t < Profile.slamDropSeconds; t += Time.deltaTime)
        {
            float u = t / Profile.slamDropSeconds;
            Rig.FistPixel[side] = new Vector2(from.x, Mathf.Lerp(from.y, groundY, u * u));
            Strike(attack, Rig.FistWorldRect(side), damage);
            yield return null;
        }
        Rig.FistPixel[side] = new Vector2(from.x, groundY);
        Rig.FistFrame[side] = BossSpriteLayout.Fist.Slam;
    }

    // The heavy hit under the fist, a weaker one in the dust to each side.
    private void Landing(int side, int attack)
    {
        var profile = Profile;
        Rect fist = Rig.FistWorldRect(side);
        float ground = transform.position.y, centre = fist.center.x, half = profile.slamHitSize.x * .5f;
        Strike(attack, new Rect(centre - half, ground, profile.slamHitSize.x, profile.slamHitSize.y), profile.slamDamage);
        Strike(attack, new Rect(centre - half - profile.slamDustWidth, ground, profile.slamDustWidth, 1f), profile.slamDustDamage);
        Strike(attack, new Rect(centre + half, ground, profile.slamDustWidth, 1f), profile.slamDustDamage);
        Impact(side, .18f);
    }

    // What a fist hitting the ground looks and sounds like (no damage here).
    private void Impact(int side, float shake)
    {
        Rect fist = Rig.FistWorldRect(side);
        float ground = transform.position.y;
        Dust(new Vector3(fist.xMin - .4f, ground, 0f), -1.2f);
        Dust(new Vector3(fist.xMax + .4f, ground, 0f), 1.2f);
        Audio.Play(Profile.sfxSlam);
        CameraDirector.Shake(shake, .28f);
    }

    // ------------------------------------------------------------------ B. crane sweep
    private IEnumerator Sweep()
    {
        var profile = Profile;
        float groundY = Rig.GroundPixelY - BossSpriteLayout.FistSize.y;
        bool firstFromLeft = PlayerX < transform.position.x;
        for (int i = 0; i < profile.sweepCount && PlayerAlive; i++)
        {
            // Lean towards the player's side. In phase 2 the second sweep is fixed: straight back from the other side.
            bool quickFollowUp = Raging && i > 0;
            bool fromLeft = quickFollowUp ? !firstFromLeft : PlayerX < transform.position.x;
            if (i == 0) firstFromLeft = fromLeft;
            int side = fromLeft ? BossGoliathRig.Left : BossGoliathRig.Right;
            int attack = ++attackSerial;
            float startX = fromLeft ? ArenaLeftPixel : ArenaRightPixel - BossSpriteLayout.FistSize.x;
            float endX = fromLeft ? ArenaRightPixel - BossSpriteLayout.FistSize.x : ArenaLeftPixel;
            float telegraph = quickFollowUp ? profile.rageSweepGap : profile.sweepTelegraphSeconds;

            // Telegraph: lean, steam from that smokestack, the fist is lowered to the far end of that side.
            bodyControlled = true; Rig.BodyFrame = LeanFrame(fromLeft);
            pilotControlled = true; pilotPose = BossSpriteLayout.Pilot.Pull0;
            fistControlled[side] = true;
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Side;
            Rig.FistFacesLeft[side] = !fromLeft;
            Run(MoveFist(side, new Vector2(startX, groundY), telegraph * .8f, EaseInOut));
            Audio.Play(profile.sfxSteam, .8f);
            for (float t = 0f; t < telegraph; t += .2f)
            {
                StackSteam(fromLeft);
                yield return new WaitForSeconds(Mathf.Min(.2f, telegraph - t));
            }

            // Sweep: a low hit that travels with the fist.
            pilotPose = BossSpriteLayout.Pilot.Pull1;
            Audio.Play(profile.sfxSweep);
            float nextDust = 0f;
            for (float t = 0f; t < profile.sweepSeconds; t += Time.deltaTime)
            {
                Rig.FistPixel[side] = new Vector2(Mathf.Lerp(startX, endX, t / profile.sweepSeconds), groundY);
                Rect fist = Rig.FistWorldRect(side);
                Strike(attack, new Rect(fist.xMin, transform.position.y, fist.width, profile.sweepHitHeight), profile.sweepDamage);
                if (t >= nextDust)
                {
                    nextDust = t + .1f;
                    Dust(new Vector3(fromLeft ? fist.xMin : fist.xMax, transform.position.y, 0f), fromLeft ? -.8f : .8f);
                }
                yield return null;
            }
            Rig.FistPixel[side] = new Vector2(endX, groundY);
            pilotPose = BossSpriteLayout.Pilot.Pull0;
            bodyControlled = false;

            bool followUpComing = Raging && i == 0 && profile.sweepCount > 1;
            if (followUpComing)
            {
                // No rest: this fist is reeled in while the other one is already on its way.
                Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
                Run(ReelIn(side, .5f));
                continue;
            }
            yield return new WaitForSeconds(profile.sweepEndPause);
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
            yield return MoveFist(side, Rig.RestFist(side), .5f, EaseInOut);
            fistControlled[side] = false;
        }
    }

    private IEnumerator ReelIn(int side, float seconds)
    {
        yield return MoveFist(side, Rig.RestFist(side), seconds, EaseInOut);
        fistControlled[side] = false;
    }

    // ------------------------------------------------------------------ C. furnace shot + overheat
    private IEnumerator SlagShot()
    {
        var profile = Profile;
        furnaceControlled = true; Rig.FurnaceStage = (int)BossSpriteLayout.Furnace.Charge;
        pilotControlled = true; pilotPose = BossSpriteLayout.Pilot.Pull0;
        gearSpeed = 2f;
        Audio.Play(profile.sfxCharge);
        yield return new WaitForSeconds(Raging ? profile.rageSlagChargeSeconds : profile.slagChargeSeconds);

        pilotPose = BossSpriteLayout.Pilot.Pull1;
        Rig.FurnaceStage = (int)BossSpriteLayout.Furnace.Overheat;
        Vector2Int range = Raging ? profile.rageSlagCount : profile.slagCount;
        int count = Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y) + 1);
        var anchors = Rig.Anchors;
        Vector3 mouth = Rig.PixelToWorld(anchors.Furnace.x + BossSpriteLayout.FurnaceSize.x * .5f, anchors.Furnace.y + 2);
        float left = transform.position.x - profile.arenaHalfWidth + .6f, width = profile.arenaHalfWidth * 2f - 1.2f;
        int aimed = Random.Range(0, count); // one glob always goes for where the player stands now
        float longest = 0f;
        for (int i = 0; i < count; i++)
        {
            // One landing spot per equal slice of the arena, jittered inside the slice: spread out, never stacked.
            float x = i == aimed ? Mathf.Clamp(PlayerX, left, left + width) : left + width * (i + Random.Range(.15f, .85f)) / count;
            float flight = Random.Range(profile.slagFlightSeconds.x, profile.slagFlightSeconds.y);
            longest = Mathf.Max(longest, flight + i * .07f);
            BossSlag.Launch(this, mouth, new Vector3(x, transform.position.y, 0f), flight);
            yield return new WaitForSeconds(.07f);
        }
        yield return new WaitForSeconds(Mathf.Max(0f, longest - count * .07f) + .15f);
        yield return Overheat(Raging ? profile.rageOverheatSeconds : profile.overheatSeconds);
    }

    /// <summary>The machine sags and the bucket rides down its rails: Bartol can be hit from the ground.</summary>
    private IEnumerator Overheat(float seconds)
    {
        float groundY = Rig.GroundPixelY - BossSpriteLayout.FistSize.y;
        bodyControlled = furnaceControlled = true;
        brake = true;
        gearSpeed = 0f;
        Rig.FurnaceStage = (int)BossSpriteLayout.Furnace.Overheat;
        for (int side = 0; side < 2; side++)
        {
            fistControlled[side] = true;
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Slam;
            Run(MoveFist(side, new Vector2(Rig.FistPixel[side].x, groundY), .25f, EaseIn));
        }
        const int steps = 3;
        for (int i = 0; i < steps; i++)
        {
            Rig.BodyFrame = SlumpFrame(i);
            Rig.PilotDrop = BossSpriteLayout.PilotOverheatDrop * (i + 1) / steps;
            yield return new WaitForSeconds(.09f);
        }
        SetOverheated(true);
        Audio.Play(Profile.sfxSteam);
        Audio.Play(Profile.sfxWhistle, .45f);

        for (float t = 0f; t < seconds; t += .35f)
        {
            StackSteam(Mathf.FloorToInt(t / .35f) % 2 == 0);
            yield return new WaitForSeconds(Mathf.Min(.35f, seconds - t));
        }

        SetOverheated(false);
        for (int i = steps - 1; i >= 0; i--)
        {
            Rig.BodyFrame = SlumpFrame(i);
            Rig.PilotDrop = BossSpriteLayout.PilotOverheatDrop * i / steps;
            yield return new WaitForSeconds(.09f);
        }
        bodyControlled = false;
        gearSpeed = 1f;
        for (int side = 0; side < 2; side++) Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
        yield return ReturnFists(.4f);
    }

    // ------------------------------------------------------------------ D. triple quake (phase 2)
    private IEnumerator Quake()
    {
        var profile = Profile;
        float raisedY = -profile.slamRaisePixels;
        float groundY = Rig.GroundPixelY - BossSpriteLayout.FistSize.y;
        float centre = BossSpriteLayout.BodySize.x * .5f;
        float spread = profile.arenaHalfWidth * profile.quakeFistSpread * BossSpriteLayout.PixelsPerUnit;
        pilotControlled = true; pilotPose = BossSpriteLayout.Pilot.Roar0;
        furnaceControlled = true; Rig.FurnaceStage = (int)BossSpriteLayout.Furnace.Overheat;

        // Telegraph: both fists high over where they will land, the ground seams glow along the whole arena.
        for (int side = 0; side < 2; side++)
        {
            fistControlled[side] = true;
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
            float x = centre + (side == BossGoliathRig.Left ? -spread : spread) - BossSpriteLayout.FistSize.x * .5f;
            Run(MoveFist(side, new Vector2(x, raisedY), profile.quakeTelegraphSeconds * .7f, EaseOut));
        }
        Audio.Play(profile.sfxChain, .7f);
        Audio.Play(profile.sfxRoar, .55f);
        var seams = new List<BossFx>();
        float markLift = BossSpriteLayout.FxMarkerSize.y * .5f / BossSpriteLayout.PixelsPerUnit;
        for (float x = -profile.arenaHalfWidth + 1f; x <= profile.arenaHalfWidth - 1f; x += 2.4f)
        {
            var seam = BossFx.Play(Rig, Rig.Marker, new Vector3(transform.position.x + x, transform.position.y + markLift, 0f), .12f,
                Profile.bodySortingOrder + 1, loop: true);
            if (seam != null) { seams.Add(seam); heldFx.Add(seam); }
        }
        yield return new WaitForSeconds(profile.quakeTelegraphSeconds);
        brake = true; // it plants itself for the pounding

        for (int i = 0; i < profile.quakeCount && PlayerAlive; i++)
        {
            int attack = ++attackSerial;
            pilotPose = BossSpriteLayout.Pilot.Roar1;
            Run(DropFist(BossGoliathRig.Left, groundY, attack, profile.slamDamage));
            yield return DropFist(BossGoliathRig.Right, groundY, attack, profile.slamDamage);
            for (int side = 0; side < 2; side++)
            {
                Landing(side, attack);
                Rect fist = Rig.FistWorldRect(side);
                // One wave to each side of each fist; all four belong to this slam (one hit at most).
                BossShockwave.Launch(this, new Vector3(fist.xMin, transform.position.y, 0f), -1f, attack);
                BossShockwave.Launch(this, new Vector3(fist.xMax, transform.position.y, 0f), 1f, attack);
            }
            pilotPose = BossSpriteLayout.Pilot.Roar0;
            // Lift again for the next one (the last slam stays down).
            float lift = Mathf.Max(.1f, profile.quakeInterval - profile.slamDropSeconds - .12f);
            if (i < profile.quakeCount - 1)
            {
                yield return new WaitForSeconds(.12f);
                for (int side = 0; side < 2; side++)
                {
                    Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
                    Run(MoveFist(side, new Vector2(Rig.FistPixel[side].x, raisedY), lift, EaseOut));
                }
                yield return new WaitForSeconds(lift);
            }
        }
        foreach (var seam in seams) Release(seam);
        yield return new WaitForSeconds(profile.quakeEndPause);
        yield return ReturnFists(.45f);
    }

    // ------------------------------------------------------------------ helpers
    private float PlayerPixelX => Rig.WorldToPixelX(PlayerX);
    private float ArenaLeftPixel => Rig.WorldToPixelX(transform.position.x - Profile.arenaHalfWidth);
    private float ArenaRightPixel => Rig.WorldToPixelX(transform.position.x + Profile.arenaHalfWidth);

    private BossSpriteLayout.Body LeanFrame(bool left) => Raging
        ? (left ? BossSpriteLayout.Body.RageLeanLeft : BossSpriteLayout.Body.RageLeanRight)
        : (left ? BossSpriteLayout.Body.LeanLeft : BossSpriteLayout.Body.LeanRight);

    private BossSpriteLayout.Body SlumpFrame(int step) =>
        (Raging ? BossSpriteLayout.Body.RageSlump0 : BossSpriteLayout.Body.Slump0) + Mathf.Clamp(step, 0, 2);

    private int FistNearest(float pixelX)
    {
        float half = BossSpriteLayout.FistSize.x * .5f;
        return Mathf.Abs(Rig.FistPixel[BossGoliathRig.Left].x + half - pixelX) <= Mathf.Abs(Rig.FistPixel[BossGoliathRig.Right].x + half - pixelX)
            ? BossGoliathRig.Left : BossGoliathRig.Right;
    }

    /// <summary>One attack can hurt the player once, however many frames, zones or projectiles it covers.</summary>
    public void Strike(int attack, Rect area, int damage)
    {
        if (lastAttackThatHit == attack) return;
        if (HitPlayer(area, damage)) lastAttackThatHit = attack;
    }

    private void Run(IEnumerator routine) => helpers.Add(StartCoroutine(routine));

    private IEnumerator MoveFist(int side, Vector2 to, float seconds, System.Func<float, float> ease)
    {
        Vector2 from = Rig.FistPixel[side];
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            Rig.FistPixel[side] = Vector2.LerpUnclamped(from, to, ease(Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        Rig.FistPixel[side] = to;
    }

    private IEnumerator ReturnFists(float seconds)
    {
        for (int side = 0; side < 2; side++)
        {
            Rig.FistFrame[side] = BossSpriteLayout.Fist.Clench;
            Run(MoveFist(side, Rig.RestFist(side), seconds, EaseInOut));
        }
        yield return new WaitForSeconds(seconds);
        fistControlled[0] = fistControlled[1] = false;
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
    private static float EaseIn(float t) => t * t;
    private static float EaseInOut(float t) => t * t * (3f - 2f * t);

    private BossFx Hold(Sprite[] frames, Vector3 position)
    {
        var fx = BossFx.Play(Rig, frames, position, 1000f, Profile.bodySortingOrder + 1, loop: true);
        if (fx != null) heldFx.Add(fx);
        return fx;
    }

    private void Release(BossFx fx)
    {
        if (fx == null) return;
        heldFx.Remove(fx);
        fx.Stop();
    }

    private void Dust(Vector3 groundPoint, float drift)
    {
        float lift = BossSpriteLayout.FxDustSize.y * .5f / BossSpriteLayout.PixelsPerUnit;
        BossFx.Play(Rig, Rig.Dust, groundPoint + Vector3.up * lift, .09f, Profile.fxSortingOrder, new Vector2(drift, .5f), drift > 0f);
    }

    private void StackSteam(bool left)
    {
        var anchors = Rig.Anchors;
        Vector2Int stack = left ? anchors.StackLeft : anchors.StackRight;
        BossFx.Play(Rig, Rig.Steam, Rig.PixelToWorld(stack.x, stack.y - 6), .12f, Profile.bodySortingOrder + 1, new Vector2(left ? -.3f : .3f, 1.6f));
    }
}
