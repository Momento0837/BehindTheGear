# 인벤토리 테스트

1. `Assets/Player/PlayerMovementDemo.unity`를 열고 Play를 누릅니다.
2. 오른쪽으로 이동해 바닥의 아이템에 닿으면 자동으로 획득합니다.
3. **I** 또는 오른쪽 아래 **가방 [I]** 버튼으로 인벤토리를 여닫습니다. **Esc / X**로도 닫습니다.
4. 아이템을 마우스 왼쪽 버튼으로 드래그합니다. 빈 칸으로 이동하고, 다른 종류와는 자리 교환하며, 같은 종류는 최대 수량까지 합칩니다.
5. 창 밖에 놓거나 드래그 중 창을 닫으면 원래 위치를 유지합니다. 아이템에 마우스를 올리면 설명을 표시합니다.

기본 30칸, 빨간 포션 12개, 파란 포션 8개, 톱니바퀴 25개(20 + 5), 결정 3개를 테스트할 수 있습니다. 톱니바퀴 20개를 5개 칸으로 옮기면 대상 칸이 20개가 되고 원래 칸에 5개가 남습니다. 가득 찬 인벤토리가 받아들이지 못한 아이템은 바닥에 남고, 다시 접근하면 획득을 시도합니다.

## 다른 씬에 연결

- Rigidbody2D/Collider2D가 있는 플레이어에 `PlayerInventory`, `InventoryUI`를 추가합니다.
- `InventoryUI.inventory`에 해당 플레이어의 `PlayerInventory`, `font`에 `Assets/Fonts/Pretendard-Bold SDF.asset`을 연결합니다.
- `Assets/Inventory/Prefabs`의 픽업 프리팹을 씬에 배치합니다. `WorldItemPickup`의 Item과 Amount로 종류와 수량을 변경합니다.
- UI는 씬 오브젝트로 저장합니다. 기존 테스트 씬을 열거나 스크립트 리로드가 끝나면 에디터가 누락된 `Inventory Canvas`를 한 번 생성하고 연결합니다. 씬은 자동 저장하지 않으므로 확인 후 Ctrl+S로 저장하세요. 이미 있는 Canvas의 배치나 크기는 덮어쓰지 않습니다.
- 다른 씬에서 연결했다면 `InventoryUI` Inspector의 **Create Editable Scene UI** 버튼으로 UI를 생성하세요. 기존 EventSystem에 입력 모듈이 없으면 Play 시 New Input System 모듈을 연결합니다.
- `PlayerInventory.capacity`로 칸 수를 지정할 수 있습니다. 30칸을 넘으면 마우스 휠로 스크롤합니다. Play 중 용량 변경은 지원하지 않습니다.

## Play 전에 가방 버튼 위치/크기 조정

1. Hierarchy의 루트 `Inventory Canvas`를 펼치고 `Inventory Shortcut`을 선택합니다. 또는 플레이어의 `InventoryUI` Inspector에서 **Select Bag Button**을 누릅니다.
2. Scene 뷰에서 **T**(Rect Tool)로 이동/크기를 조절하거나 Rect Transform의 **Pos X / Pos Y / Width / Height**를 변경합니다. 버튼 글자는 버튼 크기에 맞춰 중앙을 유지합니다.
3. `Inventory Window`도 같은 방식으로 편집할 수 있습니다. 창 배경은 창 크기에 맞춰 늘어나며 내부 슬롯/문구도 개별 Rect Transform으로 조정할 수 있습니다.
4. 창이 다른 HUD 편집을 가리면 `InventoryUI` Inspector의 **Show Window Preview**를 끕니다. 가방 버튼은 계속 보입니다. **Select Inventory Window**는 창을 다시 표시하고 선택합니다.
5. **Ctrl+S**로 저장합니다. Play 진입 시 위치와 크기를 재설정하지 않으며, 인벤토리 창만 닫힌 상태로 시작합니다.

자동 생성은 현재 열린 씬에만 적용되고 되돌리기(Undo)를 지원합니다. Play 중 바꾼 Rect Transform 값은 Unity의 일반 동작에 따라 Play 종료 시 되돌아가므로, 배치 편집은 Play를 끈 상태에서 진행하세요.

## 아이템 추가

Project 창의 **Create > Behind The Gear > Inventory Item**에서 정의를 생성합니다. 표시명, 설명, 아이콘 Sprite, 최대 중첩 수량을 설정합니다. 아이콘이 없으면 코드로 그린 포션/결정/톱니바퀴 아이콘을 사용합니다.

다른 보상 시스템은 `inventory.Add(item, amount)`를 호출하면 됩니다. 반환값은 실제 추가한 수량입니다. 나머지 수량은 호출자가 보관해야 합니다. 슬롯 이동은 `inventory.Move(from, to)`, 슬롯 조회는 `inventory.GetSlot(index)`를 사용합니다.

이번 구현은 획득·보관·이동 범위입니다. 장비 착용, 아이템 사용, 버리기, 저장/불러오기는 포함하지 않으며 Play를 종료하면 획득 내용은 초기화됩니다. 기존 Main 씬에는 플레이어가 없어 테스트 연결은 PlayerMovementDemo 씬에 적용했습니다.
