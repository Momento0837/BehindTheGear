using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    // Runs only on the explicitly placed scene component; never edits scenes in edit mode.
    public sealed class HideOutScene : MonoBehaviour
    {
        public HideOutSettings settings;
        private HideOutPlayerBridge player;
        private HideOutDummy dummy;
        private TMP_Text status, detail, feedback, storageTitle, inventoryTitle;
        private Button use, upgrade;
        private GameObject map, warehouse;
        private Transform ui;
        private Facility selected;
        private readonly TMP_Text[] labels = new TMP_Text[8];
        private readonly Image[] cardImages = new Image[8];
        private readonly HideOutAppearance[] appearances = new HideOutAppearance[8];
        private readonly SpriteRenderer[] renderers = new SpriteRenderer[8];
        private readonly TMP_Text[] inventoryRows = new TMP_Text[8], storageRows = new TMP_Text[8];
        private readonly Button[] depositButtons = new Button[8], withdrawButtons = new Button[8], equipButtons = new Button[8];
        private int inventoryPage, storagePage;
        private float nextRefresh;
        private Rigidbody2D body;
        private bool bodyWasSimulated, released;
        private readonly System.Collections.Generic.List<Behaviour> suspendedControls = new();
        private static readonly Color Background = new(.055f, .075f, .10f);
        private static readonly Color Panel = new(.095f, .13f, .17f);
        private static readonly Color Accent = new(.90f, .68f, .32f);
        private static readonly string[] Titles = { "치료 부스", "침대", "허수아비", "창고", "물약 보관대", "체력단련실", "식탁", "무기제작실" };

        private void Start()
        {
            if (settings == null || settings.font == null) { Debug.LogError("[HideOut] Settings / Korean Font is missing.", this); return; }
            var persistent = FindFirstObjectByType<PersistentPlayer2D>();
            if (persistent != null) player = persistent.GetComponent<HideOutPlayerBridge>();
            if (player == null)
            {
                // No second inventory when a real player is present.
                var existing = FindFirstObjectByType<PlayerInventory>();
                if (existing != null)
                {
                    player = existing.GetComponent<HideOutPlayerBridge>() ?? existing.gameObject.AddComponent<HideOutPlayerBridge>();
                    if (existing.GetComponent<PlayerHealth>() == null) { Debug.LogError("[HideOut] Existing player has no PlayerHealth.", existing); return; }
                }
                else
                {
                    GameObject actor = new("HideOut Sandbox Player"); actor.SetActive(false);
                    actor.AddComponent<PlayerStats>(); actor.AddComponent<PlayerInventory>();
                    var controlLock = actor.AddComponent<PlayerControlLock>();
                    JsonUtility.FromJsonOverwrite("{\"controlsToDisable\":[]}", controlLock);
                    actor.AddComponent<PlayerHealth>(); actor.AddComponent<BoxCollider2D>(); actor.AddComponent<PlayerMeleeAttack>();
                    player = actor.AddComponent<HideOutPlayerBridge>(); actor.SetActive(true);
                }
            }
            try { player.Initialize(settings, !HideOutTravel.CanReturn); }
            catch (Exception e) { Debug.LogError("[HideOut] " + e.Message, this); enabled = false; return; }
            body = player.GetComponent<Rigidbody2D>();
            if (body != null) { bodyWasSimulated = body.simulated; body.simulated = false; }
            foreach (var control in player.GetComponents<Behaviour>())
                if (control.enabled && (control is PlayerInputReader || control is PlayerMovement2D || control is PlayerInteractionController || control is InventoryUI))
                { suspendedControls.Add(control); control.enabled = false; }
            dummy = new GameObject("HideOut Training Dummy").AddComponent<HideOutDummy>();
            dummy.transform.SetParent(transform);
            BuildUI(); player.Changed += Refresh; Refresh();
        }
        public void ReleasePlayer()
        {
            if (released) return;
            released = true;
            if (body != null) body.simulated = bodyWasSimulated;
            foreach (var control in suspendedControls) if (control != null) control.enabled = true;
        }
        private void OnDestroy() { if (player != null) player.Changed -= Refresh; ReleasePlayer(); }
        private void Update() { if (player != null && player.Initialized && Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .25f; Refresh(); } }

        private void BuildUI()
        {
            if (EventSystem.current == null)
            {
                var events = new GameObject("HideOut EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var canvas = new GameObject("HideOut UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ui = canvas.transform; ui.SetParent(transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 200;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            // Fit the complete reference layout, including bottom feedback/developer controls.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var backdrop = Box(ui, "Backdrop", 0, 0, 1600, 900, Background);
            backdrop.rectTransform.anchorMin = Vector2.zero; backdrop.rectTransform.anchorMax = Vector2.one;
            backdrop.rectTransform.offsetMin = Vector2.zero; backdrop.rectTransform.offsetMax = Vector2.zero;
            Button(ui, "나가기", 28, 24, 130, 52, () =>
            {
                if (!HideOutTravel.CanReturn) player.SetMessage("직접 실행 테스트입니다. 플레이 구간의 HideOutEntrance로 진입하면 원래 위치로 돌아갑니다.");
                else player.StartCoroutine(HideOutTravel.Exit(player, gameObject.scene));
            });
            Text(ui, "HIDEOUT  /  부지 내부", 182, 22, 740, 56, 30, Color.white);
            Button(ui, "저장 불러오기", 1360, 24, 210, 52, player.Load);
            status = Text(ui, "", 28, 94, 1544, 58, 21, Accent);
            map = Box(ui, "Facilities", 28, 166, 1544, 596, Color.clear).gameObject;
            Text(map.transform, "시설 지도  /  클릭하여 이용", 10, 0, 840, 38, 22, Color.white);
            for (int i = 0; i < 8; i++)
            {
                int index = i;
                float x = (i % 3) * 274, y = 54 + (i / 3) * 172;
                var card = Button(map.transform, "", x, y, 258, 154, () => Select((Facility)index));
                cardImages[i] = card.GetComponent<Image>();
                var stripe = Box(card.transform, "Tier", 0, 0, 258, 5, Accent);
                labels[i] = Text(card.transform, "", 16, 16, 230, 116, 22, Color.white);
                var sprite = new GameObject(Titles[i] + " Sprite", typeof(SpriteRenderer), typeof(HideOutAppearance));
                sprite.transform.SetParent(transform); sprite.transform.position = new Vector3(-5 + i % 3 * 3, 3 - i / 3 * 3, 0);
                renderers[i] = sprite.GetComponent<SpriteRenderer>(); appearances[i] = sprite.GetComponent<HideOutAppearance>();
                // The same placeholder sprites are shown on the clickable facility map.
                if (settings.appearanceSprites.Length > 0) renderers[i].sprite = settings.appearanceSprites[0];
            }
            Box(map.transform, "Detail Panel", 842, 0, 702, 596, Panel);
            detail = Text(map.transform, "", 868, 24, 650, 422, 23, Color.white);
            use = Button(map.transform, "이용", 868, 474, 310, 66, Use);
            upgrade = Button(map.transform, "강화", 1200, 474, 310, 66, () =>
            {
                int level = player.State.levels[(int)selected]; player.Upgrade(selected);
                if (player.State.levels[(int)selected] != level) appearances[(int)selected].Refresh(player.State.levels[(int)selected], renderers[(int)selected], settings.appearanceSprites, true);
            });
            Text(map.transform, "영구 효과는 플레이 구간에도 유지됩니다.\n식탁 · 무기제작실은 이번 구현에서 제외됩니다.", 868, 550, 650, 44, 16, new Color(.65f, .72f, .78f));
            BuildWarehouse();
            feedback = Text(ui, "", 28, 778, 1544, 48, 19, Accent);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (player.Sandbox)
            {
                Text(ui, "개발 전용 · 별도 테스트 저장", 28, 842, 350, 32, 17, new Color(.65f, .72f, .78f));
                Button(ui, "자원 + 무기 + HP 50%", 405, 834, 350, 44, player.GrantTestResources);
                Button(ui, "셀레나 영입", 785, 834, 240, 44, player.TestRecruit);
                Button(ui, "쿨타임 초기화", 1055, 834, 240, 44, player.TestResetCooldowns);
            }
#endif
        }
        private void Select(Facility facility) { selected = facility; Refresh(); }
        private void Use()
        {
            switch (selected)
            {
                case Facility.Clinic: player.Heal(); break;
                case Facility.Bed: player.Save(); break;
                case Facility.Dummy: dummy.Test(player); break;
                case Facility.Storage: map.SetActive(false); warehouse.SetActive(true); Refresh(); break;
                case Facility.Potions: player.ReceivePotions(); break;
            }
        }
        private string Effect(Facility f, int level)
        {
            var r = settings.rules;
            return f switch
            {
                Facility.Clinic => $"회복량 {Math.Ceiling(r.baseHealing * (1d + level * (double)r.clinicPercentPerLevel))} HP / 회복 속도 +{level * r.clinicPercentPerLevel:P0}",
                Facility.Bed => $"최대 HP +{level * r.bedHPPerLevel} · 수동 저장",
                Facility.Dummy => $"장착 무기 피해 +{level} (플레이어 보너스)",
                Facility.Storage => $"가방 +{level * r.inventorySlotsPerLevel}칸 / 창고 {r.baseStorageSlots + level * r.storageSlotsPerLevel}칸",
                Facility.Potions => $"물약 {r.potionCount}개 / {Math.Max(r.minimumPotionCooldown, r.potionCooldown - level * r.potionReduction):0}초마다",
                Facility.Gym => $"최대 MP +{level * r.gymPercentPerLevel:P0} (기준 {player.State.baseMana})",
                _ => "추후 구현 — 이용 및 강화 없음"
            };
        }
        private void Refresh()
        {
            if (status == null || player == null) return;
            var s = player.State; var r = settings.rules;
            status.text = $"HP {player.Health.CurrentHealth}/{player.Health.MaxHealth}    MP {s.mana}/{player.MaxMana}    |    코인 {s.coins:N0}    행동 포인트 {s.actionPoints}    |    가방 {player.Inventory.Capacity}칸    |    {(player.Sandbox ? "테스트 저장" : "플레이 저장")}";
            feedback.text = player.Message;
            for (int i = 0; i < 8; i++)
            {
                bool locked = HideOutModel.Locked(s, (Facility)i);
                labels[i].text = Titles[i] + (i >= 6 ? "\n\n추후 구현" : $"\nLv. {s.levels[i]}  ·  외형 {HideOutModel.Tier(s.levels[i]) + 1}\n{(locked ? "셀레나 영입 필요" : "선택하여 확인")}");
                appearances[i].Refresh(s.levels[i], renderers[i], settings.appearanceSprites, false);
                cardImages[i].color = Color.Lerp(Panel, HideOutAppearance.TierColors[HideOutModel.Tier(s.levels[i])], (int)selected == i ? .65f : .38f);
                cardImages[i].sprite = renderers[i].sprite; cardImages[i].type = Image.Type.Simple;
            }
            int current = s.levels[(int)selected];
            var cost = HideOutModel.Next(s, r, selected);
            string costs = "최대 강화 단계";
            if (cost != null)
            {
                costs = $"코인 {s.coins}/{cost.coins} · 행동 {s.actionPoints}/{cost.actionPoints}";
                foreach (var material in cost.materials) costs += $"\n{settings.Name(material.itemId)} {HideOutModel.Count(s.inventory, material.itemId)}/{material.amount}";
            }
            double remaining = selected == Facility.Clinic ? s.clinicReadyAt - player.Now : selected == Facility.Potions ? s.potionsReadyAt - player.Now : 0;
            string gate = HideOutModel.Locked(s, selected) ? selected >= Facility.Dining ? "추후 구현" : "잠김: 셀레나 영입 필요" : remaining > 0 ? $"남은 쿨타임 {TimeSpan.FromSeconds(remaining):mm\\:ss}" : selected == Facility.Gym ? "상시 효과 · 별도 이용 없음" : "이용 가능";
            detail.text = $"{Titles[(int)selected]}   Lv. {current}\n\n현재  {Effect(selected, current)}\n\n다음  {(cost == null ? "—" : Effect(selected, current + 1))}\n\n강화 비용\n{costs}\n\n{gate}";
            use.interactable = !HideOutModel.Locked(s, selected) && selected != Facility.Gym && remaining <= 0;
            upgrade.interactable = !HideOutModel.Locked(s, selected) && cost != null;
            use.GetComponentInChildren<TMP_Text>().text = selected switch { Facility.Bed => "수동 저장", Facility.Dummy => "장착 무기 테스트", Facility.Storage => "창고 열기", _ => "이용" };
            if (warehouse.activeSelf) RefreshWarehouse();
        }
        private void BuildWarehouse()
        {
            warehouse = Box(ui, "Warehouse", 28, 166, 1544, 596, Panel).gameObject;
            Button(warehouse.transform, "시설로 돌아가기", 18, 16, 240, 48, () => { warehouse.SetActive(false); map.SetActive(true); });
            Button(warehouse.transform, "이름 오름차순", 960, 16, 240, 48, player.SortStorage);
            Button(warehouse.transform, "기존 종류만 수납", 1220, 16, 300, 48, player.DepositExisting);
            inventoryTitle = Text(warehouse.transform, "", 18, 80, 700, 40, 22, Accent);
            storageTitle = Text(warehouse.transform, "", 790, 80, 730, 40, 22, Accent);
            for (int i = 0; i < 8; i++)
            {
                int row = i; float y = 130 + i * 48;
                inventoryRows[i] = Text(warehouse.transform, "", 20, y, 430, 40, 20, Color.white);
                depositButtons[i] = Button(warehouse.transform, "넣기", 460, y, 112, 38, () => player.Transfer(true, inventoryPage * 8 + row));
                equipButtons[i] = Button(warehouse.transform, "장착", 584, y, 112, 38, () => player.Equip(inventoryPage * 8 + row));
                storageRows[i] = Text(warehouse.transform, "", 790, y, 540, 40, 20, Color.white);
                withdrawButtons[i] = Button(warehouse.transform, "꺼내기", 1370, y, 150, 38, () => player.Transfer(false, storagePage * 8 + row));
            }
            Button(warehouse.transform, "가방 이전", 20, 534, 190, 42, () => { inventoryPage = Mathf.Max(0, inventoryPage - 1); RefreshWarehouse(); });
            Button(warehouse.transform, "가방 다음", 230, 534, 190, 42, () => { inventoryPage = Mathf.Min((player.Inventory.Capacity - 1) / 8, inventoryPage + 1); RefreshWarehouse(); });
            Button(warehouse.transform, "창고 이전", 790, 534, 190, 42, () => { storagePage = Mathf.Max(0, storagePage - 1); RefreshWarehouse(); });
            Button(warehouse.transform, "창고 다음", 1000, 534, 190, 42, () => { storagePage = Mathf.Min((player.State.storage.Count - 1) / 8, storagePage + 1); RefreshWarehouse(); });
            warehouse.SetActive(false);
        }
        private void RefreshWarehouse()
        {
            inventoryTitle.text = $"플레이어 가방 · {player.Inventory.Capacity}칸 · {inventoryPage + 1}페이지";
            storageTitle.text = $"창고 · {player.State.storage.Count}칸 · {storagePage + 1}페이지";
            for (int i = 0; i < 8; i++)
            {
                int bagIndex = inventoryPage * 8 + i, storeIndex = storagePage * 8 + i;
                var bag = player.Inventory.GetSlot(bagIndex); var stored = storeIndex < player.State.storage.Count ? player.State.storage[storeIndex] : null;
                inventoryRows[i].text = bag == null ? "—" : bag.IsEmpty ? $"{bagIndex + 1:00}  빈 슬롯" : $"{bagIndex + 1:00}  {bag.Item.DisplayName} ×{bag.Amount}";
                storageRows[i].text = stored == null ? "—" : stored.Empty ? $"{storeIndex + 1:00}  빈 슬롯" : $"{storeIndex + 1:00}  {settings.Name(stored.id)} ×{stored.count}";
                depositButtons[i].interactable = bag != null && !bag.IsEmpty;
                equipButtons[i].interactable = bag != null && !bag.IsEmpty && bag.Item.IsEquipment;
                withdrawButtons[i].interactable = stored != null && !stored.Empty;
            }
        }
        private static void Rect(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(width, height);
        }
        private static Image Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; Rect(image.rectTransform, x, y, width, height); return image;
        }
        private TMP_Text Text(Transform parent, string value, float x, float y, float width, float height, int size, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>(); text.font = settings.font; text.fontSharedMaterial = settings.font.material;
            text.text = value; text.fontSize = size; text.color = color; text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.Normal;
            Rect(text.rectTransform, x, y, width, height); return text;
        }
        private Button Button(Transform parent, string title, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
        {
            var image = Box(parent, title, x, y, width, height, new Color(.20f, .29f, .35f)); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            var label = Text(image.transform, title, 8, 0, width - 16, height, 20, Color.white); label.alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}
