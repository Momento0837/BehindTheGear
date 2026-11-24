using System;
using System.Collections.Generic;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    // Selena situation lines are pre-generated and human-reviewed (Resources/Dialogue/Selena/selena_lines_pool.json).
    // No model runs in the game: the state is normalized exactly like slots.py `norm`, the single matching slot is
    // found (slot conditions never overlap) and one of its lines is picked, avoiding the last two lines of that slot.
    public static class SelenaEvent
    {
        public const string Returned = "returned", ClinicReady = "clinic_ready", ClinicCooldown = "clinic_cooldown",
            AfterHeal = "after_heal", PotionRefilled = "potion_refilled", NewEquipment = "new_equipment",
            Leaving = "leaving", Tired = "tired";
    }

    // Already-banded state (see SelenaGameState for how the game values are turned into these bands).
    public struct SelenaLineState
    {
        public string eventId;
        public string health;   // healthy, slightly_hurt, hurt, badly_hurt, critical, unknown
        public string stamina;  // fresh, tired, exhausted, unknown
        public bool clinicReady, potionReady;
        public string lastMap;  // 포레스트, 가베지, 마켓, 거주구역 or ""
        public string gear;     // cloak, weapon or ""
    }

    [Serializable] public sealed class SelenaLineSlot
    {
        public string id, @event, clinic, potion, map, gear, description;
        public string[] health = Array.Empty<string>(), stamina = Array.Empty<string>(), lines = Array.Empty<string>();
    }

    [Serializable] public sealed class SelenaLinePoolData
    {
        public string version, npcId;
        public SelenaLineSlot[] slots = Array.Empty<SelenaLineSlot>();
    }

    public sealed class SelenaLinePool
    {
        public const string ResourcePath = "Dialogue/Selena/selena_lines_pool";
        public const int RecentLinesToAvoid = 2;

        private static SelenaLinePool shared;
        // Shared for the play session so "recent lines" survive screen/scene changes. Not saved to disk.
        public static SelenaLinePool Shared => shared ??= Load();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetShared() => shared = null;

        public SelenaLinePoolData Data { get; }
        private readonly Dictionary<string, Queue<string>> recent = new();
        private readonly System.Random random;

        public SelenaLinePool(SelenaLinePoolData data, int? seed = null)
        {
            Data = data ?? new SelenaLinePoolData();
            Data.slots ??= Array.Empty<SelenaLineSlot>();
            random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public static SelenaLinePool Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError($"[Selena] Line pool is missing: Resources/{ResourcePath}.json");
                return new SelenaLinePool(null);
            }
            try { return new SelenaLinePool(JsonUtility.FromJson<SelenaLinePoolData>(asset.text)); }
            catch (Exception e)
            {
                Debug.LogError($"[Selena] Line pool could not be read: {e.Message}");
                return new SelenaLinePool(null);
            }
        }

        // slots.py norm(): unknown health -> healthy, unknown stamina -> fresh (tired event: fresh/unknown -> tired),
        // clinic/potion: only "ready" stays ready.
        public static SelenaLineState Normalize(SelenaLineState s)
        {
            if (string.IsNullOrEmpty(s.health) || s.health == "unknown") s.health = "healthy";
            if (string.IsNullOrEmpty(s.stamina) || s.stamina == "unknown") s.stamina = "fresh";
            if (s.eventId == SelenaEvent.Tired && s.stamina == "fresh") s.stamina = "tired";
            s.lastMap ??= string.Empty;
            s.gear ??= string.Empty;
            return s;
        }

        public SelenaLineSlot Resolve(SelenaLineState state)
        {
            var n = Normalize(state);
            foreach (var slot in Data.slots)
                if (slot != null && Matches(slot, n)) return slot;
            return null;
        }

        private static bool Matches(SelenaLineSlot x, SelenaLineState n)
        {
            if (x.@event != n.eventId) return false;
            if (x.health == null || Array.IndexOf(x.health, n.health) < 0) return false;
            if (x.stamina == null || Array.IndexOf(x.stamina, n.stamina) < 0) return false;
            return Any(x.clinic, n.clinicReady ? "ready" : "wait")
                && Any(x.potion, n.potionReady ? "ready" : "none")
                && Any(x.map, n.lastMap)
                && Any(x.gear, n.gear);
        }

        private static bool Any(string condition, string value) => condition == "any" || condition == value;

        // Returns null when no slot matches or the slot has no lines (the caller then stays silent).
        public string Pick(SelenaLineState state) => Pick(state, out _);

        public string Pick(SelenaLineState state, out string slotId)
        {
            var slot = Resolve(state);
            slotId = slot?.id;
            if (slot?.lines == null || slot.lines.Length == 0) return null;
            if (!recent.TryGetValue(slot.id, out var history)) recent[slot.id] = history = new Queue<string>();

            var candidates = new List<string>();
            foreach (var line in slot.lines)
                if (!string.IsNullOrWhiteSpace(line) && !history.Contains(line)) candidates.Add(line);
            // Fewer than three lines: avoid at least the very last one when possible.
            if (candidates.Count == 0)
                foreach (var line in slot.lines)
                    if (!string.IsNullOrWhiteSpace(line) && (slot.lines.Length == 1 || line != Last(history))) candidates.Add(line);
            if (candidates.Count == 0) return null;

            string chosen = candidates[random.Next(candidates.Count)];
            history.Enqueue(chosen);
            while (history.Count > RecentLinesToAvoid) history.Dequeue();
            return chosen;
        }

        private static string Last(Queue<string> history)
        {
            string last = null;
            foreach (var line in history) last = line;
            return last;
        }
    }
}
