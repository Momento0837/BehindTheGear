using UnityEngine;

/// <summary>
/// A glob of molten metal from the furnace (pattern C). It flies a fixed arc to a landing point chosen when it is
/// fired; a mark blinks on the ground shortly before it lands, and it hurts the player only at the moment of landing.
/// </summary>
public sealed class BossSlag : MonoBehaviour
{
    private BossGoliath boss;
    private SpriteRenderer spriteRenderer;
    private BossFx marker;
    private Vector3 from, to;
    private float flight, arc, age;

    public static BossSlag Launch(BossGoliath boss, Vector3 from, Vector3 landing, float flightSeconds)
    {
        var go = new GameObject("Boss Slag");
        var slag = go.AddComponent<BossSlag>();
        slag.boss = boss;
        slag.from = from;
        slag.to = landing;
        slag.flight = Mathf.Max(.2f, flightSeconds);
        slag.arc = boss.Profile.slagArcHeight;
        slag.spriteRenderer = go.AddComponent<SpriteRenderer>();
        if (boss.Rig.Material != null) slag.spriteRenderer.sharedMaterial = boss.Rig.Material;
        slag.spriteRenderer.sortingOrder = boss.Profile.fxSortingOrder;
        slag.spriteRenderer.sprite = boss.Rig.Slag[0];
        go.transform.position = BossGoliath.Snap(from);
        return slag;
    }

    private void Update()
    {
        if (boss == null) { Cleanup(); return; }
        age += Time.deltaTime;
        float u = Mathf.Clamp01(age / flight);
        Vector3 position = Vector3.Lerp(from, to, u) + Vector3.up * (arc * 4f * u * (1f - u));
        transform.position = BossGoliath.Snap(position);
        spriteRenderer.sprite = boss.Rig.Slag[Mathf.FloorToInt(age / .09f) % boss.Rig.Slag.Length];

        var profile = boss.Profile;
        if (marker == null && flight - age <= profile.slagMarkerSeconds)
        {
            float markY = to.y + BossSpriteLayout.FxMarkerSize.y * .5f / BossSpriteLayout.PixelsPerUnit;
            marker = BossFx.Play(boss.Rig, boss.Rig.Marker, new Vector3(to.x, markY, 0f), .1f, profile.fxSortingOrder - 1, loop: true);
        }
        if (age < flight) return;

        // Landing: a small burst, and damage only to a player standing on the mark.
        var hit = new Rect(to.x - profile.slagHitSize.x * .5f, to.y, profile.slagHitSize.x, profile.slagHitSize.y);
        boss.HitPlayer(hit, profile.slagDamage);
        BossFx.Play(boss.Rig, boss.Rig.Spark, to + Vector3.up * .4f, .07f, profile.fxSortingOrder);
        boss.Audio.Play(profile.sfxSlagHit, .6f);
        Cleanup();
    }

    private void Cleanup()
    {
        if (marker != null) marker.Stop();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (marker != null) marker.Stop();
    }
}

/// <summary>
/// The ground wave of the triple quake (pattern D). It runs along the ground away from where a fist landed and hurts
/// a player it passes through; it is low, so a jump clears it. All waves of one slam share one attack id: one hit.
/// </summary>
public sealed class BossShockwave : MonoBehaviour
{
    private BossGoliath boss;
    private SpriteRenderer spriteRenderer;
    private Vector3 position;
    private float direction, age, limitX;
    private int attack;

    public static BossShockwave Launch(BossGoliath boss, Vector3 groundPoint, float direction, int attack)
    {
        var go = new GameObject("Boss Shockwave");
        var wave = go.AddComponent<BossShockwave>();
        wave.boss = boss;
        wave.direction = Mathf.Sign(direction);
        wave.attack = attack;
        wave.position = groundPoint + Vector3.up * (BossSpriteLayout.FxShockwaveSize.y * .5f / BossSpriteLayout.PixelsPerUnit);
        wave.limitX = boss.transform.position.x + wave.direction * (boss.Profile.arenaHalfWidth + 1.5f);
        wave.spriteRenderer = go.AddComponent<SpriteRenderer>();
        if (boss.Rig.Material != null) wave.spriteRenderer.sharedMaterial = boss.Rig.Material;
        wave.spriteRenderer.sortingOrder = boss.Profile.fxSortingOrder;
        wave.spriteRenderer.flipX = wave.direction < 0f; // the sheet faces right
        wave.spriteRenderer.sprite = boss.Rig.Shockwave[0];
        go.transform.position = BossGoliath.Snap(wave.position);
        return wave;
    }

    private void Update()
    {
        if (boss == null || boss.IsDead) { Destroy(gameObject); return; }
        var profile = boss.Profile;
        age += Time.deltaTime;
        position.x += direction * profile.quakeWaveSpeed * Time.deltaTime;
        transform.position = BossGoliath.Snap(position);
        // Rise (frames 0-1), then roll between the two tallest frames.
        var frames = boss.Rig.Shockwave;
        int step = Mathf.FloorToInt(age / .07f);
        spriteRenderer.sprite = frames[step < 2 ? step : 1 + step % 2];

        float width = BossSpriteLayout.FxShockwaveSize.x / BossSpriteLayout.PixelsPerUnit;
        boss.Strike(attack, new Rect(position.x - width * .5f, boss.transform.position.y, width, profile.quakeWaveHeight), profile.quakeDamage);
        if ((limitX - position.x) * direction <= 0f) Destroy(gameObject);
    }
}
