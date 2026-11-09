# ScareCrow 훈련장 작업 안내

## 실제 작업 위치와 씬

- 작업 사본: `C:\Users\ASUS\Desktop\UnityProject\GitHub\BehindTheGear\BehindTheGear`
- 요청의 `F:\UnityProject\GitHub\BehindTheGear\BehindTheGear` 경로는 이 실행 환경에 없었습니다.
- 기존 기지: `Assets/Scenes/HideOutSystemScene/HideOutSystemScene.unity`
- 기존 훈련장: `Assets/Scenes/HideOutSystemScene/ScareCrowScene/ScareCrowScene.unity`
- 실제 씬 이름은 대소문자를 포함하여 **ScareCrowScene**입니다. 새 대체 씬을 만들거나 이름을 바꾸지 않았습니다.
- 훈련장 씬 GUID `cbe2fa8c7f978a54c91d12270fafe646`을 유지하고 `EditorBuildSettings.asset`에 이 씬만 추가했습니다.

## 진입과 왕복

월드 시설 아이콘, 허수아비 화면 영역, `Shortcut Dummy`와 `EnterTraining()`은 모두 `HideOutScene.SelectFacility(Facility.Dummy)` 경로를 공유합니다.

클릭하면 다른 시설과 동일한 `HideOutCameraController.TryFocus`로 허수아비 위치까지 먼저 이동·확대합니다. 카메라가 도착한 뒤 `SceneFadeTransition.FadeThrough`로 화면을 어둡게 하고, 완전히 검은 동안 비동기 씬 로드와 플레이어·카메라 연결을 완료한 뒤 밝힙니다. 기본 페이드 시간은 어두워짐·밝아짐 각각 0.4초이며 기지의 `HideOutScene.trainingFadeDuration` Inspector에서 조절합니다. 확대 시간과 목표 크기는 기존 시설 카메라 설정을 따릅니다. 확대부터 화면 복원까지 중복 클릭과 다른 시설 이동을 막습니다. 유지된 기지는 화면에서 숨긴 뒤 전체 보기로 복원하므로 복귀 후 다시 같은 진입 연출을 사용할 수 있습니다.

`HideOutTravel.EnterTraining/ExitTraining`은 기존 additive 방문 구조에 중첩 훈련장 방문을 연결합니다. 이동 중 잠금으로 중복 로드를 방지하고 기지 루트의 기존 활성 상태를 보관합니다. 실제 플레이어, 인벤토리, 장비, 무기 ID별 보너스, 성장 스탯, HP·MP를 복사하거나 초기화하지 않습니다. 복귀 전에 기존 기지의 물리·입력 잠금을 적용하여 씬 해제 중 중력으로 복귀 위치가 바뀌지 않게 합니다.

훈련장 `기지로 돌아가기` 버튼과 ESC는 같은 복귀 함수를 사용합니다. 인벤토리의 기존 ESC 정책을 먼저 존중하고, 시설 강화 패널이 열려 있으면 패널부터 닫습니다. Windows 전역 종료 단축키는 훈련장이 활성화된 동안 양보합니다.

## 재사용한 시스템

- 플레이어: 기존 `Assets/Player/SavePlayerRig.prefab`. 살아 있는 플레이어를 우선 사용합니다. 직접 씬 실행 시에만 없으면 프리팹 하나를 생성합니다.
- 이동·입력·공격: `PlayerMovement2D`, `PlayerInputReader`, `PlayerMeleeAttack`, 기존 Animator의 타격 시점과 공격 효과를 유지합니다.
- 광역 공격: 기존 `PlayerSkills.ShockwaveDamage` 계산과 MP 소모는 보관합니다. 현재 E/V 입력은 `PlayerGrenadeSkill`의 수류탄이며, 실제 폭발이 기존 피격 인터페이스로 최종 데미지 24를 전달합니다.
- 장착 데미지: `PlayerMeleeAttack.ResolvedDamage`와 `HideOutPlayerBridge.MeleeDamageBonus`를 그대로 사용합니다. 장착 무기, 성장 공격력, 기존 무기 ID별 허수아비 강화 보너스를 포함합니다.
- 카메라: `PlayerCameraFollow2D`에 훈련장 범위를 설정합니다. 선택적인 약한 흔들림은 훈련장 카메라에만 적용합니다.
- 강화: 기지에서 쓰던 실제 `Dummy Bottom Panel`을 훈련장 Canvas로 옮겨 사용합니다. 씬 직접 실행에서는 같은 패널 생성 함수를 사용합니다. 비용·재료·강화 처리와 보너스 저장은 기존 `HideOutPlayerBridge.UpgradeDummy()`가 소유합니다.
- 폰트: 기존 한글 폰트와 TMP 공유 Material을 사용합니다. 원본 폰트·Material을 수정하지 않습니다.

