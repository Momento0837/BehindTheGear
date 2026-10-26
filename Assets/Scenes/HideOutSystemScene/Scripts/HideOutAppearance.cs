using System;
using UnityEngine;
using UnityEngine.Events;

namespace BehindTheGear.HideOut
{
    public sealed class HideOutAppearance : MonoBehaviour
    {
        public UnityEvent onUpgrade = new();
        public event Action<int> TierChanged;
        private int tier = -1;
        public static readonly Color[] TierColors = { new(.35f, .43f, .49f), new(.27f, .63f, .54f), new(.30f, .54f, .84f), new(.88f, .65f, .25f) };
        public void Refresh(int level, SpriteRenderer renderer, Sprite[] sprites, bool playUpgrade)
        {
            int next = HideOutModel.Tier(level);
            if (next != tier)
            {
                tier = next; renderer.color = TierColors[tier];
                if (sprites != null && tier < sprites.Length && sprites[tier] != null) renderer.sprite = sprites[tier];
                TierChanged?.Invoke(tier);
            }
            if (playUpgrade) onUpgrade.Invoke();
        }
    }
}
