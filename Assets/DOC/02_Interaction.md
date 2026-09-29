# 상호작용 및 프롬프트 시스템

## 동작 흐름

`F` 입력 → `PlayerInteractionController`가 가장 가까운 `Interactable2D` 탐색 → 대상의 `Interact(GameObject)` 실행 → Inspector `onInteract` 이벤트 및 같은 오브젝트의 컷신/대화 실행

플레이어 주변에 대상이 있으면 `InteractionPromptUI`가 활성화되고 대상의 상단으로 이동해 `[F] Interact`를 표시합니다.

## 컴포넌트 설정

### 플레이어

`PlayerInteractionController`는 플레이어의 `BoxCollider2D`와 `PlayerInputReader`가 필요합니다.

- `rangeMultiplier`: 플레이어 콜라이더 크기에 곱할 탐색 범위입니다. 기본값은 `1.5`입니다.
- `interactionLayers`: 탐색할 물리 레이어입니다.
- `interactionPrompt`: 장면의 `InteractionPromptUI` 참조입니다.
- `logSuccessfulInteractions`: 성공한 상호작용을 Console에 기록할지 결정합니다.

선택 상태에서 Scene 뷰에는 초록색 Gizmo로 실제 탐색 박스가 표시됩니다. 여러 대상이 범위에 있으면 플레이어와의 제곱 거리가 가장 짧은 대상을 우선합니다.

### 상호작용 대상

대상 GameObject 또는 부모 오브젝트에 `Interactable2D`를 추가하고, 감지될 `Collider2D`를 배치합니다. 데모의 `CutScen`, `conversation`은 `BoxCollider2D`가 Trigger로 설정된 예시입니다.

`Interactable2D`는 다음을 순서대로 처리합니다.

1. Inspector의 `onInteract` UnityEvent 호출
2. 같은 오브젝트의 `CutsceneVideoSequence.PlayFromInteraction()` 호출
3. 같은 오브젝트의 `ConversationDialogue.Play()` 호출

따라서 일반 이벤트, 컷신, 대화를 한 대상에 조합할 수 있습니다. 컷신 후 대화를 원하면 대상의 `ConversationDialogue`를 자동으로 함께 붙이지 말고, 컷신 컴포넌트의 `dialogueAfterVideo`에 연결합니다.

### 프롬프트 UI

`InteractionPromptUI`에는 표시할 TMP 텍스트를 연결합니다. 대상에 Collider가 있으면 콜라이더 상단에서 `heightOffset`만큼 위에, 없으면 Transform 위치에서 위로 이동합니다. 실제 데모 씬의 UI 오브젝트는 `Interaction Prompt UI (TMP)`입니다.