## 피격과 크리티컬

`HideOutDummy`는 기존 `IMeleeDamageReceiver`를 확장한 `ICombatHitReceiver`를 구현합니다. `CombatHit`는 **확정된 최종 데미지와 실제 크리티컬 여부**를 전달하는 값입니다. 기존 정수 데미지 수신도 지원합니다.

근접과 충격파는 동일한 수신 대상의 여러 Collider를 한 공격 안에서 한 번만 처리합니다. 공격마다 집합을 비우므로 기존 연속 스윙·다단히트 시점은 유지합니다. 허수아비에는 HP, 적 AI, 사망 처리, 보상·퀘스트 처치 호출이 없습니다. HP 잔량과 무관하게 전달받은 최종 숫자를 표시합니다.

현재 프로젝트에는 실제 크리티컬 추첨·판정이 없습니다. 근접·보관된 충격파·새 수류탄 폭발은 `IsCritical=false`를 전달합니다. 향후 실제 판정기가 생기면 `ReceiveHit(new CombatHit(finalDamage, actualCritical))`로 전달하면 됩니다. 연출이 크리티컬을 추첨하지 않습니다. 자동 검사의 큰 크리티컬 숫자는 이 연결 지점에 명시적인 결과를 전달한 검사이며 실제 공격의 크리티컬 발생을 검증했다는 뜻이 아닙니다.

## 연출 설정

씬의 `Training Dummy - single damage receiver`에서 `ScareCrowHitFeedback`을 선택합니다.

- `strongDamageThreshold`: 기본 **20**. 기본 근접 10 + 등록된 연습 무기 공격 보너스 3 + 기존 강화 보너스와 성장 공격력을 조사했습니다. 기존 충격파 배율은 1.5입니다. 기본 장착 근접 13과 충격파 약 20을 구분하기 위한 임시 절대 기준입니다. 데미지 계산에는 관여하지 않습니다.
- `normal / critical / strong`: 색상, 폰트 크기, 최종 배율, 팝업 배율, 지속 시간, 상승 거리.
- `horizontalSpread`, `shadowOpacity`: 숫자 분산 범위와 그림자.
- `reactionDuration`, `swayDegrees`, `flashStrength`, `sparkDuration`, `sparkColor`: 외형 흔들림·밝아짐·스파크.
- `hitClip`, `hitVolume`: 효과음 연결 지점. 프로젝트에서 WAV·OGG·MP3를 발견하지 못해 기본 Clip은 비워 두었습니다.
- 훈련장 카메라의 `ScareCrowCameraImpact`: `enableShake` 기본 꺼짐, `strength` 기본 0.025, `duration` 기본 0.1초. 켜면 강한 타격·크리티컬에만 적용합니다.

숫자는 재사용 풀을 사용하며 한 타격당 하나를 만듭니다. 크리티컬과 강한 데미지가 겹치면 크리티컬 스타일에 약간의 크기 강조를 결합합니다. 몸통 반응은 Visual Root만 움직이고 피격 Collider는 고정합니다. 전역 timeScale, 공격 속도, 판정 시점을 변경하지 않습니다. 콤보 기능·게이지·DPS·보상을 추가하지 않았습니다.

## 임시 아트 교체

맵은 저장된 SpriteRenderer와 Collider로 구성되어 Play 전에도 보입니다. `Player Start`의 반투명 배치 표시는 런타임 플레이어가 아니며 Play 시 숨깁니다.

- `Background - replaceable`: 벽, 기둥, 배경 아트.
- `Decorations - replaceable`: 거치대, 상자, 조명. 전투 대상이 아닙니다.
- `Floor and safety boundaries`: 바닥·측면·천장 Collider를 유지하면서 바닥 SpriteRenderer를 교체합니다.
- `Training Dummy - single damage receiver/Visual Root - replaceable`: 나무·천 몸통·과녁 SpriteRenderer를 교체합니다. 부모의 `HideOutDummy`, Monster 레이어와 단일 피격 Collider를 유지합니다.
- `Impact sparks - replaceable`: 스파크 외형 교체. 부모 피격 Collider에 추가하지 않습니다.
- `Damage number anchor`, `Player Start`: 숫자 위치와 플레이어 시작 위치.
- `TrainingSquare.png`, `TrainingDisc.png`, `TrainingUnlit.mat`: 단순한 임시 형태와 기존 URP용 unlit 재질입니다.

