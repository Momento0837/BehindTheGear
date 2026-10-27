using System;
using TMPro;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    [Serializable]
    public sealed class HideOutFacilityPresentation
    {
        public string facilityId = "facility";
        public Facility facility;
        public string displayName = "시설";
        public Sprite icon;
        public Sprite facilityImage;
        [TextArea] public string currentEffectOverride;
        [TextArea] public string nextEffectOverride;
        public bool prototypeLocked;
    }

    [CreateAssetMenu(menuName = "Behind The Gear/HideOut Settings")]
    public sealed class HideOutSettings : ScriptableObject, IHideOutItems
    {
        public HideOutRules rules = new();
        public TMP_FontAsset font;
        public InventoryItemDefinition[] items = Array.Empty<InventoryItemDefinition>();
        public string scenePath = "Assets/Scenes/HideOutSystemScene/HideOutSystemScene.unity";
        public string testWeaponId = "hideout_test_weapon";
        public Sprite[] appearanceSprites = new Sprite[4];
        [Header("Prototype UI presentation (temporary)")]
        public HideOutFacilityPresentation[] prototypeFacilities = Array.Empty<HideOutFacilityPresentation>();

        public HideOutFacilityPresentation FindPresentation(Facility facility)
        {
            foreach (var presentation in prototypeFacilities ?? Array.Empty<HideOutFacilityPresentation>())
                if (presentation != null && presentation.facility == facility) return presentation;
            return null;
        }

        public InventoryItemDefinition Find(string id)
        {
            foreach (var item in items) if (item != null && item.ItemId == id) return item;
            return null;
        }
        public int MaxStack(string id) => Find(id) != null ? Find(id).MaxStack : 0;
        public string Name(string id) => Find(id) != null ? Find(id).DisplayName : id;
        public void ValidateCatalog()
        {
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var item in items)
                if (item == null || string.IsNullOrEmpty(item.ItemId) || !ids.Add(item.ItemId))
                    throw new InvalidOperationException("HideOut item catalog contains missing or duplicate IDs.");

            ids.Clear();
            foreach (var presentation in prototypeFacilities ?? Array.Empty<HideOutFacilityPresentation>())
                if (presentation == null || string.IsNullOrWhiteSpace(presentation.facilityId) || !ids.Add(presentation.facilityId))
                    throw new InvalidOperationException("HideOut prototype facility presentation contains missing or duplicate IDs.");
        }
    }
}
