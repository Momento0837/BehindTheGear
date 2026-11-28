using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    /// <summary>Editable canvas references. Layout and artwork are authored in Base_Canvas.prefab.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
    public sealed class HideOutCanvasView : MonoBehaviour
    {
        [Serializable]
        public sealed class FacilityView
        {
            public Facility facility;
            public RectTransform worldRoot;
            public Button button;
            public Image ring;
            public TMP_Text nameLabel;
            public TMP_Text levelLabel;
            public Button shortcut;
            public TMP_Text shortcutLabel;
            [NonSerialized] public Vector2 normalRingSize;
            [NonSerialized] public Color normalRingColor, normalShortcutColor, normalOutlineColor;
        }

        [Serializable]
        public sealed class SlotView
        {
            public Button button;
            public Image background, icon;
            public TMP_Text nameText, amountText;
        }

        public enum Action
        {
            Exit, MainMenu, Overview, CloseStorage, InventoryPrevious, InventoryNext, StoragePrevious, StorageNext, Deposit, Withdraw, SortStorage, DepositExisting, UpgradeStorage, TestDummy, UpgradeDummy, CloseDummy, CloseUpgrade, UseFacility, Upgrade, UnlockTestFacilities, ResetTest, GrantTestResources, RecruitTest
        }
        [Serializable] public sealed class ButtonAction { public Button button; public Action action; }

        [Header("HUD / panels / live values (keep references when restyling)")]
        public GameObject baseView;
        public GameObject upgradeOverlay;
        public GameObject storagePanel;
        public GameObject tooltip;
        public TMP_Text tooltipText;
        public TMP_Text feedbackText;
        public TMP_Text questSummaryText;
        public TMP_Text roomNameText;
        public RectTransform upgradePanel;
        public TMP_Text upgradeBackLabel;
        public TMP_Text upgradeTitle;
        public TMP_Text upgradeLevel;
        public TMP_Text progressText;
        public TMP_Text timerText;
        public TMP_Text currentEffectText;
        public TMP_Text nextEffectText;
        public TMP_Text coinCostText;
        public TMP_Text actionCostText;
        public TMP_Text materialCostText;
        public Image progressFill;
        public Image facilityImage;
        public Button upgradeButton;
        public Button useFacilityButton;
        public GameObject exhibitionPanel;
        public TMP_Text exhibitionTitle;
        public TMP_Text exhibitionBody;
        public GameObject dummyPanel;
        public TMP_Text dummyLevelText;
        public TMP_Text dummyWeaponText;
        public TMP_Text dummyDamageText;
        public TMP_Text dummyBonusText;
        public TMP_Text dummyLastDamageText;
        public TMP_Text dummyCostText;
        public Button dummyTestButton;
        public Button dummyUpgradeButton;
        public TMP_Text inventoryCapacityText;
        public TMP_Text storageCapacityText;
        public TMP_Text inventoryPageText;
        public TMP_Text storagePageText;
        public TMP_Text storageUpgradeCostText;
        public Button depositButton;
        public Button withdrawButton;
        public Button inventoryPreviousButton;
        public Button inventoryNextButton;
        public Button storagePreviousButton;
        public Button storageNextButton;
        public Button storageUpgradeButton;
        public TMP_Text[] resourceValues = new TMP_Text[4];
        public Image[] resourceBars = new Image[2];
        public RectTransform[] resourceTracks = new RectTransform[2];
        public RectTransform progressTrack;

        [Header("Facility and inventory connections")]
        public FacilityView[] facilities = Array.Empty<FacilityView>();
        public SlotView[] inventorySlots = Array.Empty<SlotView>();
        public SlotView[] storageSlots = Array.Empty<SlotView>();
        public ButtonAction[] actions = Array.Empty<ButtonAction>();
        public ScrollRect shortcutScroll;
        public Button previousFacilities, nextFacilities;

        [Header("Development controls")]
        public GameObject developmentControls;
        public Button unlockTestFacilities;
        public TMP_Text testFacilityToggleLabel;
    }
}

