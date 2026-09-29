# 영상 컷신 시스템

## 기능

`CutsceneVideoSequence`는 상호작용 대상으로부터 `PlayFromInteraction()`을 호출받아 연결된 `VideoPlayer`를 재생합니다.

- 영상 재생 전 플레이어 조작을 잠급니다.
- 지정 UI를 숨기고 영상을 카메라 Near Plane에 표시합니다.
- 영상 끝까지 재생하거나 `Space`를 기본 3초간 누르면 종료합니다.
- 종료 후 UI와 플레이어 조작을 복구합니다.
- `dialogueAfterVideo`가 연결되면 영상 종료 후 대화를 시작합니다.

## Inspector 연결

| 항목 | 용도 |
| --- | --- |
| `videoPlayer` | 재생할 `VideoPlayer` |
| `uiRootsToHide` | 영상 중 숨길 UI 루트들 |
| `playerControlLock` | 컷신 중 조작을 잠글 컴포넌트. 비어 있으면 씬에서 자동 탐색 |
| `dialogueAfterVideo` | 종료 후 시작할 `ConversationDialogue` (선택) |
| `skipHoldSeconds` | 건너뛰기에 필요한 Space 길게 누르기 시간 |

`VideoPlayer`에는 `VideoClip` 또는 URL 중 하나를 지정해야 합니다. 둘 다 비어 있으면 경고를 출력하고 재생하지 않습니다. 준비가 10초를 초과하면 오류를 출력하고 UI·조작 상태를 복구합니다.

## 상호작용 대상 만들기

1. 씬 오브젝트에 `Collider2D`와 `Interactable2D`를 붙입니다.
2. 같은 오브젝트에 `CutsceneVideoSequence`와 `VideoPlayer`를 붙입니다.
3. 컷신 컴포넌트의 `videoPlayer`에 해당 VideoPlayer를 연결합니다.
4. 필요 시 숨길 Canvas/UI 루트와 `PlayerControlLock`을 연결합니다.
5. 영상 뒤 대화가 필요하면 `dialogueAfterVideo`에 대화 컴포넌트를 연결합니다.

`Interactable2D`가 같은 오브젝트의 컷신을 자동 호출하므로 별도의 UnityEvent 연결은 필수는 아닙니다. 데모 씬의 `CutScen` 오브젝트는 이 구조와 VideoClip 연결을 갖춘 예시입니다.

## 주의사항

컷신 대상에 `ConversationDialogue`도 함께 붙어 있으면 상호작용 직후 대화가 바로 시작될 수 있습니다. **영상 종료 뒤** 대화를 재생할 목적이라면 대화 컴포넌트는 별도 오브젝트에 두고 `dialogueAfterVideo`에 연결합니다.
