# Inventory / Stats Prototype

## Scene setup

1. Player GameObject에 `PlayerInventory`와 `InventoryUI`를 붙입니다.
2. `InventoryUI.inventory`에 같은 플레이어의 `PlayerInventory`를 연결합니다.
3. `PlayerStats`는 없으면 Play 중 자동으로 추가됩니다. 시작 포인트나 스탯 편차 제한을 조절하려면 인스펙터에서 직접 붙여두는 편이 좋습니다.
4. `InventoryUI` 인스펙터의 **Create Editable Scene UI** 버튼으로 씬 캔버스를 생성합니다. 이미 생성된 UI는 Play 중 부족한 탭, 장비 장착, 스탯 강화 패널을 보강합니다.

## Controls

- `I`: 인벤토리 UI 열기 또는 닫기
- `E`: 장비 장착/스탯 강화 UI 열기 또는 닫기
- `Esc`: 열린 UI 닫기
- `1`, `2`, `3`: 소비 아이템 퀵슬롯 사용
- 인벤토리 슬롯 더블클릭: 소비 아이템 사용, 장비 아이템 장착
- 일반 아이템 접근: 자석처럼 플레이어에게 이동 후 자동 획득
- 퀘스트 아이템 접근: 자동 획득하지 않고 `F`로 획득

`I`와 `E` 창은 동시에 열리지 않습니다. 한쪽을 열면 다른 쪽 창은 닫힙니다.

## Item definition

Project 창의 **Create > Behind The Gear > Inventory Item**으로 아이템을 만듭니다.

- `Category`: 잡화, 소비, 장비, 퀘스트 중 하나
- `Max Stack`: 장비는 항상 1, 다른 아이템은 최대 50
- `Consumable Effect`: 소비 아이템 테스트 효과. 현재는 로그 출력 구조이며 실제 체력/마나 시스템이 생기면 연결합니다.
- `Item Id`: JSON 보상 지급에서 쓰는 문자열 ID

## Runtime API

- 보상, 상점, 제작 지급: `PlayerInventory.Add(item, amount)`
- 소비 사용: `PlayerInventory.UseSlot(index, user)` 또는 `UseQuickSlot(0~2, user)`
- 장비 장착/해제: `PlayerInventory.EquipFromSlot(index)` / `PlayerInventory.Unequip(slot)`
- 몬스터 드랍: `InventoryDropper.DropMonsterLoot()`
- 보스 드랍: `InventoryDropper.DropBossLoot()`
- JSON 보상 지급: `InventoryRewardGrant.GrantConfiguredRewards(inventory)`

`QuestRewardExample.json`은 다음 형식의 예시입니다.

```json
{
  "rewards": [
    { "itemId": "health_potion", "amount": 2 },
    { "itemId": "copper_gear", "amount": 1 },
    { "itemId": "sky_crystal", "amount": 1 }
  ]
}
```

## Prototype notes

- 장비 장착 슬롯은 무기/방어구/장신구 3종을 지원하며, 장착한 아이템은 가방 슬롯과 교체됩니다.
- 소비 효과는 실제 체력/마나 컴포넌트가 없어서 `InventoryItemDefinition.Use`에서 로그 기반 예시로 둡니다.
