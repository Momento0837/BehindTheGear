# BehindTheGear 구현 명세서

작성 기준: 2026-10-14, 저장소의 C# 코드·Animator Controller·Unity 씬 설정. 이 문서는 **현재 구현된 동작**을 기록합니다. Unity Play 모드 실행 결과를 확인한 문서는 아닙니다.

## 1. 범위와 실행 씬

BehindTheGear는 2D 플레이어 조작, 애니메이션, 근접 전투, NPC 상호작용·대화·퀘스트, 컷신, 씬 이동을 포함한 프로토타입입니다. 인벤토리와 능력치는 별도 테스트 씬에서 구성되어 있습니다.

| 씬 | 역할 | Build Settings |
| --- | --- | --- |
| `Assets/Scenes/Controls/ControlSchemeSelect.unity` | 조작 방식 선택 후 게임 씬으로 이동 | 포함 |
| `Assets/Player/PlayerMovementDemo.unity` | 플레이어·몬스터·NPC·컷신·포털 기능 데모 | 포함 |
| `Assets/Scenes/Main/Main.unity` | 포털의 이동 대상 및 도착 지점 | 포함 |
| `Assets/Scenes/Inventory/InventoryTestScene.unity` | 인벤토리·장비·능력치 테스트 | 미포함 |

데모 씬의 플레이어 GameObject 이름은 `Temporary Player`이지만 Animator에는 **`Assets/!Animation/Player/Player.controller`**가 연결되어 있습니다. 별도 파일 `Temporary Player.controller`는 이 명세의 애니메이션 대상으로 사용하지 않습니다.

## 2. 플레이어 입력과 이동

### 2.1 입력

`PlayerInputReader`가 Unity Input System 입력을 읽고 기능별 이벤트를 발행합니다. 선택한 조작 방식은 `PlayerPrefs`에 저장됩니다.

| 기능 | WASD·마우스 방식 | 방향키 방식 |
| --- | --- | --- |
| 수평 이동 | `A` / `D` | `←` / `→` |
| 점프 | `W` 또는 `Space` | `↑` 또는 `Space` |
| 앉기 | `S` 누르고 있기 | `↓` 누르고 있기 |
| 기본 공격 | 마우스 왼쪽 | `Z` |
| 보조 공격 | 마우스 오른쪽 | `X` |
| 스킬 1 / 2 | `Q` / `E` | `C` / `V` |
| 상호작용 | `F` | `F` |
| 임시 피해 입력 | `K` | `K` |

마우스 공격은 UI 위를 클릭한 경우 발생하지 않습니다. `K`는 `PlayerHealth.TakeDamage(1)`을 호출합니다. 데모 플레이어의 방어력은 11이지만 최소 피해량이 1이므로 유효한 입력 한 번에 HP가 1 감소합니다. 무적 시간 동안의 반복 입력은 무시됩니다.

### 2.2 이동 규칙

- `PlayerMovement2D`가 `Rigidbody2D`의 수평 속도와 점프를 처리하고 `BoxCollider2D.Cast`로 접지 상태를 판정합니다.
- 앉으면 플레이어 콜라이더 높이가 절반이 되고 이동 속도에 배율이 적용됩니다. 앉은 상태에서 점프하면 `PlatformEffector2D` 일방향 발판을 통과하도록 일정 시간 충돌을 무시합니다.
- `PlayerSpriteFacing`은 마지막 수평 입력 방향으로 스프라이트를 뒤집습니다. 멈추면 마지막 방향을 유지합니다.
- 데모 플레이어 설정은 이동 속도 7, 점프 속도 15, 앉기 속도 배율 0.7, 발판 통과 시간 0.25초입니다.

## 3. 애니메이션과 공격 콤보

애니메이션 전환은 `Player.controller`의 상태 전이와 Animator 매개변수로 처리합니다. `PlayerMovement2D`는 `Animator.Play()`를 사용해 공격 상태를 직접 지정하지 않습니다.

| 입력·상태 | Animator 상태 또는 처리 |
| --- | --- |
| 정지 / 수평 이동 | `Idle` ↔ `Running`; `IsMoving` Bool로 전환 |
| 정지 중 기본 공격 | `Attack_1` → `Attack_2` → `Attack_3` |
| 이동 중 기본 공격 | `Run_Attack_1` → `Run_Attack_2` → `Run_Attack_3` |
| 정지 / 이동 중 점프 | `Jump` / `Run_Jump`; `Jump` Trigger와 `IsGrounded` Bool 사용 |
| 정지 / 이동 중 피격 | `Damaged` / `Run_Damaged` |
| HP가 0이 됨 | `Dead` Trigger, 조작 잠금 및 수평 이동 중단 |