`ScareCrowTrainingSetup`은 명시적 최초 구성 도구입니다. 이미 완성된 방이 있으면 덮어쓰지 않습니다. 아트 교체는 저장된 씬 오브젝트에서 진행합니다.

## 변경 파일과 검증

기존 파일 수정:

- `Assets/Scenes/HideOutSystemScene/Scripts/HideOutScene.cs`
- `Assets/Scenes/HideOutSystemScene/Scripts/HideOutEntrance.cs`
- `Assets/Scenes/HideOutSystemScene/Scripts/HideOutDummy.cs`
- `Assets/Scenes/HideOutSystemScene/HideOutSystemScene.unity` — 기존 플레이어 프리팹 참조.
- `Assets/Player/Script/PlayerMeleeAttack.cs`
- `Assets/Player/Script/PlayerSkills.cs` — 기존 미추적 사용자 파일을 백업한 뒤 피격 전달 부분만 추가.
- `Assets/Player/Script/PlayerMapBounds2D.cs` — 기존 경계 설정 조회·복원.
- `Assets/Player/Script/WindowsQuitOnEscape.cs`
- `Assets/Player/Script/SceneFadeTransition.cs` — 기존 페이드에 비동기 씬 준비 완료까지 검은 화면을 유지하는 경로 추가.
- `Assets/Scenes/HideOutSystemScene/Tests/Editor/HideOutPlayChecks.cs` — 허수아비 클릭의 변경된 동작 반영.
- `ProjectSettings/EditorBuildSettings.asset`

기존 미추적 `ScareCrowScene.unity`를 완성하고, 같은 폴더에 훈련장 제어·피드백·카메라·배치 표시 스크립트, 임시 Sprite와 Material, 이 안내를 추가했습니다. 새 Editor 도구는 `Assets/Editor/ScareCrowTrainingSetup.cs`와 `ScareCrowTrainingChecks.cs`이며 Unity가 생성한 `.meta`를 함께 유지합니다.

검증은 새 Unity를 실행하지 않고 기존에 열려 있던 **6000.3.23f1**에서 수행합니다. `Tools > HideOut > Verify ScareCrow training room`은 임시 런타임 세션에서 검사하며 실제 저장 파일을 쓰지 않습니다. 결과와 촬영 화면은 `Temp/ScareCrowValidation/`에 저장합니다. 기존 하이드아웃 회귀 검사는 `Temp/HideOutValidation/play-checks.txt`를 참고합니다.

2026-11-05 최종 실행 결과:

- Unity 에디터 컴파일과 저장된 씬 참조 확인 완료.
- 훈련장 Play 검사 **40개 통과, 런타임 오류 0개**: 두 진입점의 기존 카메라 이동·확대 선행, 완전히 검은 동안 씬 연결, 점진적 화면 복원, 중복 클릭, 플레이어 단일성, 상태 유지, 실제 이동과 공격 애니메이션, 최종 데미지, 중복 Collider, 후속 스윙, 강한 숫자, 크리티컬 정보 연결 지점, 외형 복원, 무보상, 기존 충격파, 강화 비용·보너스, 실제 ESC, 직접 씬 실행과 복귀.
- 기존 하이드아웃 Play 회귀 검사 **133개 통과**.
- Play 촬영 화면의 훈련장·숫자·기존 HUD 배치를 직접 확인.
- 작업 범위 C#의 `git diff --check` 통과. 전체 저장소 검사는 기존 `Forest.unity` diff의 공백 항목을 포함하므로 전체 통과라고 보고하지 않습니다.
- 기존 씬 `.meta` 바이트 일치, 무관한 기존 사용자 diff 보존, ProjectVersion·Packages 무변경 확인.

수정 전 백업은 `Temp/ScareCrowTrainingBackup-20261105/`입니다. 기존 사용자 변경은 유지하며 브랜치 `JungHO`를 바꾸지 않았습니다. `ProjectVersion.txt`, Packages, 원본 폰트와 사용자 작업을 변경하지 않았습니다. 실제 개별 무기 전체, 출시 빌드의 Windows ESC, 최종 아트·음원으로 느끼는 타격감은 별도 수동 확인 대상입니다.

2026-11-06 은신·수류탄 통합 후 훈련장 검사 결과는 **48개 통과, 런타임 오류 0개**입니다. 실제 은신 공격 보너스, 수류탄 폭발 숫자 24, 잔여 개수 저장 연결과 기지 왕복 후 보존을 추가로 확인했습니다. 세부 밸런스와 조작 방법은 `Assets/Player/PlayerSkillsNotes.md`를 참고합니다.
