# 독 단검 투척 스킬

## 입력과 기존 기능 보존

- 기본 E: 독 단검 세 개를 동시에 발사. 방향은 현재 키보드 방향 입력을 정규화하며, 입력이 없으면 마지막 방향을 유지한다. 방향 입력과 E를 같은 프레임에 눌러도 새 방향을 사용한다.
- 방향키 조작에서는 E와 기존 V를 같은 투척 입력으로 사용한다. 두 키를 함께 눌러도 한 번만 발동한다.
- K: 에디터와 Development Build에서만 개발용 스킬 실험실을 열고 닫는다. 투척(독 단검/수류탄), 근접·보조(은신/기존 충격파)를 각각 선택한다. 장착 후 K 또는 닫기로 닫으면 해당 스킬 키로 사용한다. 일반 빌드에는 이 메뉴를 자동 추가하지 않는다.
- 수류탄 선택 시 기존처럼 E/V를 누르고 조준하고 놓아서 투척한다. `PlayerGrenadeSkill`, `PlayerGrenadeProjectile` 원본 구현과 저장 참가자, 탄약, 자해 정책을 유지한다. 스킬 교체는 탄약이나 쿨타임을 초기화하지 않는다.
- 기존 이동·점프·Ctrl 걷기·Shift 대쉬와 근접 공격의 입력 및 물리 코드를 교체하지 않는다. 현재 프로젝트는 횡방향 이동과 점프를 사용하는 구조이므로 W/위쪽 방향 입력은 기존 점프와 위쪽 조준에 함께 사용된다.

## 연결과 인스펙터

기존 씬 플레이어와 Continue 플레이어 모두 `PlayerAttackController`가 `PlayerPoisonDaggerSkill`을 자동 연결한다. 해당 컴포넌트의 Settings가 비어 있으면 `Resources/PlayerSkills/PoisonDaggerSettings`를 읽는다. 다른 설정 에셋을 직접 연결해도 된다.

Project 창에서 `Assets/Resources/PlayerSkills/PoisonDaggerSettings.asset`를 선택해 수정한다. 수치는 모두 float이다. 쿨타임은 다음 사용부터, 속도·피해·수명·편대 설정은 다음 발사부터 적용된다. 비행 중인 단검은 발사 당시의 값을 유지한다.

| 항목 | 적용 값 |
|---|---:|
| cooldown | 7 |
| projectileSpeed | 12 |
| spawnOffset | 0.6 |
| centerLeadDistance | 0.4 |
| sideOffset | 0.35 |
| hitDamageMultiplier | 0.4 |
| poisonDuration | 4 |
| poisonTickInterval | 1 |
| poisonDamageMultiplier | 0.1 |
| cameraDespawnMargin | 3 |
| maxLifetime | 5 |
| daggerLength | 0.75 |
| hitboxWidth | 0.12 |
| spriteForwardAngle | 45도 |

`projectilePrefab`은 같은 폴더의 `PoisonDaggerProjectile.prefab`에 연결되어 있다. `daggerSprite`는 사용자가 제공한 PNG의 단일 스프라이트이며 원본 PNG 바이트는 변경하지 않았다. 투명 여백은 임포트 사각형에서만 제외하고 Point 필터를 사용한다. 원래 이미지의 날끝이 우상단이므로 Visual을 -45도 보정한 뒤 발사 방향으로 회전한다. 짧은 초록색 궤적은 `showTrail`로 끌 수 있다.

## 충돌 기준

- Enemy Layers 기본값: Monster(9). 부모 `MonsterFollowController` 또는 해당 레이어의 부모 `IMeleeDamageReceiver`를 찾는다. 플레이어 체력 컴포넌트/Player 레이어와 존재하는 Ally/Friendly 레이어는 제외한다.
- Obstacle Layers 기본값: Ground(6), Interactable(8). 트리거는 제외한다.
- 현재 Ground 레이어는 바닥과 벽을 함께 사용한다. `ignoreWalkableGround`가 켜져 있으면 발사 당시 발밑 이하의 넓고 낮은 Ground 슬래브와 `PlatformEffector2D` 발판을 바닥으로 취급한다. 세로 벽과 나머지 고체 장애물은 막는다.
- 구분이 애매한 개별 지형에는 `PoisonDaggerSurface2D`를 추가해 Floor 또는 Obstacle을 명시할 수 있다. 다른 무기/플레이어의 물리 충돌은 바꾸지 않는다.
- 빠른 비행은 이동 구간의 캡슐 형상을 원 스윕으로 검사한다. 적의 BoxCollider는 후보 검색에 사용하고, 적중 확정에는 기존 PolygonCollider 또는 스프라이트의 임포트된 물리 외곽/메시를 재사용한다. 픽셀별 런타임 검사는 하지 않는다. 기존 콜라이더 교체나 전역 레이어 충돌 변경은 없다.
- 기존 `PlayerMapBounds2D`의 수평 경계, 카메라 가시 범위에 여유 거리 및 단검 반길이를 더한 범위, 최대 수명으로 제거한다. 맵 경계와 카메라가 없어도 수명 제한은 적용된다.

