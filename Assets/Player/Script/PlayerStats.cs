using System;
using UnityEngine;

public enum PersonalStatType
{
    Evasion,
    Stealth,
    Attack,
    Defense,
    Persuasion
}

[DisallowMultipleComponent]
public sealed class PlayerStats : MonoBehaviour
{
    [SerializeField, Min(0)] private int availablePoints = 5;
    [SerializeField, Min(0)] private int evasion;
    [SerializeField, Min(0)] private int stealth;
    [SerializeField, Min(0)] private int attack;
    [SerializeField, Min(0)] private int defense;
    [SerializeField, Min(0)] private int persuasion;
    [SerializeField, Min(0)] private int statSpreadLimit = 5;

    public event Action Changed;

    public int AvailablePoints => availablePoints;
    public int StatSpreadLimit => statSpreadLimit;
    public int Evasion => evasion;
    public int Stealth => stealth;
    public int Attack => attack;
    public int Defense => defense;
    public int Persuasion => persuasion;
    public float MonsterAttackIgnoreChance => evasion * .005f;
    public float DetectionGainMultiplier => Mathf.Max(0f, 1f - stealth * .015f);
    public int AttackPowerBonus => attack;
    public float DamageReduction => defense < 2 ? 0f : defense * .005f;
    public int ShopPriceDiscount => persuasion;

    public int GetValue(PersonalStatType type)
    {
        return type switch
        {
            PersonalStatType.Evasion => evasion,
            PersonalStatType.Stealth => stealth,
            PersonalStatType.Attack => attack,
            PersonalStatType.Defense => defense,
            PersonalStatType.Persuasion => persuasion,
            _ => 0
        };
    }

    public bool TryUpgrade(PersonalStatType type, out string message)
    {
        if (availablePoints <= 0)
        {
            message = "포인트가 부족합니다.";
            return false;
        }

        int current = GetValue(type);
        if (current + 1 > GetLowestStatValue() + statSpreadLimit)
        {
            message = "스탯 편차 제한으로 강화할 수 없습니다.";
            return false;
        }

        SetValue(type, current + 1);
        availablePoints--;

        string displayName = GetDisplayName(type);
        message = $"{displayName}{GetSubjectParticle(displayName)} 1 증가했습니다.";
        Changed?.Invoke();
        return true;
    }

    public void AddPoints(int amount)
    {
        if (amount <= 0) return;
        availablePoints += amount;
        Changed?.Invoke();
    }

    public string GetEffectSummary(PersonalStatType type)
    {
        return type switch
        {
            PersonalStatType.Evasion => $"몬스터 공격 무시 +{GetValue(type) * .5f:0.#}%",
            PersonalStatType.Stealth => $"발각 증가 속도 -{GetValue(type) * 1.5f:0.#}%",
            PersonalStatType.Attack => $"공격력 +{AttackPowerBonus}, 인벤토리 +{Attack * 2}칸",
            PersonalStatType.Defense => defense < 2 ? "2부터 피해 경감 적용" : $"받는 피해 -{defense * .5f:0.#}%",
            PersonalStatType.Persuasion => $"상점 가격 -{ShopPriceDiscount}원",
            _ => string.Empty
        };
    }

    public static string GetDisplayName(PersonalStatType type)
    {
        return type switch
        {
            PersonalStatType.Evasion => "회피",
            PersonalStatType.Stealth => "은신",
            PersonalStatType.Attack => "공격",
            PersonalStatType.Defense => "방어",
            PersonalStatType.Persuasion => "설득",
            _ => type.ToString()
        };
    }

    private static string GetSubjectParticle(string text)
    {
        if (string.IsNullOrEmpty(text)) return "이";

        char last = text[^1];
        if (last < 0xAC00 || last > 0xD7A3) return "이";

        return (last - 0xAC00) % 28 == 0 ? "가" : "이";
    }

    private int GetLowestStatValue()
    {
        return Mathf.Min(evasion, stealth, attack, defense, persuasion);
    }

    private void SetValue(PersonalStatType type, int value)
    {
        switch (type)
        {
            case PersonalStatType.Evasion:
                evasion = value;
                break;
            case PersonalStatType.Stealth:
                stealth = value;
                break;
            case PersonalStatType.Attack:
                attack = value;
                break;
            case PersonalStatType.Defense:
                defense = value;
                break;
            case PersonalStatType.Persuasion:
                persuasion = value;
                break;
        }
    }
}
