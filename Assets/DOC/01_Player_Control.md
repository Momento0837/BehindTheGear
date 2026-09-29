# 플레이어 입력 및 이동 시스템

## 구성

플레이어 오브젝트에는 아래 컴포넌트를 함께 부착합니다.

- `Rigidbody2D`, `BoxCollider2D`
- `PlayerInputReader`
- `PlayerMovement2D`
- `PlayerSpriteFacing` (스프라이트 좌우 반전이 필요할 때)
- `PlayerAttackController` (공격 이벤트 연결이 필요할 때)

`PlayerMovement2D`는 `Rigidbody2D`, `BoxCollider2D`, `PlayerInputReader`를 요구하며, `PlayerSpriteFacing`과 `PlayerAttackController`는 `PlayerInputReader`를 요구합니다.

## 기본 조작

| 입력 | 기능 |
| --- | --- |
| `A` / `←` | 왼쪽 이동 |
| `D` / `→` | 오른쪽 이동 |
| `W` / `↑` / `Space` | 점프 |
| `S` / `↓` | 앉기 |
| 앉은 상태에서 점프 입력 | 일방향 발판 아래로 통과 |
| `Z`, `X`, `C`, `V` | 각각의 공격 UnityEvent 호출 |
| 마우스 좌·우 클릭 | 각각의 공격 UnityEvent 호출 |

## 역할 분리

### PlayerInputReader

매 프레임 키보드/마우스를 읽고 동작을 직접 수행하지 않습니다. 대신 `MoveChanged`, `CrouchChanged`, `JumpPressed`, 공격 이벤트, `InteractPressed` 이벤트를 발행합니다. 따라서 입력 장치를 바꾸더라도 각 게임 기능을 독립적으로 확장할 수 있습니다.

### PlayerMovement2D

- `FixedUpdate`에서 수평 속도와 점프를 물리에 적용합니다.
- `BoxCollider2D.Cast`로 아래 방향 접지를 검사합니다.
- 앉는 동안 콜라이더 높이를 절반으로 줄이고, 중심을 아래로 이동해 바닥 위치를 유지합니다.
- `PlatformEffector2D`가 달린 발판과는 설정된 시간 동안 충돌을 무시하여 아래로 내려갑니다.

주요 Inspector 값은 `moveSpeed`, `jumpVelocity`, `crouchSpeedMultiplier`, `groundLayers`, `oneWayPlatformLayers`, `groundCheckDistance`, `dropThroughDuration`입니다. `oneWayPlatformLayers`가 비어 있으면 모든 레이어에서 일방향 발판을 탐색합니다.

### PlayerSpriteFacing

수평 이동 방향이 바뀔 때만 `SpriteRenderer.flipX`를 갱신합니다. 정지 중에는 마지막으로 바라보던 방향을 유지합니다.

### PlayerAttackController

입력 리더의 공격 이벤트를 Inspector의 여섯 개 `UnityEvent`로 전달합니다. 현재 데모 씬에서는 이벤트가 비어 있으므로 입력만 받아도 게임플레이 효과는 발생하지 않습니다.

## 데모 씬 설정

`Assets/Player/PlayerMovementDemo.unity`의 `Temporary Player`는 이동 속도 `7`, 점프 속도 `15`로 구성되어 있습니다. 같은 씬의 `One Way Platform (S+W to drop)`에는 `PlatformEffector2D`가 붙어 있어 앉은 상태에서 점프 입력으로 통과할 수 있습니다.
