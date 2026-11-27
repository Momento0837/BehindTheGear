using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    public sealed partial class HideOutScene
    {
        private HideOutCanvasView boundCanvas;
        private readonly Dictionary<GameObject, bool> authoredFacilityArtwork = new();

        private void RefreshFacilityArtwork(Button button, bool locked)
        {
            // Keep the circular button, selection ring and labels; hide only its artwork.
            foreach (Transform child in button.transform)
            {
                if (child.GetComponent<TMP_Text>() != null) continue;
                if (child.name == "Temporary Facility Lock") { child.gameObject.SetActive(false); continue; }
                var artwork = child.gameObject;
                if (!authoredFacilityArtwork.TryGetValue(artwork, out bool authoredActive))
                    authoredFacilityArtwork.Add(artwork, authoredActive = artwork.activeSelf);
                artwork.SetActive(!locked && authoredActive);
            }
        }

        private void BindCanvas(HideOutCanvasView view)
        {
            boundCanvas = view;
            ui = view.transform;
            canvasRect = view.GetComponent<RectTransform>();
            baseView = view.baseView;
            upgradeOverlay = view.upgradeOverlay;
            storagePanel = view.storagePanel;
            tooltip = view.tooltip;
            tooltipText = view.tooltipText;
            feedbackText = view.feedbackText;
            questSummaryText = view.questSummaryText;
            roomNameText = view.roomNameText;
            upgradePanel = view.upgradePanel;
            upgradeBackLabel = view.upgradeBackLabel;
            upgradeTitle = view.upgradeTitle;
            upgradeLevel = view.upgradeLevel;
            progressText = view.progressText;
            timerText = view.timerText;
            currentEffectText = view.currentEffectText;
            nextEffectText = view.nextEffectText;
            coinCostText = view.coinCostText;
            actionCostText = view.actionCostText;
            materialCostText = view.materialCostText;
            progressFill = view.progressFill;
            facilityImage = view.facilityImage;
            upgradeButton = view.upgradeButton;
            useFacilityButton = view.useFacilityButton;
            exhibitionPanel = view.exhibitionPanel;
            exhibitionTitle = view.exhibitionTitle;
            exhibitionBody = view.exhibitionBody;
            dummyPanel = view.dummyPanel;
            dummyLevelText = view.dummyLevelText;
            dummyWeaponText = view.dummyWeaponText;
            dummyDamageText = view.dummyDamageText;
            dummyBonusText = view.dummyBonusText;
            dummyLastDamageText = view.dummyLastDamageText;
            dummyCostText = view.dummyCostText;
            dummyTestButton = view.dummyTestButton;
            dummyUpgradeButton = view.dummyUpgradeButton;
            inventoryCapacityText = view.inventoryCapacityText;
            storageCapacityText = view.storageCapacityText;
            inventoryPageText = view.inventoryPageText;
            storagePageText = view.storagePageText;
            storageUpgradeCostText = view.storageUpgradeCostText;
            depositButton = view.depositButton;
            withdrawButton = view.withdrawButton;
            inventoryPreviousButton = view.inventoryPreviousButton;
            inventoryNextButton = view.inventoryNextButton;
            storagePreviousButton = view.storagePreviousButton;
            storageNextButton = view.storageNextButton;
            storageUpgradeButton = view.storageUpgradeButton;
            Array.Copy(view.resourceValues, resourceValues, resourceValues.Length);
            Array.Copy(view.resourceBars, resourceBars, resourceBars.Length);
            foreach (var facility in view.facilities)
            {
                Facility id = facility.facility;
                facilityOverlayRoots[id] = facility.worldRoot;
                facilityButtons[id] = facility.button;
                hotspotRings[id] = facility.ring;
                facilityLevelLabels[id] = facility.levelLabel;
                facilityNameLabels[id] = facility.nameLabel;
                authoredFacilityNames[id] = facility.nameLabel != null ? facility.nameLabel.text : DisplayName(id);
                shortcutButtons[id] = facility.shortcut;
                shortcutLabels[id] = facility.shortcutLabel;
                facility.normalRingSize = facility.ring.rectTransform.sizeDelta;
                facility.normalRingColor = facility.ring.color;
                facility.normalShortcutColor = facility.shortcut.GetComponent<Image>().color;
                facility.normalOutlineColor = facility.button.GetComponent<Outline>()?.effectColor ?? Color.white;
                facility.button.onClick.AddListener(() => SelectFacility(id));
                facility.shortcut.onClick.AddListener(() => SelectFacility(id));
                AddFacilityHover(facility.button, id);
                AddFacilityHover(facility.shortcut, id);
            }
            BindSlots(view.inventorySlots, inventorySlotViews, SelectInventorySlot);
            BindSlots(view.storageSlots, storageSlotViews, SelectStorageSlot);
            foreach (var entry in view.actions)
            {
                if (entry.button == null) continue;
                var action = ResolveCanvasAction(entry.action);
                if (action != null) entry.button.onClick.AddListener(action);
            }
            view.previousFacilities.onClick.AddListener(() => ScrollPrefabShortcuts(-1));
            view.nextFacilities.onClick.AddListener(() => ScrollPrefabShortcuts(1));
            view.shortcutScroll.onValueChanged.AddListener(_ => RefreshPrefabNavigation());
            RefreshPrefabNavigation();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            testFacilityToggleLabel = view.testFacilityToggleLabel;
            if (view.developmentControls != null) view.developmentControls.SetActive(player.Sandbox);
#else
            if (view.developmentControls != null) view.developmentControls.SetActive(false);
            if (view.unlockTestFacilities != null) view.unlockTestFacilities.gameObject.SetActive(false);
#endif
            storagePanel.SetActive(false);
            dummyPanel.SetActive(false);
            upgradeOverlay.SetActive(false);
            exhibitionPanel.SetActive(false);
            tooltip.SetActive(false);
        }

        private static void BindSlots(HideOutCanvasView.SlotView[] source, List<StorageSlotView> destination, Action<int> select)
        {
            destination.Clear();
            for (int i = 0; i < source.Length; i++)
            {
                int row = i;
                var slot = source[i];
                destination.Add(new StorageSlotView { background = slot.background, icon = slot.icon,
                    name = slot.nameText, amount = slot.amountText });
                slot.button.onClick.AddListener(() => select(row));
            }
        }

        private UnityAction ResolveCanvasAction(HideOutCanvasView.Action action)
        {
            switch (action)
            {
                case HideOutCanvasView.Action.Exit: return ExitHideOut;
                case HideOutCanvasView.Action.MainMenu: return SaveGameSession.ReturnToMenu;
                case HideOutCanvasView.Action.Overview: return ReturnToOverview;
                case HideOutCanvasView.Action.CloseStorage: return CloseStoragePanel;
                case HideOutCanvasView.Action.InventoryPrevious: return () => ChangeInventoryPage(-1);
                case HideOutCanvasView.Action.InventoryNext: return () => ChangeInventoryPage(1);
                case HideOutCanvasView.Action.StoragePrevious: return () => ChangeStoragePage(-1);
                case HideOutCanvasView.Action.StorageNext: return () => ChangeStoragePage(1);
                case HideOutCanvasView.Action.Deposit: return DepositSelected;
                case HideOutCanvasView.Action.Withdraw: return WithdrawSelected;
                case HideOutCanvasView.Action.SortStorage: return SortStorage;
                case HideOutCanvasView.Action.DepositExisting: return DepositExistingTypes;
                case HideOutCanvasView.Action.UpgradeStorage: return UpgradeStorage;
                case HideOutCanvasView.Action.TestDummy: return StartOrAttackDummyTest;
                case HideOutCanvasView.Action.UpgradeDummy: return UpgradeDummy;
                case HideOutCanvasView.Action.CloseDummy: return CloseDummyPanel;
                case HideOutCanvasView.Action.CloseUpgrade: return CloseUpgrade;
                case HideOutCanvasView.Action.UseFacility: return UseSelectedFacility;
                case HideOutCanvasView.Action.Upgrade: return UpgradeSelected;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                case HideOutCanvasView.Action.UnlockTestFacilities: return ToggleFacilityLocksForTesting;
                case HideOutCanvasView.Action.ResetTest: return player.ResetTestProgress;
                case HideOutCanvasView.Action.GrantTestResources: return player.GrantTestResources;
                case HideOutCanvasView.Action.RecruitTest: return player.TestRecruit;
#endif
                default: return null;
            }
        }

        private void ScrollPrefabShortcuts(int direction)
        {
            var scroll = boundCanvas.shortcutScroll;
            float overflow = scroll.content.rect.width - scroll.viewport.rect.width;
            if (overflow <= 0f) return;
            var cards = boundCanvas.facilities;
            float stride = cards.Length > 1
                ? Mathf.Abs(cards[1].shortcut.GetComponent<RectTransform>().anchoredPosition.x
                    - cards[0].shortcut.GetComponent<RectTransform>().anchoredPosition.x)
                : scroll.viewport.rect.width;
            scroll.StopMovement();
            scroll.horizontalNormalizedPosition = Mathf.Clamp01(scroll.horizontalNormalizedPosition + direction * stride / overflow);
            RefreshPrefabNavigation();
        }

        private void RefreshPrefabNavigation()
        {
            var scroll = boundCanvas.shortcutScroll;
            float overflow = scroll.content.rect.width - scroll.viewport.rect.width;
            float offset = scroll.horizontalNormalizedPosition * overflow;
            boundCanvas.previousFacilities.interactable = overflow > .5f && offset > .5f;
            boundCanvas.nextFacilities.interactable = overflow > .5f && offset < overflow - .5f;
        }

        private HideOutCanvasView.FacilityView PrefabFacility(Facility facility)
            => boundCanvas == null ? null : Array.Find(boundCanvas.facilities, item => item.facility == facility);

#if UNITY_EDITOR
        // One-time editor export of the existing design. Runtime binding uses typed serialized references,
        // so object names and hierarchy layout can be edited without breaking the buttons.
        public HideOutCanvasView CaptureCanvasForPrefab()
        {
            if (!Application.isPlaying || ui == null || boundCanvas != null || !player.Sandbox)
                throw new InvalidOperationException("Export from the directly-played generated base UI.");
            var view = ui.gameObject.AddComponent<HideOutCanvasView>();
            view.baseView = baseView;
            view.upgradeOverlay = upgradeOverlay;
            view.storagePanel = storagePanel;
            view.tooltip = tooltip;
            view.tooltipText = tooltipText;
            view.feedbackText = feedbackText;
            view.questSummaryText = questSummaryText;
            view.roomNameText = roomNameText;
            view.upgradePanel = upgradePanel;
            view.upgradeBackLabel = upgradeBackLabel;
            view.upgradeTitle = upgradeTitle;
            view.upgradeLevel = upgradeLevel;
            view.progressText = progressText;
            view.timerText = timerText;
            view.currentEffectText = currentEffectText;
            view.nextEffectText = nextEffectText;
            view.coinCostText = coinCostText;
            view.actionCostText = actionCostText;
            view.materialCostText = materialCostText;
            view.progressFill = progressFill;
            view.facilityImage = facilityImage;
            view.upgradeButton = upgradeButton;
            view.useFacilityButton = useFacilityButton;
            view.exhibitionPanel = exhibitionPanel;
            view.exhibitionTitle = exhibitionTitle;
            view.exhibitionBody = exhibitionBody;
            view.dummyPanel = dummyPanel;
            view.dummyLevelText = dummyLevelText;
            view.dummyWeaponText = dummyWeaponText;
            view.dummyDamageText = dummyDamageText;
            view.dummyBonusText = dummyBonusText;
            view.dummyLastDamageText = dummyLastDamageText;
            view.dummyCostText = dummyCostText;
            view.dummyTestButton = dummyTestButton;
            view.dummyUpgradeButton = dummyUpgradeButton;
            view.inventoryCapacityText = inventoryCapacityText;
            view.storageCapacityText = storageCapacityText;
            view.inventoryPageText = inventoryPageText;
            view.storagePageText = storagePageText;
            view.storageUpgradeCostText = storageUpgradeCostText;
            view.depositButton = depositButton;
            view.withdrawButton = withdrawButton;
            view.inventoryPreviousButton = inventoryPreviousButton;
            view.inventoryNextButton = inventoryNextButton;
            view.storagePreviousButton = storagePreviousButton;
            view.storageNextButton = storageNextButton;
            view.storageUpgradeButton = storageUpgradeButton;
            view.resourceValues = resourceValues.ToArray();
            view.resourceBars = resourceBars.ToArray();
            view.resourceTracks = resourceBars.Select(bar => bar.transform.parent.Find("Gauge Track").GetComponent<RectTransform>()).ToArray();
            view.progressTrack = progressFill.transform.parent.Find("Progress Track").GetComponent<RectTransform>();
            view.facilities = settings.topView.shortcutOrder.Select(f => new HideOutCanvasView.FacilityView {
                facility = f, worldRoot = facilityOverlayRoots[f], button = facilityButtons[f],
                ring = hotspotRings[f], nameLabel = facilityNameLabels[f], levelLabel = facilityLevelLabels[f],
                shortcut = shortcutButtons[f], shortcutLabel = shortcutLabels[f]
            }).ToArray();
            HideOutCanvasView.SlotView CopySlot(StorageSlotView slot) => new() {
                button = slot.background.GetComponent<Button>(), background = slot.background, icon = slot.icon,
                nameText = slot.name, amountText = slot.amount
            };
            view.inventorySlots = inventorySlotViews.Select(CopySlot).ToArray();
            view.storageSlots = storageSlotViews.Select(CopySlot).ToArray();
            var roleByName = new Dictionary<string, HideOutCanvasView.Action>
            {
                ["Exit Button"] = HideOutCanvasView.Action.Exit,
                ["Main Menu Button"] = HideOutCanvasView.Action.MainMenu,
                ["Reset HideOut Camera"] = HideOutCanvasView.Action.Overview,
                ["Exhibition Overview"] = HideOutCanvasView.Action.Overview,
                ["Storage Close"] = HideOutCanvasView.Action.CloseStorage,
                ["Inventory Previous"] = HideOutCanvasView.Action.InventoryPrevious,
                ["Inventory Next"] = HideOutCanvasView.Action.InventoryNext,
                ["Storage Previous"] = HideOutCanvasView.Action.StoragePrevious,
                ["Storage Next"] = HideOutCanvasView.Action.StorageNext,
                ["Deposit Selected"] = HideOutCanvasView.Action.Deposit,
                ["Withdraw Selected"] = HideOutCanvasView.Action.Withdraw,
                ["Sort Storage"] = HideOutCanvasView.Action.SortStorage,
                ["Deposit Existing Types"] = HideOutCanvasView.Action.DepositExisting,
                ["Upgrade Storage"] = HideOutCanvasView.Action.UpgradeStorage,
                ["Dummy Test Button"] = HideOutCanvasView.Action.TestDummy,
                ["Dummy Upgrade Button"] = HideOutCanvasView.Action.UpgradeDummy,
                ["Dummy Close Button"] = HideOutCanvasView.Action.CloseDummy,
                ["Close Upgrade"] = HideOutCanvasView.Action.CloseUpgrade,
                ["Back Button"] = HideOutCanvasView.Action.CloseUpgrade,
                ["Use Facility"] = HideOutCanvasView.Action.UseFacility,
                ["Upgrade Button"] = HideOutCanvasView.Action.Upgrade,
                ["Unlock All Test Facilities"] = HideOutCanvasView.Action.UnlockTestFacilities,
                ["Reset Test Progress"] = HideOutCanvasView.Action.ResetTest,
                ["Grant Test Resources"] = HideOutCanvasView.Action.GrantTestResources,
                ["TEST Selena"] = HideOutCanvasView.Action.RecruitTest,
            };
            var buttons = ui.GetComponentsInChildren<Button>(true);
            view.actions = buttons.Where(button => roleByName.ContainsKey(button.name))
                .Select(button => new HideOutCanvasView.ButtonAction { button = button, action = roleByName[button.name] }).ToArray();
            view.shortcutScroll = ui.GetComponentInChildren<ScrollRect>(true);
            view.previousFacilities = buttons.Single(b => b.name == "Previous Facilities");
            view.nextFacilities = buttons.Single(b => b.name == "Next Facilities");
            view.unlockTestFacilities = buttons.Single(b => b.name == "Unlock All Test Facilities");
            view.developmentControls = ui.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Development Only Controls").gameObject;
            view.testFacilityToggleLabel = testFacilityToggleLabel;
            return view;
        }
#endif
    }
}

