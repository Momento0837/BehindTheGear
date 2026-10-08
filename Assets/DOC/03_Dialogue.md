# JSON 대화 시스템

## 기능

`ConversationDialogue`는 Inspector에 연결한 기존 Canvas/UI만 이용해 JSON 대화를 표시합니다. 대화 중에는 `PlayerControlLock`을 획득해 플레이어 조작을 잠그며, 종료 시 해제합니다.

- 글자가 `characterInterval` 간격으로 한 글자씩 출력됩니다.
- 출력 중 `Space`, `Enter`, 마우스 왼쪽 클릭: 현재 문장을 즉시 모두 표시합니다.
- 문장이 모두 표시된 뒤 화면의 `다음` 버튼, `Enter`, `Space`, 마우스 왼쪽 클릭: 다음 문장으로 진행합니다.
- 마지막 문장에서는 버튼이 `닫기`로 바뀌며, 입력하면 대화를 종료합니다.
- `Finished` 이벤트를 구독해 대화 종료 후 동작을 추가할 수 있습니다.

퀘스트 NPC는 먼저 의뢰 대화를 재생하고, 대화가 끝난 뒤 수락/거절 선택지를 표시합니다. 수락하면 안내 대사를 모두 마친 다음 퀘스트 아이템을 생성합니다.

## JSON 형식

대화 파일은 `TextAsset`으로 지정합니다. 현재 예시는 `Assets/Resources/Dialogue/cutscene_conversation.json`입니다.

```json
{
  "lines": [
    { "speaker": "Ethan", "text": "대사 내용" }
  ]
}
```

`speaker`와 `text`는 문자열입니다. 코드에는 향후 초상화 확장을 위한 `portrait` 필드가 있으나, JSON에서 Sprite를 직접 참조하는 방식은 아직 구현되어 있지 않습니다.

## Inspector 연결

| 항목 | 용도 |
| --- | --- |
| `dialogueJson` | 대화 JSON TextAsset |
| `playerControlLock` | 대화 중 조작 잠금. 비어 있으면 씬에서 자동 탐색 |
| `dialogueCanvasRoot` | 대화 동안만 켜는 부모 Canvas |
| `dialogueUiObjects` | 대화 중 켜고 종료 시 끄는 개별 UI |
| `dialoguePanelImage`, `portraitImage` | 선택 사항인 배경/초상화 Image |
| `speakerText`, `bodyText` | 화자명과 본문 TMP 텍스트 |
| `continueIndicator` | 문장 완료 후 표시할 안내 오브젝트 |

화자명 텍스트를 연결하지 않으면 화자 이름이 본문 첫 줄에 자동으로 포함됩니다. Canvas 부모가 비활성화된 경우에도 재생할 때 먼저 활성화해 UI가 정상 표시됩니다.

## PlayerControlLock 설정

플레이어에 `PlayerControlLock`을 추가하고 다음을 지정합니다.

- `controlsToDisable`: 잠글 조작 Behaviour (예: `PlayerInputReader`, `PlayerMovement2D`, `PlayerInteractionController`, `PlayerAttackController`)
- `playerBody`: 정지시킬 `Rigidbody2D`

잠금은 카운트 방식입니다. 대화와 컷신이 동시에 잠금을 요청해도 마지막 요청이 해제될 때까지 조작이 유지됩니다. 오브젝트가 비활성화되면 안전하게 잠금을 초기화하고 등록된 컴포넌트를 다시 활성화합니다.
