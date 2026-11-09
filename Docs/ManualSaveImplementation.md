# 구현 완료: 수동 저장·이어하기 및 퀘스트 진행 저장

Unity 6000.3.23f1에서 구현·검증했다. Unity 버전, 패키지, 렌더 파이프라인은 변경하지 않았다. 모든 테스트 세이브는 프로젝트 `Temp` 아래에 격리한다. 저장·이어하기와 퀘스트 저장의 완료 범위 및 실제로 실행한 검증을 아래에 정리한다.

## 정상 플레이에서 사용하는 순서

1. `Assets/Scenes/Controls/ControlSchemeSelect.unity`에서 Play한다. 기존 시작 씬에 슬롯 1 메뉴가 표시된다.
2. `새 게임` → 조작 방식 선택 → 기존 숲 튜토리얼로 진입한다. 기존 슬롯이 있으면 별도의 덮어쓰기 확인이 나온다. 승인해도 기존 파일은 첫 침대 저장 성공까지 보존된다.
3. 숲의 기지 입구 `HideOutEntrance` 근처에서 F로 상호작용한다. 입구 위치는 숲 `(-2, -2.5)`, 시장 `(52, -2.5)`이다. 기존 플레이어가 기존 기지 UI로 들어간다.
4. 기지에서 침대 아이콘/바로가기 선택 → 카메라 이동 후 `저장하기`를 누른다. 성공한 파일 교체 뒤에만 `저장 완료`가 표시된다. 침대 강화와 저장은 별도 버튼이다. 기존 침대에는 별도의 휴식 동작이 없었다.
5. `메인 메뉴`로 돌아간 뒤 `이어하기`를 누르면 필드를 거치지 않고 기지 내부 UI로 시작한다. 필드 HUD는 기지에서 숨기고, 복귀 시 복원한다. 인벤토리는 기존 설계 UI를 재사용하며 기지 창고 UI도 복원된 슬롯을 표시한다.
6. 현재 기지의 `나가기` 버튼은 기존 작업에서 지정한 임시 목적지 `Market`으로 이동한다. `SaveGameSession.StartExpedition()`의 기본 목적지는 `Forest`이고 목적지 인자를 전달할 수도 있다. 기존 additive 필드 복귀 API는 유지하며, 허수아비 훈련장 왕복은 기존 기지와 실제 플레이어를 유지하는 별도 중첩 방문이다.
7. 탐사 중 숲의 기존 NPC는 수집 퀘스트, 시장의 기존 NPC는 몬스터 5마리 처치 퀘스트를 제공한다. 기존 대화 UI에서 수락하고, 목표 달성 후 다시 대화해 완료 보고한다. 숲 수집 퀘스트의 실제 아이템 ID는 기존 `sky_crystal`이다.

튜토리얼은 기존 진행 순서를 유지한다. 저장 후 이어하기에서 시작하는 탐사는 튜토리얼 영상/일회성 장비 공급을 반복하지 않는 경로다. 퀘스트 수락 전 처치는 소급 집계하지 않는다.

## 실제 소유자와 변경한 진입점

| 영역 | 실제 소유자 / 진입점 | 변경 |
|---|---|---|
| 시작 메뉴 | `ControlSchemeSelectScene`, `ManualSaveMenu` | 기존 조작 선택 씬에 1슬롯 메뉴, 덮어쓰기/삭제 별도 확인 |
| 플레이 세션 | `SaveGameSession`, `PersistentPlayer2D` | 한 번만 복원, 새 게임/이어하기/메뉴 복귀 시 휘발 상태 정리 |
| 기지 진입 | `HideOutEntrance`, `HideOutTravel` | 기존 F 상호작용과 additive 방문 유지, 출입 저장 제거 |
| 기지·재화·강화 | `HideOutPlayerBridge`, `HideOutModel`, `HideOutScene` | 시설·창고·치료 등의 조작은 메모리 트랜잭션, 침대 버튼만 디스크 저장 |
| 인벤토리·장비 | `PlayerInventory`, `PlayerStats`, `InventoryUI` | 아이템 ID/수량/배열 인덱스, 장비 6부위, 퀵슬롯, 스탯과 시설 보너스로 용량 재계산 |
| 퀘스트 | `QuestJournal`, `QuestGiver2D`, `QuestTracker` | 씬 NPC와 분리한 ID별 상태·목표량·보상 기록, 기존 대화/HUD 갱신 |
| 처치·획득 | `MonsterFollowController.Die`, `PlayerInventory.Add`, `QuestPickup2D` | 실제 처치와 실제 수납량으로 진행 갱신, 부분 수납량 유지 |
| 방 이동·드롭 | `SceneFadeTransition`, `StorySceneFlow.PrepareMarket`, `WorldItemPickup`, `RoomDropCache` | 직전 방 하나의 메모리 스냅샷, 도착 스냅샷 선확보 후 교체, 획득 완료 드롭 제외 |
| 파일 | `ManualSaveStore`, `SaveSnapshotValidation` | 버전·해시·ID·스택·장착 부위·시설·퀘스트·스탯 전체 검증, 임시 파일 flush 후 교체 |

