# Hierarchy 에디터 보조 기능

## 개요

`Assets/Editor/HierarchyScriptControls.cs`는 Unity Editor 전용 기능입니다. 빌드에는 포함되지 않으며 Hierarchy 각 오브젝트 오른쪽에 상태와 조작 버튼을 추가합니다.

## 제공 기능

- 의미 배지와 아이콘 표시
  - `PLAYER`: `PlayerMovement2D`
  - `UI`: `Canvas`
  - `ONE-WAY`: `PlatformEffector2D`
  - `CAMERA`: `Camera`
  - `2D PHYSICS`: `Rigidbody2D` 또는 `Collider2D`
- `Object: ON/OFF`: GameObject 전체 활성화/비활성화
- `Scripts: ON/MIX/OFF`: 해당 오브젝트의 사용자 `MonoBehaviour` 전체 활성화/비활성화

## 동작 특성

- 버튼 조작은 Unity Undo에 기록되어 `Ctrl+Z`로 되돌릴 수 있습니다.
- `Scripts` 버튼은 `Assets/` 아래에 있으며 `Editor` 폴더 밖에 있는 사용자 스크립트만 대상으로 합니다.
- Hierarchy 변경 시 오브젝트 아이콘을 갱신합니다.

이 기능은 `[InitializeOnLoad]`로 자동 등록됩니다. 별도의 컴포넌트 부착이나 씬 설정은 필요하지 않습니다.
