using System;
using BehindTheGear.HideOut;
using UnityEngine;

/// <summary>
/// Player level and experience (EXP). EXP comes from the violet pickups monsters drop (RewardPickup); when the ring is
/// full the level goes up and the player is paid action points (AP) through the existing PlayerFieldWallet.
/// The curve and the AP per level live in Resources/Monsters/RewardTable (MonsterRewardTable).
///
/// Lives next to PlayerInventory / HideOutPlayerBridge and joins the bed save through IHideOutSaveParticipant
/// (extension key "player_level_v1"), so the save format itself is unchanged and older saves simply load as level 1.
/// Added automatically to every player (see Hook); nothing has to be wired in a scene or prefab.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerLevel : MonoBehaviour, IHideOutSaveParticipant, IHideOutSaveDefault
{
    private const int HighestStoredLevel = 9999;

    private static MonsterRewardTable table;

    public int Level { get; private set; } = 1;
    /// <summary>EXP gathered inside the current level (0 .. ExperienceToNext - 1).</summary>
    public int Experience { get; private set; }
    public int ExperienceToNext => Table.ExperienceToNext(Level);
    public bool IsMaxLevel => ExperienceToNext <= 0;
    public float Progress => IsMaxLevel ? 1f : Mathf.Clamp01(Experience / (float)ExperienceToNext);
    public const string SaveExtensionKey = "player_level_v1";
    public string SaveKey => SaveExtensionKey;

    /// <summary>Level or EXP changed (pickup, level-up or a loaded save).</summary>
    public event Action Changed;
    /// <summary>EXP was gained from the world: amount and where the pickup was (for the fly-in effect).</summary>
    public event Action<int, Vector3> ExperienceGained;
    /// <summary>One call per level gained, with the new level and the AP paid for it.</summary>
    public event Action<int, int> LeveledUp;
    /// <summary>Level and EXP were replaced by a loaded save: show them as they are, without any level-up fanfare.</summary>
    public event Action Restored;

    /// <summary>Level settings; falls back to the built-in defaults when the table asset is missing.</summary>
    public static MonsterRewardTable Table
    {
        get
        {
            if (table == null) table = Resources.Load<MonsterRewardTable>(MonsterRewardTable.ResourcePath);
            if (table == null) table = ScriptableObject.CreateInstance<MonsterRewardTable>();
            return table;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Hook()
    {
        // Domain reload is disabled in this project: drop cached state and never subscribe twice.
        table = null;
        PlayerTargetRegistry.TargetChanged -= OnPlayerChanged;
        PlayerTargetRegistry.TargetChanged += OnPlayerChanged;
    }

    // Every player that registers itself as the monsters' target gets a level (new game, continue, direct scene play).
    private static void OnPlayerChanged(Transform player)
    {
        if (player == null) return;
        var inventory = player.GetComponentInParent<PlayerInventory>();
        if (inventory == null) inventory = player.GetComponentInChildren<PlayerInventory>();
        if (inventory != null) EnsureOn(inventory.gameObject);
    }

    /// <summary>The level component of the player object that owns the inventory; added (with its badge) when missing.</summary>
    public static PlayerLevel EnsureOn(GameObject owner)
    {
        var level = owner.GetComponent<PlayerLevel>();
        if (level == null) level = owner.AddComponent<PlayerLevel>();
        if (owner.GetComponent<PlayerLevelBadge>() == null) owner.AddComponent<PlayerLevelBadge>();
        return level;
    }

    /// <summary>Adds EXP and levels up as often as it fills the ring. Returns the number of levels gained.</summary>
    public int AddExperience(int amount, Vector3? source = null)
    {
        if (amount <= 0 || IsMaxLevel) return 0;
        int gained = 0, firstNewLevel = Level + 1;
        long pool = (long)Experience + amount;
        while (true)
        {
            int need = ExperienceToNext;
            if (need <= 0 || pool < need) break;
            pool -= need;
            Level++;
            gained++;
        }
        Experience = IsMaxLevel ? 0 : (int)Math.Min(pool, int.MaxValue);

        int actionPointsPerLevel = Mathf.Max(0, Table.actionPointsPerLevel);
        if (gained > 0 && actionPointsPerLevel > 0)
        {
            // Same path as AP picked up in the field: the wallet adds it to the base state (or keeps it until the
            // base state exists, e.g. before the first base visit).
            var wallet = GetComponent<PlayerFieldWallet>();
            if (wallet == null) wallet = gameObject.AddComponent<PlayerFieldWallet>();
            wallet.Add(RewardPickup.Kind.ActionPoint, checked(gained * actionPointsPerLevel));
        }

        ExperienceGained?.Invoke(amount, source ?? transform.position);
        Changed?.Invoke();
        for (int i = 0; i < gained; i++) LeveledUp?.Invoke(firstNewLevel + i, actionPointsPerLevel);
        return gained;
    }

    // ---------- Bed save (IHideOutSaveParticipant) ----------
    [Serializable] private sealed class LevelSave { public int version = 1; public int level = 1; public int experience; }

    /// <summary>Level stored in a save (1 for saves made before levels existed). Used by the save menu.</summary>
    public static int LevelIn(HideOutSave save)
    {
        var entry = save != null && save.extensions != null ? save.extensions.Find(x => x != null && x.key == SaveExtensionKey) : null;
        if (entry == null || string.IsNullOrEmpty(entry.json)) return 1;
        try { return Mathf.Max(1, JsonUtility.FromJson<LevelSave>(entry.json).level); }
        catch (Exception) { return 1; }
    }

    public string CaptureJson() => JsonUtility.ToJson(new LevelSave { level = Level, experience = Experience });

    public void ValidateJson(string json)
    {
        var data = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<LevelSave>(json);
        if (data == null || data.version != 1 || data.level < 1 || data.level > HighestStoredLevel || data.experience < 0)
            throw new ArgumentException("플레이어 레벨 저장 값이 올바르지 않습니다.");
    }

    // HideOutPlayerBridge re-applies the captured state after every base transaction, so this must stay a no-op when
    // nothing changed. Loading never pays level-up AP again: the AP is already part of the saved state.
    public void RestoreJson(string json)
    {
        ValidateJson(json);
        var data = JsonUtility.FromJson<LevelSave>(json);
        int level = data.level, experience = data.experience;
        int need = Table.ExperienceToNext(level);
        // The curve may have been retuned since the save: keep the level and never hand out a free level-up.
        experience = need <= 0 ? 0 : Mathf.Min(experience, need - 1);
        if (level == Level && experience == Experience) return;
        Level = level;
        Experience = experience;
        Restored?.Invoke();
        Changed?.Invoke();
    }

    // A save made before levels existed: the loaded game starts at Lv 1 instead of keeping the current session's level.
    public void RestoreDefault() => RestoreJson(JsonUtility.ToJson(new LevelSave()));
}
