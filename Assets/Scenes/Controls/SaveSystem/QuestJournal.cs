using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BehindTheGear.HideOut;
using UnityEngine;

[Serializable] public sealed class QuestObjective
{
    public string kind = "collect", targetId = "";
    public int required = 1;
}
/// <summary>Permanent quest records; scene NPCs are views, never the state owners.</summary>
public static class QuestJournal
{
    private static List<QuestProgress> records = new();
    public static event Action Changed;
    public static QuestDefinition Definition(string id)
    {
        var asset = Resources.Load<TextAsset>("Quests/" + id);
        if (asset == null) return null;
        var definition = JsonUtility.FromJson<QuestDefinition>(asset.text);
        return definition != null && definition.questId == id ? definition : null;
    }
    public static string Id(TextAsset asset) => asset != null ? JsonUtility.FromJson<QuestDefinition>(asset.text)?.questId : null;
    public static QuestProgress Find(string id) => records.Find(r => r.questId == id);
    public static bool Accept(string id)
    {
        var definition = Definition(id);
        if (definition?.objectives == null || definition.objectives.Length == 0 || Find(id) != null) return false;
        records.Add(new QuestProgress { questId = id, stage = 1, amounts = new int[definition.objectives.Length] });
        Publish(); return true;
    }
    public static void Record(string kind, string targetId, int amount)
    {
        if (amount <= 0) return;
        bool changed = false;
        foreach (var record in records.Where(r => r.stage == 1))
        {
            var definition = Definition(record.questId);
            for (int i = 0; i < definition.objectives.Length; i++)
            {
                var objective = definition.objectives[i];
                if (objective.kind != kind || (!string.IsNullOrEmpty(objective.targetId) && objective.targetId != targetId)) continue;
                record.amounts[i] += Math.Min(amount, objective.required - record.amounts[i]);
                changed = true;
            }
            if (definition.objectives.Select((o, i) => record.amounts[i] >= o.required).All(v => v)) record.stage = 2;
        }
        if (changed) Publish();
    }
    public static bool Report(string id, HideOutPlayerBridge player)
    {
        var record = Find(id);
        var definition = Definition(id);
        if (record == null || record.stage != 2 || record.rewardClaimed || player == null) return false;
        // Change is a memory transaction. Claim flag is set before publishing to block reentrant callbacks.
        bool claimed = false;
        bool success = player.Change(s =>
        {
            s.coins = checked(s.coins + definition.rewardCoins);
            s.actionPoints = checked(s.actionPoints + definition.rewardActionPoints);
            record.rewardClaimed = true; record.stage = 3; claimed = true;
            return "퀘스트 완료 · 보상 지급 완료";
        });
        if (!success && claimed) { record.rewardClaimed = false; record.stage = 2; }
        if (success) Publish();
        return success;
    }
    public static List<QuestProgress> Capture() => records.Select(r => new QuestProgress
        { questId = r.questId, stage = r.stage, rewardClaimed = r.rewardClaimed, amounts = (int[])r.amounts.Clone() }).ToList();
    public static void Validate(List<QuestProgress> values)
    {
        if (values == null || values.Count > 256) throw new InvalidDataException("퀘스트 기록이 손상되었습니다.");
        var ids = new HashSet<string>();
        foreach (var record in values)
        {
            var definition = record != null ? Definition(record.questId) : null;
            if (definition?.objectives == null || !ids.Add(record.questId) || record.stage < 1 || record.stage > 3
                || record.amounts == null || record.amounts.Length != definition.objectives.Length
                || record.rewardClaimed != (record.stage == 3)) throw new InvalidDataException("잘못된 퀘스트 상태입니다.");
            if (record.stage == 1 && record.amounts.Select((n, i) => n == definition.objectives[i].required).All(v => v))
                throw new InvalidDataException("완료 목표와 퀘스트 단계가 일치하지 않습니다.");
            for (int i = 0; i < record.amounts.Length; i++)
                if (record.amounts[i] < 0 || record.amounts[i] > definition.objectives[i].required
                    || record.stage >= 2 && record.amounts[i] != definition.objectives[i].required)
                    throw new InvalidDataException("잘못된 퀘스트 진행량입니다.");
        }
    }
    public static void Restore(List<QuestProgress> values) { Validate(values); records = values.Select(r => new QuestProgress
        { questId = r.questId, stage = r.stage, rewardClaimed = r.rewardClaimed, amounts = (int[])r.amounts.Clone() }).ToList(); Publish(); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear() { records = new(); Publish(); }
    public static void Publish()
    {
        var active = records.LastOrDefault(r => r.stage < 3);
        if (active != null)
        {
            var d = Definition(active.questId);
            string text = d.objective + " " + string.Join(" · ", active.amounts.Select((n, i) => n + "/" + d.objectives[i].required));
            QuestTracker.Accept(typeof(QuestJournal), d.questName, active.stage == 2 ? text + " · NPC에게 보고" : text);
        }
        else QuestTracker.Complete(typeof(QuestJournal));
        Changed?.Invoke();
    }
}
