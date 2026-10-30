using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    public interface IHideOutRecruitmentSource { bool IsSelenaRecruited { get; } }
    // Future world/quest save providers join the same atomic envelope instead of a second save file.
    public interface IHideOutSaveParticipant
    {
        string SaveKey { get; }
        string CaptureJson();
        void ValidateJson(string json);
        void RestoreJson(string json);
    }

    [DisallowMultipleComponent]
    public sealed class HideOutPlayerBridge : MonoBehaviour, IMeleeDamageModifier
    {
        private const string BonusSource = "HideOut";
        public HideOutSettings Settings { get; private set; }
        public HideOutSave State { get; private set; }
        public PlayerInventory Inventory { get; private set; }
        public PlayerHealth Health { get; private set; }
        public PlayerMeleeAttack Melee { get; private set; }
        public bool Sandbox { get; private set; }
        public bool Initialized => State != null;
        public string Message { get; private set; } = "시설을 선택하세요.";
        public event Action Changed;
        public event Action<Facility, int> UpgradeSucceeded;
        private bool busy, saveBlocked;
        private int lastCommandFrame = -1;
        private float nextDummyUpgradeTime;
        private float nextStorageUpgradeTime;
        public double Now => Settings.rules.offlineCooldown ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d : State.activeClock;
        public float RecoveryRateMultiplier => 1 + State.levels[0] * Settings.rules.clinicPercentPerLevel;
        public int MaxMana => HideOutModel.MaxMana(State, Settings.rules);
        public InventoryItemDefinition EquippedWeapon => Inventory != null
            ? Inventory.GetEquipped(InventoryItemDefinition.EquipmentSlot.Weapon) : null;
        public int EquippedWeaponDummyBonus => Initialized && EquippedWeapon != null
            ? HideOutModel.DummyBonus(State, EquippedWeapon.ItemId) : 0;
        public int EquippedWeaponFinalDamage => EquippedWeapon != null && Melee != null ? Melee.ResolvedDamage : 0;
        public int MeleeDamageBonus
        {
            get
            {
                if (!Initialized) return 0;
                var weapon = EquippedWeapon;
                return (weapon != null ? weapon.GetStatIncrease(PersonalStatType.Attack) + HideOutModel.DummyBonus(State, weapon.ItemId) : 0)
                    + (Inventory.Stats != null ? Inventory.Stats.AttackPowerBonus : 0);
            }
        }
        private string validationSavePath;
        public string SavePath => validationSavePath ?? Path.Combine(Application.persistentDataPath, "HideOutPrototype", Sandbox ? "sandbox-v1.json" : "player-v1.json");
#if UNITY_EDITOR
        public void InitializeEditorSample(HideOutSettings settings)
        {
            Settings = settings;
            State = new HideOutSave { coins = 250, actionPoints = 10, baseMana = settings.rules.baseMana };
            HideOutModel.Normalize(State, settings.rules);
        }
        public void InitializeForValidation(HideOutSettings settings, string isolatedPath)
        {
            if (Initialized) throw new InvalidOperationException("Validation must use a fresh player.");
            string tempRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp")) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(isolatedPath);
            if (!fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Validation saves must stay inside project Temp.");
            validationSavePath = fullPath;
            Initialize(settings, true);
        }
#endif

        [Serializable] private sealed class InventoryConfiguration
        {
            public int capacity = 15, slotsPerAttackStat = 2, questCapacity = 5;
        }

        public void Initialize(HideOutSettings settings, bool sandbox)
        {
            if (Initialized) return;
            Settings = settings; Sandbox = sandbox;
            settings.ValidateCatalog();
            Inventory = GetComponent<PlayerInventory>(); Health = GetComponent<PlayerHealth>(); Melee = GetComponent<PlayerMeleeAttack>();
            if (Inventory == null || Health == null) throw new InvalidOperationException("PlayerInventory and PlayerHealth are required on the player.");
            State = new HideOutSave { baseMana = settings.rules.baseMana, mana = settings.rules.baseMana };
            Capture(State); HideOutModel.Normalize(State, settings.rules);
            if (File.Exists(SavePath))
            {
                try
                {
                    var loaded = ReadSave();
                    // Entry from a live play scene keeps the live player; only an explicit load restores them.
                    if (!sandbox) { State = loaded; ApplyBonuses(); Capture(loaded); }
                    State = loaded; Apply(State);
                    Message = sandbox ? "테스트 저장 복원 완료" : "기지 진행 복원 · 현재 플레이어 상태 유지";
                }
                catch (Exception e) { saveBlocked = true; Message = "저장 읽기 실패 — 원본 보호를 위해 저장 차단: " + e.Message; Debug.LogError(Message, this); }
            }
            ApplyBonuses();
        }
        private void Update()
        {
            if (!Initialized) return;
            if (!Settings.rules.offlineCooldown) State.activeClock += Time.unscaledDeltaTime;
            bool recruited = State.selenaRecruited;
            foreach (var component in GetComponents<MonoBehaviour>())
                if (component is IHideOutRecruitmentSource source) recruited = source.IsSelenaRecruited;
            if (recruited != State.selenaRecruited) { State.selenaRecruited = recruited; Changed?.Invoke(); }
        }
        public void SetMessage(string text) { Message = text; Changed?.Invoke(); }
        public void SetSelenaRecruited() => Change(s => { s.selenaRecruited = true; return "셀레나 영입 완료"; });

        public bool Change(Func<HideOutSave, string> operation)
        {
            if (!Application.isPlaying) return false;
            if (busy || lastCommandFrame == Time.frameCount) return false;
            busy = true; lastCommandFrame = Time.frameCount;
            try
            {
                Capture(State);
                var candidate = JsonUtility.FromJson<HideOutSave>(JsonUtility.ToJson(State));
                string result = operation(candidate); // Failed operations throw before publishing.
                Validate(candidate);
                WriteSave(candidate);
                State = candidate; Apply(State); Message = result; Changed?.Invoke();
                return true;
            }
            catch (Exception e) { SetMessage(e.Message); return false; }
            finally { busy = false; }
        }
        public void Upgrade(Facility f)
        {
            if (HideOutModel.IsExhibition(f)) { SetMessage("추후 구현 시설입니다. 현재 이용·강화할 수 없습니다."); return; }
            if (f == Facility.Dummy) { UpgradeDummy(); return; }
            if (f == Facility.Storage) { UpgradeStorage(); return; }
            bool upgraded = Change(s =>
            {
                if (!HideOutModel.Upgrade(s, Settings.rules, f, out string message)) throw new InvalidOperationException(message);
                return message;
            });
            if (upgraded) UpgradeSucceeded?.Invoke(f, State.levels[(int)f]);
        }
        public void UpgradeStorage()
        {
            if (Time.unscaledTime < nextStorageUpgradeTime)
            {
                SetMessage("업그레이드 입력을 처리 중입니다.");
                return;
            }
            bool changed = Change(s =>
            {
                if (!HideOutModel.Upgrade(s, Settings.rules, Facility.Storage, out string message))
                    throw new InvalidOperationException(message);
                return message;
            });
            if (changed)
            {
                nextStorageUpgradeTime = Time.unscaledTime + .25f;
                UpgradeSucceeded?.Invoke(Facility.Storage, State.levels[(int)Facility.Storage]);
            }
        }
        public void UpgradeDummy()
        {
            if (EquippedWeapon == null)
            {
                SetMessage("장착된 무기가 없습니다.");
                return;
            }
            if (Time.unscaledTime < nextDummyUpgradeTime)
            {
                SetMessage("업그레이드 입력을 처리 중입니다.");
                return;
            }
            nextDummyUpgradeTime = Time.unscaledTime + .25f;
            bool upgraded = Change(s =>
            {
                string weaponId = s.equipment != null && s.equipment.Length > 0 ? s.equipment[0] : null;
                var weapon = Settings.Find(weaponId);
                if (weapon == null || !weapon.IsEquipment || weapon.Slot != InventoryItemDefinition.EquipmentSlot.Weapon)
                    throw new InvalidOperationException("장착된 무기가 없습니다.");
                if (!HideOutModel.UpgradeDummy(s, Settings.rules, weaponId, out string message))
                    throw new InvalidOperationException(message);
                return message;
            });
            if (upgraded) UpgradeSucceeded?.Invoke(Facility.Dummy, State.levels[(int)Facility.Dummy]);
        }
        public void Heal() => Change(s =>
        {
            if (!HideOutModel.Heal(s, Settings.rules, Health.MaxHealth, Now, out string message)) throw new InvalidOperationException(message);
            return message;
        });
        public void ReceivePotions() => Change(s =>
        {
            if (!HideOutModel.ReceivePotions(s, Settings.rules, Settings, Now, out string message)) throw new InvalidOperationException(message);
            return message;
        });
        public bool Save() => Change(s => "수동 저장 완료");
        public void Transfer(bool deposit, int index) => Change(s =>
        {
            int n = HideOutModel.Move(deposit ? s.inventory : s.storage, deposit ? s.storage : s.inventory, index, Settings);
            if (n == 0) throw new InvalidOperationException("빈 슬롯이거나 옮길 공간이 없습니다.");
            return $"{n}개 이동 완료 · 남은 아이템은 원래 위치에 보존";
        });
        public void DepositExisting() => Change(s => $"기존 종류 {HideOutModel.DepositExisting(s, Settings)}개 수납");
        public void SortStorage() => Change(s => { HideOutModel.Sort(s.storage, Settings); return "창고 이름 오름차순 정렬 완료"; });
        public void Equip(int index) => Change(s =>
        {
            var slot = s.inventory[index]; var item = Settings.Find(slot.id);
            if (slot.Empty || item == null || !item.IsEquipment || item.Slot == InventoryItemDefinition.EquipmentSlot.None) throw new InvalidOperationException("장착할 수 없는 아이템입니다.");
            int equipmentIndex = (int)item.Slot - 1;
            string previous = s.equipment[equipmentIndex]; s.equipment[equipmentIndex] = slot.id;
            slot.id = previous; slot.count = string.IsNullOrEmpty(previous) ? 0 : 1;
            return "장비 교체 완료";
        });
        public void Load()
        {
            if (busy) return;
            try { var loaded = ReadSave(); Validate(loaded); State = loaded; Apply(State); SetMessage("저장 복원 완료"); }
            catch (Exception e) { SetMessage("불러오기 실패: " + e.Message); }
        }
        private HideOutSave ReadSave()
        {
            var loaded = JsonUtility.FromJson<HideOutSave>(File.ReadAllText(SavePath));
            if (loaded == null || loaded.version != 1) throw new InvalidDataException("지원하지 않거나 손상된 저장 형식");
            HideOutModel.Normalize(loaded, Settings.rules); Validate(loaded); return loaded;
        }
        private void WriteSave(HideOutSave candidate)
        {
            if (saveBlocked) throw new IOException("기존 저장 읽기 실패 상태입니다. 원본을 덮어쓰지 않습니다.");
            string path = SavePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(candidate, true));
                stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        private void Capture(HideOutSave s)
        {
            s.hp = Health.CurrentHealth;
            s.healthConfiguration = JsonUtility.ToJson(Health);
            s.playerStats = JsonUtility.ToJson(Inventory.Stats);
            s.inventoryConfiguration = JsonUtility.ToJson(JsonUtility.FromJson<InventoryConfiguration>(JsonUtility.ToJson(Inventory)));
            s.baseInventorySlots = Inventory.Capacity - State.levels[3] * Settings.rules.inventorySlotsPerLevel;
            s.inventory = ReadSlots(Inventory.Capacity, Inventory.GetSlot);
            s.questInventory = ReadSlots(Inventory.QuestCapacity, Inventory.GetQuestSlot);
            for (int i = 0; i < 6; i++) s.equipment[i] = Inventory.GetEquipped((InventoryItemDefinition.EquipmentSlot)(i + 1))?.ItemId;
            for (int i = 0; i < 3; i++) s.quickSlots[i] = Inventory.GetQuickSlot(i)?.ItemId;
            foreach (var component in GetComponents<MonoBehaviour>())
            {
                if (!(component is IHideOutSaveParticipant participant)) continue;
                var entry = s.extensions.Find(x => x.key == participant.SaveKey);
                if (entry == null) { entry = new ExtensionState { key = participant.SaveKey }; s.extensions.Add(entry); }
                entry.json = participant.CaptureJson();
            }
        }
        private static List<ItemStack> ReadSlots(int count, Func<int, PlayerInventory.Slot> read)
        {
            var result = new List<ItemStack>();
            for (int i = 0; i < count; i++) { var slot = read(i); result.Add(slot.IsEmpty ? new ItemStack() : new ItemStack(slot.Item.ItemId, slot.Amount)); }
            return result;
        }
        private void Validate(HideOutSave s)
        {
            if (s.coins < 0 || s.actionPoints < 0 || s.hp < 0 || s.baseInventorySlots < 1)
                throw new InvalidDataException("잘못된 저장 수치입니다. 원본은 유지됩니다.");
            foreach (var list in new[] { s.inventory, s.questInventory, s.storage })
                foreach (var slot in list)
                    if (slot == null || slot.count < 0 || (string.IsNullOrEmpty(slot.id) && slot.count != 0)
                        || !slot.Empty && (Settings.Find(slot.id) == null || slot.count > Settings.MaxStack(slot.id)))
                        throw new InvalidDataException("미등록 아이템 또는 잘못된 스택입니다. Settings의 Items 목록을 확인하세요.");
            foreach (string id in s.equipment) if (!string.IsNullOrEmpty(id) && Settings.Find(id) == null) throw new InvalidDataException("미등록 장비: " + id);
            foreach (string id in s.quickSlots) if (!string.IsNullOrEmpty(id) && Settings.Find(id) == null) throw new InvalidDataException("미등록 퀵슬롯: " + id);
            var weaponIds = new HashSet<string>();
            foreach (var record in s.dummyWeaponBonuses)
            {
                var weapon = record != null ? Settings.Find(record.weaponId) : null;
                if (record == null || record.bonus < 0 || weapon == null || !weapon.IsEquipment
                    || weapon.Slot != InventoryItemDefinition.EquipmentSlot.Weapon || !weaponIds.Add(record.weaponId))
                    throw new InvalidDataException("잘못되거나 중복된 허수아비 무기 강화 기록입니다.");
            }
            foreach (var component in GetComponents<MonoBehaviour>())
                if (component is IHideOutSaveParticipant participant)
                { var entry = s.extensions.Find(x => x.key == participant.SaveKey); if (entry != null) participant.ValidateJson(entry.json); }
        }
        private void ApplyBonuses()
        {
            Health.SetMaxHealthBonus(BonusSource, State.levels[1] * Settings.rules.bedHPPerLevel);
            Inventory.SetCapacityBonus(BonusSource, State.levels[3] * Settings.rules.inventorySlotsPerLevel);
        }
        private void Apply(HideOutSave s)
        {
            if (!string.IsNullOrEmpty(s.playerStats)) JsonUtility.FromJsonOverwrite(s.playerStats, Inventory.Stats);
            if (!string.IsNullOrEmpty(s.healthConfiguration)) JsonUtility.FromJsonOverwrite(s.healthConfiguration, Health);
            if (!string.IsNullOrEmpty(s.inventoryConfiguration)) JsonUtility.FromJsonOverwrite(s.inventoryConfiguration, Inventory);
            ApplyBonuses();
            for (int i = 0; i < Inventory.Capacity; i++) { var slot = i < s.inventory.Count ? s.inventory[i] : new ItemStack(); Inventory.GetSlot(i).Set(Settings.Find(slot.id), slot.count); }
            for (int i = 0; i < Inventory.QuestCapacity; i++) { var slot = i < s.questInventory.Count ? s.questInventory[i] : new ItemStack(); Inventory.GetQuestSlot(i).Set(Settings.Find(slot.id), slot.count); }
            var equipped = new InventoryItemDefinition[6]; var quick = new InventoryItemDefinition[3];
            for (int i = 0; i < 6; i++) equipped[i] = Settings.Find(s.equipment[i]);
            for (int i = 0; i < 3; i++) quick[i] = Settings.Find(s.quickSlots[i]);
            Inventory.RestoreEquipmentAndQuickSlots(equipped, quick);
            Health.RestoreSavedHealth(s.hp); Inventory.NotifyContentsChanged();
            s.mana = Mathf.Clamp(s.mana, 0, MaxMana);
            foreach (var component in GetComponents<MonoBehaviour>())
                if (component is IHideOutSaveParticipant participant)
                { var entry = s.extensions.Find(x => x.key == participant.SaveKey); if (entry != null) participant.RestoreJson(entry.json); }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void GrantTestResources()
        {
            if (!Sandbox) { SetMessage("테스트 자원은 직접 실행 샌드박스에서만 사용할 수 있습니다."); return; }
            Change(s => { s.coins += 1000; s.actionPoints += 100; HideOutModel.Add(s.inventory, "hideout_scrap", 100, Settings); s.equipment[0] = Settings.testWeaponId; s.hp = Math.Max(1, Health.MaxHealth / 2); return "테스트 자원 지급 · 연습 무기 장착 · HP 50%"; });
        }
        public void ResetTestProgress()
        {
            if (!Sandbox) { SetMessage("테스트 초기화는 직접 실행 샌드박스에서만 사용할 수 있습니다."); return; }
            Change(s =>
            {
                s.levels = new int[8];
                s.coins = 0;
                s.actionPoints = 0;
                s.inventory = new List<ItemStack>();
                s.questInventory = new List<ItemStack>();
                s.storage = new List<ItemStack>();
                s.equipment = new string[6];
                s.quickSlots = new string[3];
                s.selenaRecruited = false;
                s.clinicReadyAt = 0;
                s.potionsReadyAt = 0;
                s.activeClock = 0;
                s.baseMana = Settings.rules.baseMana;
                s.mana = s.baseMana;
                HideOutModel.Normalize(s, Settings.rules);
                return "테스트 초기화 완료 · 시설/자원/창고/해금/쿨타임 초기화";
            });
        }
        public void TestRecruit() { if (Sandbox) SetSelenaRecruited(); }
        public void TestResetCooldowns() { if (Sandbox) Change(s => { s.clinicReadyAt = s.potionsReadyAt = 0; return "테스트 쿨타임 초기화"; }); }
#endif
    }
}