`HideOutSave`는 기존 데이터를 확장했다. 저장 대상은 창고, 일반/퀘스트 인벤토리 슬롯, 장비, 퀵슬롯, 코인/AP, 개인 스탯과 잔여 포인트, 시설 ID/단계/해금, 셀레나 영입 플래그, 시설 쿨타임, 기존 무기 ID별 허수아비 강화 보너스, HP/MP 및 기존 확장 참가자 데이터다. 현재 아이템 구현에는 개별 복사본별 내구도나 강화 인스턴스가 없다. 허수아비 강화는 기존 설계대로 무기 정의 ID별로 저장한다.

필드 위치·귀환 씬·방·바닥 드롭·적 상태·진행 중 공격·퀵슬롯 전투 쿨타임은 파일에 쓰지 않는다. 기지 방문의 귀환 정보는 `HideOutTravel` 메모리에서만 유지한다.

## 저장 규격과 실패 처리

- 경로: `Application.persistentDataPath/HideOutPrototype/slot-1-v2.json`.
- v2 파일은 `formatVersion`, JSON `payload`, SHA-256을 가진 envelope다. 정상 파일이 있으면 `.tmp`를 기록하고 `Flush(true)`한 뒤 `File.Replace`로 교체한다. 직전 파일은 `.bak`으로 남는다. 실패 시 기존 파일을 삭제하는 대체 경로는 없다.
- 이전 `player-v1.json`을 읽을 수 있다. 다음 수동 저장 때 v2 슬롯을 생성한다. 이전 파일은 그대로 보존하며, 슬롯 삭제 확인 시 v1/v2와 각 `.bak`/`.tmp`를 함께 정리한다. 백업은 자동으로 불러오지 않는다.
- 이어하기는 전체 검증을 먼저 끝낸 뒤 기지를 연다. 적용 단계가 실패하면 이전 스냅샷으로 되돌린다. 기본 초기화는 디스크를 읽거나 저장하지 않는다.
- 방 이동, 퀘스트 수락/완료, 시설 조작, 메뉴 복귀, 종료에는 디스크 저장 호출이 없다. 조작 방식의 기존 PlayerPrefs 설정 저장은 게임 세이브와 별개다.
- 손상·미지원 파일은 메뉴에서 오류를 안내하고 유지한다. 새 게임을 시작해도 첫 침대 저장 전에는 덮어쓰지 않는다.

## 씬·프리팹 연결

- `ControlSchemeSelect.unity`: `ManualSaveMenu`에 기지 설정과 이어하기 플레이어 프리팹 연결.
- `Forest.unity`, `Market.unity`: 기존 `HideOutEntrance.prefab`의 인스턴스 배치, Interactable 레이어, NPC 퀘스트 정의 연결.
- `Assets/Player/SavePlayerRig.prefab`: 기존 숲의 플레이어, 설계 인벤토리 캔버스, 기존 HUD 참조를 함께 묶은 이어하기용 프리팹. 런타임 인스턴스 ID를 저장하지 않는다.
- `Forest.unity`의 기존 중복 인벤토리 UI 직렬화는 현재 설계 UI를 쓰는 구조로 정리했다. 기존 플레이어·맵·NPC·입구를 유지하며, 대량 diff의 주된 부분은 이전 UI 위젯 제거다.
- `HideOutPrototype.asset`: 기존 아이템 카탈로그에 누락되었던 `tutorial_gear`를 포함. 기존 시설 ID와 설정을 유지.
- `EditorBuildSettings.asset`: 기존 기지 씬을 활성 빌드 씬에 추가.
- `HideOutSystemScene.unity`와 기존 입구 프리팹 원본은 수정할 필요가 없었다. 기지의 기존 런타임 UI 생성 코드를 확장했다.

`ManualSaveSetup`의 메뉴는 씬 연결을 다시 생성하는 개발 도구다. 이미 연결된 상태로 제공하므로 일반 플레이에는 실행할 필요가 없다. `Temp/ManualSaveValidation/request.txt`의 제한된 명령은 열린 동일 버전 에디터에서 검사를 실행하기 위한 도구이며 빌드에는 포함되지 않는다.

## 검증 증거

