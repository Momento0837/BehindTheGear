using System;

namespace BehindTheGear.HideOut
{
    // What just happened in the treatment screen. The line source answers each event with one short Selena line.
    public enum TreatmentTalkEvent
    {
        Enter,           // Arrived from the base.
        HealDone,        // HideOutPlayerBridge.Heal succeeded.
        HealOnCooldown,  // Heal pressed while clinicReadyAt is still ahead.
        HealNotNeeded,   // Heal pressed at full HP.
        HealUnavailable, // Heal refused for another reason (e.g. HP 0).
        UpgradeDone,     // Recovery amount (clinic level) went up through the existing upgrade transaction.
        UpgradeShort,    // Not enough coins / action points / materials.
        UpgradeMaxed,    // No further upgrade step is defined.
        CooldownReady,   // The cooldown reached zero while the player was in the room.
        SpeedUpgradeDone,  // Time reduction went up: the next heal is ready sooner.
        HealDowngraded,    // "-" on recovery amount: one level down, part of the cost returned.
        SpeedDowngraded,   // "-" on time reduction: the wait gets longer again.
        DowngradeBlocked,  // "-" refused (already level 0, or no room for the returned materials).
    }

    // HP bands Selena can see directly (the player sits in front of her). Lines describe them qualitatively;
    // exact numbers stay in the system line, not in her speech.
    public enum TreatmentHealthBand { Unknown, Critical, Wounded, Scratched, Full }

    // Only state that is observable in the treatment room. An SLM source receives the same fields.
    public readonly struct TreatmentTalkContext
    {
        public readonly TreatmentTalkEvent talkEvent;
        public readonly int currentHealth, maxHealth, clinicLevel, healAmount;
        public readonly double cooldownRemaining;
        public readonly string systemMessage;

        public TreatmentTalkContext(TreatmentTalkEvent talkEvent, int currentHealth, int maxHealth, int clinicLevel,
            int healAmount, double cooldownRemaining, string systemMessage)
        {
            this.talkEvent = talkEvent; this.currentHealth = currentHealth; this.maxHealth = maxHealth;
            this.clinicLevel = clinicLevel; this.healAmount = healAmount;
            this.cooldownRemaining = Math.Max(0, cooldownRemaining); this.systemMessage = systemMessage ?? string.Empty;
        }

        public bool OnCooldown => cooldownRemaining > 0;
        public TreatmentHealthBand Band
        {
            get
            {
                if (maxHealth <= 0) return TreatmentHealthBand.Unknown;
                if (currentHealth >= maxHealth) return TreatmentHealthBand.Full;
                float ratio = currentHealth / (float)maxHealth;
                return ratio <= .25f ? TreatmentHealthBand.Critical : ratio <= .6f ? TreatmentHealthBand.Wounded : TreatmentHealthBand.Scratched;
            }
        }
    }

    // Replace point for the generative Selena (SLM). deliver may be called later (async generation);
    // pass null or an empty string to stay silent. Implement on a MonoBehaviour and assign it to
    // TreatmentRoomScene.dialogueSource; when nothing is assigned the rule-based lines below are used.
    public interface ITreatmentDialogueSource
    {
        void RequestLine(TreatmentTalkContext context, Action<string> deliver);
    }

    // Temporary rule-based fallback (prototype, not the generative model).
    public sealed class TreatmentRuleLines : ITreatmentDialogueSource
    {
        private readonly Random random = new();
        private string last;

        public void RequestLine(TreatmentTalkContext c, Action<string> deliver) => deliver?.Invoke(Pick(Pool(c)));

