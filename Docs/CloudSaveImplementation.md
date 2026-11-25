# 계정 클라우드 저장 (Cloud Save) — 구현 및 설정 안내

침대 수동 저장(`slot-1-v2.json`)을 **게임 계정**에 함께 보관해서, 다른 PC(다른 IP/환경)에서 같은 계정으로 로그인하면 진행이 그대로 이어지게 한다. Steam 계정 연동과 같은 사용감을 목표로 했지만, Steam Cloud는 Steamworks App ID(유료 등록)와 Steam 앱 관리 설정이 있어야 해서 **Unity Gaming Services(UGS)의 아이디/비밀번호 계정 + Cloud Save**를 사용한다. 둘 다 무료 등급으로 쓸 수 있다.

- 패키지 추가 없음: `Packages/manifest.json` 변경 0. 이미 들어 있는 `UnityWebRequest`로 UGS REST API를 직접 호출한다.
- 기존 저장 규칙 유지: 디스크 저장은 여전히 **침대에서만**. 게임이 불러오는 원본도 여전히 로컬 슬롯 파일이다. 클라우드는 그 파일의 사본을 올리고/받는 역할만 한다.
- Unity 버전·렌더 파이프라인·씬·프리팹·ProjectSettings는 변경하지 않았다.

## 1. 처음 한 번 해야 하는 설정 (Unity Dashboard) — 완료

에디터의 기존 Services 연결(`ProjectSettings.asset`의 `cloudProjectId: 7294680d-…`, 조직 `rkddbstjwns`)은 현재 계정으로 접근 권한이 없어서 쓰지 않는다. `ProjectSettings`는 건드리지 않고, 별도 클라우드 프로젝트 ID를 설정 에셋으로 지정한다.

- 클라우드 프로젝트: **BehindTheGear-Cloud** (Unity 계정 `misosky21c`의 조직), 환경 `production`
- Project ID: `8cd61a87-717f-4725-ba4f-75f9cced4d3a`
- `Assets/Resources/CloudSaveConfig.asset`의 `projectIdOverride`에 위 ID를 넣어 두었다. 이 값이 `Application.cloudProjectId`보다 우선한다.
- Dashboard에서 완료한 것: Player Authentication → Identity Providers → **Username Password** 추가, **Cloud Save** 설정 가이드 완료.

주의: 에디터 `Project Settings > Services`의 **New Link…**는 누르지 않는다. 누르면 팀 공용 `ProjectSettings.asset`이 바뀐다. 클라우드 저장은 에디터 Services 연결 없이 Project ID만으로 동작한다.

다른 Cloud 프로젝트로 바꾸려면 `CloudSaveConfig.asset`의 `projectIdOverride`만 바꾸면 된다. 바꾸면 기존 계정과 저장은 이어지지 않는다(프로젝트마다 계정이 따로다).

## 2. 플레이어 사용 흐름

1. 메인 메뉴 저장 화면 오른쪽 위 **`계정 연동하기`** 버튼 → 아이디/비밀번호 입력 → `계정 만들기` 또는 `로그인`.
   - 아이디: 영문 소문자·숫자·`. - @ _`, 3~20자 (대소문자 구분 없음)
   - 비밀번호: 8~30자, 대문자·소문자·숫자·특수문자 각 1개 이상 (UGS 규칙)
2. 로그인 직후 자동 동기화:
   - 계정이 비어 있고 이 PC에 저장이 있으면 → 계정에 올림
   - 이 PC에 저장이 없고 계정에 있으면 → 이 PC로 내려받음 (**다른 PC에서 이어하기**)
3. 이후 침대(세이브 포인트)에서 저장할 때마다 계정에도 조용히 자동 업로드. 기지 메시지는 `저장 완료` 하나만 뜨고, 업로드가 안 됐을 때만 `저장 완료 · 계정 업로드 실패` / `저장 완료 · 계정 확인 필요`가 붙는다.
   - 메뉴 오른쪽 위 버튼 상태: `계정 연동하기` → `연동 중...` → `계정 연동 완료`(3초) → `계정 연동됨`. 로그아웃 시 `로그아웃 완료`(3초). 선택이 필요하면 `계정 확인 필요`, 자동 로그인 실패 시 `계정 연결 실패`.
4. 같은 PC에서는 다음 실행 때 저장된 로그인 세션으로 자동 로그인 + 동기화한다 (비밀번호는 저장하지 않음).
5. 다른 PC에서 같은 계정으로 로그인 → 메뉴에서 자동으로 최신 진행을 내려받음 → `이어하기`.

## 3. 동기화 판정 규칙

