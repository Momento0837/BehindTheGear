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
        public GameObject visualPrefab;
        public GameObject[] tierVisualPrefabs = new GameObject[4];
        public Vector3 visualPosition;
        public Vector3 visualEuler;
        public Vector3 visualScale = Vector3.one;
        [TextArea] public string currentEffectOverride;
        [TextArea] public string nextEffectOverride;
        public bool prototypeLocked;
        [TextArea(3, 10)] public string exhibitionDescription;
        [Header("Exhibition room placement")]
        public Vector3 roomPosition;
        public Vector2 roomSize = new(7f, 6.8f);
        public Vector3 iconOffset = new(0f, 3.4f, .2f);
    }

    [Serializable]
    public sealed class HideOutFacilityCameraFocus
    {
        public Facility facility;
        public string anchorName = "FacilityCameraAnchor";
        public Vector3 cameraPosition;
        [Min(.1f)] public float orthographicSize = 5f;
    }

    [Serializable]
    public sealed class HideOutTopViewPresentation
    {
        public Vector3 overviewCameraPosition = new(12f, 15f, -16f);
        public Vector3 cameraEuler = new(37f, -37f, 0f);
        [Min(.1f)] public float overviewOrthographicSize = 13.8f;
        [Range(.1f, 2f)] public float cameraMoveDuration = .7f;
        [Header("Free explore input")]
        [Range(4f, 20f)] public float dragThresholdPixels = 10f;
        [Range(.1f, 3f)] public float dragSensitivity = 1f;
        [Tooltip("Left-button drag multiplier; middle-button speed stays unchanged.")]
        [Range(.1f, 1f)] public float leftDragMultiplier = .4f;
        [Tooltip("Small pan allowance on axes where the overview is larger than the map. Zero locks that axis.")]
        [Min(0f)] public float overviewPanAllowance = 1.5f;
        [Range(.02f, .5f)] public float movementSmoothTime = .14f;
        [Min(.1f)] public float maximumMoveSpeed = 22f;
        public bool useInertia = true;
        [Range(1f, 30f)] public float inertiaDamping = 12f;
        [Min(.1f)] public float zoomStep = .9f;
        [Range(.02f, .5f)] public float zoomSmoothTime = .14f;
        [Min(.1f)] public float minimumOrthographicSize = 3.8f;
        [Min(.1f)] public float maximumOrthographicSize = 13.8f;
        [Min(0f)] public float boundsPadding = .2f;
        public Facility[] shortcutOrder = { Facility.Storage, Facility.Clinic, Facility.Dummy, Facility.Bed, Facility.Potions, Facility.Gym, Facility.Dining, Facility.Workshop };
        public HideOutFacilityCameraFocus[] facilities =
        {
            new() { facility = Facility.Clinic, anchorName = "TreatmentBoothCameraAnchor", cameraPosition = new Vector3(6.1f, 15f, -11.2f), orthographicSize = 4.8f },
            new() { facility = Facility.Storage, anchorName = "WarehouseCameraAnchor", cameraPosition = new Vector3(13.2f, 15f, -15.1f), orthographicSize = 5.2f },
            new() { facility = Facility.Dummy, anchorName = "TrainingDummyCameraAnchor", cameraPosition = new Vector3(20.2f, 15f, -11.1f), orthographicSize = 4.8f },
            new() { facility = Facility.Bed, anchorName = "BedCameraAnchor", cameraPosition = new Vector3(7f, 15f, -18f), orthographicSize = 4.8f },
            new() { facility = Facility.Potions, anchorName = "PotionsCameraAnchor", cameraPosition = new Vector3(13f, 15f, -19f), orthographicSize = 4.8f },
            new() { facility = Facility.Gym, anchorName = "GymCameraAnchor", cameraPosition = new Vector3(20f, 15f, -18f), orthographicSize = 4.8f },
            new() { facility = Facility.Dining, anchorName = "DiningCameraAnchor", cameraPosition = new Vector3(-1f, 15f, -15f), orthographicSize = 5.6f },
            new() { facility = Facility.Workshop, anchorName = "WorkshopCameraAnchor", cameraPosition = new Vector3(29f, 15f, -15f), orthographicSize = 5.6f }
        };

        public HideOutFacilityCameraFocus Find(Facility facility)
        {
            foreach (var focus in facilities ?? Array.Empty<HideOutFacilityCameraFocus>())
                if (focus != null && focus.facility == facility) return focus;
            return null;
        }
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
        [Header("Top-view camera presentation (temporary)")]
        public HideOutTopViewPresentation topView = new();

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

            if (topView == null || topView.overviewOrthographicSize <= 0f || topView.cameraMoveDuration <= 0f
                || topView.dragThresholdPixels < 0f || topView.movementSmoothTime <= 0f || topView.maximumMoveSpeed <= 0f
                || topView.zoomSmoothTime <= 0f || topView.minimumOrthographicSize <= 0f
                || topView.maximumOrthographicSize < topView.minimumOrthographicSize)
                throw new InvalidOperationException("HideOut isometric presentation contains invalid camera values.");

            var facilities = new System.Collections.Generic.HashSet<Facility>();
            foreach (var focus in topView.facilities ?? Array.Empty<HideOutFacilityCameraFocus>())
                if (focus == null || string.IsNullOrWhiteSpace(focus.anchorName) || focus.orthographicSize <= 0f
                    || !facilities.Add(focus.facility))
                    throw new InvalidOperationException("HideOut isometric presentation contains missing or duplicate facility focus values.");
        }
    }
}