기본 공격을 연속 입력하면 다음 번호의 공격을 예약합니다. **현재 공격 애니메이션이 끝난 뒤** 예약된 공격이 시작됩니다. 공격 종료 후 0.3초 이내에 다시 누른 경우에도 다음 번호로 이어집니다. 그 시간을 넘기면 1번부터 다시 시작하며, 3번 다음은 1번입니다. 애니메이션 중 여러 번 눌러도 예약은 한 번만 저장됩니다. 점프·피격 시 현재 콤보와 예약 입력은 초기화됩니다. 다음 공격을 시작하는 시점의 이동 입력에 따라 일반 공격과 달리기 공격 중 하나가 선택됩니다.

대화·컷신 중에는 `PlayerControlLock`이 조작 컴포넌트를 비활성화합니다. 잠금 중에도 접지 상태를 Animator에 갱신하므로, 점프 도중 대화를 시작한 뒤 착지해도 점프 애니메이션에 머무르지 않도록 처리합니다.

## 4. 체력과 근접 전투

| 항목 | 현재 구현 |
| --- | --- |
| 플레이어 체력 | 데모 최대 HP 10. `PlayerHealthHUD`가 `HP 현재 / 최대`를 표시 |
| 플레이어 피해 | `max(1, 공격력 - PlayerHealth.defense)`, 0.2초 무적 시간 적용 |
| 사망 | `Dead` 애니메이션 Trigger, `PlayerControlLock` 획득, 1.5초 검은 화면 전환 후 현재 씬 재로드 호출 |
| 플레이어 근접 판정 | 기본 공격 입력 시 전방 사각형에 있는 몬스터에 피해. 데모 피해량 10, 사거리 2, 쿨다운 1초 |
| 몬스터 | 인식 범위 진입 후 추적, 시야 차단 시 정지, 공격 범위에서 주기적으로 플레이어 피해 처리. HP 0이면 드롭 생성 후 비활성화 |

**공격 애니메이션과 타격 판정은 별도 동작**입니다. `PlayerMovement2D`가 콤보 애니메이션을 관리하고, `PlayerMeleeAttack`이 자체 1초 쿨다운으로 실제 피해를 처리합니다. 따라서 콤보의 모든 애니메이션이 반드시 한 번씩 피해를 주는 구조는 아닙니다. 데모 씬의 `PlayerAttackController` UnityEvent는 비어 있으며, 보조 공격과 스킬 입력에 연결된 별도 타격 동작은 이 씬에서 확인되지 않습니다.

## 5. 상호작용, 대화, 퀘스트

### 5.1 상호작용

`PlayerInteractionController`는 플레이어 앞쪽의 Trigger Collider를 탐색하고 시야가 막히지 않은 가장 가까운 `Interactable2D`를 대상으로 정합니다. 대상이 있으면 `[F] Interact` 안내를 표시합니다. `F`를 누르면 대상의 `onInteract` 이벤트가 호출되고, 같은 오브젝트에 연결된 포털 → 퀘스트 제공자 → 컷신·일반 대화 순으로 해당 기능이 실행됩니다.

### 5.2 JSON 대화

`ConversationDialogue`는 JSON의 `lines` 배열을 읽어 `speaker`와 `text`를 TextMeshPro UI에 표시합니다. 현재 기본 글자 간격은 0.05초입니다. 문장 출력 중 `Space`, `Enter`, 마우스 왼쪽 클릭으로 남은 글자를 즉시 표시할 수 있고, 문장 출력 후 같은 입력 또는 화면 버튼으로 다음 문장에 진행합니다. 마지막 문장 버튼은 `닫기`로 표시됩니다. 대화 중 플레이어 조작이 잠기고 종료 시 해제됩니다.

대사 데이터는 `Assets/Resources/Dialogue`에 있습니다.

| 파일 | 용도 |
| --- | --- |
| `cutscene_conversation.json` | 컷신 뒤 일반 대화 |
| `quest_offer.json` | 퀘스트 제안 |
| `quest_accepted.json` | 수락 뒤 대화 |
| `quest_rejected.json` | 거절 뒤 대화 |
| `quest_completed.json` | 완료 대화 |