시계(시간) 비교가 아니라 **내용 해시** 세 개로 판정한다. PC마다 시간이 틀려도 안전하다.

- `L`: 이 PC 슬롯의 SHA-256 (기존 envelope의 `sha256` 필드 그대로)
- `C`: 계정에 올라간 사본의 SHA-256
- `S`: **이 PC가** 이 계정과 마지막으로 맞춘 해시 (`CloudAccount/sync-<계정ID>.json`)

| 상황 | 동작 |
|---|---|
| L = C | 그대로 (동기화됨) |
| 이 PC에 저장 없음, S 없음 (새 PC) | 계정 → 이 PC 내려받기 |
| 계정에 저장 없음, S 없음 (처음 연동) | 이 PC → 계정 올리기 |
| L = S, C ≠ S (다른 PC에서 진행함) | 내려받기 |
| C = S, L ≠ S (이 PC에서 진행함) | 올리기 |
| 둘 다 바뀜 / 기록 없이 서로 다름 | **플레이어에게 선택** (`계정 기록 사용` / `이 PC 기록 사용` / `나중에`) |
| 이 PC에서 삭제한 슬롯 (S는 있음) | 몰래 되살리지 않고 선택 요청 |
| 손상·구버전(v1) 저장, 손상된 계정 사본 | 자동 덮어쓰기 금지, 선택 요청 (쓸 수 없는 쪽 버튼은 숨김) |

추가 안전장치:
- 업로드는 Cloud Save `writeLock`을 사용한다. 읽은 뒤 다른 PC가 먼저 써 버렸으면(409) 덮어쓰지 않고 다시 동기화를 요청한다.
- 게임 안(기지)에서는 **업로드만** 한다. 실행 중인 세이브를 몰래 바꾸지 않는다. 내려받기는 메인 메뉴에서만.
- 내려받기 전: 해시 검증 → 기존 `ManualSaveStore.Read` + `HideOutPlayerBridge.ValidateSnapshot`으로 이어하기와 동일한 검증 → 기존 슬롯을 `CloudAccount/backups/`에 최근 5개까지 백업 → 임시 파일 flush 후 `File.Replace`(기존 `.bak` 유지). 실패하면 기존 슬롯은 그대로다.
- 메뉴에서 `저장 삭제` 시 로그인 상태면 문구가 "이 PC와 계정에 보관된 저장 데이터를 모두 삭제"로 바뀌고 계정 사본도 지운다.
- 동기화 중에는 `이어하기`/`새 게임`/`저장 삭제`를 막고 안내한다.
- 에디터 저장 검사(`Tools > Manual Save > …`)가 슬롯을 `Temp`로 돌리는 동안에는 클라우드 기능이 완전히 꺼진다 (검사 데이터가 실제 계정에 올라가지 않음).

## 4. 파일

새 파일 (`Assets/Scenes/Controls/SaveSystem/Cloud/`):

| 파일 | 역할 |
|---|---|
| `CloudSaveConfig.cs` | 선택 설정 ScriptableObject (프로젝트 ID 덮어쓰기, 저장 키, 타임아웃, 메뉴 자동 동기화) |
| `CloudHttp.cs` | UnityWebRequest JSON 호출, 오류 → 한국어 안내 |
| `CloudRunner.cs` | 정적 서비스용 숨은 코루틴 실행기 (씬 이동으로 파괴돼도 다음 메뉴에서 자동 복구) |
| `CloudAccountService.cs` | 회원가입/로그인/세션 토큰 갱신/로그아웃, 세션 파일 `CloudAccount/session.json` |
| `CloudSaveSync.cs` | 판정 규칙, 업로드/다운로드/삭제, 백업, 이 PC의 동기화 기록 |
| `CloudAccountPanel.cs` | 메뉴 오른쪽 위 계정 버튼 + 로그인/동기화/충돌 선택 대화상자 (기존 확인창 스타일 재사용) |

에디터: `Assets/Scenes/Controls/SaveSystem/Editor/CloudSaveChecks.cs` — `Tools > Cloud Save > Run offline checks`(네트워크·세이브 파일 사용 안 함), `Tools > Cloud Save > Open account data folder`.

기존 파일 최소 수정 (원본은 `UserSettings/CloudSaveBackup/`에 보관):
- `ManualSaveMenu.cs`: 계정 패널 생성 2줄, 동기화 중 버튼 차단, 삭제 시 계정 사본 함께 삭제, 키보드 내비게이션에 계정 버튼 추가, 새 게임 확인 문구에 계정 안내 1줄.
- `HideOutPlayerBridge.cs`: 침대 저장 성공 직후 `CloudSaveSync.UploadAfterBedSave(this);` 1줄. 이 호출은 예외를 밖으로 던지지 않아서 "저장 완료"가 "저장 실패"로 바뀌는 일이 없다. 로그인하지 않았으면 아무것도 하지 않는다.