- [통합 플레이 검사](ManualSaveValidation/integration-checks.txt): 51개 통과. 정상 메뉴/입구/침대, 미저장 롤백 2/5, 실제 장착과 시설 슬롯, 실제 처치/NPC 보고, A→B→A, 획득 후 재생성 방지, 빈 B로 캐시 교체, 부분/전체 획득, 덮어쓰기 취소/승인/첫 저장, 삭제 확인, 손상/미지원/해시 오류, 파일 잠금 실패 보존.
- [별도 플레이 세션 복원 검사](ManualSaveValidation/restart-checks.txt): 12개 통과. Play를 종료하고 다시 시작해 디스크에서 복원, 보상 중복 방지, v1 호환과 v2 전환, 두 기존 NPC의 수락 대화와 실제 퀘스트 아이템 획득/보고.
- [기존 순수 모델 검사](ManualSaveValidation/model-checks.txt): 30개 통과. 시설 비용, 창고 이동/정렬, 3,000회 혼합 이동, 강화·치료·물약·MP·전시 시설 규칙.
- [기존 기지 플레이 회귀 검사](ManualSaveValidation/hideout-play-checks.txt): 수동 저장 규약에 맞춰 기존 자동 저장 가정을 수정한 뒤 136개 통과. 강화/창고/쿨타임/더미/카메라/시설 단계 시각화/출입 복귀와 중복 보너스 방지를 확인했다. [미리보기 검사](ManualSaveValidation/preview-checks.txt)도 통과했다.
- [씬 연결 검사](ManualSaveValidation/wiring-checks.txt): 정리한 실제 씬의 메뉴/입구/NPC/카탈로그/빌드 참조 통과.
- 전체 런타임 및 에디터 C# 컴파일 오류 0개. 남은 경고 2개는 기존 `ConversationDialogue.npcLaugh`, `HierarchyScriptControls`의 obsolete API 경고다.
- [기지 이어하기 화면](ManualSaveValidation/continue-hideout.png)을 렌더 결과로 확인했다. 작업 중 에디터가 생성한 폰트/메시 직렬화 변경은 분리해 보관하고 원래 폰트·UI 데이터를 보존했다.

자동 검사는 기존 플레이 진입점과 실제 컴포넌트/버튼 콜백을 사용한다. 자원 준비, 대화 텍스트 넘기기, 처치·드롭 준비 일부는 코드로 주입했다. 사람이 키보드/마우스로 끝까지 플레이한 결과로 표기하지 않는다. 방 전환 실패는 미커밋 전환을 사용한 캐시 보존 검사이며, 실제 씬 파일 손상/비동기 로더 고장은 주입하지 않았다.

저장 검사 도구는 실제 메뉴를 통한 조작 방식 선택도 실행하므로, 검사 전 선택과 해당 PlayerPrefs 키의 존재 여부를 보관하고 검사 종료 시 복원한다. 현재 플레이어의 실제 저장 슬롯 대신 프로젝트 Temp 슬롯만 사용한다.

2026-11-06 최신 저장·훈련장·스킬 통합 작업 트리에서 저장 통합 검사 51개, Play 재시작 복원 검사 12개와 씬 연결 검사를 다시 통과했다. 위 `ManualSaveValidation`의 통합·재시작·씬 연결 텍스트 결과를 최신 실행 결과로 갱신했다. Windows 실행 파일 재시작 검증으로 표현하지 않는다.

## 남은 수동 확인 및 기존 미연결 기능

- Windows 플레이어를 실제 빌드하고 실행 파일을 종료/재실행하는 검사는 하지 않았다. 에디터 Play 종료/재시작으로 도메인과 세션 메모리를 끊은 복원은 검증했다.
- 두 조작 방식에서 실제 F 키 접근 거리/방향, NPC 선택지 클릭, 침대 카메라 포커스, 작은 해상도의 텍스트 가독성을 확인하면 된다. 기지 입구/침대/NPC 콜백 자체는 자동 검사에서 실행했다.
- 디스크 공간 부족·권한 거부·프로세스 강제 종료/전원 차단은 주입하지 않았다. 임시 파일 배타 잠금에 따른 기록 실패와 정상 파일의 바이트 보존은 검증했다.
- 식당과 무기 제작실은 기존부터 전시/추후 구현 시설이다. 저장되는 기존 단계는 보존하며 이용·강화 잠금을 유지한다.
- 셀레나 영입 플래그/물약 해금은 저장한다. 기존 프로젝트에는 실제 영입을 발생시키는 NPC/퀘스트 연결이 없으므로 새 영입 스토리를 임의로 만들지 않았다. 현재는 기존 `SetSelenaRecruited` API와 샌드박스 테스트 경로만 존재한다.
- 개별 아이템 내구도/개체별 강화 시스템은 현재 구현되어 있지 않다.

일반 검사는 `Tools > Manual Save > Run isolated integration checks`에서 실행한다. 검사는 프로젝트 Temp 슬롯을 사용하므로 플레이어의 실제 세이브를 덮어쓰지 않는다. Unity 버전을 바꾸거나 패키지를 갱신하지 않아도 된다.

## 스크립트 폴더 정리

저장 전용 런타임 스크립트 6개는 `Assets/Scenes/Controls/SaveSystem/`에 모았다: `ManualSaveMenu.cs`, `ManualSaveStore.cs`, `SaveGameSession.cs`, `SaveSnapshotValidation.cs`, `QuestJournal.cs`, `RoomDropCache.cs`.

전용 에디터 도구 `ManualSaveSetup.cs`, `ManualSaveChecks.cs`는 같은 폴더의 `Editor/` 하위에 있다. 각 `.meta`와 GUID를 유지해 이동했으며 기존 씬·프리팹 연결을 유지한다. 기지·인벤토리·NPC의 기존 기능 스크립트는 원래 기능 폴더에 남아 있다.