### 5.3 퀘스트 흐름

데모의 `Quest Giver NPC`는 제안 대화 → 수락/거절 선택 → 수락 대화 → 퀘스트 오브젝트 생성 → 접촉하여 수집 → 완료 대화 순서로 진행합니다. 선택 버튼은 제안 문장이 끝난 뒤 나타납니다. 거절하면 다시 제안받을 수 있습니다. 퀘스트 진행 단계는 `NotStarted`, `WaitingForItem`, `ReadyToComplete`, `Completed`입니다. 이 퀘스트의 `QuestPickup2D`는 아래 인벤토리 시스템의 퀘스트 아이템과 별도 구현입니다.

### 5.4 흔들리는 대화 NPC

`Quest Giver NPC - Shaking Dialogue`에만 `DialogueTextEffects`가 연결되어 있습니다. 글자 출력 중 글자별 흔들림과 낙하 먼지가 발생하고, 한 줄 출력 후 흔들림이 앞 글자부터 순서대로 사라집니다. 현재 흔들림 폭 1.5, 속도 28, 순차 안정 시간 0.7초, 글자별 안정 시간 0.4초입니다. 먼지는 글자당 3개, 크기 5~8, 지속시간 0.48~0.72초, 중력 110으로 설정되어 있습니다. 각 값은 해당 NPC의 Inspector에서 수정할 수 있습니다. 자세한 항목은 [대화 이펙트 설정](06_Dialogue_Text_Effects.md)을 참조합니다.

## 6. 컷신과 씬 이동

- `CutsceneVideoSequence`는 `VideoPlayer`를 준비해 영상을 재생하는 동안 조작을 잠그고 지정한 UI를 숨깁니다. 영상 종료 또는 `Space` 3초 누르기로 종료하며, 필요하면 종료 뒤 대화를 시작합니다.
- `ScenePortal2D`는 상호작용한 플레이어의 `PersistentPlayer2D`를 통해 대상 씬으로 이동합니다. `SceneFadeTransition`이 검은 화면으로 전환하고, 이동 후 `SceneSpawnPoint2D`의 ID에 맞춰 플레이어를 배치합니다.
- 플레이어 오브젝트는 씬 이동 중 `DontDestroyOnLoad`로 유지됩니다. 대상 씬과 Spawn Point ID가 일치해야 지정 위치에 도착합니다.

## 7. 인벤토리와 능력치 테스트 씬

`InventoryTestScene`에는 `PlayerInventory`, `InventoryUI`, `PlayerStats`와 아이템 정의·획득 기능이 있습니다. 이 씬은 현재 Build Settings의 실행 씬 목록에 포함되어 있지 않습니다.

| 기능 | 현재 구현 |
| --- | --- |
| 인벤토리 | 기본 일반 슬롯 15칸, 퀘스트 슬롯 5칸, 소비 아이템 퀵슬롯 3칸. 같은 아이템 합치기, 슬롯 이동·교환, 필터 제공 |
| 장비 | 무기·방어구·장신구·머리·신발·기타 슬롯에 장착·해제 |
| UI 입력 | `I`: 인벤토리, `E`: 장비·능력치, `1`~`3`: 퀵슬롯, `Esc`: 창 닫기. 두 창을 함께 열면 나란히 배치 |
| 필드 아이템 | 일반 아이템은 접근 시 플레이어에게 이동해 자동 획득. 인벤토리 퀘스트 아이템은 접근 후 `F`로 획득 |
| 능력치 | 회피·은신·공격·방어·설득. 시작 포인트 5, 능력치 간 편차 제한 5. 공격 능력치 1당 일반 인벤토리 2칸 증가 |

아이템 정의는 `InventoryItemDefinition` ScriptableObject이며 범주는 잡화·소비·장비·퀘스트입니다. `InventoryDropper`와 JSON 보상 지급 코드는 별도로 있습니다. 현재 소비 아이템의 체력·마나 회복은 로그 출력 예시이고 `PlayerHealth`를 실제로 회복하지 않습니다. `PlayerStats`의 회피·은신·공격력·방어력 경감·상점 할인 수치도 해당 게임플레이 시스템에 직접 적용되는 코드 연결은 확인되지 않았습니다. 데모 몬스터의 드롭은 `MonsterFollowController`가 자체 처리하며 `InventoryDropper`를 직접 호출하지 않습니다.

## 8. 대사를 다른 로컬 환경에서 재사용하는 방법