        private static string[] Pool(TreatmentTalkContext c)
        {
            var band = c.Band;
            switch (c.talkEvent)
            {
                case TreatmentTalkEvent.Enter:
                    if (band == TreatmentHealthBand.Full) return new[]
                    {
                        "다친 데는 없어 보이는데··· 그냥 들른 거야?",
                        "멀쩡하네. 다행이다.",
                        "오늘은 치료할 게 없겠다. 쉬었다 가.",
                    };
                    if (c.OnCooldown) return new[]
                    {
                        "아직 약이 준비가 안 됐어. 조금만 기다려 줘.",
                        "상처는 보이는데··· 지금 쓸 약이 없어. 조금만 버텨.",
                    };
                    return band switch
                    {
                        TreatmentHealthBand.Critical => new[]
                        {
                            "그 몸으로 여기까지 걸어온 거야? 얼른 앉아.",
                            "피를 너무 많이 흘렸어. 바로 치료하자.",
                        },
                        TreatmentHealthBand.Wounded => new[]
                        {
                            "또 다쳐서 왔네. 앉아, 봐 줄게.",
                            "상처가 꽤 깊어 보여. 무리하지 마.",
                        },
                        _ => new[]
                        {
                            "조금 긁힌 정도네. 그래도 치료하고 가.",
                            "큰 상처는 아니지만, 그냥 두면 덧나.",
                        },
                    };
                case TreatmentTalkEvent.HealDone:
                    return band == TreatmentHealthBand.Full
                        ? new[] { "됐다. 이제 좀 낫지?", "다 됐어. 이번엔 조심해서 다녀와." }
                        : new[] { "일단 급한 데는 막았어. 다음 치료까지는 시간이 좀 필요해.", "완전히 낫진 않았어. 약이 준비되면 다시 와." };
                case TreatmentTalkEvent.HealOnCooldown:
                    return new[] { "약이 아직 준비 중이야. 시간 되면 다시 해 줄게.", "조금만 기다려. 지금 무리하게 쓰면 안 들어." };
                case TreatmentTalkEvent.HealNotNeeded:
                    return new[] { "치료는 지금 할 필요가 없어 보여요. 불편하시면 언제든 오세요.", "지금은 멀쩡해요. 다치시면 그때 다시 와도 좋아요." };
                case TreatmentTalkEvent.HealUnavailable:
                    return new[] { "지금 상태로는 제가 해 줄 수 있는 게 없어요··· 죄송해요···" };
                case TreatmentTalkEvent.UpgradeDone:
                    return new[] { "치료 부스를 강화했어요. 치료가 더 잘될 거예요.", "시설이 업그레이드되었어요. 치료가 더 잘될 거예요." };
                case TreatmentTalkEvent.UpgradeShort:
                    return new[] { "재료가 모자라요. 필요한 걸 챙겨 오시면 강화할 수 있어요.", "이걸로는 부족해요. 조금 더 모아 오세요." };
                case TreatmentTalkEvent.UpgradeMaxed:
                    return new[] { "더 이상 강화할 수 없어요." };
                case TreatmentTalkEvent.SpeedUpgradeDone:
                    return new[]
                    {
                        "멋진 선택이에요! 앞으로 더 빨리 치료받을 수 있어요!",
                        "약 준비가 훨씬 빨라졌어요. 이제 덜 기다리셔도 돼요.",
                        "기구를 손봤더니 손이 한결 빨라졌어요. 급할 때 바로 봐 드릴게요.",
                        "치료 대기 시간이 줄었어요. 시설이 많이 좋아졌죠?",
                    };
                case TreatmentTalkEvent.HealDowngraded:
                    return new[]
                    {
                        "이런··· 회복량이 줄어들었어요. 하지만 이든 씨의 선택을 전 믿어요.",
                        "치료 효과가 조금 약해졌어요··· 그래도 제가 최선을 다할게요.",
                        "이든 씨, 강화 재료가 많이 급하신가 봐요?",
                    };
                case TreatmentTalkEvent.SpeedDowngraded:
                    return new[]
                    {
                        "치료 시간이 늘어나고 말았어요··· 괜찮을 거예요.",
                        "모처럼 빨라졌는데··· 이제 조금 더 기다리셔야 해요.",
                        "이든 씨, 강화 재료가 많이 급하신가 봐요?",
                    };
                case TreatmentTalkEvent.DowngradeBlocked:
                    return new[] { "더 낮출 수 있는 게 없어요. 지금 그대로가 좋아요." };
                case TreatmentTalkEvent.CooldownReady:
                    return band == TreatmentHealthBand.Full
                        ? new[] { "약 준비됐어. 다칠 일 없으면 좋겠지만." }
                        : new[] { "준비됐어. 이제 치료할 수 있어.", "기다렸지? 이리 와, 치료하자." };
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
