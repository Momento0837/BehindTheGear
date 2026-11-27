using System;

namespace BehindTheGear.HideOut
{
    // What just happened in the bedroom screen. Selena answers each event with one short line.
    public enum BedTalkEvent
    {
        Enter,         // Arrived from the base.
        Saved,         // HideOutPlayerBridge.Save succeeded (bed save, the only disk save).
        SaveFailed,    // Save refused or failed; the previous save is kept.
        UpgradeDone,   // Bed level went up (max HP bonus).
        UpgradeShort,  // Not enough coins / action points / materials.
        UpgradeMaxed,  // No further bed upgrade step is defined.
    }

    // Only what Selena can see in the bedroom. An SLM source receives the same fields.
    public readonly struct BedTalkContext
    {
        public readonly BedTalkEvent talkEvent;
        public readonly int currentHealth, maxHealth, bedLevel;
        public readonly bool hasSave;
        public readonly double minutesSinceSave;

        public BedTalkContext(BedTalkEvent talkEvent, int currentHealth, int maxHealth, int bedLevel, bool hasSave, double minutesSinceSave)
        {
            this.talkEvent = talkEvent; this.currentHealth = currentHealth; this.maxHealth = maxHealth;
            this.bedLevel = bedLevel; this.hasSave = hasSave; this.minutesSinceSave = Math.Max(0, minutesSinceSave);
        }

        public bool Hurt => maxHealth > 0 && currentHealth < maxHealth * .6f;
    }

    // Replace point for the generative Selena (SLM): implement on a MonoBehaviour and assign it to
    // BedRoomScene.dialogueSource. deliver must run on the main thread; null or empty stays silent.
    public interface IBedDialogueSource
    {
        void RequestLine(BedTalkContext context, Action<string> deliver);
    }

    // Temporary rule-based lines (prototype, not the generative model).
    public sealed class BedRuleLines : IBedDialogueSource
    {
        private readonly Random random = new();
        private string last;

        public void RequestLine(BedTalkContext c, Action<string> deliver) => deliver?.Invoke(Pick(Pool(c)));

        private static string[] Pool(BedTalkContext c)
        {
            switch (c.talkEvent)
            {
                case BedTalkEvent.Enter:
                    if (c.Hurt) return new[]
                    {
                        "다치신 채로 쉬시면 안 돼요··· 치료 부스도 꼭 이용하세요.",
                        "이든 씨, 많이 다치셨어요. 쉬기 전에 치료부터 받으세요.",
                    };
                    if (!c.hasSave) return new[]
                    {
                        "아직 남겨 둔 기록이 없어요. 오늘 일, 한 번 저장해 둘까요?",
                        "침대 정리해 뒀어요. 쉬기 전에 기록부터 남겨 두세요.",
                    };
                    if (c.minutesSinceSave > 20) return new[]
                    {
                        "마지막 기록이 꽤 오래됐어요. 지금 저장해 두실래요?",
                        "푹 쉬다 가세요, 이든 씨. 저장도 잊지 마시고요.",
                    };
                    return new[]
                    {
                        "푹 쉬다 가세요, 이든 씨. 오늘도 고생 많으셨어요.",
                        "여기라면 안심하고 쉬셔도 돼요.",
                        "이불 새로 깔아 뒀어요. 편하게 쉬세요.",
                    };
                case BedTalkEvent.Saved:
                    return new[]
                    {
                        "기록해 뒀어요! 이제 안심하셔도 돼요.",
                        "저장했어요. 오늘 일도 잘 남겨 뒀어요.",
                        "꼼꼼하시네요. 기록 완료예요!",
                    };
                case BedTalkEvent.SaveFailed:
                    return new[] { "어라··· 기록이 제대로 안 남았어요. 이전 기록은 그대로 있으니 한 번만 더 해 볼까요?" };
                case BedTalkEvent.UpgradeDone:
                    return new[]
                    {
                        "침대가 훨씬 푹신해졌어요! 이제 더 튼튼해지실 거예요.",
                        "잠자리가 좋아야 힘이 나죠. 최대 체력이 늘었어요!",
                        "새 매트리스 어때요? 푹 자고 나면 몸이 다를 거예요.",
                    };
                case BedTalkEvent.UpgradeShort:
                    return new[] { "재료가 조금 모자라요. 다음에 꼭 바꿔 드릴게요.", "이걸로는 부족해요. 조금 더 모아 오세요." };
                case BedTalkEvent.UpgradeMaxed:
                    return new[] { "이보다 좋은 침대는 저도 구할 수가 없어요." };
                default:
                    return Array.Empty<string>();
            }
        }

        // Avoids saying the same line twice in a row.
        private string Pick(string[] pool)
        {
            if (pool == null || pool.Length == 0) return null;
            int index = random.Next(pool.Length);
            if (pool.Length > 1 && pool[index] == last) index = (index + 1) % pool.Length;
            return last = pool[index];
        }
    }
}
