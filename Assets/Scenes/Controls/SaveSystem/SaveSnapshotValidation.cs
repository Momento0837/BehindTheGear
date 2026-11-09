using System;
using System.Collections.Generic;
using System.IO;
using BehindTheGear.HideOut;
using UnityEngine;

public static class SaveSnapshotValidation
{
    [Serializable] private sealed class InventoryConfig { public int capacity = 15, slotsPerAttackStat = 2, questCapacity = 5; }
    [Serializable] private sealed class Stats
    {
        public int availablePoints = 0, evasion = 0, stealth = 0, attack = 0, defense = 0, persuasion = 0, statSpreadLimit = 0;
        public bool Valid => availablePoints >= 0 && availablePoints <= 1000000 && evasion >= 0 && evasion <= 1000
            && stealth >= 0 && stealth <= 1000 && attack >= 0 && attack <= 1000 && defense >= 0 && defense <= 1000
            && persuasion >= 0 && persuasion <= 1000 && statSpreadLimit >= 0;
    }
    [Serializable] private sealed class Health { public int maxHealth = 0, defense = 0; }
    public static void StampFacilities(HideOutSave s, HideOutSettings settings)
    {
        s.facilities = new();
        foreach (Facility f in Enum.GetValues(typeof(Facility)))
            s.facilities.Add(new FacilityProgress { facilityId = settings.FindPresentation(f).facilityId,
                level = s.levels[(int)f], unlocked = !HideOutModel.Locked(s, f) });
    }
    public static void ValidateStructure(HideOutSave s, HideOutSettings settings)
    {
        if (s == null || (s.version != 1 && s.version != 2) || s.levels == null || s.levels.Length != 8
            || s.inventory == null || s.questInventory == null || s.storage == null || s.equipment == null || s.equipment.Length != 6
            || s.quickSlots == null || s.quickSlots.Length != 3 || s.extensions == null || s.dummyWeaponBonuses == null
            || s.inventory.Count > 4096 || s.storage.Count > 4096 || s.questInventory.Count > 30)
            throw new InvalidDataException("손상된 저장 구조입니다.");
        if (s.version == 2)
        {
            if (s.facilities == null || s.facilities.Count != 8) throw new InvalidDataException("시설 기록이 손상되었습니다.");
            var ids = new HashSet<string>();
            foreach (var record in s.facilities)
            {
                var presentation = record != null ? Array.Find(settings.prototypeFacilities, p => p.facilityId == record.facilityId) : null;
                if (presentation == null || !ids.Add(record.facilityId) || record.level != s.levels[(int)presentation.facility]
                    || record.unlocked != !HideOutModel.Locked(s, presentation.facility)) throw new InvalidDataException("잘못된 시설 ID/해금 상태입니다.");
            }
        }
        for (int i = 0; i < 8; i++)
        {
            var definition = Array.Find(settings.rules.facilities, f => (int)f.facility == i);
            if (s.levels[i] < 0 || s.levels[i] > (definition?.upgrades?.Length ?? 0)) throw new InvalidDataException("잘못된 시설 단계입니다.");
        }
        QuestJournal.Validate(s.quests);
        if (string.IsNullOrWhiteSpace(s.inventoryConfiguration) || string.IsNullOrWhiteSpace(s.playerStats)
            || string.IsNullOrWhiteSpace(s.healthConfiguration)) throw new InvalidDataException("플레이어 설정이 누락되었습니다.");
        var config = JsonUtility.FromJson<InventoryConfig>(s.inventoryConfiguration);
        var stats = JsonUtility.FromJson<Stats>(s.playerStats);
        var health = JsonUtility.FromJson<Health>(s.healthConfiguration);
        if (config == null || config.capacity < 1 || config.capacity > 120 || config.slotsPerAttackStat < 0 || config.slotsPerAttackStat > 100
            || config.questCapacity < 1 || config.questCapacity > 30 || stats == null || !stats.Valid || health == null
            || health.maxHealth < 1 || health.maxHealth > 1000000 || health.defense < 0)
            throw new InvalidDataException("잘못된 플레이어 설정입니다.");
        int capacity = config.capacity + stats.attack * config.slotsPerAttackStat + s.levels[3] * settings.rules.inventorySlotsPerLevel;
        if (capacity > 4096 || s.inventory.GetRange(Math.Min(capacity, s.inventory.Count), Math.Max(0, s.inventory.Count - capacity)).Exists(slot => slot == null || !slot.Empty) || s.questInventory.Count > config.questCapacity
            || s.hp > health.maxHealth + s.levels[1] * settings.rules.bedHPPerLevel || s.mana < 0 || s.baseMana < 1 || s.baseMana > 1000000
            || !Finite(s.clinicReadyAt) || !Finite(s.potionsReadyAt) || !Finite(s.activeClock))
            throw new InvalidDataException("저장 용량/스탯 범위를 벗어났습니다.");
        var keys = new HashSet<string>();
        foreach (var extension in s.extensions)
            if (extension == null || string.IsNullOrEmpty(extension.key) || !keys.Add(extension.key))
                throw new InvalidDataException("잘못된 확장 저장 기록입니다.");
    }
    private static bool Finite(double n) => n >= 0 && !double.IsNaN(n) && !double.IsInfinity(n);
}
