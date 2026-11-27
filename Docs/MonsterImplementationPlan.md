# 몬스터 구현 계획 — 살쾡이 1종 (BehindTheGear)

> 작성일: 2026-11-26 · 브랜치 `JungHO` (HEAD `5c05012`) 기준
> 이전 8종 계획(살쾡이·갱단A/B·두목·산짐승·군인)은 폐기하고, **살쾡이 잡몹 1종**부터 다시 시작한다.
> 상태 구분: **[사실]** 코드/씬에서 확인 · **[결정]** 사용자와 합의 · **[미검증]** Unity Play Mode 확인 전

---

## 1. 기존 구조 [사실]

- 몬스터 로직은 `Assets/Monster/Scen/MonsterFollowController.cs` 하나가 전부 담당한다 (순찰·추적·공격·피격·넉백·사망·드롭).
- 근접 공격, 수류탄, 충격파, 독 단검이 모두 `MonsterFollowController`를 직접 찾으므로 **새 몬스터도 이 컴포넌트를 유지**한다.
- 애니메이션은 상태 이름 `Idle / Walk / Attack / Damaged / Dead`로 재생하고, 클립 길이도 클립 이름으로 찾는다. 공격 판정은 `Attack` 클립의 `ApplyAttackHit` 이벤트에서 일어난다.
- 스프라이트 기본 방향은 **왼쪽**(오른쪽으로 갈 때 `flipX`). 기존 몬스터 스프라이트 규격: 48×48, PPU 11, Point 필터.
- Forest의 `Monster_1`: 루트에 BoxCollider2D 3개(몸통 4×4, 인식 12×8, 공격 6×4), Rigidbody2D(중력 3), 스케일 0.9.
- 플레이어 기준값: 달리기 7 / 걷기 5.25 / 공격력 10 / HP 10 / 방어 11. 받는 피해 = `max(1, 공격력 − 방어)`.

## 2. 살쾡이 컨셉 [결정]

- 아키타입: **돌진 맹수형(사족보행)**. 폐허 숲(쓰러진 가로등·고사목)에서 떼로 다니는 굶주린 살쾡이.
- 실루엣: 낮게 웅크린 네발, 마른 몸, 스라소니 귀 깃털, 빛나는 노란 눈, 공격 시 발톱 잔상(스미어).
- 털 색: **녹슨 주황**(무채색 숲 배경에서 잘 보이도록 채도를 높임). 처음 흙갈색은 배경에 묻혀 교체.
- 성격: **빠르고, 약하고, 체력이 낮고, 공격이 잦고, 무리로 나온다.**

## 3. 수치 (`Assets/Resources/Monsters/Wildcat.asset`, Inspector에서 조절)

| 항목 | 값 | 참고 |
|---|---|---|
| HP | 10 | 기본 공격 10 → **한 대에 사망** (단검 4는 3번) |
| 공격력 | 8 | 현재 공식으로 1 피해 |
| 이동속도 | 5.5 | 걷는 플레이어보다 빠르고, 달리는 플레이어보다 느림 |
| 공격 후 휴식 | 0.6초 | 공격 클립 0.4초 + 0.6초 = 약 1초 주기 (갱단B 약 1.57초) |
| 돌진 | 4.5칸 안에서 전조 없이 속도 12로 0.22초, 쿨타임 2.5초 | 피격 시 즉시 취소 |
| 배치 | Forest에 **4마리** 랜덤 배치 (`fieldCount`) | 갱단과 함께 컷신 전에 미리 서 있고, 갱단 AI가 켜질 때 같이 켜짐. 갱단 주변 ±3칸(`fieldSpread`), 플레이어와 최소 10칸(`fieldMinPlayerDistance`), 서로 1.6칸 이상. 개체별 속도 ±10% |
| 크기 | 0.7배 (`visualScale`) | Forest 몬스터(0.9) 기준이라 실제 0.63. 콜라이더도 함께 줄어듦 |
| 그리기 순서 | `sortingOrder` 10 | 갱단B(1)보다 앞. 작은 몬스터일수록 큰 값을 줘서 앞에 보이게 |
| 간격 유지 | `keepSpacing`, 간격 1.6 | 플레이어 쪽 앞에 동료가 1.6 안에 있으면 멈춰 줄을 섬. 몬스터끼리 몸통 충돌 무시(밀치기·엉킴 방지) |
| 체력바 | 머리 위 12×1 픽셀 + 1px 테두리, 조명 영향 없음 (`showHealthBar`) | 맞으면 밝은 잔상이 0.35초 남았다가 줄어듦, 컷신 중(AI 꺼짐)·사망 시 숨김 |
| 몸통 / 공격 범위 | 2.6×1.8 (오프셋 y −1.1) / 4×3 (오프셋 y −0.6) | 바닥 높이는 갱단B와 동일 |

## 4. 구현 내용 [결정]