## 피해와 독

공격력은 기존 `PlayerMeleeAttack.ResolvedDamage`를 발사 시점에 저장한다. 기존 체력 API가 int이므로 계수 적용 후 반올림하며, 양수 계수는 최소 1이다. 각 단검은 한 번만 직접 피해를 주고 소멸한다. 별도 추가 피해는 없다.

적 하나당 `PoisonStatus2D` 한 개와 틱 시계 하나를 사용한다. 기본 첫 피해는 적중 1초 뒤이며 4초 동안 총 네 번이다. 재적중하면 종료 시각만 현재부터 4초로 갱신하고 다음 틱 시각과 기존 간격을 유지한다. 최신 단검의 발사 당시 독 피해량이 이전 값을 대체하며 합산하지 않는다. 같은 프레임에 여러 단검이 맞아도 직접 피해는 각각, 독 틱은 하나다. 죽거나 비활성화된 대상에서는 종료한다.

## 검증 실행

이번 작업의 변경 파일은 다음과 같다. 각 Unity 에셋의 `.meta`도 함께 생성했다.

- 신규 런타임: `PlayerPoisonDaggerSkill.cs`, `PlayerPoisonDaggerProjectile.cs`, `PoisonDaggerSettings.cs`, `PoisonDaggerTarget.cs`, `PoisonStatus2D.cs`, `PoisonDaggerSurface2D.cs`, `PlayerSkillDevelopmentMenu.cs` (`Assets/Player/Script/`).
- 기존 연결 수정: 같은 폴더의 `PlayerInputReader.cs`, `PlayerSpriteFacing.cs`, `PlayerAttackController.cs`, `InGameSkillHUD.cs`.
- 신규 에셋: `Assets/Resources/PlayerSkills/PoisonDagger.png`, `PoisonDaggerProjectile.prefab`, `PoisonDaggerSettings.asset`.
- 에디터 도구: `Assets/Editor/PoisonDaggerAssetSetup.cs`, `PoisonDaggerChecks.cs`. 기존 `PlayerSkillChecks.cs`, `ScareCrowTrainingChecks.cs`에는 수류탄 검증 전에 수류탄을 명시적으로 장착하는 한 줄씩 추가했다.

`PoisonDaggerChecks`는 기존 Play 세션에서 `Temp/PoisonDaggerValidation/request.flag`가 생성되면 격리된 로컬 물리 씬과 임시 입력 장치로 실행한다. 실제 씬을 다시 열거나 저장 파일을 쓰지 않으며 완료 후 일시정지와 입력 설정을 복구한다. 결과와 편대/개발 메뉴 미리보기는 같은 Temp 폴더에 생성된다. 모든 실제 맵에서의 수동 플레이 확인은 별도로 수행한다.

실제 플레이어의 물리가 중단된 하이드아웃 상태에서만 실행하도록 제한한다. Unity 6000.3.23f1의 격리된 Play 씬에서 86개 검증을 통과했고 런타임 오류는 0개였다. 8방향·정지 발사, 편대 유지, 단검별 충돌, 자식 콜라이더 중복 차단, 큰 BoxCollider의 스프라이트 밖 통과, 직접 피해·발사 공격력 스냅샷·독 네 번·갱신 주기·비활성/체력 0 종료, 고속 벽/적 충돌, 바닥/센서/플레이어 통과, 맵/카메라/수명 제거, 설정 변경, 수류탄 입력·탄약·쿨타임 보존, K 메뉴 선택, 기존 달리기·걷기·대쉬·은신·기본 공격 입력을 확인했다. 체력 0 종료는 실제 퀘스트/드롭 상태를 건드리지 않기 위해 테스트 체력을 직접 0으로 만든 상태에서 확인했으며, 실제 사망 연출 전체를 재실행한 검증은 아니다.

전체 전투 맵의 모든 지형 조합, 실제 적별 사망 연출 및 일반 빌드 패키징은 이번 자동 검증에 포함하지 않았다. 기존 수류탄의 폭발 회귀 검증 전체도 재실행하지 않았으며, 이번에는 실제 E 조준·해제 투척과 교체 중 상태 보존을 확인했다.
