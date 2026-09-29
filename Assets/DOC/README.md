# BehindTheGear 개발 문서

이 폴더는 현재 프로토타입에 구현된 기능과 씬 연결 방법을 시스템 단위로 정리한 문서입니다.

## 구현 시스템

| 시스템 | 문서 | 핵심 스크립트 |
| --- | --- | --- |
| 플레이어 입력·이동 | [01_Player_Control.md](01_Player_Control.md) | `PlayerInputReader`, `PlayerMovement2D`, `PlayerSpriteFacing`, `PlayerAttackController` |
| 상호작용·프롬프트 | [02_Interaction.md](02_Interaction.md) | `PlayerInteractionController`, `Interactable2D`, `InteractionPromptUI` |
| 대화 | [03_Dialogue.md](03_Dialogue.md) | `ConversationDialogue`, `PlayerControlLock` |
| 영상 컷신 | [04_Cutscene.md](04_Cutscene.md) | `CutsceneVideoSequence`, `PlayerControlLock` |
| Unity 에디터 보조 | [05_Editor_Hierarchy.md](05_Editor_Hierarchy.md) | `HierarchyScriptControls` |

## 확인된 데모 씬과 의존성

- 기능 데모 씬: `Assets/Player/PlayerMovementDemo.unity`
- 메인 씬: `Assets/Scenes/Main/Main.unity`
- 대화 데이터: `Assets/Resources/Dialogue/cutscene_conversation.json`
- 입력: Unity **Input System** 패키지 (`com.unity.inputsystem`)
- UI: TextMeshPro 및 uGUI
- 영상: Unity VideoPlayer

## 공통 주의사항

- 스크립트는 필요한 컴포넌트를 `RequireComponent`로 명시하지만, Inspector 참조가 필요한 UI·컷신 항목은 씬에서 직접 연결해야 합니다.
- `PlayerControlLock`에는 비활성화할 조작 컴포넌트와 플레이어 `Rigidbody2D`를 지정합니다. 대화와 컷신이 겹쳐도 잠금 횟수를 관리해 먼저 끝난 시스템이 조작을 잘못 풀지 않게 합니다.
- 현재 전투 입력은 `UnityEvent`를 호출하는 연결 지점만 구현되어 있습니다. 실제 공격 판정·애니메이션·피해 처리는 별도 구현 대상입니다.