대사는 외부 서버가 아니라 프로젝트 안의 JSON `TextAsset`으로 저장되어 있습니다. `Assets/Resources/Dialogue/*.json`이 원본 데이터입니다.

1. **같은 Unity 프로젝트를 다른 PC나 폴더에서 여는 경우:** Git으로 동기화하거나 `Assets`, `Packages`, `ProjectSettings` 폴더를 복사합니다. `Assets/Resources/Dialogue`의 JSON과 각 `.meta` 파일을 함께 유지하면 기존 씬의 참조 GUID가 보존됩니다. 현재 프로젝트의 Unity 버전은 `6000.3.25f1`입니다.
2. **다른 Unity 프로젝트에서 대사 내용만 사용하는 경우:** Unity Project 창에서 `Assets/Resources/Dialogue`를 선택하고 **Assets > Export Assets**로 내보낸 뒤, 대상 프로젝트에서 **Assets > Import Package > Custom Package**로 가져올 수 있습니다. JSON과 `.meta`를 직접 복사해도 됩니다. 대화 컴포넌트가 파일을 자동 검색하지 않으므로 대상의 `ConversationDialogue` 또는 `QuestGiver2D` Inspector에서 가져온 `TextAsset`을 연결해야 합니다. 메뉴 이름은 이 프로젝트의 Unity 6.3 매뉴얼 기준입니다([내보내기](https://docs.unity3d.com/6000.3/Documentation/Manual/AssetPackagesCreate.html), [가져오기](https://docs.unity3d.com/6000.3/Documentation/Manual/AssetPackagesImport.html)).
3. **동일한 대화 화면·흔들림·먼지까지 재사용하는 경우:** JSON만으로는 화면이 만들어지지 않습니다. `ConversationDialogue`, 필요한 경우 `DialogueTextEffects` 코드와 이들이 참조하는 TextMeshPro/UI 오브젝트 및 씬·프리팹 설정도 함께 옮겨야 합니다. Export Package에 필요한 에셋과 종속성을 포함하고, 가져온 뒤 Inspector 참조를 확인합니다.

JSON 형식은 다음과 같습니다.

```json
{
  "lines": [
    { "speaker": "김철수", "text": "대사 내용" }
  ]
}
```

## 9. 현재 구현 경계와 확인 사항

- 본 문서는 코드와 에셋 연결을 읽어 작성했습니다. Unity Play 모드에서 기능별 재생 확인은 별도로 필요합니다.
- `PlayerMeleeAttack`의 피해량·쿨다운은 Animator 콤보와 독립적입니다. 애니메이션별 타격 시점을 일치시키려면 추가 연결이 필요합니다.
- `PlayerHealth.defense`와 `PlayerStats.Defense`는 별도 값입니다. 능력치 방어 강화가 플레이어 피해 계산에 반영되는 연결은 확인되지 않았습니다.
- 사망 후 현재 씬을 다시 불러오지만, 영속 플레이어의 HP와 조작 잠금 상태를 초기화하는 경로는 코드에서 확인되지 않았습니다. 재시작 동작은 Play 모드에서 확인이 필요합니다.
- 퀘스트 단계와 인벤토리 데이터는 런타임 컴포넌트 상태입니다. 파일 저장·불러오기 기능은 확인되지 않았습니다.

## 10. 주요 구현 파일

- 플레이어 입력·이동·애니메이션: `Assets/Player/Script/PlayerInputReader.cs`, `PlayerMovement2D.cs`, `Assets/!Animation/Player/Player.controller`
- 전투·체력: `Assets/Player/Script/PlayerMeleeAttack.cs`, `PlayerHealth.cs`, `PlayerDeathRestart.cs`, `Assets/Monster/Scen/MonsterFollowController.cs`
- 상호작용·대화·퀘스트: `Assets/Player/Script/PlayerInteractionController.cs`, `ConversationDialogue.cs`, `DialogueTextEffects.cs`, `QuestGiver2D.cs`
- 컷신·씬 이동: `Assets/Player/Script/CutsceneVideoSequence.cs`, `ScenePortal2D.cs`, `PersistentPlayer2D.cs`, `SceneFadeTransition.cs`
- 인벤토리·능력치: `Assets/Scenes/Inventory/Scripts/PlayerInventory.cs`, `InventoryUI.cs`, `InventoryItemDefinition.cs`, `Assets/Player/Script/PlayerStats.cs`