PC에 생기는 데이터 (`Application.persistentDataPath/CloudAccount/`): `session.json`(세션 토큰), `sync-<계정ID>.json`, `backups/`.

## 5. 검증 상태

2026-11-26: 실제 UGS 서버로 계정 생성·로그인·세이브 포인트 저장 후 업로드·Dashboard의 `manual_slot_1` 확인 완료 (에디터 Play).


실행한 것 (Unity 에디터 밖, 정적 검증):
- 새 C# 파일 6개 + 수정한 `ManualSaveMenu.cs` + 에디터 검사 파일을 C# 9(Roslyn)로 Unity/TMP/프로젝트 API 시그니처 스텁에 대해 컴파일: 오류·경고 0.
- 판정표·두 PC 왕복 시나리오(A 저장 → B 내려받기 → B 저장 → A가 최신본 받기)·해시 무결성(기존 `ManualSaveStore` 해시와 동일 알고리즘)·아이디/비밀번호 규칙·JSON 이스케이프: 28개 통과.

**아직 실행하지 않은 것** (직접 확인 필요):
- Unity 6000.3.23f1 에디터 컴파일 및 Play Mode.
- 실제 UGS 서버 호출 (Dashboard 설정 후 가능). 특히 `sign-up` 응답이 `sign-in`과 같은 형식(토큰 포함)인지 첫 실행 때 확인.
- 기존 `Tools > Manual Save > Run isolated integration checks` 회귀 실행.
- 두 대의 PC(또는 아래 방법)로 이어하기 확인, 빌드(Windows Player)에서의 동작.

## 6. 테스트 순서 (권장)

1. Dashboard 설정(1장) 후 `Tools > Cloud Save > Run offline checks` → 콘솔에 `… passed, 0 failed`.
2. `ControlSchemeSelect.unity` Play → 저장 화면 오른쪽 위 `계정 연동하기` → `계정 만들기`.
3. 게임 진행 → 기지 침대에서 저장 → 메시지 `저장 완료` 확인 (실패 문구가 붙지 않아야 함). Dashboard의 Player Management에서 해당 플레이어의 Cloud Save 데이터 `manual_slot_1` 확인.
4. **다른 PC 흉내 (같은 PC에서)**: Play 종료 → `Tools > Cloud Save > Open account data folder`에서 `sync-*.json`, `session.json` 삭제 → `HideOutPrototype/slot-1-v2.json`(과 `.bak`)을 다른 곳으로 옮김 → Play → 로그인 → 계정 진행이 내려받아지고 `이어하기`가 활성화되는지.
5. 충돌 확인: 로그아웃 상태로 새 게임 → 침대 저장 → 로그인 → `어느 기록으로 이어갈까요?` 대화상자가 뜨는지, 각 버튼 결과.
6. 실제 다른 PC에서 같은 계정으로 로그인해 이어지는지 (가장 중요).
7. 기존 `Tools > Manual Save > Run isolated integration checks` 회귀 확인.

## 7. 주의사항 / 한계

- UI 텍스트 폰트(`PFStardustNormal`)는 Dynamic 아틀라스라 새 한글 글자가 Play 중 추가되어 폰트 `.asset`이 변경될 수 있다. 기존 규칙대로 그 자동 변경은 커밋에서 제외한다.
- 세션 토큰은 이 PC에 평문 JSON으로 저장된다 (UGS 공식 SDK도 PlayerPrefs에 같은 방식으로 보관). 비밀번호는 어디에도 저장하지 않는다.
- 비밀번호 찾기/변경 UI는 없다. UGS에 `update-password` API는 있으니 필요하면 추가할 수 있다.
- 오프라인이면 동기화만 실패하고 로컬 저장으로 계속 플레이할 수 있다. 다음 온라인 메뉴 진입 때 판정 규칙대로 맞춘다.
- 업로드 중 메뉴로 나가는 등으로 실행기가 파괴되면 그 업로드는 중단되지만, 다음 메뉴 진입 때 `C = S, L ≠ S`로 판정되어 다시 올라간다.
- Cloud Save 무료 한도와 정책은 Unity Dashboard 기준을 따른다. 저장 파일은 기존 상한(4MB) 안이라 Cloud Save 항목 한도(5MB) 이내다.