| 파일 | 내용 |
|---|---|
| `Assets/Monster/Scen/MonsterProfile.cs` | 몬스터별 수치·돌진·애니메이터·충돌 크기·무리 설정을 담는 ScriptableObject |
| `Assets/Resources/Monsters/Wildcat.asset` | 살쾡이 프로필 |
| `Assets/!Animation/Wildcat/` | 임시 도트 스프라이트 5종(Idle 4f·Walk 4f·Attack 3f·Damaged 2f·Dead 3f), 클립 5개, `Wildcat.overrideController`(갱단B 상태머신 재사용) |
| `MonsterFollowController.cs` | 돌진 옵션 추가(**기본 꺼짐** → 기존 몬스터 영향 없음), `ApplyProfile()` 추가 |
| `Assets/Monster/Scen/MonsterHealthBar.cs` | 몬스터 머리 위 체력바 (프로필에서 켠 몬스터에만 추가) |
| `Assets/Editor/MonsterHealthBarTools.cs` | **Tools > Behind The Gear > Monster > Add / Remove Health Bars** — 프리팹·씬·프로필의 모든 몬스터에 체력바 일괄 추가/제거 (새 몬스터 추가 후 다시 누르면 됨) |
| `MonsterDamageNumbers.cs` / `DamageNumberStyle.cs` | 모든 몬스터 피격 시 머리 위 가로 중앙에 피해 숫자 표시. 피해가 클수록 크림→노랑→주황→빨강, 크기·튀어오름도 커짐 (4/10/18/30 기준, `Resources/Monsters/DamageNumberStyle.asset`에서 조절). 독 도트는 작은 연두색. 표시 전용, 전투 수치 변화 없음 |
| `WildcatFieldSpawner.cs` | Forest 로드 3번째 프레임(스토리가 몬스터 목록을 만든 직후)에 살쾡이를 AI 꺼진 채 배치, 스토리 몬스터가 켜지면 같이 켬. 포탈 조건·필드 저장에서는 제외(매 방문마다 새로 배치), 챕터 4처럼 몬스터가 꺼진 Forest에는 배치 안 함 |
| ~~`MonsterTestSpawner.cs`~~ | 7키 테스트 소환 — **삭제** |
| ~~`Assets/Editor/MonsterTestSpawnerChecks.cs`~~ | 7키 테스트 소환 자동 검사 — **삭제** |

테스트 소환 몬스터는 퀘스트 처치 수·드롭·필드 저장에서 제외된다.

## 5. 처치 보상 — 코인 · 기지 부품 · AP [결정, 미검증]

몬스터 등급(`MonsterRank`)별로 `Assets/Resources/Monsters/RewardTable.asset`의 줄을 굴린다. 기존 붉은 천 + 1% 장비 드롭은 그대로 두고 그 위에 추가.

| 등급 | 코인 | 기지 부품 | AP |
|---|---|---|---|
| 물량 잡몹 (Swarm) — 살쾡이 | 10% · 1 | 1% · 1개 | 1% · 1 |
| 잡몹 (Common) — 갱단 | 1~2 | 3% · 1개 | 25% · 1 |
| 중간 몹 (Elite) | 4~7 | 30% · 1개 | 15% · 1~2 |
| 중간 보스 (MiniBoss) | 12~20 | 100% · 1~2개 | 50% · 2~3 |
| 보스 (Boss) | 30~50 | 100% · 3~5개 | 100% · 3~5 |

- 등급: `MonsterProfile.rank` 또는 몬스터 Inspector의 `Drops > Rank`. 기본값은 잡몹, 살쾡이는 물량 잡몹.
- 코인·AP: 튀어올라 땅(Ground 레이어)에만 떨어지고, 세로축으로 빙글빙글 돈다. 자석 없음 — 플레이어가 닿아야 획득. 60초 뒤 깜빡이다 사라짐. 코인이 많으면 최대 8개로 나눠 떨어짐. 획득 시 "+N" / "AP +N" 표시.
- 기지 부품: 기존 아이템 픽업(`WorldItemPickup`)으로 인벤토리에 들어감 (`hideout_scrap`, 시설 강화 재료).
- 코인·AP는 플레이어의 `PlayerFieldWallet`에 모였다가 기지 데이터(`HideOutPlayerBridge`)에 바로 더해진다. 아직 기지에 한 번도 안 간 챕터 1 초반엔 지갑에 보관됐다가 첫 기지 진입 시 합산. 새 게임/메뉴 복귀 시 미반영분은 사라짐.
- 테스트 소환 몬스터는 보상 없음.

| 파일 | 역할 |
|---|---|
| `MonsterRewardTable.cs` + `RewardTable.asset` | 등급 enum과 등급별 보상표 |
| `MonsterRewards.cs` | 사망 시 굴려서 픽업 생성 |
| `RewardPickup.cs` | 코인/AP 픽업 (도트 스프라이트는 코드로 생성) |
| `PlayerFieldWallet.cs` | 필드에서 주운 코인/AP → 기지 데이터 |

아직 없음: 필드 화면의 코인 HUD, 정식 코인/AP 아이콘 아트, 중간 몹·보스 몬스터 자체.

## 6. 남은 일

1. **[미검증]** Unity 6000.3.23f1에서 컴파일, Forest 전투 시작 시 5마리 배치, 돌진·공격·피격·사망 애니메이션, 수류탄·독 단검·충격파 피격 확인.
2. 임시 도트를 정식 아트로 교체 (같은 파일 이름·48×48 프레임 유지 시 클립 수정 불필요).
3. 밸런스: 피해 최소값 1 + 플레이어 HP 10이라 3마리에게 둘러싸이면 초당 약 3 피해. 체감 후 휴식 시간·무리 수 조정.
4. Forest 외 다른 맵 배치, 살쾡이 처치를 Forest 클리어 조건에 넣을지는 다음 단계에서 결정.
