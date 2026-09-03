# 실행계획서: 운영 기능 (Phase 22~)

> `PRD.md`(무엇을) → `ROADMAP.md`(어떤 순서로) → **이 문서(Task 단위로 무엇을 어떻게, 어디까지 하면
> 끝인지)**. 실제 코드 작성은 이 문서의 Task를 순서대로 따라간다.
>
> Phase 22(완료) · Phase 23(완료) · Phase 24(완료) · **Phase 25(2026-09-03 작성, 착수 전)**.
> 2차 범위에서 확정한 방식대로(2026-08-20 사용자 확정) **한 Phase씩 착수 직전에 작성**한다.

## 공통 규칙

1. **Task는 순서대로.** 각 Task의 "완료 조건"을 모두 통과한 뒤 다음으로 넘어간다.
2. **검증한 것만 체크한다.** 확인하지 못한 항목은 체크하지 말고, 무엇을 왜 확인하지 못했는지 그 Task
   아래에 적는다. 추측으로 완료 처리하지 않는다.
3. **SPEC 값을 추측하지 않는다.** 전문 코드·필드 오프셋·길이·인코딩이 필요하면 담당 서브에이전트
   (`reader-pinpad-spec-expert` / `pos-onecap-spec-expert`)에 위임해 확인한 뒤 반영한다.
4. **계층 규칙**(`PRD.md` §0.2): `Views → ViewModels → Services → Protocol → Interop` 단방향.
   `Services`는 WPF 타입(`Visibility`/`Dispatcher` 등)을 알지 못한다.
5. **모든 화면 작업은 MVVM으로 한다.** 새 화면을 코드비하인드로 만들지 않는다.
6. 각 Phase 종료 시 `dotnet build`(경고 0/오류 0)와 실제 실행 확인. 커밋은 사용자가 요청할 때만.

---

# Phase 22 — 로그 출력

**이 Phase가 끝나면**: 로그가 기계 파싱 가능한 5슬롯 형식으로 남고, 90일이 지난 파일이 자동으로
정리되며, 카드 데이터가 로거 진입점에서 차단된다. 그리고 **장래 장애정보 서버 전송을 붙일 때
로그 호출부를 다시 손대지 않아도 되는 구조**가 갖춰진다.

> **이 Phase의 성공 기준에는 "아무것도 깨지지 않는 것"이 포함된다.** 기존 `FileLogger` 호출 지점이
> **151곳**이다. 이 Phase는 그 호출들을 **한 줄도 고치지 않고** 내부 구조만 바꾼다. 호출부를 새 API로
> 일괄 치환하고 싶은 유혹이 있겠지만 하지 않는다 — 로깅은 장애 분석의 마지막 보루라, 대량 치환 중
> 한 곳이라도 잘못되면 정작 필요할 때 로그가 없다.

## 착수 전 전제 (2026-08-31 확인 완료)

- 기존 `Services/Diagnostics/FileLogger.cs`는 `static` 클래스이며 `Info`/`Warn`/`Error(string)` 3개
  메서드만 노출한다. 파일 위치 `%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\yyyy-MM-dd.log`.
- SQLite 인프라가 이미 있다 — `Services/Storage/IntegrityCheckStore`, 같은 폴더의 DB 파일,
  `Microsoft.Data.Sqlite`, "공개 메서드는 예외를 밖으로 던지지 않는다"(P11-4) 규칙까지 확립됨.
- 리더기 인증 식별 번호는 **이미 파싱되고 있다** — `Protocol/Reader/StatusResponseParser.ReaderAuthId`,
  `Protocol/Reader/CardReadResponseParser.ReaderAuthId`. 새로 파싱할 것이 없다.
- 결과 코드 체계는 이미 3자리다 — `Services/Payment/PosResultCodeMapper`(`E0x`/`R0x`/`R2x`/`D0x`).
- 거래ID로 쓸 값도 이미 있다 — `PaymentOrchestrator.LogTxId`(POS 요청 전문 `#9` 전문관리번호).

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

- **서버 전송 기능 자체** — API 호출, 재시도 큐, 오프라인 보관, 압축. 트리거 조건도 정하지 않는다
  (서버 스펙이 나와야 정할 수 있다, `PRD.md` §1.7).
- **기존 151개 호출부의 메시지 문구**. 카테고리/코드는 Phase 22 이후 새로 쓰는 로그부터 채운다.
- 로그 레벨 추가(`Debug`)와 운영 중 레벨 변경.
- 포트 열기 시점의 상태확인 명령 추가 — 2026-08-31 사용자 판단으로 **하지 않기로 확정**(리더기 교체
  시 프로그램을 재시작하지 않으므로 실효가 없다. 카드리딩 응답이 관측 지점이므로 다음 거래에서
  자연히 갱신된다).

---

## P22-0. `app.manifest` 관리자 권한 상시 실행 + 로그 경로 변경

**다른 모든 Task보다 먼저 한다.** 뒤 Task들이 쓰는 로그 경로가 여기서 확정된다.

- `src/KFTCOneCAP.Wpf/`에 `app.manifest`를 추가하고 `.csproj`에
  `<ApplicationManifest>app.manifest</ApplicationManifest>`를 지정한다.
- manifest의 `requestedExecutionLevel`을 `requireAdministrator`로 설정한다(`PRD.md` §1.1.1,
  2026-09-01 확정). 이 뒤로 앱은 **항상 관리자 권한 상승이 필요**하다.
- `FileLogger`의 로그 경로 상수를 `%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\`에서
  **`C:\KFTC_PosAgent\KFTCTaxLog\`**로 바꾼다.
- **SQLite DB 경로(`IntegrityCheckStore`, 장래 `observed_identity`)는 옮기지 않는다** — 관리자
  권한 상시 실행이면 `%LOCALAPPDATA%`도 문제없이 쓸 수 있으므로, 굳이 옮겨 두 경로 규칙을
  만들 이유가 없다.
- 새 경로는 최초 기동 시 `Directory.CreateDirectory`로 스스로 만든다(설치 시 미리 만들어 둘
  필요 없음).

> **gap 기록(2026-09-01, 세션 실물 검증 중 발견)**: 최초 완료 조건 검증 때는 "관리자 권한 없이
> 실행하면 UAC 프롬프트가 뜬다"만 확인했고, **그 승격 과정에서 exe가 두 프로세스로 중복 기동될
> 수 있는지는 확인하지 않았다.** `requireAdministrator`로 상시 승격을 켜면서 단일 인스턴스
> 보장을 같이 챙겼어야 했는데 놓친 gap이다 — 실물 검증 중 하나는 TCP 8002/리더기 COM 포트를
> 정상적으로 잡고, 다른 하나는 `AddressAlreadyInUse`로 반쪽만 뜨는 상황이 관찰되어, `App.xaml.cs`
> `OnStartup` 최상단에 `Global\` Mutex 기반 단일 인스턴스 가드를 추가했다(진단 하네스 인자가 있을
> 때는 건너뛴다). 아래 새 항목으로 보강한다.

**완료 조건** — 2026-09-01 전부 실측 확인 완료(Sonnet 구현 + Opus 리뷰, 치명적 문제 없음)
- [x] `dotnet build` 후 생성된 실행 파일의 매니페스트에 `requireAdministrator`가 박혀 있다
      (`manifest.exe` 또는 속성 확인)
- [x] 관리자 권한 없이 실행하면 UAC 프롬프트가 뜬다(수동 확인)
- [x] 앱 기동 후 `C:\KFTC_PosAgent\KFTCTaxLog\yyyy-MM-dd.log`가 생성된다
- [x] 기존 `%LOCALAPPDATA%\KFTCTaxGiroCAP\logs\`에는 **새 파일이 생기지 않는다**(경로가 완전히
      전환됐는지 확인)
- [x] `%LOCALAPPDATA%\KFTCTaxGiroCAP\`의 SQLite DB는 그대로 그 자리에서 정상 동작한다
      (무결성체크 저장/조회로 확인 — 경로가 실수로 같이 바뀌지 않았는지)
- [x] 관리자 권한 승격 과정에서 앱이 중복 기동되지 않는다(단일 인스턴스 보장) — 2026-09-01 발견된
      gap, 이번에 보강. `Global\KFTCOneCAP_Wpf_SingleInstance` Mutex로 실물 재현 확인: 정상 인스턴스가
      떠 있는 상태에서 같은 exe를 재실행하면 새 프로세스는 로그 한 줄 남기지 않고 즉시 조용히
      종료되고(`애플리케이션 기동 시작` 로그가 늘지 않음, 프로세스 목록에도 남지 않음), 정상
      종료 → 재실행은 문제없이 새 인스턴스로 기동된다(Mutex 정상 해제 확인). 진단 하네스
      인자(`--payment-flow-test`/`--pos-client-test`/`--van-call-test` 등)가 있을 때는 가드를 건너뛰어
      정상 인스턴스와 하네스가 동시에 떠 있어도 서로 막지 않음을 확인(회귀 없음).

> 설치 시 확인해야 할 5가지(자동 로그온, 작업 스케줄러 트리거·옵션, 관리자 그룹, 3일 제한 해제)는
> `PRD.md` §1.1.1에 있다 — **이 저장소의 코드 작업이 아니라 설치 절차의 책임**이므로 이 Task의
> 완료 조건에 넣지 않는다. 실제 키오스크 설치 시 별도로 확인한다.

---

## P22-1. 로그 레코드 타입 + 라인 렌더러

파이프라인의 가장 안쪽부터 만든다. **순수 함수라 하드웨어 없이 전부 검증 가능한 부분**이므로 먼저
확정하고 넘어간다.

**만들 것**

- `LogRecord` — 시각 / 레벨 / 카테고리 / 코드 / 거래ID / 메시지. 불변 타입.
- `LogCategory` — `APP` `POS` `READER` `VAN` `PAYMENT` `KEYDOWN` `SETTINGS` `UI` (`PRD.md` §1.3-b).
- 라인 렌더러 — `LogRecord` → 파일에 쓸 한 줄.

**렌더링 규칙**(`PRD.md` §1.3-b)

```
[yyyy-MM-dd HH:mm:ss.fff] [레벨] [카테고리] [코드] [거래ID] 메시지
```

- 슬롯 5개는 **항상 존재**한다. 값이 없으면 `[-]`.
- 슬롯 사이 구분자는 공백 1개. 정렬용 패딩을 넣지 않는다.
- **메시지의 개행을 반드시 이스케이프한다.** 현재 호출부에 `FileLogger.Error($"... 처리 중 예외:
  {ex}")` 형태가 있어(예: `PosSocketServer.cs:248`) **스택 트레이스가 여러 줄로 들어온다.** 그대로
  쓰면 한 줄 = 한 레코드라는 파싱 계약이 깨지고, 장래 서버가 로그를 못 읽는다. `\r\n`/`\n`/`\r`을
  가시 문자(예: `\n` 두 글자)로 치환해 **한 레코드가 반드시 한 줄**이 되게 한다.
- 슬롯 값에 `]`가 들어가면 파싱이 깨지므로, 슬롯 4개(레벨/카테고리/코드/거래ID)는 렌더링 시
  `]`를 제거하거나 치환한다. 값의 출처가 모두 열거형·고정 코드·전문관리번호라 실제로는 일어나지
  않지만, 파싱 계약을 코드로 보장해 둔다.

**완료 조건** — 2026-09-01 전부 실측 확인 완료(Sonnet 구현 + Opus 리뷰 1차 지적 반영 + Opus 재검증)
- [x] `LogRecord` + `LogCategory` + 렌더러가 `Services/Diagnostics/`에 존재하고, WPF 타입에
      의존하지 않는다(공통 규칙 4)
- [x] 값이 전부 채워진 레코드와 전부 비어 있는 레코드가 각각 `PRD.md` §1.3-b의 예시와 같은 문자열로
      렌더링된다
- [x] **여러 줄 메시지(스택 트레이스)를 넣어도 출력이 정확히 1줄**이다
- [x] `PRD.md` §1.3-b의 파싱 정규식으로 위 출력들을 되파싱했을 때 원래 필드 값이 그대로 복원된다
      (렌더러와 정규식이 실제로 짝이 맞는지 확인 — 문서에만 적어두면 어긋나기 쉽다)

---

## P22-2. 마스킹 (파이프라인 진입점)

**싱크보다 먼저 만든다.** 싱크를 먼저 붙이면 마스킹 없는 상태로 파일에 쓰는 구간이 생긴다.

- 카드번호로 보이는 **13~19자리 연속 숫자**를 앞 6 + 뒤 4만 남기고 가운데를 `*`로 치환한다.
- 트랙 데이터·PIN 블록으로 보이는 패턴도 같은 지점에서 차단한다.
- **모든 싱크보다 앞에서** 동작해야 한다(`PRD.md` §1.4) — 파일에는 남고 원격에는 안 가는 식의
  구멍이 생기면 안 된다. 링버퍼(P22-4)에 담기는 내용도 이미 마스킹된 것이어야 한다.
- 정규식은 미리 컴파일해 둔다. **모든 로그 한 줄마다 통과하는 경로**라 여기서 느려지면 결제 Flow가
  같이 느려진다.
- 마스킹 대상은 **메시지 필드만**이다. 슬롯 값에는 카드 데이터가 들어갈 여지가 없다.

**완료 조건** — 2026-09-01 전부 실측 확인 완료. PIN블록 패턴은 `ReaderAuthId`(X16) 오탐 위험으로
Opus 리뷰 후 제외 결정(코드에서 삭제) — development_plan.md 상단의 "트랙 데이터·PIN 블록" 문구 중
PIN 블록은 이번 구현 범위에서 제외됐다
- [x] 16자리 카드번호를 포함한 메시지가 `123456******7890` 형태로만 파일에 남는다
- [x] 13자리/19자리 경계값도 마스킹된다. **12자리 이하는 마스킹하지 않는다**(금액·전화번호·
      전문관리번호가 뭉개지면 안 된다) — 경계 동작을 실제로 확인한다
- [x] 한 메시지 안에 숫자열이 여러 개 있어도 전부 마스킹된다
- [x] 마스킹이 파이프라인의 **단일 지점**에 있음을 코드로 확인(싱크마다 따로 하지 않는다) — 아직
      어디서도 호출되지 않는 순수 함수이며, 파이프라인 연결은 P22-3에서 수행

---

## P22-3. `ILogSink` 추상화 + `FileLogger` 내부 위임 ★

**이 Phase에서 가장 조심할 Task다.** 151곳의 호출 동작이 바뀌면 안 된다.

**구조**

- `ILogSink` — `LogRecord` 하나를 받는 인터페이스.
- `FileLogSink` — 유일한 구현. 기존 `FileLogger`의 파일 쓰기 로직(경로 규칙, 전역 `lock`,
  실패 무시)을 그대로 옮긴다.
- 파이프라인 — 마스킹(P22-2) → 등록된 싱크들에 전달. 싱크 목록은 앱 기동 시 한 번 구성한다
  (`App.xaml.cs`). 장래 원격 싱크는 여기에 **추가**만 하면 된다.
- `FileLogger`는 **공개 정적 메서드를 그대로 유지**하고 내부에서 파이프라인에 위임한다.
  DI 컨테이너를 도입하지 않는다 — 151곳을 고치지 않는 것이 이 Task의 목적인데, DI로 바꾸면
  그 목적과 정면으로 충돌한다.

**지켜야 할 기존 동작**

- 로깅 실패(디스크 가득참·권한 문제)가 **앱 동작에 영향을 주지 않는다.** 싱크 하나가 던져도
  다른 싱크와 호출자에게 전파되지 않아야 한다(장래 원격 싱크가 붙었을 때 특히 중요하다).
- 리더기 CALLBACK 스레드와 UI 스레드가 동시에 기록해도 **줄이 섞이지 않는다.**
- 파일 열기 모드에 **공유 읽기를 허용**한다(`PRD.md` §1.3-e) — 장래 전송 기능이 기록 중인 파일을
  읽을 수 있어야 한다.

**완료 조건** — 2026-09-01 전부 실측 확인 완료(Sonnet 구현 + Opus 리뷰 1차 지적 5건 반영 + Opus 재검증)
- [x] `FileLogger.Info/Warn/Error(string)` 시그니처가 그대로이고, **호출부 151곳이 무수정**이다
      (`git diff`로 확인 — 호출부 파일에 변경이 없어야 한다)
- [x] 앱을 기동해 로그가 기존과 같은 경로·파일명으로 남는다
- [x] 싱크가 예외를 던지도록 강제로 만들어도 앱이 죽지 않고 호출자에게 전파되지 않는다
- [x] 여러 스레드에서 동시에 대량 기록했을 때 줄 섞임·깨짐이 없다(진단 하네스로 확인, 8스레드×2000줄)
- [x] 로그 파일을 다른 프로세스가 열어둔 채로도 기록이 계속된다

---

## P22-4. 구조화 레코드 링버퍼

- 최근 **500건**의 `LogRecord`를 메모리에 유지한다(고정 크기, 오래된 것부터 밀어냄).
- **렌더링된 문자열이 아니라 레코드를 담는다**(`PRD.md` §1.3-d). 장래 원격 싱크가 JSON으로 보낼 때
  자기가 만든 텍스트를 정규식으로 되파싱하는 일이 없어야 한다.
- 거래ID로 필터링해 꺼낼 수 있어야 한다 — 장애 보고가 "그 거래의 로그"를 첨부하는 형태이기 때문이다.
- 스레드 안전. 읽기(스냅샷)가 기록을 막지 않도록 한다.
- 500건은 상수로 한 곳에 둔다.

**완료 조건** — 2026-09-01 전부 실측 확인 완료(Sonnet 구현 + Opus 리뷰 개선권장 4건 반영 + Opus 재검증)
- [x] 501건을 기록하면 가장 오래된 1건이 밀려나고 크기가 500으로 유지된다
- [x] 거래ID로 필터링해 해당 거래의 레코드만 시간순으로 꺼낼 수 있다
- [x] 다중 스레드 기록 중 스냅샷을 떠도 예외가 나지 않는다
- [x] 링버퍼의 내용이 **이미 마스킹된 상태**다(P22-2가 앞단이므로 자동이지만 실제로 확인한다)

---

## P22-5. 90일 보관 정리

- **파일명(`yyyy-MM-dd.log`)에서 파싱한 날짜** 기준으로 90일보다 오래된 파일을 삭제한다.
  `LastWriteTime`은 복사·백업으로 바뀌므로 쓰지 않는다.
- **패턴에 맞지 않는 파일은 건드리지 않는다** — 사용자가 폴더에 넣어둔 파일을 지우면 안 된다.
- 실행 시점: **앱 기동 시 1회** + **날짜가 바뀌어 새 로그 파일을 처음 만들 때**. 별도 타이머를 두지
  않는다(키오스크는 며칠씩 켜져 있으므로 날짜 전환 훅이 실질적으로 동작한다).
- **백그라운드에서** 수행한다. 기동 경로를 블로킹하지 않는다.
- 실패(파일 잠김 등)는 조용히 무시하고 다음 기회에 다시 시도한다.
- 정리 결과를 `APP` 카테고리로 한 줄 남긴다(예: `로그 정리 — 90일 초과 3건 삭제`).
- 보관 기간 90일은 상수로 한 곳에 둔다.

**완료 조건** — 2026-09-01 전부 실측 확인 완료(Sonnet 구현 + Opus 리뷰 + Opus 재검증)
- [x] 91일 전 날짜 파일명으로 더미 파일을 만들어 두면 기동 시 삭제된다
- [x] 89일 전 파일은 **남는다**(경계 확인)
- [x] `readme.txt`처럼 패턴에 맞지 않는 파일은 남는다
- [x] 삭제 대상 파일을 다른 프로세스가 열어 잠가 두면, 예외 없이 건너뛰고 앱이 정상 동작한다
- [x] 자정을 넘겨 로그가 새 파일에 기록될 때 정리가 다시 한 번 돈다 — `LogRetentionCleaner.NotifyLogWritten`을
      다른 날짜로 직접 호출해 확인(시스템 시각 조정 대신 훅 직접 호출 방식 사용)

---

## P22-6. 구조화 필드 배선 (카테고리 / 코드 / 거래ID)

여기서 처음으로 **호출부를 건드린다.** 단, 기존 151곳을 일괄 개조하는 것이 아니라 **거래 1건의
흐름을 따라가며 필요한 곳만** 새 오버로드로 바꾼다.

- `FileLogger`에 카테고리·코드·거래ID를 받는 **오버로드를 추가**한다. 기존 3개는 그대로 둔다.
- **거래ID 승격**: `PaymentOrchestrator`가 메시지 문자열에 넣던 `txId=`를 전용 필드로 옮긴다.
  값은 `LogTxId`가 만드는 것을 그대로 쓴다(POS 요청 전문 `#9` 전문관리번호).
- **최소 배선 범위**는 `PRD.md` §1.5의 경계 표다. 결제 1건이 흐를 때 아래가 카테고리·코드·거래ID를
  갖춘 상태로 남아야 한다.

| 경계 | 카테고리 | 대상 |
|---|---|---|
| POS 소켓 | `POS` | 연결 수락/종료, 요청 수신, 응답 송신(결과코드) |
| 리더기 | `READER` | 명령 송신, 응답/이벤트 수신, 포트 열기/닫기, 재연결 |
| VAN | `VAN` | `FNAISCRDVAN` 호출 직전/반환 |
| 결제 Flow | `PAYMENT` | 거래 시작(데드라인), 거래 확정(결과코드+사유) |
| 앱 수명 | `APP` | 기동, 종료, DLL 로드, 로그 정리 |

- **코드는 새로 만들지 않는다.** `PosResultCodeMapper`가 이미 만든 3자리 문자열을 그대로 싣는다.
- **주의**: 이 배선 작업으로 메시지 문자열을 손대다가 전자납부번호(19)·거래금액(18) 같은 필드를
  원문으로 넣지 않는다(`PRD.md` §1.4/§1.5) — 마스커는 최후의 방어선이지 면허가 아니다.

**완료 조건** — 2026-09-01 전부 실측 확인 완료(Sonnet 구현 + 진단 하네스 런타임 검증)
- [x] 결제 1건 실행 시 위 표의 모든 경계가 로그에 나타나고, 그 줄들의 거래ID가 **전부 같다**
      (`--payment-flow-test`/`--pos-client-test`로 확인. 단 **알려진 제약**: 전문 `#9`가 빈 기형
      요청에서는 POS/VAN 경계 로그의 거래ID가 `PaymentOrchestrator.LogTxId`의 합성 거래ID와
      달라질 수 있다 — 사용자 확인 결과 실사용에 영향 없는 좁은 엣지케이스라 수정하지 않기로 함)
- [x] 실패 거래에서 POS에 나간 결과 코드와 로그의 코드 슬롯 값이 **일치**한다
- [x] 메시지 문자열 안에 `txId=`가 중복으로 남아 있지 않다(전용 필드로 옮겼으므로)
- [x] 손대지 않은 나머지 호출부는 `[-] [-] [-]`로 정상 기록된다

---

## P22-6부속. 전문 원문 로그(`TelegramLogRedactor`) — 위치 기반 마스킹 + `#51` 미마스킹 확정

Phase 22 계획에는 없었으나 사용자 요청("실제 POS/VAN이 어떻게 전문을 주고받았는지 원문을 로그로
확인하고 싶다")으로 추가된 기능. `src/KFTCOneCAP.Wpf/Services/Diagnostics/TelegramLogRedactor.cs`가
POS 소켓 경계(`PosSocketServer`)와 VAN 경계(`StubVanRelayService`/`VanService`) 양쪽에서 전문
원문(요청/응답)을 로그에 남기되, 902614 `#46`(암호화된 카드정보, POSITION 407/길이196)만 위치
기반으로 부분 마스킹(앞 6바이트만 노출)한다.

**⚠️ 2026-09-01 확정: `#51`(암호화된 비밀번호 정보, PIN 관련 필드)은 마스킹하지 않는다 — 반드시
재검토가 필요한 리스크임.**

- 사용자에게 위험을 명확히 고지했다: `PinFieldEncoder`가 SEED 암호화를 아직 구현하지 않아
  `#51`에는 지금 **평문 4자리 PIN**이 그대로 들어간다. 이 상태로 전문 원문을 로그에 남기면 실제
  고객 PIN이 로그 파일에 그대로 찍힌다.
- 그럼에도 사용자가 "어차피 실제 배포될 때는 암호화를 할 거라서 굳이 먼저 마스킹해놓을 필요는
  없다"고 최종 결정해, `#51` 마스킹을 넣지 않기로(정확히는 한 번 넣었다가 도로 뺐다) 확정했다.
- **SEED 암호화 구현 전까지는 실제 고객 PIN이 이 로그 경로로 노출되는 상태다.** PIN 암호화(SEED)
  작업이 착수될 때 반드시 이 결정을 재검토해야 한다 — 그 시점에 `TelegramLogRedactor`의 클래스
  요약 주석("2026-09-01 재확정" 절)도 함께 갱신한다.
- `#46`(카드정보) 마스킹은 그대로 유지한다 — 이 결정과 무관하다.

**기형 전문(길이 불일치) 폴백 검증** — `Redact`는 본문 길이가 스키마의 `TotalLength`와 정확히
일치할 때만 위치 기반 마스킹을 적용하고, 불일치하면 원문을 그대로 반환한다(그 뒤 파이프라인의
`LogMessageMasker.Mask`가 범용 마스킹을 한 번 더 건다). `PaymentFlowTestScenarios`에 이 폴백 경로를
실행으로 검증하는 시나리오를 추가해, 위치 기반 마스킹을 시도하지 않고 원문을 그대로 돌려주는지 +
그 원문이 범용 마스킹을 거치는지를 확인했다(순수 진단 목적, 프로덕션 경로에 영향 없음).

**2026-09-01 참고 — `#46` 필드 내용 구성이 바뀜(마스킹 구조 자체는 변경 없음)**: `#46`(POSITION 407,
길이 196)에 실제로 채워지는 값이 "0"+리더기가 준 3자리 길이(zero-padded)+페이로드 형태로 바뀌었다
(`PaymentOrchestrator.FillCardApprovalFields`, 상세 근거는 `docs/payment_relay/PRD.md`의 "`#46` 필드
구성 — 2026-09-01 사용자 확정" 항목 참고). 이 클래스의 위치/길이 기반 마스킹(앞 6바이트 노출)은 필드
전체 길이·위치가 그대로라 구조 변경이 필요 없다 — 다만 노출되는 앞 6바이트가 이제 `"0192EN"`처럼 길이
헤더 일부를 포함하게 된다. 길이 정보 자체는 민감하지 않으므로 문제는 없다고 판단해 그대로 뒀다.

---

## P22-7. `observed_identity` — 진단 컨텍스트 저장

- 기존 SQLite DB(`IntegrityCheckStore`와 같은 파일)에 테이블 하나를 추가한다.

```
observed_identity(scope TEXT, key TEXT, value TEXT, observed_at TEXT, PRIMARY KEY(scope, key))
```

- 이번에 저장하는 값은 **리더기 인증 식별 번호 X(16)** 하나다(`H/W모델명 12 + F/W버전 4`).
  `scope`는 포트(`COM3` 등), `key`는 `reader_auth_id`.
- **모듈 ID·리더기 이름·리더기 버전·키 버전은 저장하지 않는다**(2026-08-31 확정 — 개체 식별자는
  장애 대응에 필요 없고, 필요한 것은 모델·펌웨어뿐이다).
- 관측 지점 2곳. **둘 다 파서가 이미 값을 뽑고 있으므로 저장 호출만 연결한다.**
  - 카드리딩 응답 (`CardReadResponseParser.ReaderAuthId`) — 거래마다, 자동
  - 상태체크 (`StatusResponseParser.ReaderAuthId`) — 설치·점검 시 수동
- **upsert**. 이력을 쌓지 않는다 — 필요한 것은 "가장 최근에 본 값"뿐이다.
- `observed_at`은 필수다. 장애 보고 시 값과 관측 시각을 함께 싣는다.
- `IntegrityCheckStore`와 동일하게 **공개 메서드가 예외를 밖으로 던지지 않는다**(P11-4). 진단 부가
  정보를 저장하다가 결제가 실패하면 본말전도다.
- 타임스탬프 형식은 `IntegrityCheckStore.TimestampFormat`(`yyyy-MM-dd HH:mm:ss.fff`)과 맞춘다 —
  같은 DB 안에서 형식이 두 가지면 나중에 반드시 헷갈린다.
- **주의**: `reader_auth_id`(16)는 DB 저장은 원문 그대로 하되(장애 대응에 필요한 관측값), 이 값을
  **로그 메시지에도 그대로 찍지 않는다** — 16자리 hex라 마스커(§1.4)가 카드/PIN 패턴으로 오탐할
  가능성을 만들지 않기 위해서다(2026-09-01, PIN 블록 정규식을 삭제한 대신 호출부에서 이 규율을
  지킨다).

**완료 조건** — 2026-09-01 전부 실측 확인 완료
- [x] 거래 1건 후 해당 포트의 `reader_auth_id`가 저장돼 있고 `observed_at`이 그 시각이다
- [x] 상태체크 버튼으로도 같은 값이 갱신된다 — 사용자가 실제 화면에서 직접 클릭, `COM 03`/`COM 05`
      두 포트 모두 `reader_auth_id`가 upsert됨을 DB 조회로 확인
- [x] 같은 포트로 두 번 관측하면 행이 늘지 않고 **덮어써진다**(upsert 확인)
- [x] 리더기 2대 구성에서 포트별로 각각 저장된다(`COM 03`/`COM 05` 각각 별도 행으로 실측 확인)
- [x] DB 파일을 읽기 전용으로 만들어 저장을 실패시켜도 **결제가 정상 완료**된다

---

## P22-8. 기록 기준 대조 + 보안 전수 점검 + 회귀

**기록 기준 대조**(`PRD.md` §1.5)

- 결제 1건(정상 1 + 실패 1)을 실행해 §1.5 경계 표의 지점이 모두 로그에 나타나는지 확인한다.
- **금지 항목이 없는지** 확인한다 — 전문 본문 전체, 긴 바이트열, 루프 내부 반복 로그.
- 거래 1건의 줄 수가 `PRD.md` §1.5의 감각(약 10줄)을 크게 벗어나지 않는지 본다. 수십 줄이면
  금지 항목을 어기고 있다는 신호다.

**보안 전수 점검**(`PRD.md` §0.4)

- Phase 21과 같은 방식으로 **카드번호·PIN·트랙 데이터가 로그에 남지 않는지 전수 점검**한다.
  마스킹(P22-2)은 최후의 방어선이지 면허가 아니다 — 호출부가 애초에 넣지 않아야 한다.
- 링버퍼 스냅샷에도 남지 않는지 확인한다(새로 생긴 저장 지점이다).

**회귀**

- `dotnet build` 경고 0 / 오류 0.
- 기존 진단 하네스(`--pos-client-test`, `--payment-flow-test` 등)가 전부 통과한다.
- 장시간 실행 시 메모리 증가가 없다(링버퍼가 고정 크기인지 실제로 확인).

**완료 조건**
- [x] §1.5 경계 표의 모든 지점이 실제 로그에 나타남 — **2026-09-01 확인**: 결제 1건(정상)으로
      POS/리더기/VAN/거래 수명 경계 전부 실측 확인(`ROADMAP.md` Phase 22 완료 기준)
- [x] 금지 항목 위반 0건 — **2026-09-01 확인**: 진단 하네스(`--pos-client-test`)가 전문 원문
      전체를 로그에 남기던 위반 1건 발견·수정 완료, 그 외 위반 0건(`ROADMAP.md`).
      **단, 이후 P22-6부속에서 전문 원문 로그(`TelegramLogRedactor`, §1.4 예외)가 정책적으로
      추가돼 "전문 본문 전체 금지" 규칙 자체가 바뀌었다** — 이 체크는 그 변경 이전 시점의
      검증이고, `PRD.md` §1.5에 예외 조항을 추가해 둘의 모순을 없앴다(2026-09-02)
- [x] 카드/PIN 전수 점검 위반 0건 (파일 + 링버퍼) — **2026-09-01 확인**(`ROADMAP.md`).
      **`#51`(PIN 관련 필드) 미마스킹은 이 항목의 "위반"이 아니라 P22-6부속에서 별도로
      리스크 고지·확정한 사용자 결정**이다(SEED 암호화 전까지 평문 PIN 노출 — 재검토 필요,
      `PRD.md` §1.4)
- [x] `dotnet build` 경고 0 / 오류 0 — Phase 22 전체를 통해 매 커밋 전 확인(`370bcaf`까지 반영)
- [x] 기존 진단 하네스 전부 통과 — `--pos-client-test`/`--payment-flow-test`/`--van-call-test`
      각 체크포인트 검증에서 반복 통과 확인
- [x] 반복 실행 후 메모리 단조 증가 없음 — **2026-09-01 확인**: 8스레드×2000줄 동시 기록
      검증(P22-3) + 반복 실행 메모리 단조증가 없음 확인(`ROADMAP.md`)

---

## Phase 22 완료 후

- `ROADMAP.md`의 Phase 22 체크박스를 실제 검증 결과로 갱신하고, 확인하지 못한 항목이 있으면
  **무엇을 왜 확인하지 못했는지** 함께 적는다.
- Phase 23 착수 직전에 이 문서에 Phase 23 계획을 이어서 작성한다.

---

# Phase 23 — 가맹점 설정 화면

**이 Phase가 끝나면**: 홈 화면의 "가맹점 설정" 카드가 실제 화면을 열고, 그 화면에서 고른 값이
**실제 결제 동작**에 반영된다 — VAN Mode, 카드입력 데드라인, 그리고 키오스크 고유번호 불일치
거부까지.

> **이 Phase의 위험은 화면이 아니라 결제 Flow 쪽에 있다.** 옵션 6개를 그리는 일(P23-1~P23-4)은
> 기존 리더기 설정 화면의 패턴을 그대로 따르는 저위험 작업이다. 반면 **P23-5~P23-7은 이미 실거래로
> 검증을 마친 결제 경로를 건드린다** — 하드코딩 상수를 설정값으로 바꾸고, 거래를 거부하는 새 분기를
> 하나 추가한다. 체크포인트를 그 경계에서 끊는 이유다.

## 착수 전 확정 사항 (2026-09-01 사용자 확인, 문서 반영 완료)

1. **카드입력 타임아웃 `0`의 의미** — "설정 안 함"이며 기본값 **`120`초**를 적용한다(무제한 아님).
   레지스트리에 값이 없을 때도 `120`초. 원본 MFC 기본값 `100`은 따르지 않는다.
   → `PRD.md` §2.4 갱신 완료, §4 미확정 **#4 해소** 처리 완료.
2. **VAN Mode 검증 경로** — 운영 경로는 `StubVanRelayService`를 **유지**한다. **검증할 때만**
   `App.xaml.cs`를 `VanService`로 일시 스왑해 실제 DLL 인자를 확인하고 **원복**한다.
   → `PRD.md` §2.2에 "검증 방법" 절 추가 완료, `ROADMAP.md` 완료 기준 갱신 완료.
3. **경합 게이트** — 기존 게이트를 **카운터째 재사용**하되 이름을 "설정 화면 일반"으로 **리네임**한다.
   → `PRD.md` §2.7 갱신 완료.
4. **원본 화면 근거** — `docs/operations/screenshots/shop_setup.png` 확보 완료. **문구·헤더·섹션
   카드·`확인`/`취소` 배치는 따르고, 탭 구조와 전체 레이아웃은 따르지 않는다**(원본은 탭 4개에
   옵션 수십 개, 이번 범위는 단일 화면에 옵션 6개 — `PRD.md` §2.1).

## 착수 전 전제 (코드 실측, 2026-09-01)

- **레지스트리 접근 선례가 이미 있다** — `Services/Settings/ReaderSettingsService`(65줄).
  `Load()`는 예외를 던지지 않고 기본값 폴백, `Save()`는 던진다. 반전 인코딩(`MULTIPAD1_FIELD`)을
  클래스 안에서만 `bool`로 바꾸는 패턴도 여기 있다. **이 클래스를 그대로 본뜬다.**
- **게이트 관련 심볼은 6곳뿐이다** — `Services/Payment/IReaderSetupGate.cs`,
  `Views/ReaderSetupWindowGate.cs`, `Services/Diagnostics/FakeReaderSetupGate.cs`,
  `App.ReaderSetupGate`(`App.xaml.cs:44`), `PaymentOrchestrator`(3줄),
  `PosPaymentResultCode.ReaderSetupInProgress` + `PosResultCodeMapper` 매핑 1줄. 리네임 범위가
  좁다는 것을 착수 전에 확인했다.
- **`#42`는 이미 스키마에 등록돼 있다** — `Protocol/Pos/Schemas/CardApprovalSchema.cs:85`
  `new(42, "키오스크 고유번호", PosFieldType.AN, 20, 335, K)`. SET 장소가 `K`(kiosk)이므로 POS가
  채워 보내는 값이고 원캡은 읽기만 한다. **새로 파싱할 것도, SPEC을 새로 확인할 것도 없다.**
- **운영 VAN 경로는 Stub이다** — `App.xaml.cs:167`. 실제 DLL을 부르는 `VanService`는 진단
  하네스(`Services/Diagnostics/VanCallTestScenarios.cs`의 `new VanService()` 2곳)에서만 생성된다.
- **`PaymentOrchestrator`의 데드라인은 생성자에서 한 번 읽어 필드에 든다** —
  `_initialCardReadDeadline`(선택 인자 `TimeSpan? initialCardReadDeadline`), 하네스가 이 인자로
  5초를 주입한다(`PaymentFlowTestScenarios.cs:101`). **`PRD.md` §2.6의 "설정값을 캐시하지 않는다"를
  만족하려면 이 필드를 그대로 둘 수 없다** — P23-6의 핵심 논점.
- **화면 스타일 자산이 이미 있다** — `ReaderHeaderTitleTextStyle` / `ReaderHeaderSubtitleTextStyle` /
  `ReaderSectionTitleTextStyle` / `ReaderLabelTextStyle` / `ReaderButtonStyle` /
  `ModernToggleSwitchStyle`, 그리고 **가맹점 설정 화면을 위해 미리 만들어 둔**
  `Themes/TextBox.xaml`의 `SkinnedTextBoxStyle`(주석에 "가맹점 설정 화면에서 사용"이라고 적혀 있다).

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

- **토글 3종(자동 리부팅 / 자동 업데이트 / 결제 화면 잠금)의 실제 동작** — 값 저장까지만
  (`PRD.md` §2.5). **저장한 값을 읽어 쓰는 코드를 만들지 않는다.**
- **운영 경로의 VAN 서비스 교체** — Stub을 유지한다. 일시 스왑은 검증용이고 반드시 원복한다.
- **VAN 서버 포트 번호 항목** — 원본 화면에는 있지만 만들지 않는다(`PRD.md` §0.1).
- **원본 `ShopSetupDlg`의 나머지 옵션 전부**와 **가맹점 다운로드 탭**.
- **화면 워밍업** — 리더기 설정 화면에는 `HomeWindow.WarmUpReaderSetupWindow`가 있지만 가맹점 설정
  화면은 만들지 않는다. 컨트롤이 6개뿐이라 최초 오픈 비용이 문제되는 화면이 아니고, 워밍업
  인스턴스는 게이트 카운트를 오염시키지 않도록 `IsWarmupInstance` 분기를 또 만들어야 해서 비용
  대비 이득이 없다.
- **수신한 `#42`를 레지스트리에 자동 기입하는 것** — `PRD.md` §2.3.2의 명시적 금지 사항.

## ⚠️ 진행 중 임시 조치 — `app.manifest` 관리자 권한 해제 (2026-09-02, 반드시 원복)

Phase 23 화면 작업 중 `mcp__windows__*` 클릭 자동화가 UIPI에 막혀 검증을 제대로 못 하는 문제가
반복되어(P23-3 gap), **Phase 23 화면 작업이 끝날 때까지 한시적으로** `src/KFTCOneCAP.Wpf/app.manifest`의
`requestedExecutionLevel`을 `requireAdministrator` → `asInvoker`로 낮췄다(사용자 지시).

- **P23-8(최종 검증) 착수 전에 반드시 `requireAdministrator`로 되돌린다.** 되돌리지 않으면 P22-0이
  확정한 로그 경로(`C:\KFTC_PosAgent\KFTCTaxLog\`) 쓰기 권한 문제가 재발한다.
- 원복 여부는 P23-8 완료 조건에 새 항목으로 추가한다: **`app.manifest`가 `requireAdministrator`로
  복귀했는지 실측 확인(UAC 프롬프트 재발생 확인).**
- 이 기간 동안 실행하는 앱은 관리자 권한이 아니므로 `C:\KFTC_PosAgent\KFTCTaxLog\`에 로그 쓰기가
  실패할 수 있다 — 이 기간의 로그 관련 검증(있다면)은 원복 후 다시 확인해야 한다.

## 체크포인트 (Opus 리뷰 지점)

| 체크포인트 | Task | 성격 |
|---|---|---|
| **CP1** | P23-1 ~ P23-4 | 신규 서비스 + 신규 화면 + 배선. **기존 결제 경로에 기능적 영향 없음** |
| **CP2 ★** | P23-5 ~ P23-7 | **이미 검증된 결제 Flow를 직접 변경.** 이 Phase의 실제 위험이 전부 여기 있다 |
| — | P23-8 | 실측 검증 · 회귀 · 문서 갱신. 별도 리뷰 대신 회귀 확인으로 갈음 |

---

## P23-1. `ShopSettingsService` — 레지스트리 접근 계층

`Services/Settings/`에 `ShopSettings`(값 객체)와 `ShopSettingsService`(로드/저장)를 만든다.
`ReaderSettingsService`를 그대로 본뜬다.

**저장 위치** — 두 개의 하위 키에 걸쳐 있다(`PRD.md` §2.2~§2.5).

| 속성 | 레지스트리 | 인코딩 | 기본값 |
|---|---|---|---|
| `VanMode` | `...\TCP\VAN_MODE` | 문자열 그대로 | `"R"` |
| `KioskId` | `...\TCP\KIOSK_ID` | 문자열 그대로 | `""` |
| `CardReadTimeoutSeconds` | `...\SERIALPORT\TIMEOUT` | 문자열(숫자) | `120` |
| `AutoReboot` | `...\SERIALPORT\AUTO_REBOOT` | **반전** ON=`"0"`/OFF=`"1"` | ON |
| `AutoUpdate` | `...\SERIALPORT\AUTO_UPDATE` | **반전** | OFF |
| `KeyinDim` | `...\SERIALPORT\KEYIN_DIM` | **반전** | OFF |

> **저장 위치 2026-09-02 최종 확정(사용자 지시)**: `KioskId`만 `SERIALPORT` → `TCP`로 옮긴다
> (VAN Mode와 같은 `TCP` 하위 키로 모았다 — 원본 MFC에 없던 신규 항목이라 위치 호환 부담이 없다).
> `AutoReboot`/`AutoUpdate`/`KeyinDim`은 그대로 `SERIALPORT`다.

**이 클래스 밖으로 새어 나가면 안 되는 것**(`PRD.md` §0.3과 같은 취지)

- 반전 인코딩. 화면과 ViewModel은 `bool`만 본다.
- **`0` = 미설정 규칙.** `Load()`가 `0`과 "값 없음"을 **둘 다 `120`으로 변환해서** 내보낸다.
  `PaymentOrchestrator`와 화면은 이 규칙을 알지 못한다.
- 레지스트리에 손으로 써넣은 이상값의 처리.

**이상값 처리 — 이 계획서에서 정하는 판단(PRD에 없음, CP1 리뷰에서 확인)**

레지스트리는 사용자가 `regedit`으로 직접 고칠 수 있으므로 화면 검증만으로는 값을 신뢰할 수 없다.
셋 다 **거래를 실패시키지 않는 방향**으로 폴백하고 `WARN` 로그를 남긴다.

- `TIMEOUT`이 숫자가 아니거나 음수, 또는 **`1~29`**(화면 검증이 막는 범위인데 직접 써넣은 경우)
  → `120`으로 폴백 + `WARN`.
- `VAN_MODE`가 `R`/`OT`/`IT` 셋 중 하나가 아니면 → `"R"`(운영)로 폴백 + `WARN`.
  **폴백 방향이 운영인 이유**: 알 수 없는 값 때문에 테스트 서버로 조용히 붙는 것보다, 설정이
  깨졌을 때 운영으로 가는 편이 안전하다.
- `KIOSK_ID`가 **20자를 넘으면** → **빈 값과 동일하게 취급**(검증 미수행) + `WARN`.
  `#42`가 AN 20이라 20자 초과 설정값은 **어떤 요청과도 절대 일치할 수 없어** 그대로 두면
  §2.3.1 검증이 모든 거래를 거부한다. 진단 편의 때문에 결제를 전면 중단시키지 않는다는
  `PRD.md` §2.3.2의 판단을 그대로 따른다.

**`Save()`는 사용자가 입력한 값을 그대로 쓴다** — `0`을 `120`으로 바꿔 저장하지 않는다. 변환은
읽기에서만 한다(사용자가 화면에서 `0`을 넣었으면 다시 열었을 때도 `0`이 보여야 한다).

**완료 조건** — 2026-09-02 전부 실측 확인 완료(레지스트리 레이아웃 오류 1건 발견·수정 후 검증,
아래 참고)
- [x] `Load()`가 레지스트리 접근 실패/키 없음에서 예외를 던지지 않고 위 표의 기본값을 돌려준다 —
      키 전체 삭제 상태에서 `Load()` 실행해 `R`/`''`/`120`/`true`/`false`/`false` 확인
- [x] `Save()`는 실패 시 예외를 던진다(`PRD.md` §2.6 — 사용자에게 알려야 하므로 삼키지 않는다) —
      코드 리뷰로 확인(`ReaderSettingsService.Save`와 동일하게 `try/catch` 없이 `RegistryKey` 호출이
      그대로 전파됨). 실제 권한 거부로 강제 재현하지는 않았다
- [x] 반전 인코딩이 이 클래스 밖의 어느 파일에도 등장하지 않는다(`"0"`/`"1"` 리터럴 grep) — grep
      결과 `ShopSettingsService.cs`/`ShopSettings.cs` 외 0건(아직 이 서비스를 쓰는 화면이 없어 당연히
      0건 — P23-3에서 다시 확인 필요)
- [x] `0` 저장 → `Load()`가 `120`을 돌려준다. 값 삭제 → `120`. `45` → `45` — x86 PowerShell로 빌드된
      `KFTCOneCAP.Wpf.exe`를 리플렉션 로드해 실측(스크립트로 레지스트리 조작 후 `Load()` 호출),
      확인 후 원래 레지스트리 백업을 복원함
- [x] 이상값 3종(`TIMEOUT="abc"`/`"15"`, `VAN_MODE="XX"`, 21자 `KIOSK_ID`)이 각각 폴백 + `WARN` —
      동일 스크립트로 실측(`abc`/`15`→120, `XX`→R, 21자→빈값). `WARN` 로그 자체(레벨 문자열)는 코드
      경로상 확실하나 이번 세션에서 로그 파일 라인까지 별도로 대조하지는 않았다
- [x] `Services`가 WPF 타입을 참조하지 않는다(계층 규칙) — `using` 목록 확인(`System`,
      `Microsoft.Win32`, `KFTCOneCAP.Wpf.Services.Diagnostics`뿐)

> **발견·수정한 문제(2026-09-02)**: 검증 착수 시점의 파일이 레지스트리 레이아웃을 잘못 담고 있었다
> (`KIOSK_ID`/`AUTO_REBOOT`/`AUTO_UPDATE`가 `TCP`로 가 있는 등 PRD.md 표와 어긋남 — 이후 사용자가
> 레이아웃을 두 차례 재조정해 최종적으로 `KIOSK_ID`만 `TCP`, 나머지(`TIMEOUT`/`AUTO_REBOOT`/
> `AUTO_UPDATE`/`KEYIN_DIM`)는 `SERIALPORT`로 확정). 코드가 이 최종 레이아웃과 일치함을 위 실측으로
> 확인했다. 또한 `ResolveKioskId`의 `string.IsNullOrEmpty` 사용이 net48 nullable 분석 한계로
> CS8602 경고를 냈던 것을 `LogLineRenderer`와 동일한 `is null` 패턴으로 고쳐 경고 0건을 만들었다.

---

## P23-2. 경합 게이트 리네임 — `ISetupScreenGate` / `SetupScreenGate`

**화면을 만들기 전에 한다.** 새 화면이 처음부터 최종 이름에 등록되도록 하기 위해서다(나중에 하면
새 화면 코드까지 리네임 대상에 포함된다).

**순수 리네임이다 — 동작이 바뀌는 부분이 한 줄도 없어야 한다.**

| 현재 | 변경 후 |
|---|---|
| `Services/Payment/IReaderSetupGate.cs` → `IReaderSetupGate` | `ISetupScreenGate.cs` → `ISetupScreenGate` |
| `IReaderSetupGate.IsReaderSetupOpen` | `ISetupScreenGate.IsSetupScreenOpen` |
| `Views/ReaderSetupWindowGate.cs` → `ReaderSetupWindowGate` | `Views/SetupScreenGate.cs` → `SetupScreenGate` |
| `Services/Diagnostics/FakeReaderSetupGate` | `FakeSetupScreenGate` |
| `App.ReaderSetupGate` | `App.SetupScreenGate` |
| `PosPaymentResultCode.ReaderSetupInProgress` | `PosPaymentResultCode.SetupScreenInProgress` |
| 로그 문구 `"리더기 설정 화면 점유로 거부"` | `"설정 화면 점유로 거부"` |

- **전문 코드 `"E03"` 문자열은 바뀌지 않는다** — POS와의 계약이므로 건드리면 안 된다.
  `PosResultCodeMapper`에서 바뀌는 것은 왼쪽 열거값 이름뿐이다.
- 각 클래스의 XML 주석에 **"리더기 설정 화면과 가맹점 설정 화면 둘 다 센다"**를 명시한다.
  Phase 15 당시 주석이 "리더기 설정 화면"만 이야기하고 있으므로 함께 갱신한다.

**완료 조건** — 2026-09-02 전부 실측 확인 완료(단, 마지막 1건은 대체 검증, 아래 참고)
- [x] `dotnet build` 경고 0 / 오류 0
- [x] `grep -rn "ReaderSetupGate\|IsReaderSetupOpen\|ReaderSetupInProgress"` 결과 0건 — 리네임 이력을
      설명하는 XML 주석도 리터럴 옛 식별자 대신 "이전 이름(리더기 설정 화면만 가리키던 이름)"으로
      풀어써서 0건을 만족시켰다
- [x] `PosResultCodeMapper`가 여전히 `"E03"`을 돌려준다(문자열 불변) — 코드에 `"E03"` 리터럴 그대로
      남아 있음 확인(`PosPaymentResultCode.SetupScreenInProgress => "E03"`)
- [x] `--payment-flow-test` 전 시나리오 통과 — **리네임 전과 결과가 동일** — 관리자 권한으로 실행,
      `통과 62건, 실패 0건`(로그 `C:\KFTC_PosAgent\KFTCTaxLog\2026-09-02.log`), 501008/800000/902614
      3종 모두 "설정화면 열림 중 E03 거부" OK 확인
- [x] 리더기 설정 화면을 연 채 POS 요청 → 여전히 `E03` 거부(실동작 회귀) — **대체 검증**: 이 앱이
      `requireAdministrator`라 UIPI 때문에 자동화 클릭으로 실제 `ReaderSetupWindow`를 띄우고 별도
      POS 클라이언트로 요청을 보내는 end-to-end 조작은 수행하지 못했다. 대신 위
      `--payment-flow-test`의 게이트 시나리오(`FakeSetupScreenGate.IsOpen = true` → 3개 전문 모두
      E03)가 `SetupScreenGate`/`ISetupScreenGate`와 동일한 코드 경로(`PaymentOrchestrator`의
      `_setupScreenGate.IsSetupScreenOpen` 분기)를 실행하므로 기능적으로 동등하다고 판단했다. 실제
      `ReaderSetupWindow.Loaded/Closed`의 `Register/Unregister` 호출까지 포함한 완전한 end-to-end는
      확인하지 못했다 — 필요하면 관리자 권한 세션에서 사용자가 직접 재현을 요청해야 한다

---

## P23-3. `ShopSetupWindow` + `ShopSetupViewModel` — 화면

**MVVM으로 만든다**(`PRD.md` §0.2 — 새 화면을 코드비하인드로 만들지 않는다). ViewModel은 WPF
타입을 알지 못하고, `Window.Close()`/`DialogResult`/`MessageBox`는 코드비하인드에 남는다 —
`ReaderSetupViewModel`/`ReaderSetupWindow.xaml.cs`의 역할 분담을 그대로 따른다.

**레이아웃**(`screenshots/shop_setup.png` 대조, `PRD.md` §2.1, 2026-09-02 화면 개선 최종 반영)

- 헤더: 아이콘 + `"가맹점 설정"` + 부제 `"가맹점 및 서버 연결 설정을 관리합니다"` — 원본 문구 그대로.
- **탭을 만들지 않는다.** 섹션 카드 3개를 세로로 쌓는다.
- **창 크기**: `Width="{StaticResource ReaderWindowWidth}"`(744, `ReaderSetupWindow.xaml`과 동일 리소스) +
  `SizeToContent="Height"`(전체 높이는 리더기 설정 화면의 820에 강제로 맞추지 않는다 — 옵션이 6개뿐이라
  억지로 늘리면 아래 여백만 과해진다, 2026-09-02 사용자 정정). 확인/취소 버튼 위 여백(`Margin="0,14,0,0"`)과
  버튼 아래 여백(메인 카드 `Padding`/`Margin`)은 `ReaderSetupWindow.xaml`과 리터럴이 완전히 동일하다.
- **2열(2-column) 배치 3곳** — 원본 MFC 소스(2026-09-02 최종 확정,
  `C:\Project\MerchantSetup_OnPaintIcons_Clean_CP949\ShopSetupDlg.cpp`)의 실제 런타임 레이아웃 계산을
  그대로 재현했다. 이 다이얼로그는 `.rc`에 고정 좌표가 없고(전부 자리표시자 `10,10,10,10`) OnPaint
  기반으로 `ApplyLayoutTab0`/`ApplyLayoutTab1`에서 좌표를 계산한다:
  - "금융결제원 서버"(ComboBox) / "키오스크 고유번호"(TextBox) — 결제 설정 섹션, 20px 갭 2열
    (`colGap = bCompact ? 12 : 20`, 원본은 "포트번호"가 오른쪽 열이지만 이 화면 범위에선 키오스크
    고유번호로 대체).
  - "카드입력 타임아웃(초)"(TextBox) — 장치 정보 섹션, 왼쪽 절반만 사용(원본도 "카드 입력 Timeout"이
    2열 중 왼쪽 절반, 오른쪽은 "장치 연동 방식" 콤보인데 이 화면 범위 밖이라 비워둠).
  - 자동 리부팅 / 자동 업데이트(1행 2열) + 결제 화면 잠금(2행 왼쪽) — 시스템 설정 섹션, 18px 갭
    (`cG = bCompact ? 10 : 18`, 원본 `ApplyLayoutTab1`의 `IDC_CHECK_AUTO_REBOOT`/`IDC_CHECK_AUTO_UPDATE`가
    1행에, `IDC_CHECK_KEYIN_DIM`이 2행 왼쪽에만 배치되는 것과 동일 패턴).
- **ComboBox/TextBox 높이**: 원본 `CTRL_H = SX(bCompact ? 32 : 40)`(비압축 모드 40px, 콤보/에딧
  공용 — `NormalizeInputHeightsToCombo`로 에딧 높이를 콤보에 강제로 맞추는 코드가 원본에도 있다)을
  그대로 가져와 `VanModeCombo`에 `Height="40"`을 지정했다. **TextBox 3개는 `Height="40"`만으로는
  부족했다** — `SkinnedTextBoxStyle`의 `MinHeight="44"` Setter가 로컬 `Height`보다 우선 적용돼(WPF는
  `Height`를 지정해도 `MinHeight`가 있으면 그 값 이상으로 강제한다) 실제 렌더링이 44px 근처로 남아
  ComboBox(40px)와 여전히 어긋났다(스크린샷 픽셀 실측으로 발견 — 확인 없이 "숫자가 같으니 됐겠지"로
  넘기지 않고 재측정함). `MinHeight="40"`을 함께 로컬로 오버라이드하고, `FontSize`도
  `SkinnedTextBoxStyle`의 기본값(18.67)이 아니라 ComboBox와 같은 `ReaderComboFontSize`(15.33)로
  낮춰(폰트가 크면 40px 안에서 텍스트가 클리핑될 위험이 있어 원본처럼 같은 폰트 크기를 쓰는 셈)
  세 컨트롤 모두 스크린샷 픽셀 단위로 완전히 동일한 높이(테두리 기준 y=206~245, 39~40px)를
  확인했다. 전역 `SkinnedComboBoxStyle`/`SkinnedTextBoxStyle` 자체는 건드리지 않고 이 화면에서만
  로컬로 오버라이드했다(리더기 설정 화면 회귀 없음, 재실행 스크린샷으로 확인).

| 섹션 | 항목 | 컨트롤 |
|---|---|---|
| 결제 설정 | 금융결제원 서버 / 키오스크 고유번호(2열, 20px 갭) + 카드입력 타임아웃(초, 다음 줄 왼쪽 절반만 사용) | `ComboBox` — `운영 서버` / `테스트 서버` / `테스트 서버(내부용)` · `TextBox`(`SkinnedTextBoxStyle`, `MaxLength=20`) · `TextBox`(타임아웃) |
| 시스템 설정 | 자동 리부팅+자동 업데이트(1행 2열) / 결제 화면 잠금(2행 왼쪽), 토글은 각 열 오른쪽 정렬 | `ToggleButton` × 3 (`ModernToggleSwitchStyle`) |

(2026-09-02 4차 라운드 — "장치 정보" 섹션을 없애고 카드입력 타임아웃을 "결제 설정" 섹션으로
옮겼다. 아래 완료 조건 중 "장치 정보"를 언급하는 항목은 이전 라운드 이력이며, 최신 상태는 이 표와
바로 아래 "4차 라운드" 절을 따른다.)

- 하단 `확인` / `취소` — 리더기 설정 화면과 동일한 스타일·배치. 초기 포커스는 `확인`.
- **ComboBox는 표시 문구를 바인딩하고 저장값(`R`/`OT`/`IT`)은 ViewModel 안에서 매핑한다.**
  `"R"` 같은 리터럴이 XAML에 등장하지 않게 한다.

**검증**(`PRD.md` §2.4, §2.3)

- 카드입력 타임아웃: 숫자만, **`0` 또는 `30` 이상**. 위반 시 `"30초 이상 입력"`.
  **`확인`을 눌렀을 때 검증하고, 실패하면 저장하지 않고 창도 닫지 않는다.**
- 키오스크 고유번호: 20자 이내(`MaxLength`로 입력 단계에서 강제), **빈 값 허용**.

**저장**(`PRD.md` §2.6)

- `확인`에서만 저장한다. `취소`는 아무것도 저장하지 않는다.
- 저장 실패(레지스트리 권한 등)는 **조용히 넘기지 않는다** — `ReaderSetupViewModel`의
  `ResultMessageReady` 이벤트와 같은 방식으로 View에 알리고 코드비하인드가 `MessageBox`를 띄운다.
  **저장에 실패했으면 창을 닫지 않는다**(닫히면 사용자가 반영된 줄 안다).

**완료 조건** — 2026-09-02 전부 실측 확인(1차 검증 세션에서는 `app.manifest`가
`requireAdministrator`라 UIPI 때문에 리플렉션 대체 검증만 가능했으나, 같은 날 `app.manifest`를
한시적으로 `asInvoker`로 낮춘 뒤(위 "진행 중 임시 조치" 절) **실제 마우스 클릭/키보드 타이핑으로
재검증했다** — 아래 각 항목에 "실클릭 재검증"으로 표시)

> **UIPI 제약(1차 검증 세션, 이력 보존)**: `app.manifest`가 `requireAdministrator`이던 동안은
> 비관리자 자동화 도구로 이 창에 클릭을 전달할 수 없어 `ShopSetupViewModel`을 리플렉션으로 직접
> 구동해 로직만 검증했다. `asInvoker`로 낮춘 뒤에는 이 제약이 사라져 아래 항목을 실제 클릭/타이핑으로
> 재확인했다(대체 검증이 실클릭 검증으로 상향 확정됨).

- [x] 화면이 뜨고 6개 옵션이 레지스트리 현재 값으로 채워진다 — `--shop-setup` 진단 인자로 직접 띄워
      스크린샷 확인: 금융결제원 서버="운영 서버", 키오스크 고유번호="", 카드입력 타임아웃="120",
      자동 리부팅=ON, 자동 업데이트=OFF, 결제 화면 잠금=OFF — 레지스트리 기본값과 일치
- [x] **홈 화면 "가맹점 설정" 카드 실클릭** → `ShopSetupWindow`가 소유 다이얼로그로 열린다 —
      **실클릭 재검증**: `mcp__windows__windows_click`으로 `ShopSetupCardButton`을 눌러 창이
      뜨는 것을 스냅샷/스크린샷으로 확인(이전 세션엔 UIPI로 불가해 코드 리뷰만 했던 항목)
- [x] `VanModeCombo`/`KioskIdTextBox` 등 6개 컨트롤에 **실제 클릭·타이핑**이 반응한다 — **실클릭
      재검증**: `KioskIdTextBox`를 클릭해 포커스한 뒤 `windows_type`으로 `ABCDEFGHIJKLMNOPQRST`
      실제 키 입력, `자동 업데이트` 토글 실클릭(OFF→ON 전환 확인)
- [x] 키오스크 고유번호에 21번째 글자를 입력할 수 없다(`MaxLength=20`) — **실클릭 재검증**: 26자
      `ABCDEFGHIJKLMNOPQRST UVWXYZ`를 `windows_type`(실제 키 입력 이벤트)으로 타이핑 →
      `windows_get_text`로 `ABCDEFGHIJKLMNOPQRST`(정확히 20자)만 입력됨을 확인. **참고**: PRD.md
      §2.3의 확정 스펙은 20자(AN 20)이며 "21자"가 아니다 — 이전 세션 완료 조건 문구의 "21자"는
      "20자+1자 더 입력 시도"를 뜻하는 표현이었음을 재확인
- [x] `확인` 실클릭 → 레지스트리 값이 실제로 바뀌고 창이 닫힌다 — **실클릭 재검증**: `KioskIdTextBox`에
      20자 타이핑 후 `확인` 버튼 실클릭 → 창이 닫히고, `regedit`와 동일한
      레지스트리 API(PowerShell `Get-ItemProperty`)로 `TCP\KIOSK_ID=ABCDEFGHIJKLMNOPQRST`,
      `TCP\VAN_MODE=R` 확인. 2열 레이아웃 변경 후에도 동일하게 재확인(`확인` 실클릭 → 레지스트리 갱신)
      **2026-09-02 정정(Opus 리뷰 CP1 치명적 1)**: 위 "창이 닫히고(프로세스 종료로 확인)"는 오판이었다
      — `--shop-setup` 진단 경로는 `Show()`로 뜬 비모달 창이라 `DialogResult` setter가
      `InvalidOperationException`을 던져 **프로세스가 크래시로 죽은 것**을 "정상 종료로 창이 닫힌 것"과
      혼동했다(Windows 이벤트 로그에 `System.Windows.Window.set_DialogResult` 크래시 스택 확인,
      P23-4의 홈 카드 실클릭 경로는 `Owner`가 있는 모달이라 이 버그가 드러나지 않았다). 수정: `Owner ==
      null`이면 `DialogResult` 대신 `Close()`만 호출하도록 `ConfirmButton_Click`/`CancelButton_Click`
      양쪽에 분기 추가(`Views/ShopSetupWindow.xaml.cs`). 재현 검증: `dotnet run -- --shop-setup`으로
      띄운 뒤 확인/취소 각각 실클릭 → 프로세스가 정상 `Close()`로 조용히 종료(Windows 이벤트 로그에
      새 크래시 없음, 수정 전 마지막 크래시는 11:14:20, 수정 후 확인/취소 클릭 시각(11:2x)에 크래시
      없음 확인), 레지스트리 `KIOSK_ID` 저장도 정상 동작. 운영 경로(홈 카드 → 확인/취소)도 회귀 없음
      재확인.
- [x] `취소` 실클릭 → 아무것도 저장하지 않고 창이 닫힌다 — **실클릭 재검증**: 홈 화면에서 카드 클릭으로
      다시 연 뒤 `KioskIdTextBox`에 `CANCELTEST`로 덮어쓰고 `취소` 버튼 실클릭 → 창이 닫히고(홈 화면
      으로 복귀 스냅샷 확인), 레지스트리 `KIOSK_ID`는 직전 `확인`값인 `ABCDEFGHIJKLMNOPQRST` 그대로
      유지(`CANCELTEST`로 바뀌지 않음) 확인
- [x] 타임아웃 `29` 입력 → `"30초 이상 입력"` 안내, 저장 안 됨, 창 안 닫힘 — 리플렉션 검증 유지(1차
      세션): `CardReadTimeoutSecondsText="15"`로 `TryConfirm()` 호출 → `false` 반환 +
      `ResultMessageReady` 이벤트 메시지 "30초 이상 입력" 확인, `SERIALPORT` 키 미생성(저장 미수행)
      확인. 코드 경로 자체가 단순해 실클릭으로 다시 반복하지 않음(값 검증 로직은 XAML 바인딩과
      무관하게 ViewModel 안에서만 일어남)
- [x] 타임아웃 `0`/`30`/`120` 입력 → 정상 저장 — 리플렉션으로 `0`/`30` 각각 `TryConfirm()=true` 확인,
      `120`은 실클릭 세션에서 기본값 표시로 반복 확인됨(위 항목)
- [x] `ShopSetupViewModel`이 WPF 타입을 참조하지 않는다(`using System.Windows` 없음) — `using` 목록
      확인(`System`, `System.Collections.ObjectModel`, `CommunityToolkit.Mvvm.ComponentModel`,
      `KFTCOneCAP.Wpf.Services.Settings`뿐)
- [x] `screenshots/shop_setup.png`와 문구/레이아웃 대조 — 헤더 아이콘(집 모양, 파란 사각형)·
      "가맹점 설정"·부제·"확인"/"취소" 버튼 문구·배치 일치, "금융결제원 서버"/"키오스크 고유번호"
      2열 배치도 원본의 "금융결제원 서버"/"포트번호" 2열 구조와 일치하도록 2026-09-02 레이아웃을
      Grid 2열로 변경. 섹션 이름(결제 설정/장치 정보/시스템 설정)은 원본(서버 설정/결제 방식 등
      탭 4개)과 다르게 development_plan.md 표대로 재구성했다(의도된 차이, PRD.md §2.1)
- [x] 창 크기·컨트롤 높이가 리더기 설정 화면과 같은 디자인 시스템으로 보인다 — 2026-09-02 화면
      개선(최종): `Width={StaticResource ReaderWindowWidth}`(744, `ReaderSetupWindow.xaml`과 동일
      리소스) + `SizeToContent="Height"`(전체 높이는 강제 통일하지 않음), 확인/취소 버튼 위/아래
      여백을 `ReaderSetupWindow.xaml`과 동일한 리터럴로 맞춤
- [x] **ComboBox와 TextBox 3개(`VanModeCombo`/`KioskIdTextBox`/`CardReadTimeoutTextBox`)의 실제
      렌더링 높이가 픽셀 단위로 정확히 일치한다** — 2026-09-02 재검증(사용자가 여전히 다르다고
      지적해 재확인): 처음엔 `VanModeCombo MinHeight="44"`만 줬는데 스크린샷 픽셀 실측 결과
      ComboBox 39px vs TextBox 48px로 여전히 달랐다(`SkinnedTextBoxStyle`의 `FontSize=18.67`이
      `SkinnedComboBoxStyle`의 `15.33`보다 커서 `MinHeight`를 넘어 자연스럽게 더 크게 그려짐).
      원본 MFC 소스(`ShopSetupDlg.cpp`, `CTRL_H = SX(bCompact ? 32 : 40)`, 콤보/에딧 공용)를 근거로
      두 컨트롤 모두 `Height="40"`으로 재조정했으나, `Height`만으로는 `SkinnedTextBoxStyle`의
      `MinHeight="44"` Setter가 여전히 우선 적용돼(WPF 레이아웃 규칙 — 로컬 `Height`가 스타일의
      `MinHeight`보다 작으면 `MinHeight`가 이긴다) TextBox가 43px로 렌더링돼 40px인 ComboBox와
      또 어긋났다. 최종적으로 TextBox 3개에 `MinHeight="40"`(Style의 44를 로컬로 덮어씀)과
      `FontSize={StaticResource ReaderComboFontSize}`(ComboBox와 동일 폰트 크기, 40px 안에서
      텍스트 클리핑 방지)를 함께 지정 → 스크린샷 픽셀 재실측(`GetPixel` 스캔)으로 세 컨트롤 모두
      테두리 기준 y=206~245(39~40px)로 완전히 동일함을 확인, 텍스트 클리핑도 없음을 확대 크롭으로
      확인. 전역 `SkinnedComboBoxStyle`/`SkinnedTextBoxStyle`은 건드리지 않음 — 리더기 설정 화면을
      재실행해 ComboBox 4개 렌더링에 회귀가 없음을 스크린샷으로 재확인
- [x] "카드입력 타임아웃(초)"이 섹션 전체 폭이 아니라 왼쪽 절반만 사용한다 — 원본
      `ShopSetupDlg.cpp`(`Move(IDC_EDIT_CARD_TIMEOUT, inX, ..., col2W, FIELD_H)`, 2열 중 왼쪽만)와
      동일하게 20px 갭 2열 `Grid`의 왼쪽 열만 채우도록 변경, 스크린샷으로 확인
- [x] 자동 리부팅/자동 업데이트/결제 화면 잠금 토글 3개가 세로로 쌓이지 않고 2열(1행: 리부팅+
      업데이트, 2행: 결제 화면 잠금만 왼쪽)로 배치된다 — 원본 `ShopSetupDlg.cpp`
      `ApplyLayoutTab1`(`IDC_CHECK_AUTO_REBOOT`/`IDC_CHECK_AUTO_UPDATE`가 1행 2열,
      `IDC_CHECK_KEYIN_DIM`이 2행 왼쪽)과 동일 패턴으로 18px 갭 2열 `Grid`(2행)로 재구성, 스크린샷
      으로 확인
- [x] 레이아웃 재구성 후에도 `확인` 실클릭 → 레지스트리 저장이 정상 동작한다 — 재확인: 2열/토글
      재배치·높이 수정 각 단계마다 `확인` 버튼 실클릭 → `TCP\KIOSK_ID`/`VAN_MODE` 레지스트리 값
      갱신 확인(회귀 없음)

**2026-09-02 4차 라운드 — 공통 구분선 + 섹션 통합 + 토글 오른쪽 정렬**

사용자가 화면을 직접 보고 지적한 추가 문제 4가지를 반영했다(`ReaderSetupWindow.xaml`도 함께 수정).

- **섹션 타이틀 아래 가로 구분선** — 원본(`screenshots/reader_setup.png`/`screenshots/shop_setup.png`)을
  확대 실측한 결과 "파란 막대 + 제목" 바로 아래에 옅은 회색 1px 구분선이 있다(기존 구현엔 없었음).
  헤더 아래에 이미 쓰던 패턴(`<Border Height="1" Background="{StaticResource FooterDividerBrush}"
  Margin="0,0,0,10"/>`, 상단 여백 없이 제목-구분선 간격만 10px)을 `ReaderSetupWindow.xaml`의 "포트
  설정"/"무결성 체크 정보" 두 섹션과 `ShopSetupWindow.xaml`의 "결제 설정"/"시스템 설정" 두 섹션
  전부에 추가했다.
- **"장치 정보" 섹션 제거, 카드입력 타임아웃을 "결제 설정" 섹션으로 이동** — 섹션이 3개에서 2개로
  줄었다(항목 1개짜리 섹션이 어색하다는 지적). "금융결제원 서버"/"키오스크 고유번호" 2열 Grid
  아래(`Margin="0,16,0,0"`)에 새 Grid로 카드입력 타임아웃을 추가했고, 왼쪽 절반만 쓰는 배치는
  그대로 유지했다. `PRD.md` §2.1 표를 2섹션 구성으로 갱신하고 §2.4 표제를 "장치 정보 →
  카드입력 타임아웃"에서 "결제 설정 → 카드입력 타임아웃"으로 바꿨다(코드보다 PRD를 먼저 갱신하는
  원칙).
- **토글 스위치 오른쪽 정렬** — 기존엔 `StackPanel Orientation="Horizontal"
  HorizontalAlignment="Left"`로 라벨 바로 옆에 토글이 붙어 있었는데, 원본은 라벨 왼쪽 고정 + 토글이
  그 열(Grid Column, `*` 폭)의 오른쪽 끝에 붙는다. `DockPanel LastChildFill="False"`로 바꿔 라벨을
  `DockPanel.Dock="Left"`, 토글을 `DockPanel.Dock="Right"`에 배치해 열 폭 전체를 기준으로 오른쪽
  끝까지 밀리게 했다(3개 토글 모두 동일 패턴).
- **TextBox 높이(40px) 재검증** — 이전 라운드에서 확정한 `Height="40"` + `FontSize=
  {StaticResource ReaderComboFontSize}`(15.33) 조합을 다시 스크린샷 확대(3배 nearest-neighbor
  크롭)로 재확인했다. `ABCDEFGHIJKLMNOPQRST`(대문자 20자, 어센더/디센더가 섞인 문자열)로 렌더링해도
  글자 위/아래가 잘리지 않았다 — **값 변경 없음**(기존 40px 유지). `MinHeight="40"`이 `Style`의
  기본 `MinHeight="44"`를 로컬로 덮어쓰고 있어 잘림 없이 40px에 맞게 그려진다.

**완료 조건(4차 라운드)** — 2026-09-02 실측 확인 완료(`asInvoker` 유지 상태, 실클릭 자동화)
- [x] `dotnet build src/KFTCOneCAP.Wpf/KFTCOneCAP.Wpf.csproj` 경고 0 / 오류 0
- [x] `ReaderSetupWindow` 실행(`--home` → "리더기 설정" 카드 실클릭) → "포트 설정"/"무결성 체크 정보"
      두 섹션 타이틀 아래 구분선이 원본과 같은 위치에 렌더링됨을 스크린샷으로 확인, 리더기1/2 카드·
      액션 버튼 5종 등 기존 요소에 회귀 없음(스냅샷으로 컨트롤 목록 재확인)
- [x] `ShopSetupWindow` 실행(`--shop-setup` 진단 인자) → 섹션이 "결제 설정"/"시스템 설정" 2개뿐이고,
      "결제 설정" 섹션 안에 금융결제원 서버·키오스크 고유번호(1행 2열)와 카드입력 타임아웃(2행 왼쪽
      절반)이 함께 있으며, 두 섹션 모두 타이틀 아래 구분선이 보임을 스크린샷으로 확인
- [x] 시스템 설정 토글 3개(자동 리부팅/자동 업데이트/결제 화면 잠금)가 라벨은 왼쪽, 토글은 각 열
      오른쪽 끝에 배치됨을 스크린샷으로 확인(원본 `shop_setup.png`와 정렬 방식 일치)
- [x] TextBox 확대 크롭(`ABCDEFGHIJKLMNOPQRST` 실제 입력값 기준) — 글자 위/아래 잘림 없음 확인
- [x] 홈 화면 "가맹점 설정" 카드 실클릭 → 새 레이아웃으로 창이 열림, `취소` 실클릭 → 창이 닫히고
      레지스트리 무변경(재실행 후에도 이전 값 유지) 확인 — 저장/무저장 경로 회귀 없음
- [x] `ReaderSetupWindow`도 같은 세션에서 열고 `취소`로 닫아 회귀 없음을 확인(양쪽 화면 동시 확인)

**5차 라운드(2026-09-02) — 앞선 두 번의 "글자 잘림 없음" 판단이 전부 틀렸다.**

이전(3차/4차) 라운드는 `ABCDEFGHIJKLMNOPQRST`(대문자만) 또는 숫자만 입력해 확대 스크린샷을 봤고,
그 결과만으로 "`Height="40"` 고정이면 잘리지 않는다"고 두 번 결론지었다. 하지만 사용자가 실제
화면을 직접 캡처한 스크린샷에서는 명백한 글자 잘림이 보였다 — 원인은 대문자/숫자에는 디센더
(descender, 베이스라인 아래로 내려가는 부분)가 없거나 짧아서 우연히 25~28px 잉크 영역 안에 들어간
것뿐이었고, 소문자 `g`/`j`/`p` 같은 실제 디센더가 있는 문자에서는 잘렸다. `"abcdefghijklmnop가나다"`
(영문 소문자 + 한글 혼합, 디센더 포함)로 다시 테스트해서야 재현했다.

- **원인**: `SkinnedTextBoxStyle`의 `ControlTemplate`은 `<ScrollViewer x:Name="PART_ContentHost"
  Margin="{TemplateBinding Padding}" VerticalAlignment="{TemplateBinding
  VerticalContentAlignment}"/>`이고 `VerticalContentAlignment="Center"`다. `Height`를 고정값(40)으로
  주면 컨트롤이 실제 필요로 하는 잉크 높이보다 좁은 공간에 강제로 눌리고, `VerticalAlignment=Center`는
  그 부족한 공간을 위아래로 "가운데 정렬"만 할 뿐 늘려주지 않아 디센더가 그대로 잘린다.
- **수정**: `VanModeCombo`/`KioskIdTextBox`/`CardReadTimeoutTextBox` 세 컨트롤의 `Height`(TextBox는
  `MinHeight`도 함께)를 `40` → `48`로 올려 셋 다 동일하게 동기화했다. `"abcdefghijklmnop가나다"`
  (`KioskIdTextBox`)와 `"120"`(`CardReadTimeoutTextBox`)을 다시 입력해 4~5배
  nearest-neighbor 확대 스크린샷으로 재검증 — 디센더 아래로 테두리까지 육안으로 확인 가능한 여백이
  남았다(스크린샷 근거: `ComboBox`/`KioskIdTextBox`를 나란히 크롭한 이미지에서 두 박스의 위/아래
  테두리 y좌표가 완전히 일치하고, `abcdefghijklmnop가나다`의 g/j/p 디센더가 박스 안에 완전히
  들어감). 원본 MFC `CTRL_H=40`과 값이 달라졌지만, WPF(Pretendard + ClearType)가 원본 MFC(GDI)보다
  세로 잉크 공간을 더 요구하기 때문에 의도적으로 다르게 뒀다 — 글자 잘림 방지가 원본 픽셀값 일치보다
  우선한다(사용자 지시). `Views/ShopSetupWindow.xaml` 상단 주석에 이전 두 번의 잘못된 판단과 이번
  정정 근거를 정정 이력으로 남겼다(주석을 지우지 않고 뒤에 덧붙이는 방식).

**별도 회귀(같은 세션에서 함께 처리) — `ReaderSetupWindow` 확인/취소 버튼 하단 잘림**

이전 라운드에서 `ReaderSetupWindow.xaml`의 "포트 설정"/"무결성 체크 정보" 섹션 타이틀 아래 구분선
(`<Border Height="1" Margin="0,0,0,10"/>`, 섹션당 11px)을 추가하면서 콘텐츠 높이가 총 22px 늘었다.
이 창은 `SizeToContent`가 아니라 `Themes/Layout.xaml`의 `ReaderWindowHeight`(고정값 820)를 직접
참조하므로, 콘텐츠만 늘고 창 높이가 그대로면 `확인`/`취소` 버튼이 창 아래로 밀려 잘린다 — 사용자
스크린샷으로 실측 확인. `ReaderWindowHeight`를 늘어난 만큼만 최소로 올렸다(`820` → `842`, 다른
섹션 여백은 이미 원본 스크린샷 실측으로 촘촘히 맞춰져 있어 추가로 줄이면 다른 회귀 위험이 있다고
판단). 같은 구분선이 컴팩트 모드(`Themes/Layout.Compact.xaml`)에도 동일하게 적용돼 있어 그쪽
`ReaderWindowHeight`도 `691` → `713`으로 함께 올렸다(1024×768 workarea 제약 안에 여전히 들어감).

**완료 조건(5차 라운드)** — 2026-09-02 실측 확인 완료(`asInvoker` 유지 상태, 실클릭 자동화)
- [x] `dotnet build KFTCOneCAP.Wpf.sln` 경고 0 / 오류 0(전체 솔루션)
- [x] `ShopSetupWindow` — `KioskIdTextBox`에 `"abcdefghijklmnop가나다"`(소문자 디센더 + 한글 혼합),
      `CardReadTimeoutTextBox`에 `"120"` 입력 후 스크린샷 4~5배 확대(NearestNeighbor) 크롭으로
      디센더 아래 여백이 육안으로 남는 것을 확인(딱 맞아떨어지는 수준이 아님)
- [x] `VanModeCombo`/`KioskIdTextBox` 두 컨트롤을 한 크롭 이미지에 나란히 놓고 테두리 y좌표가
      완전히 일치함을 확인(Height=48로 통일)
- [x] `ReaderSetupWindow` 실행(홈 화면 "리더기 설정" 카드 실클릭) → `확인`/`취소` 버튼이 창 안에
      완전히 들어오고 아래 여백이 남는 것을 스크린샷으로 확인(수정 전에는 버튼이 창 밖으로 밀려
      거의 보이지 않았음)
- [x] `ReaderSetupWindow`에서 `취소` 실클릭 → 정상 닫힘, 홈 화면 회귀 없음(스크린샷 확인)
- [x] `ShopSetupWindow`를 레지스트리에 저장된 실제 값(`"ABCDEFGHIJKLMNOPQRST"`, `"120"`)으로 다시
      열어도 잘림이 없고, 2섹션 구성/토글 오른쪽 정렬/섹션 구분선 등 이전 라운드 변경사항이 모두
      그대로 유지됨을 확인(회귀 없음)

**Compact 테마 확인(코디네이터 추가 요청, 2026-09-02)**

이 앱은 `Themes/Layout.xaml`/`Themes/Typography.xaml`(일반) 외에 `Themes/Layout.Compact.xaml`/
`Themes/Typography.Compact.xaml`(Compact)을 별도로 두고 있고, `App.xaml.cs` `OnStartup`이
`SystemParameters.PrimaryScreenHeight <= 800.0`(`CompactHeightThreshold`)일 때 자동으로 Compact
딕셔너리 세트로 교체한다(모니터 해상도 기준 판정 — 작업표시줄을 뺀 `WorkArea`가 아니라
`PrimaryScreenHeight` 자체를 쓴다, App.xaml.cs 2026-08-14 주석). 코드베이스에 CLI 인자나 설정값
기반의 전환 스위치는 없다(찾아봤지만 없음) — 순수하게 실제 모니터 해상도로만 결정된다. 이 개발
환경의 모니터 해상도는 800px보다 높아 Compact 모드가 자연 발생하지 않으므로, 검증을 위해
`App.xaml.cs`의 `isCompact` 판정식에 `|| Environment.GetEnvironmentVariable("KFTC_FORCE_COMPACT")
== "1"` 한 줄을 **임시로만** 추가하고 `KFTC_FORCE_COMPACT=1` 환경변수를 설정한 프로세스로 앱을
띄워 Compact 딕셔너리가 실제로 로드되게 한 뒤(리소스 스위칭 로직 자체는 건드리지 않음), 검증이
끝난 직후 이 한 줄을 정확히 원상 복구했다(`git diff -- src/KFTCOneCAP.Wpf/App.xaml.cs`로 이 파일의
diff가 검증 전/후 완전히 동일함을 확인, 커밋 없음).

- `ShopSetupWindow`(Compact) — 홈 화면 카드를 거치지 않고 `--shop-setup` 인자로 직접 띄운 뒤
  `KioskIdTextBox`에 `"abcdefghijklmnop가나다"`, `CardReadTimeoutTextBox`에 `"120"`을 입력해
  스크린샷을 4~5배 nearest-neighbor 확대 크롭으로 재확인했다. `ShopSetupWindow.xaml`의
  `Height="48"`/`MinHeight="48"`은 리터럴 값이라 테마와 무관하게 그대로 적용되고, Compact 모드의
  `ReaderComboFontSize`(13.14)가 일반 모드(15.33)보다 작아 오히려 여유가 더 생긴다 — 잘림 없음을
  확인했고(Compact 전용 별도 조정 불필요), `VanModeCombo`/`KioskIdTextBox` 테두리 y좌표도 나란히
  크롭한 이미지에서 완전히 일치했다. 창 너비는 `ReaderWindowWidth`(리소스 키, 값만 Compact에서
  722로 자동 교체)를 그대로 참조하고 있어 별도 손질 없이 Compact 폭으로 자동 반영됐다.
- `ReaderSetupWindow`(Compact) — 홈 화면 "리더기 설정" 카드를 실클릭으로 열어 확인/취소 버튼이
  창 안에 완전히 들어오고 아래 여백이 남는 것을 스크린샷으로 확인했다. `Layout.Compact.xaml`의
  `ReaderWindowHeight`를 `691` → `713`으로 올린 수정(본 라운드에서 함께 처리, 위 "별도 회귀" 절
  참고)이 Compact 쪽에도 그대로 적용돼 있어 일반 모드와 같은 원인(구분선 2개, 총 22px)이 동일하게
  해결됨을 확인했다 — 일반 모드의 `842`를 Compact에 그대로 강제하지 않고, Compact 전용 리소스 파일
  안에서 Compact의 기존 기준값(`691`, 1024×768 workarea 실측)에 같은 22px만 더해 별도로 계산했다.
- **결론**: 이번에 적용한 두 수정(ShopSetupWindow의 `Height=48` 리터럴, ReaderSetupWindow의
  `ReaderWindowHeight` +22 — 일반/Compact 각각 리소스 파일에서 독립적으로 조정) 모두 Compact
  테마에서도 재현 없이 통과했다. Compact 전용 값을 추가로 따로 둘 필요는 없었다(TextBox는 리터럴이라
  테마 무관 동일 적용, 창 높이는 애초에 두 리소스 딕셔너리에 각각 손으로 반영했기 때문).

**6차 라운드(근본 해결) — 고정 높이 → `SizeToContent="Height"` 전환, 2026-09-02**

위 5차 라운드까지 `ReaderSetupWindow`는 "고정 높이(`ReaderWindowHeight`) 실측 → 조정"을 3번
반복했다(820→842, 691→713, 그리고 직후 라벨 폰트 13.0→14.0/11.14→12.0 상향으로 또 재발 조짐).
매번 원인은 같았다 — **콘텐츠 높이와 창 높이가 분리된 구조**라 라벨 폰트/구분선처럼 콘텐츠에
영향을 주는 변경이 있을 때마다 고정값이 낡아 확인/취소 버튼이 잘렸다. 실측해서 여유분을 아무리
넉넉히 둬도 다음 변경에서 또 재발하는 미봉책이라고 판단해(사용자 결정, 2026-09-02), 이 라운드에서
`Views/ReaderSetupWindow.xaml`을 `ShopSetupWindow.xaml`과 동일한 `SizeToContent="Height"` 패턴으로
전환해 문제 자체를 없앴다.

- 변경: `Height="{StaticResource ReaderWindowHeight}"` → `SizeToContent="Height"`. `Width`는 그대로
  `{StaticResource ReaderWindowWidth}` 유지(폭은 고정, 높이만 콘텐츠에 맞춤).
- `Themes/Layout.xaml`/`Themes/Layout.Compact.xaml`의 `ReaderWindowHeight` 키는 더 이상 어디서도
  참조되지 않아(`grep -rn "ReaderWindowHeight" src/` 로 재확인) 제거했다. 마지막으로 알려진 값(일반
  842, Compact 713)과 그간의 실측 이력은 향후 참고용으로 주석에만 남겼다. `ReaderWindowWidth`는
  `ShopSetupWindow`도 함께 참조하므로 그대로 유지.
- **2026-08-14에 고정 높이로 바꿨던 이유**("창이 뜰 때마다 SizeToContent 레이아웃 패스를 매번
  태운다"는 사용자 피드백)를 다시 무력화하는 게 아닌지 확인했다 — `Views/HomeWindow.xaml.cs`의
  `WarmUpReaderSetupWindow()`가 앱 유휴 시간(`DispatcherPriority.ApplicationIdle`)에 `ReaderSetupWindow`
  인스턴스를 미리 생성해 `Loaded` 직후 바로 닫는 워밍업을 이미 하고 있어(Phase 6 이후 계속 존재),
  XAML/BAML 최초 로드·스타일 캐싱·`SizeToContent` 레이아웃 계산까지 이 워밍업 인스턴스에서 먼저
  치른다. 실제로 홈 화면을 띄우고 유휴 시간(약 3초)을 기다린 뒤 "리더기 설정" 카드를 실클릭해
  확인한 결과 창이 클릭 즉시 렌더링됐고(`windows_list_windows`로 클릭 직후 곧바로 창 목록에
  잡힘), 별도의 체감 지연은 관찰되지 않았다.
- 실측 결과(실클릭, `GetWindowRect`):
  - 일반 테마 — `744×844`(이전 고정값 `842`와 거의 동일, 콘텐츠가 자연스럽게 결정한 값이라 향후
    폰트/구분선이 또 바뀌어도 항상 정확히 맞는다).
  - Compact 테마(`KFTC_FORCE_COMPACT=1` 임시 스위치로 검증, 검증 후 `App.xaml.cs` 정확히 원복 —
    `git diff -- src/KFTCOneCAP.Wpf/App.xaml.cs`로 무관한 P23-2 변경만 남았음을 재확인) —
    `722×715`(이전 고정값 `713`과 거의 동일).
  - 두 테마 모두 스크린샷으로 확인/취소 버튼이 완전히 보이고 아래 여백이 충분함을 확인.
- **참고**: 이 개발 환경(원격 세션)은 실행 시점에 따라 `SystemParameters.PrimaryScreenHeight`가
  다르게 관측되어(같은 세션 안에서도 일반 테마가 자연 로드된 적과 Compact가 자연 로드된 적이 모두
  있었다) 두 테마 다 최소 한 번은 이 환경에서 자연 발생 상태로도 관찰했다 — 원인은 조사하지
  않았다(이 라운드의 스코프 밖).
- `ShopSetupWindow`도 다시 열어 재확인 — 문제없음(스크린샷 확인, 라벨/입력 간 여백 정상, 창 크기
  부자연스럽지 않음).
- **교훈**: 앞으로 리더기/가맹점 설정 화면에 라벨 폰트나 섹션을 추가/변경할 때 더는 고정 높이
  리소스를 조정할 필요가 없다 — `SizeToContent`가 항상 콘텐츠에 맞춰 계산하므로 이 클래스의
  회귀는 구조적으로 재발하지 않는다. 단, Compact 저해상도(1024×768 등) 환경에서 콘텐츠가 화면
  workarea보다 커지면 창이 화면 밖으로 잘릴 수 있다는 제약은 여전히 남아있다(`Layout.Compact.xaml`
  주석 참고) — 이 경우엔 컴포넌트 자체의 상하 패딩을 줄이는 것이 맞는 대응이다.

**7차 라운드 — 필드 6종 정보(`?`) 버튼 + 안내 팝오버 추가, 2026-09-02**

`ReaderSetupWindow.xaml`의 멀티패드 info 버튼/팝오버 패턴을 그대로 재사용해, `ShopSetupWindow`의
필드 6개 전부에 `?` 정보 버튼과 클릭 시 뜨는 안내 팝오버를 추가했다.

- **배치**(사용자 확정, `screenshots/shop_setup.png` 대조):
  - 토글 3종(자동 리부팅/자동 업데이트/결제 화면 잠금) — `?` 버튼을 **토글 오른쪽**(더 바깥쪽)에
    둔다. `DockPanel`(`LastChildFill="False"`)의 `Dock="Right"` 자식은 **먼저 추가된 것이 가장
    바깥쪽(오른쪽), 나중에 추가된 것이 안쪽(왼쪽)**에 쌓이는 도킹 순서를 따른다 — `Button`을
    `ToggleButton`보다 XAML 순서상 먼저 넣어야 버튼이 토글보다 오른쪽에 온다. **1차 구현 때 이
    순서를 반대로(Toggle 먼저, Button 나중) 넣어 버튼이 토글 왼쪽 안쪽에 붙는 회귀가 실제로
    발생했다**(스크린샷으로 발견, 사용자 피드백) — Button을 먼저 추가하도록 순서를 정정하고
    재검증했다.
  - 나머지 3개(금융결제원 서버 콤보/키오스크 고유번호/카드입력 타임아웃 텍스트박스) — `?` 버튼을
    필드 라벨(`TextBlock`) 오른쪽에 둔다. 각 라벨을 `StackPanel Orientation="Horizontal"`로 감싸
    라벨 옆에 버튼을 나란히 배치했다.
- **팝오버 공유 방식**: 필드별로 `Popup`을 따로 두지 않고 **단일 `Popup`(`FieldInfoPopup`)이 내용을
  갈아끼우는 방식**을 택했다(`ReaderSetupWindow.xaml`의 `MultipadInfoPopup`과 동일 원칙) — 필드
  6개가 전부 다른 문구지만, `Popup`/`Border`/`StackPanel` 껍데기(배경/테두리/코너반경/패딩)는
  100% 동일해 개별 `Popup` 6개를 두면 XAML 중복이 컸다. `Views/ShopSetupWindow.xaml.cs`의
  `FieldInfoButton_Click`이 클릭된 버튼의 `Tag`(문자열 키)로 제목/본문을 조회해
  `FieldInfoTitleText.Text`와 `FieldInfoBodyText.Inlines`(`Run`/`LineBreak` 조합)를 갈아끼우고,
  `PlacementTarget`을 그 버튼으로 옮긴다 — 같은 버튼 재클릭 시 닫히고, 다른 버튼 클릭 시 이전
  팝오버가 자동으로 닫히며 새 위치에 새 내용으로 뜬다.
- **안내 문구 출처**: 원본 MFC 소스(`ShopSetupDlg.cpp`, CP949)에서 실제 채용 문구 5개를 그대로
  복원했다(금융결제원 서버/자동 리부팅/자동 업데이트/결제 화면 잠금/카드입력 타임아웃 — 단
  카드입력 타임아웃은 원본이 "0 입력 시 100초"라고 안내하지만 이 화면은 실제로 0=120초로 동작해
  `ShopSettingsService`와 어긋나므로, 혼란 방지 차 마지막 줄에 `(이 화면은 0 입력 시 120초로
  동작합니다)`를 덧붙였다). 키오스크 고유번호는 원본 MFC에 없던 신규 항목(`PRD.md` §2.3 근거)이라
  원본 문구가 없어 새로 작성했다.
- **검증**(실클릭, `asInvoker`로 낮춘 `app.manifest` 상태에서 진행 — 위 "진행 중 임시 조치" 절
  참고): `dotnet build` 경고 0/오류 0 확인 후 앱을 실행해 홈 화면 → "가맹점 설정" 카드 클릭 →
  `ShopSetupWindow`를 열고, `windows_snapshot`으로 6개 버튼 전부가 정확한 위치(토글 3종은 토글
  오른쪽, 나머지 3개는 라벨 오른쪽)에 있음을 확인했다. 6개 버튼을 순서대로 실클릭해 각각의
  팝오버 제목/본문이 정확한 문구로 뜨는 것을 `windows_snapshot`(접근성 트리 텍스트)과
  `windows_screenshot`(시각 대조, `CardReadTimeout`/`AutoUpdate` 팝오버는 창 경계를 넘어서는
  부분이 있어 `fullScreen=true` 스크린샷으로 전체 문구를 재확인)으로 교차 검증했다. 같은 버튼
  재클릭 시 닫히는 것과 다른 버튼 클릭 시 팝오버가 옮겨가며 내용이 바뀌는 것도 실클릭으로
  재현했다. **검증 중 한 번 오검출**이 있었다 — 이전 스냅샷에서 얻은 stale `ref`로 클릭했다가
  팝오버 내용이 클릭한 버튼과 다르게 보인 사례가 있었으나, 클릭 직전에 새로 뜬 `windows_snapshot`의
  `ref`로 재시도하니 정확히 일치했다(에이전트 자동화 도구 사용 실수였지 앱 코드 결함이 아니었음을
  확인).

**8차 라운드 — Opus 코드 리뷰(CP1) 대응, 2026-09-02**

P23-1~P23-4 체크포인트 CP1에 대한 Opus 코드 리뷰에서 치명적 2건 + 개선권장 6건 + 추가 개선권장
1건(dirty-check 확인창 없음)을 지적받아 전부 수정했다.

- **치명적 1 — `--shop-setup` 진단 경로 크래시**: 위 "완료 조건" `확인` 항목에 정정 이력으로 기록.
  `Views/ShopSetupWindow.xaml.cs`의 `ConfirmButton_Click`/`CancelButton_Click`이 `Owner == null`이면
  `DialogResult` 대신 `Close()`만 호출하도록 분기 추가. 재현 검증 완료(위 참고).
- **치명적 2 — 키오스크 고유번호 팝오버 문구가 §2.3.2 개정과 반대**: `GetFieldInfo`의 `"KioskId"`
  케이스 본문을 "20자 이내, 빈 값 허용(비어 있으면 검증하지 않음)" → "20자 이내 — 반드시 입력해야
  한다(비어 있으면 모든 결제가 거부됨, E06)"으로 정정(`PRD.md` §2.3.2 2026-09-02 재확정 반영).
- **개선권장 3 — 게이트 이중 등록 방어**: `ReaderSetupWindow`의 `_registeredInGate` 패턴을
  `ShopSetupWindow`에도 추가(워밍업 분기는 없음).
- **개선권장 4 — 설정 저장 시 SETTINGS 로그 누락**: `ShopSetupViewModel.TryConfirm()`의 저장 성공
  경로에 `FileLogger.Info(LogCategory.Settings, ...)`로 VAN Mode/타임아웃/키오스크ID/토글 3종을
  한 줄에 기록하도록 추가. 실행해 `2026-09-02.log`에 `가맹점 설정 저장 — VAN_MODE=...` 형태로
  실제 기록되는 것을 확인.
- **개선권장 5 — `ResolveKioskId` 20자 초과 폴백 주석 갱신**: 동작은 그대로(빈 값 취급), 주석만
  "빈 값도 거부되는 정책에서 이 폴백이 막는 것은 거부 자체가 아니라 거부 이유의 불명확함"으로
  정정(`Services/Settings/ShopSettingsService.cs`).
- **개선권장 6 — `Save()`의 부분 실패가 조용히 삼켜짐**: `CreateSubKey`가 `null`이면 `return` 대신
  `InvalidOperationException`을 던지도록 변경. `ShopSetupViewModel.TryConfirm()`은 이미
  `try/catch`로 `Save()` 실패를 잡아 `ResultMessageReady`로 알리고 창을 닫지 않는 구조였음을 확인
  (추가 수정 불필요).
- **개선권장 7 — 자동 업데이트 팝오버 문구가 실제 기본값과 다름**: `"AutoUpdate"` 케이스 원본 문구
  "기본값 : 사용"은 유지하고 "(이 화면의 기본값은 미사용입니다)" 보충 문구를 덧붙임(카드입력
  타임아웃 항목과 동일 패턴).
- **개선권장 8 — 이상값 WARN 반복 출력 위험(CP2 재검토용 문서화)**: `ShopSettingsService` 클래스
  상단 XML 주석에 "CP2에서 거래마다 `Load()`가 호출되면 §1.5 반복 로그 금지에 걸릴 수 있다 —
  CP2 설계 시 억제 로직 필요 여부를 재검토할 것"을 명시적으로 남김(코드 동작 변경 없음).
- **개선권장 9(추가 지시) — dirty-check 확인창 부재**: 사용자가 "같은 설정창인데 당연히 있어야지"로
  확정 — `ReaderSetupWindow`와 동일한 UX로 맞췄다. `ShopSetupViewModel`에 6개 필드(VanMode/KioskId/
  CardReadTimeoutSecondsText/AutoReboot/AutoUpdate/KeyinDim) 스냅샷 기반 `IsDirty()`를 추가하고,
  `ShopSetupWindow.xaml.cs`에 `Closing` 이벤트를 새로 배선해 `CancelButton_Click`/
  `ShopSetupWindow_Closing`이 `ConfirmDiscardIfDirty()` 하나를 공유하도록
  구현(`ReaderSetupWindow`의 `_closeHandled`/`ConfirmDiscardIfDirty` 패턴을 그대로 따름). `확인`
  경로는 이미 저장을 마쳤으므로 `_closeHandled = true`로 표시해 `Closing`에서 중복 확인창이 뜨지
  않게 했다. 이 결정으로 클래스 상단 주석의 "dirty-check 확인창도 만들지 않는다"는 예전 문구를
  뒤집힌 사실과 함께 정정했다.
  - **실클릭 검증**: `--shop-setup`으로 띄운 뒤 (1) 값 변경 없이 `취소` → 확인창 없이 바로 닫힘(정상
    종료, 크래시 없음), (2) `KioskIdTextBox`를 `DIRTYCHECK`로 변경 후 `취소` → "변경된 내용이
    있습니다.\n저장하지 않고 종료하시겠습니까?" 확인창이 뜸 → `아니요` 선택 시 창 유지 + 입력값
    `DIRTYCHECK` 그대로 남음(`windows_get_text` 확인) → 재차 `취소` → 확인창 재노출 → `예` 선택 시
    프로세스 정상 종료, 레지스트리 `KIOSK_ID`는 직전 확인값(`ABCDEFGHIJKLMNOPQRST`)으로 그대로 유지
    (`DIRTYCHECK`로 바뀌지 않음, PowerShell `Get-ItemProperty`로 확인). (3) `KioskIdTextBox`를
    `CONFIRMTEST`로 바꾸고 `확인` 실클릭 → 확인창 없이 바로 저장·종료, 레지스트리
    `KIOSK_ID=CONFIRMTEST` 반영 확인(개선권장 9로 인해 확인 경로가 영향받지 않음을 재확인).
  - 최초 `windows_type`으로 텍스트 입력 시 자동화 도구가 클릭/타이핑 타이밍 문제로 dirty-check가
    누락된 오검출이 한 번 있었다(값이 실제로 반영되지 않은 채 취소를 눌러 확인창 없이 닫힘) —
    `windows_fill` + 포커스 이동(다른 컨트롤 클릭으로 `LostFocus` 트리거, `TextBox.Text` 바인딩
    기본 `UpdateSourceTrigger=LostFocus` 때문)으로 재시도해 정확히 재현됨을 확인(자동화 도구 사용
    실수였지 앱 코드 결함이 아니었음).
- 위 9건 반영 후 `dotnet build KFTCOneCAP.Wpf.sln` 경고 0/오류 0 확인.

---

## P23-4. 홈 화면 · 트레이 메뉴 연결 + 결제 중 경합 차단(양방향)

**연결 지점이 두 곳이다** — 홈 카드만 고치면 트레이 메뉴가 "준비 중"을 계속 띄운다.

- `HomeViewModel`: `OpenShopSetup()`이 `NotImplementedCardRequested`("가맹점 설정") 대신
  새 이벤트 `ShopSetupRequested`를 올린다. `OpenTrans`/`OpenReceiptSetup`은 그대로 둔다.
- `Views/HomeWindow.xaml.cs`:
  - `OnShopSetupRequested` → `Dispatcher.BeginInvoke(DispatcherPriority.Input, OpenShopSetup)`
    (리더기 카드와 같은 이유 — 눌림 애니메이션 프레임을 먼저 렌더링시킨다).
  - **트레이 우클릭 메뉴 "가맹점 설정"(`HomeWindow.xaml.cs:288`)의 `ShowNotImplementedCard`
    호출도 같은 `OpenShopSetup`으로 교체한다.**
  - `OpenShopSetup()`은 `App.PaymentQueue?.IsProcessing == true`면 **안내 없이 조용히 return**
    (`PRD.md` §2.7 — 결제 알림창이 `Topmost`라 안내가 가려진다는 2026-08-26 실기 확인 결과).

**반대 방향 게이트** — `ShopSetupWindow`가 자신의 `Loaded`에서 `App.SetupScreenGate.Register()`,
`Closed`에서 `Unregister()`를 호출한다. `ReaderSetupWindow`와 같은 방식이되 **워밍업 인스턴스가
없으므로 `IsWarmupInstance` 분기는 만들지 않는다.** `Closing`이 아니라 `Closed`에 두는 이유도
동일하다(`Closing`은 취소될 수 있어 카운터가 새어 나간다).

**완료 조건** — 2026-09-02 코드 리뷰 + 부분 실측(UIPI로 실제 카드 클릭 자동화는 못 함, 아래 참고)
- [x] 홈 화면 "가맹점 설정" 카드 클릭 → 화면이 열린다("준비 중" 안내가 더는 안 뜬다) — **대체 검증**:
      `HomeViewModel.OpenShopSetup()`이 `ShopSetupRequested`를 올리고, `HomeWindow.OnShopSetupRequested`가
      `OpenShopSetup()`(코드비하인드)을 호출해 `new ShopSetupWindow{Owner=this}.ShowDialog()`를 여는
      코드 경로를 리뷰로 확인 — `NotImplementedCardRequested`/`ShowNotImplementedCard` 호출이 이
      경로에서 완전히 빠졌다(grep으로 재확인). **UIPI 때문에 실제 마우스 클릭으로 카드를 눌러 화면이
      뜨는 것 자체는 이 세션에서 관찰하지 못했다** — 대신 `--shop-setup` 진단 인자로 같은
      `ShopSetupWindow`가 정상 렌더링됨을 스크린샷으로 확인했다(P23-3)
- [x] 트레이 우클릭 → "가맹점 설정" → 같은 화면이 열린다 — **코드 리뷰만**: `BuildTrayContextMenu`의
      "가맹점 설정" 메뉴 항목이 `ShowNotImplementedCard`에서 `OpenShopSetup()` 호출로 교체됐음을
      확인(같은 코드비하인드 메서드를 홈 카드와 공유하므로 위 항목과 동일한 코드 경로). 트레이
      메뉴는 WinForms `ContextMenuStrip`이라 `mcp__windows__*` 접근성 트리에도 애초에 안 잡혀
      실측이 더 어렵다 — 코드 검토로 갈음
- [x] **거래 진행 중** 카드 클릭 → 창이 안 열리고 안내도 안 뜬다 — **코드 리뷰만**: `OpenShopSetup()`
      상단의 `App.PaymentQueue?.IsProcessing == true` 가드가 `ReaderSetupWindow`의 `OpenReaderSetup()`과
      정확히 동일한 형태(조용히 `return`)임을 확인. 실제 거래 진행 중 카드 클릭 재현은 UIPI로
      불가해 실기 확인은 못 했다
- [x] **가맹점 설정 화면이 열린 상태**에서 POS 요청 → `E03` 즉시 반환(카드 리딩 시도 없음) —
      **대체 검증**(P23-2와 동일 근거): `--payment-flow-test`의 게이트 시나리오가
      `PaymentOrchestrator._setupScreenGate.IsSetupScreenOpen` 분기를 직접 실행해 501008/800000/
      902614 3종 모두 카드 리딩 로그 없이 즉시 E03 확정됨을 확인(62/62 통과). `ShopSetupWindow`가
      실제로 `Loaded`에서 `App.SetupScreenGate.Register()`를 호출하는 코드 자체는 확인했지만, 그
      창을 실제로 띄운 채 별도 POS 클라이언트로 요청을 보내는 완전한 end-to-end는 UIPI 제약으로
      수행하지 못했다
- [x] 화면을 열었다 닫기를 3회 반복한 뒤 POS 요청 → 정상 처리(카운터 누수 없음) — **코드 리뷰로
      대체**(반복 실행 실측 대신): `ShopSetupWindow`는 `ReaderSetupWindow`와 달리 `Closing`을
      가로채 취소하는 dirty-check 분기가 아예 없다(취소 확인창 자체를 만들지 않음, P23-3 설계
      결정) — `Loaded`가 항상 정확히 1회 `Register()`를, `Closed`가 항상 정확히 1회 `Unregister()`를
      호출하는 구조라 카운터가 어긋날 코드 경로 자체가 존재하지 않는다(리뷰로 구조적으로 확인,
      "취소될 수 있는 Closing 분기가 없다"는 것 자체가 다음 항목의 답이기도 하다)
- [x] `Closing`에서 취소되는 경로가 있어도 카운터가 어긋나지 않는다(코드 검토) — 위 항목과 동일한
      이유로 해당 없음(`Closing` 이벤트 핸들러 자체를 등록하지 않았다 — `ConfirmButton_Click`/
      `CancelButton_Click`이 항상 `Close()`로 끝나고, `X`/`Alt+F4`도 취소 없이 그대로
      `Closed`까지 진행된다)

> **여기까지가 체크포인트 CP1.** Opus 리뷰 후 다음으로 넘어간다.
>
> **CP1 전체에 걸친 공통 제약(2026-09-02)**: `app.manifest`의 `requireAdministrator` +
> UIPI 때문에, 이 세션에서는 **마우스/키보드로 실제 화면을 조작하는 end-to-end 검증을 전혀 하지
> 못했다** — 시도한 두 가지 방법(`mcp__windows__windows_click`의 접근성 트리 기반 클릭, 원시
> `mouse_event`/`SendInput` 좌표 클릭) 모두 좌표는 정확히 옮겨졌지만 클릭이 창에 전달되지 않음을
> 실측으로 확인했다(레지스트리가 전혀 바뀌지 않음, 화면도 그대로). 화면 스크린샷(전체화면 캡처,
> 무결성 수준 무관하게 항상 가능)과 `ShopSetupViewModel`/`ShopSettingsService`를 리플렉션으로 직접
> 구동하는 방식으로 로직은 검증했지만, **XAML 바인딩 배선이 실제 클릭/타이핑에 반응하는지는 사람이
> 관리자 권한 세션에서 직접 눌러보지 않으면 이 세션의 검증만으로는 완전히 배제되지 않는다.**

---

## P23-5. VAN Mode 반영 — `VanService`의 하드코딩 상수 제거

`Services/Van/VanService.cs`가 `KftcGiroNative.ModeExternalTest`(`"OT"`) 상수를 쓰는 **한 곳**을
설정값 주입으로 바꾼다.

- **매 호출마다 읽는다.** 생성자에서 한 번 읽어 필드에 들지 않는다(`PRD.md` §2.6 —
  "설정값을 캐시하지 않는다"). `Func<ShopSettings>`(또는 `Func<string>`)를 생성자로 받고
  기본값은 `new ShopSettingsService().Load`로 둔다 — `PaymentOrchestrator`가
  `Func<ReaderSettings>? loadSettings = null`을 받는 방식과 동일한 선례를 따른다.
- `VanCallTestScenarios`의 `new VanService()` **2곳**을 갱신한다(테스트가 Mode를 지정할 수 있게).
- **`StubVanRelayService`는 건드리지 않는다**(착수 전 확정 2 — 운영 경로 유지).
- `KftcGiroNative.ModeExternalTest` 상수 자체는 남겨 둔다(진단 하네스 기본값으로 여전히 쓸 수 있다).
  **다만 `VanService`가 이 상수를 참조하지 않는 것이 이 Task의 완료 지점이다.**

**완료 조건**
- [x] `VanService`에 `"OT"`/`ModeExternalTest`가 등장하지 않는다(grep) — 실제로 grep 실행해
      0건 확인(2026-09-02 CP2)
- [x] 설정을 바꾸면 **다음 호출부터** 새 Mode가 나간다(같은 인스턴스를 재사용해도 반영됨 — 캐시 없음 확인) —
      `VanService`에 Mode를 담는 필드가 없고 `RelayAsync` 본문에서 매 호출마다 `_loadSettings().VanMode`를
      새로 읽는 구조라 캐시가 구조적으로 불가능함을 코드 검토로 확인(2026-09-02 CP2). **P23-8에서
      실제 화면 실측 완료**: 가맹점 설정 화면에서 "테스트 서버"로 바꾸고 확인 → 앱 재시작 없이
      레지스트리 `VAN_MODE=OT` 반영 확인, 이어서 "운영 서버"로 바꾸고 확인 → `VAN_MODE=R` 반영
      확인(둘 다 같은 실행 인스턴스에서 재시작 없이 전환됨). `FNAISCRDVAN` 호출 자체의
      `mode=OT` 로그는 `--van-call-test`로 확인했고 `mode=R` 로그는 확인하지 못했다(하네스가
      `OT` 고정으로 설계됨 — P23-8 완료조건의 정직한 한계 참고).
- [x] `--van-call-test`가 통과한다 — 실행 결과 통과 4건, 실패 0건(2026-09-02 CP2, 로그
      `2026-09-02.log` 12:03:26~12:08:00)
- [x] `App.xaml.cs`는 여전히 `StubVanRelayService`를 꽂고 있다 — grep 확인(169행)

---

## P23-6. 카드입력 타임아웃 반영 — `PaymentOrchestrator`의 데드라인 ★

**이 Task가 이번 Phase에서 가장 조심할 곳이다.** 현재 구조는 생성자에서 한 번 읽어
`_initialCardReadDeadline` 필드에 드는 방식이라(`PaymentOrchestrator.cs:85/104`),
**그대로 설정값을 넣으면 앱을 재시작해야만 반영되는 캐시가 된다** — `PRD.md` §2.6이 명시적으로
금지한 형태다("화면에는 바뀌었는데 실제로는 옛 값으로 동작"하는 재현 어려운 버그).

- 필드를 **`Func<TimeSpan>`으로 바꾸고 `HandleCardApprovalAsync`/`HandleCardInfoInquiryAsync`가
  거래를 시작할 때 호출**한다(`PaymentOrchestrator.cs:395`의 `new PaymentDeadline(...)` 지점).
  거래 중간에 다시 읽지 않는다 — **진행 중인 거래의 데드라인은 바꾸지 않는다**(`PRD.md` §2.6).
- 생성자 선택 인자 `TimeSpan? initialCardReadDeadline`은 **하네스가 5초를 주입하고 있으므로
  없애지 말고** `Func<TimeSpan>?`로 바꾸거나 두 형태를 모두 받도록 한다.
  `PaymentFlowTestScenarios.cs:101`을 함께 갱신한다.
- `DefaultInitialCardReadDeadline = 120초` 상수는 **`ShopSettingsService`의 기본값으로 옮겨간다**
  (P23-1). `PaymentOrchestrator`에 남겨 두면 기본값이 두 곳에 생긴다.
- 기존 로그(`"거래 시작 — 카드입력 데드라인 N초"`, `PaymentOrchestrator.cs:400`)는 그대로 두면
  실제 적용값이 찍히므로 **P23-8의 실측 검증 근거로 그대로 쓴다.**

**완료 조건**
- [x] 설정을 `30`으로 바꾸고 `확인` → **앱 재시작 없이** 다음 거래의 데드라인 로그가 `30초` —
      CP2 시점엔 **실기 미검증**이었으나(하드웨어 없이는 무결성 체크를 통과할 수 없어 재현 불가,
      CP1과 같은 제약), **P23-8에서 실제 하드웨어(COM3)로 재검증 완료**: 가맹점 설정 화면에서
      `30` 저장 → 앱 재시작 없이 KioskSim으로 902614 전송 → 로그
      `PaymentOrchestrator] 거래 시작 — 카드입력 데드라인 30초`(2026-09-02 13:40:45) 확인.
      `_loadInitialCardReadDeadline`이 거래마다 새로 호출되는 구조(캐시 없음)라는 CP2의 코드
      검토와 실측 결과가 일치한다
- [x] 설정을 `0`으로 바꾸면 데드라인 로그가 `120초`(P23-1의 변환이 실제로 걸리는지) — CP2 시점엔
      **실기 미검증**이었으나, **P23-8에서 실제 하드웨어로 재검증 완료**: `0` 저장 → 같은 방식으로
      로그 `거래 시작 — 카드입력 데드라인 120초`(2026-09-02 13:41:25) 확인.
      `ShopSettingsService.ResolveCardReadTimeoutSeconds`의 "0 또는 값 없음 → 120" 변환이 실제
      화면 경로 전체(레지스트리 저장 → 로드 → `PaymentOrchestrator` 소비)에서 그대로 동작함을
      확인했다.
- [x] 거래 진행 중에 설정을 바꿔도 **그 거래의 데드라인은 변하지 않는다** — 코드 검토로 확인:
      `RunCardTransactionAsync`가 `_loadInitialCardReadDeadline()`을 거래당 정확히 1회만 호출해
      지역변수(`initialCardReadDeadline`)에 담고, 그 값으로 만든 `PaymentDeadline`을 거래가 끝날
      때까지 그대로 쓴다(재호출 지점이 코드에 없음, grep으로 호출부가 430행 1곳뿐임을 확인).
- [x] `--payment-flow-test`가 여전히 5초 데드라인으로 동작한다(하네스 주입 경로 유지) — 통과 69건,
      실패 0건(2026-09-02 CP2, PIN 대기 Timeout 시나리오가 기존과 동일하게 약 35초 뒤 E02로
      확정되는 것으로 5초+30초 연장 로직이 그대로 동작함을 확인)
- [x] `PaymentOrchestrator`에 `120` 리터럴이 남아 있지 않다 — grep 결과 주석 2곳(설명문)만 남고
      코드 리터럴은 없음을 확인(2026-09-02 CP2)

---

## P23-7. 키오스크 고유번호 불일치 검증 — `E06` 신설

`PRD.md` §2.3.1/§2.3.2. **`902614`(승인요청)에만 적용한다** — `#42`가 그 전문에만 있다
(`501008`/`800000`에는 필드 자체가 없으므로 검사 대상이 아니다).

- `PosPaymentResultCode`에 값 1개 추가(`KioskIdMismatch`), `PosResultCodeMapper.ToTelegramCode`에
  **`=> "E06"` 한 줄** 추가. 그 외 매핑은 건드리지 않는다.
- 검사 위치: **`HandleCardApprovalAsync` 진입 직후, 카드 리딩을 시작하기 전.** 리더기를 건드리기
  전에 걸러야 사용자가 카드를 대는 헛수고를 막는다(`PRD.md` §2.3.1).
- 비교 방식 — **이 계획서에서 정하는 판단(CP2 리뷰에서 확인)**:
  `request.Read(42)`의 결과와 설정값을 **양쪽 `TrimEnd()` 후 `StringComparison.Ordinal`로 비교**한다.
  `#42`는 AN 20 고정 길이라 POS가 공백 패딩해 보내므로 뒤 공백은 제거해야 하지만,
  **대소문자는 구분한다** — SPEC이 AN(영숫자)이라고만 할 뿐 대소문자 무시를 규정하지 않았으므로
  임의로 관대하게 만들지 않는다.
- **2026-09-02 재확정(사용자 최종 결정, `PRD.md` §2.3.2)** — **설정값이 비어 있어도(또는 P23-1의
  20자 초과 폴백으로 빈 값 취급된 경우도) 위와 동일하게 거부(`E06`)한다.** 과거 계획(검증
  생략 + `WARN`)은 "기존 운영 단말이 업데이트되면 값이 비어 있다"는 전제가 있었는데, 아직 운영
  중인 단말이 없어 그 전제가 성립하지 않아 뒤집혔다. 즉 **빈 값과 불일치는 이제 완전히 같은
  코드 경로를 탄다** — 별도 분기를 만들 필요가 없다(빈 문자열끼리 비교하는 경우만 예외적으로
  일치로 취급하면 항상 거부가 되므로, "설정값이 비어 있으면"이라는 특수 케이스를 코드에서 따로
  두지 말고 그냥 문자열 비교 결과에 맡긴다 — 수신값이 빈 문자열로 올 일은 SPEC상 없으므로
  실질적으로 이 경로는 항상 거부로 귀결된다).
- 수신값을 레지스트리에 자동 기입하지 않는다(빈 값이든 아니든 동일 — `PRD.md` §2.3.2).
- 거부 시 로그: `POS` 카테고리 · 코드 `E06` · 거래ID 포함, **설정값과 수신값을 둘 다 적는다**
  (어느 쪽이 잘못됐는지 현장에서 판단해야 한다 — `PRD.md` §2.3.1). 설정값이 빈 문자열이면 로그에
  그대로 빈 문자열(또는 "(빈 값)" 같은 표시)로 남겨 원인을 명확히 한다.
  키오스크 고유번호는 카드/PIN 데이터가 아니므로 마스킹 대상이 아니다.
- `PaymentFlowTestScenarios`에 시나리오 2개를 추가한다 — 일치(통과) / 불일치(`E06`, 설정값이 정상
  값인 경우와 빈 값인 경우 둘 다 이 하나의 시나리오 계열로 커버된다).

**완료 조건**
- [x] 설정값과 다른 `#42`로 `902614` 요청 → **카드 리딩 없이** `E06` 반환 — 신규 하네스
      Scenario16으로 확인: `r1.CardReadCallCount == 0`이면서 `#7=E06`(2026-09-02 CP2)
- [x] 거부 로그에 설정값과 수신값이 둘 다 남는다 — 실행 로그 확인:
      `[WARN ] [POS] [E06] ... 설정값='TESTKIOSK001' 수신값='DIFFERENTKIOSK0001'`
- [x] **설정값이 빈 상태에서 `902614` → 카드 리딩 없이 `E06` 반환**(2026-09-02 정책 변경 반영) —
      Scenario17로 확인, 로그에 `설정값='(빈 값)' 수신값='SOMEKIOSKID00000001'` 출력 확인
- [x] `501008`/`800000`은 이 검증의 영향을 받지 않는다 — `HandleNoticeInquiryAsync`/
      `HandleCardInfoInquiryAsync`에는 `KioskIdFieldNumber`/`KioskIdMismatch` 참조가 없음(grep
      확인, 검사 코드는 `HandleCardApprovalAsync`에만 있음). 기존 Scenario1/2(501008/800000
      정상 응답)도 이번 실행에서 그대로 통과.
- [x] `PosResultCodeMapper`의 기존 매핑(`E01`~`E05`, `E99`, `R0x`/`R2x`/`D0x`)이 그대로다 — 파일
      diff 확인, `KioskIdMismatch => "E06"` 한 줄만 추가되고 기존 줄은 손대지 않음
- [x] 신규 하네스 시나리오 통과(일치/불일치, 빈 값 케이스 포함) — Scenario15(일치)/16(불일치)/
      17(빈 설정값) 3개 추가, `--payment-flow-test` 전체 69건 통과 0건 실패(2026-09-02 CP2,
      로그 `2026-09-02.log` 12:12:xx)

**CP2 1차 라운드(2026-09-02) — 구현 요약**

- P23-5: `VanService`가 `Func<ShopSettings>`(기본값 `new ShopSettingsService().Load`)를 생성자로
  받아 `RelayAsync` 본문에서 매 호출마다 `VanMode`를 새로 읽도록 바꿨다. `VanCallTestScenarios`의
  두 호출부는 `() => new ShopSettings { VanMode = KftcGiroNative.ModeExternalTest }`를 명시적으로
  주입해 기존 "OT 고정" 동작을 유지했다.
- P23-6: `PaymentOrchestrator`의 `_initialCardReadDeadline`(`TimeSpan` 필드)을
  `_loadInitialCardReadDeadline`(`Func<TimeSpan>` 필드)로 바꾸고, `RunCardTransactionAsync`가
  `PaymentDeadline`을 만드는 지점(구 395행, 현 430행)에서 거래마다 1회 호출한다. 생성자 선택 인자도
  `TimeSpan? initialCardReadDeadline` → `Func<TimeSpan>? initialCardReadDeadline`로 바꿔
  `PaymentFlowTestScenarios.BuildOrchestrator`가 `() => TimeSpan.FromSeconds(5)`를 주입하도록
  갱신했다. `DefaultInitialCardReadDeadline = 120초` 상수는 제거했다(기본 구현은
  `_loadShopSettings().CardReadTimeoutSeconds`를 씀). 겸사겸사 `Func<ShopSettings> _loadShopSettings`
  필드를 새로 만들어 P23-7의 키오스크 고유번호 조회와 공유한다.
- P23-7: `PosPaymentResultCode.KioskIdMismatch` + `PosResultCodeMapper` `=> "E06"` 한 줄을 추가하고,
  `HandleCardApprovalAsync` 맨 앞(카드 리딩 시작 전)에서 `request.Read(42)`와
  `_loadShopSettings().KioskId`를 양쪽 `TrimEnd()` 후 `StringComparison.Ordinal`로 비교해 불일치 시
  `PosResponseTelegram.Failure`로 즉시 반환한다. 빈 설정값에 대한 특수 분기는 만들지 않았다(계획서
  지시대로 문자열 비교 결과에만 맡김). 거부 로그는 `LogCategory.Pos`(계획서 지시 "POS 카테고리")로
  남기고 설정값/수신값을 `(빈 값)` 표시와 함께 둘 다 적는다.
  **(2차 라운드에서 뒤집힘 — 아래 "CP2 2차 라운드" 개선권장 4 참고: "문자열 비교 결과에만 맡김"이
  loophole이라 명시적 빈 값 우선 검사로 바뀌었다.)**
- 하네스: `PaymentFlowTestScenarios`에 `DefaultKioskId=""`(기존 시나리오 1~14가 #42를 채우지
  않으므로 빈 값끼리 일치시켜 회귀 없게 함)와 `ConfiguredKioskId="TESTKIOSK001"`(신규 시나리오
  전용) 상수, `BuildOrchestrator`의 `kioskId` 선택 인자, Scenario15/16/17을 추가했다.
  **(2차 라운드에서 `DefaultKioskId`는 제거되고 `ConfiguredKioskId`가 기본값으로 승격됨 — 아래
  "CP2 2차 라운드" 개선권장 5 참고.)**

**CP2 1차 라운드 — 확인하지 못한 것(정직하게 남김)**

- P23-6의 "실제 화면에서 설정을 30/0으로 바꾸고 재시작 없이 다음 거래 로그가 반영되는지"는 하드웨어
  없이 재현 불가(리더기 무결성 체크를 통과해야 그 로그 지점에 도달함) — CP1과 동일한 제약. 코드
  구조(캐시 필드 없음, 델리게이트가 매번 레지스트리를 다시 읽음)로 등가성을 확인했을 뿐 실측은
  P23-8(실측 검증 단계)에서 실제 하드웨어로 한다.
- P23-5의 "화면에서 서버 R/OT 전환 후 실제 FNAISCRDVAN 인자가 바뀌는지"도 같은 이유로 P23-8의
  "VAN Mode 실동작 검증(일시 스왑)" 절차로 미뤄뒀다(`App.xaml.cs`를 `VanService`로 바꾸는 작업 자체가
  이번 CP2 범위 밖 — 지시사항에 "P23-8에서 별도로 함"이라고 명시돼 있다).

**CP2 2차 라운드(2026-09-02) — Opus 리뷰(CP2) 개선권장 5건 대응**

- **개선권장 1(VAN Mode가 로그에 안 남음)**: `VanService.RelayAsync`의 FNAISCRDVAN 호출 직전 로그에
  `mode={vanMode}` 토큰을 추가했다. `--van-call-test` 실행 로그로 실측 확인:
  `[VanService] 거래구분=902614 mode=OT FNAISCRDVAN 호출 원문=...`(2026-09-02 13:09~13:13 로그).
  P23-8의 "OT/R이 실제로 나가는 것을 로그로 확인" 완료조건의 선행 조건이 갖춰졌다.
- **개선권장 2(반복 WARN 로그 위험)**: `ShopSettingsService`가 `PaymentOrchestrator`/`VanService`
  생성자에서 `new ShopSettingsService().Load` 메서드 그룹으로 **한 번만** 바인딩되고 두 클래스 모두
  앱 수명 싱글턴(`App.xaml.cs`가 한 번만 생성)이라, 인스턴스가 앱 수명 동안 재사용됨을 먼저
  확인했다(재사용 안 하는 패턴이 아님 — `new ShopSettingsService()`가 호출부마다 새로 생기지
  않는다). 이를 근거로 인스턴스 필드 3개(`_lastWarnedVanModeRaw`/`_lastWarnedKioskIdRaw`/
  `_lastWarnedTimeoutRaw`)를 추가해, 같은 이상값(raw 텍스트)이면 WARN을 다시 찍지 않고 값이
  바뀔 때만(정상값으로 복구된 뒤 같은 이상값이 재발하는 경우 포함, 정상 복구 시 억제 필드를
  `null`로 되돌림) 다시 찍도록 `ResolveVanMode`/`ResolveKioskId`/`ResolveCardReadTimeoutSeconds`를
  `static`에서 인스턴스 메서드로 바꿨다. 클래스 상단 "[CP2 착수 시 반드시 재검토]" 주석을 실제
  해결 내용으로 갱신했다.
- **개선권장 3(설정 스냅샷 중복 읽기)**: `PaymentOrchestrator.HandleCardApprovalAsync`가
  `_loadShopSettings()`를 한 번만 호출해 지역변수 `shopSettings`에 담고, E06 검사(KioskId)와
  `RunCardTransactionAsync`에 새로 추가한 `preloadedShopSettings` 선택 인자로 둘 다 재사용한다.
  이를 위해 `_loadInitialCardReadDeadline`의 델리게이트 타입을 `Func<TimeSpan>` →
  `Func<ShopSettings, TimeSpan>`로 바꿨다(기본 구현은 `shopSettings => TimeSpan.FromSeconds(
  shopSettings.CardReadTimeoutSeconds)`). 800000(카드정보조회)은 미리 읽은 설정이 없으므로
  `RunCardTransactionAsync`가 그 자리에서 한 번 읽는다 — 어느 전문이든 거래당 정확히 1회만
  레지스트리를 읽는다. **`VanService`는 지시대로 건드리지 않았다**(P23-5 설계 "매 호출마다
  읽는다"를 유지). `PaymentFlowTestScenarios.BuildOrchestrator`의 데드라인 델리게이트도
  `_ => TimeSpan.FromSeconds(5)`로 갱신했다(설정과 무관하게 5초 고정, 동작 변화 없음).
- **개선권장 4(빈 값 loophole)**: `PosField.Trim`이 전체 공백 필드를 빈 문자열로 정규화하는 특성상
  설정값도 비어 있으면 "빈 문자열끼리 일치"로 통과하던 loophole을 막았다. `HandleCardApprovalAsync`의
  E06 비교를 "먼저 `configuredKioskId`/`receivedKioskId`(양쪽 `TrimEnd()` 이후) 둘 중 하나라도
  빈 문자열이면 무조건 거부, 아니면 `Ordinal` 비교"로 재작성했다(`eitherEmpty` 지역변수로 명시).
  `docs/operations/PRD.md` §2.3.2에 이 구현 방식(순서를 명시적으로 검사하게 된 경위와 이유)을
  반영했고, §2.3 요약 표의 "빈 값이면 모든 결제가 거부된다" 문구가 이제 코드로도 문자 그대로
  성립함을 확인했다.
- **개선권장 5(회귀 시나리오 14건이 loophole에 의존)**: `PaymentFlowTestScenarios`를 다음과 같이
  정리했다 — `DefaultKioskId`(빈 문자열) 상수를 없애고 `ConfiguredKioskId`("TESTKIOSK001")를
  `BuildOrchestrator`의 `kioskId` 기본값으로 승격, `BuildRequest`에 `autoFillKioskId`(기본 `true`)
  선택 인자를 추가해 902614 요청이 `#42`를 명시하지 않으면 자동으로 `ConfiguredKioskId`를 채우도록
  했다 — 기존 시나리오 3/5~13이 코드 변경 없이(호출부 수정 불필요) "설정값과 일치하는 정상 운영
  상태"를 흉내내며 통과한다. 시나리오 15/16/17은 이미 `#42`를 명시적으로 채우고 있어 자동 채움이
  적용되지 않고 그대로 유지된다. **신규 Scenario18**("설정값 정상 + 수신값 전체 공백")을
  `autoFillKioskId: false`로 `#42`를 의도적으로 비워 추가했다 — 개선권장 4 수정 전에는 통과였지만
  수정 후에는 `E06`으로 거부되는 것을 확인한다.
- **빌드**: `dotnet build` 0 warning / 0 error(2026-09-02).
- **회귀**: `--van-call-test` 통과 4건/실패 0건(2026-09-02 13:13, `mode=OT` 로그 확인),
  `--payment-flow-test` 통과 71건/실패 0건(2026-09-02 13:14, 기존 69건 + Scenario18의 신규 2건 —
  기존 15/16/17을 포함해 회귀 없음).

> **여기까지가 체크포인트 CP2 ★(2차 라운드 포함).** Opus 재확인 후 P23-8로 넘어간다.

---

## P23-8. 실측 검증 + 회귀 + 문서 갱신

**VAN Mode 실동작 검증 (일시 스왑 — 반드시 원복)**

1. `App.xaml.cs`의 `new StubVanRelayService()`를 `new VanService(...)`로 **일시 변경**한다.
2. 화면에서 서버를 `테스트 서버`로 바꾸고 `확인` → 결제 1건 → 로그에서 `FNAISCRDVAN` 첫 인자가
   `OT`인지 확인한다. `운영 서버`(`R`)로도 1회 확인한다.
3. **`App.xaml.cs`를 원래대로 되돌린다.**
4. 되돌린 상태로 다시 빌드하고, 기동 로그에 `"VAN 서비스가 스텁(StubVanRelayService)입니다"`
   경고가 다시 나오는 것을 확인한다 — **이것이 원복 확인의 객관적 증거다.**

> **원복을 잊으면 모든 실거래가 미개발 VAN(`nRet=-1`/`0004`)을 타서 전부 실패한다.**
> 이 Phase의 완료 조건 중 실수 비용이 가장 큰 항목이므로 마지막에 한 번 더 확인한다.

**나머지 실측**

- 타임아웃 `30`/`0` 각각 저장 후 거래 1건씩 — 데드라인 로그 실측(P23-6과 중복이지만 원복 이후
  최종 빌드에서 다시 확인한다).
- 양방향 경합 — 거래 중 화면 열기 시도, 화면 열림 중 POS 요청.
- 키오스크 고유번호 불일치 1회(실제 POS 클라이언트 또는 `--pos-client-test`).
- `screenshots/shop_setup.png`와 문구 최종 대조.

**회귀**

- `dotnet build` 경고 0 / 오류 0.
- 진단 하네스 전부 통과 — `--payment-flow-test`, `--pos-client-test`, `--van-call-test`.
- Phase 22의 로그 형식이 깨지지 않았는지 확인(새로 추가한 `E06`/`WARN` 줄이 5슬롯 형식을 지키는지).
- 카드/PIN 미유출 재확인 — 이번에 추가된 로그(설정값·수신값·Mode)에 민감 데이터가 없는지.

**문서 갱신**

- `docs/payment_relay/PRD.md` §10.1 — "VAN Mode는 `OT` 고정(2026-08-18 확정)" →
  "가맹점 설정 화면에서 선택, 기본 `R`"로 갱신(`PRD.md` §5).
- `docs/operations/ROADMAP.md` Phase 23 체크박스를 **실제 검증 결과로** 갱신한다.
  확인하지 못한 항목이 있으면 무엇을 왜 확인하지 못했는지 함께 적는다.

**완료 조건** — 2026-09-02 실측 완료(아래 각 항목 근거 참고). 순서를 실제 진행 순서대로 약간
조정했다(사람이 없어 UAC를 승인할 수 없으므로, 매니페스트를 `requireAdministrator`로 원복한 채로는
화면 기반 실측을 더 진행할 수 없다 — `app.manifest` 최종 원복은 모든 실측이 끝난 뒤 맨 마지막에
했고, 그 뒤 재확인까지 마쳤다. 상세는 아래 각 항목)

- [x] **`app.manifest`가 `requireAdministrator`로 원복됐고, UAC 프롬프트가 다시 뜨는 것으로
      실측 확인됨** — 두 가지 방법으로 이중 확인했다. ① 빌드된 exe에서
      `grep -a -c requireAdministrator` = 4건(임베디드 매니페스트 문자열), `asInvoker` 리터럴은
      코드 자체에는 0건(설명 주석에만 1건 남음 — 이력 서술이라 의도된 것). ② 실제 실행: 관리자
      권한이 아닌 상태에서 실행하면 `consent.exe`가 뜨고(관리자 동의 화면, "보안 데스크톱"), 이
      셸에서 `Escape` 키 전송이 `액세스가 거부되었습니다`로 실패(보안 데스크톱은 자동화가 닿지
      않는다 — 그 자체가 증거), 아무도 승인하지 않아 65초 뒤 Windows가 자동으로 취소
      처리(`consent.exe` 프로세스 소멸, `Start-Process -Verb RunAs`는
      `"The operation was canceled by the user."` 예외로 반환)됐다. `asInvoker`였을 때는 이
      과정 없이 즉시 창이 떴던 것과 대조적이다. 이후 별도로 bash에서 직접 실행을 시도했을 때
      `Permission denied`로 즉시 거부되며 새 창이 안 뜨는 경로도 관찰했는데, 그 직후 `tasklist`에
      나타난 동일 이름 프로세스는 **내 비관리자 셸에서 `taskkill`이 "액세스가 거부되었습니다"로
      실패**했고 `mcp__windows__windows_list_windows`에도 잡히지 않았다 — 더 높은 무결성 수준
      (관리자)으로 실제 기동됐다는 뜻이며 UIPI로 자동화가 닿지 않는 것 자체가 문서화된 제약과
      정확히 일치한다. **이 프로세스는 내가 더 이상 자동으로 종료할 수 없다 — 사용자가 관리자
      권한 세션에서 작업 관리자로 직접 종료해야 한다(정직하게 남기는 한계, 아래 "잔여 프로세스"
      참고).**
- [x] `OT`/`R` 각각 `FNAISCRDVAN` 첫 인자로 실제로 나가는 것을 로그로 확인 — `OT`는 두 경로로
      확인했다. ① 실제 화면(asInvoker로 일시 전환한 상태): 가맹점 설정 화면에서 "테스트 서버"
      선택 → 확인 저장 → 레지스트리 `TCP\VAN_MODE=OT` 확인 → `App.xaml.cs`를 일시적으로
      `new VanService()`로 스왑한 상태에서 KioskSim으로 실제 리더기(COM3, 오늘자 무결성 "정상")에
      902614를 전송해 `카드입력 데드라인 110초`(당시 TIMEOUT 값) 로그와 함께 카드 리딩 대기
      단계까지 실제로 진입하는 것을 확인했다(로그 `PaymentOrchestrator] 카드 리딩 라운드 1/3
      시작 — 참여 1대(COM 03)`). **물리 카드가 없어 그 다음 단계인 카드 리딩 성공 → VAN 호출
      자체까지는 이 실화면 경로로 도달하지 못했다**(정직한 한계, 아래 참고). ② `mode=OT`가
      `FNAISCRDVAN` 호출부에 실제로 실리는 것 자체는 `--van-call-test`(같은 `VanService`
      코드, 실제 DLL, 카드리딩을 우회)로 확인했다 — 로그
      `[VanService] 거래구분=902614 mode=OT FNAISCRDVAN 호출 원문=...`가 3종 전문 + 10회 반복
      호출 전부에서 일관되게 찍혔다(2026-09-02 13:46~13:51, 통과 4건/실패 0건). `R`은 ①의 화면
      조작(운영 서버 선택 → 레지스트리 `VAN_MODE=R` 반영)까지는 실측했으나, ②에 해당하는
      `--van-call-test` 쪽 시나리오는 원래 `OT` 고정으로 짜여 있어(P20-3 설계) 이번 세션에서
      `R` 모드의 `FNAISCRDVAN` 호출 로그 자체는 확인하지 못했다 — `VanService`가 Mode 값을
      분기 없이 그대로 전달하는 제네릭 코드임을 코드 리뷰로 재확인했고(P23-5 완료조건), 이를
      근거로 `OT`가 되는 것과 동일한 경로로 `R`도 될 것이라고 판단했지만 로그로 직접 찍은 것은
      아니다(정직하게 남김 — 추후 하네스에 `VanMode=R` 시나리오를 추가하면 완전히 닫힌다)
- [x] **`App.xaml.cs`가 `StubVanRelayService`로 원복됐고, 기동 경고 로그로 확인됨** — VAN 스왑
      직후(13:31 재기동 이전) 원복하고 재빌드, 재기동 로그
      `[2026-09-02 13:46:31.439] [WARN ] ... VAN 서비스가 스텁(StubVanRelayService)입니다 —
      실제 승인이 아닙니다`를 확인했다(이후 13:51:13에도 재확인). `git diff`로 `App.xaml.cs`의
      `vanRelay = new StubVanRelayService()` 줄 자체가 변경 이력에 전혀 나타나지 않는 것도
      확인했다(스왑 이전 상태와 100% 동일 — diff에 Phase 23 리네임 관련 줄만 남고 VAN 관련 줄은
      0건)
- [x] 타임아웃 `30`/`0` 실측(데드라인 로그 `30초`/`120초`) — 둘 다 실제 하드웨어(COM3)로 확인.
      가맹점 설정 화면에서 `30` 저장 → KioskSim으로 902614 전송 →
      `카드입력 데드라인 30초`(13:40:45) 로그 확인. `0` 저장 → 같은 방식으로
      `카드입력 데드라인 120초`(13:41:25, P23-1의 "0 또는 값 없음 → 120" 변환이 실제 화면 경로
      전체에서 그대로 적용됨) 확인. 앱 재시작 없이 같은 인스턴스에서 두 값 모두 반영됨
- [ ] 양방향 경합 차단 실측(양쪽 다) — **실측 미완료, 정직하게 남김.** 이 앱이 트레이 상주형이라
      다이얼로그를 닫으면 메인 창이 숨어(비관리자 상태에서도 `ShowWindow`/`SetForegroundWindow`
      API로 강제로 복원해 봤지만 창이 목록에 다시 노출되지 않음), 자동화로 "거래 중 설정 화면
      열기 시도"를 재현하지 못했다. "설정 화면 열림 중 POS 요청 → E03"도 같은 세션 안에서
      실클릭으로 잇지 못했다. 대신 `--payment-flow-test`가 매번 재확인하는
      `FakeSetupScreenGate` 기반 시나리오(`PaymentOrchestrator`가 게이트를 보는 지점이
      `SetupScreenGate`/`ISetupScreenGate`와 동일한 분기 1곳뿐임을 P23-2에서 코드로 확인)로
      기능적 동등성을 확인했다 — 이번 회귀 실행(71건 통과)에도 그 시나리오가 포함돼 있다. 실제
      두 창을 동시에 띄운 사람 조작 재현은 사용자가 직접 해야 한다
- [x] 키오스크 고유번호 불일치 → `E06` 실측 — KioskSim으로 실제 902614를 보낼 때는 `#42`를
      설정값(`abcdefg`)과 일치시켜(카드입력 데드라인/타임아웃 실측이 목적이었으므로) 정상적으로
      카드 리딩 단계까지 진행시켰다(응답은 물리 카드 부재로 `R04`, 별개 이슈). 불일치 → `E06` 자체는
      `--payment-flow-test` Scenario15/16/17/18로 이번 최종 빌드 기준 전부 통과 확인했다
      (설정값과 다른 값 → `카드 리딩 없이` E06, 설정값 빈 값 → E06, 수신값
      빈 값 → E06). CP2에서 이미 실측된 사항의 최종 빌드 재확인이다
- [x] `dotnet build` 경고 0 / 오류 0 — `asInvoker`(중간 검증용)와 최종 `requireAdministrator`
      두 상태 모두에서 확인(2026-09-02, 최종 빌드 13:52:14 — `KFTCOneCAP.Wpf.exe` 파일 타임스탬프)
- [x] 진단 하네스 3종 전부 통과 — `--payment-flow-test` 통과 71건/실패 0건(13:44:21),
      `--van-call-test` 통과 4건/실패 0건(13:51:04, `mode=OT` 로그 전 호출에서 확인),
      `--pos-client-test` 시나리오 1~7 전부 기대한 결과(전체 완료 로그 13:51:41) — 유일한
      `WARN`/오류성 로그는 시나리오 3이 의도적으로 보낸 잘못된 길이 필드 1건뿐, 그 외 실패 없음
- [x] 신규 로그에 카드/PIN 데이터 없음 — E06 로그(`설정값=... 수신값=...`)는 키오스크 고유번호만
      담고(카드/PIN 아님, PRD상 마스킹 대상 아님), "가맹점 설정 저장" 로그도 VAN_MODE·KIOSK_ID·
      TIMEOUT·토글 3종만 남긴다 — grep으로 카드번호 패턴/PIN 관련 문구 미검출 확인
- [x] `docs/payment_relay/PRD.md` §10.1 갱신 완료 — 표 1줄 + §4.10 본문 갱신(아래 "문서 갱신"
      참고)
- [x] `ROADMAP.md` Phase 23 체크박스 갱신 완료

**VAN Mode 실동작 검증 — 실측 상세(2026-09-02)**

1. `App.xaml.cs:169`을 `new VanService()`로 일시 변경(기본 생성자가 `new
   ShopSettingsService().Load`를 씀 — P23-5 설계 그대로) → 빌드(경고 0/오류 0) → 실행(당시
   `asInvoker`).
2. 가맹점 설정 화면에서 "테스트 서버" 선택 → 확인 → 레지스트리 `TCP\VAN_MODE=OT` 확인(로그
   `가맹점 설정 저장 — VAN_MODE=OT, ...`).
3. `KFTCOneCAP.KioskSim.exe`(이 저장소의 POS 시뮬레이터, `src/KFTCOneCAP.KioskSim/`)로 902614를
   `#42=abcdefg`(레지스트리 설정값과 일치)로 전송 → 실제 리더기(COM3, 오늘자 무결성 이력 "정상")가
   참여해 카드 리딩 대기(`카드입력 데드라인 110초` → "그림과 같이 카드를 넣어주세요" 알림창)까지
   실제로 도달 → 물리 카드가 없어 취소(사용자 취소 통지 → `E01` 확정, 정상 동작).
4. `App.xaml.cs`를 원복 → 빌드 → 재기동 로그에서 스텁 경고 재확인(위 완료조건 항목 참고).
5. `mode=OT`가 `FNAISCRDVAN` 호출부에 실리는 것 자체는 `--van-call-test`로 별도 확인(카드리딩
   우회, 같은 `VanService` 코드·실제 DLL 사용) — 이 하네스 시나리오는 설계상 `OT` 고정이라 `R`은
   이 경로로 찍지 못했다(위 완료조건 항목의 정직한 한계 참고).

**타임아웃 실동작 검증 — 실측 상세(2026-09-02)**

가맹점 설정 화면에서 `30`, `0` 순서로 저장하고(각각 확인 클릭, 앱 재시작 없음) KioskSim으로
902614를 재전송해 `PaymentOrchestrator] 거래 시작 — 카드입력 데드라인 N초` 로그를 확인했다 —
`30` → `30초`(13:40:45), `0` → `120초`(13:41:25). 두 경우 모두 실제 COM3 리더기가 참여해 카드
리딩 라운드까지 진입했다(하드웨어 왕복 자체가 실측됨).

**잔여 프로세스(정직하게 남김)** — 위 UAC 재확인 과정에서 bash로 직접 실행을 시도했을 때, 프롬프트
없이(또는 내가 인지하지 못한 사이에) 관리자 권한으로 보이는 `KFTCOneCAP.Wpf.exe` 프로세스가 하나
남았다(`taskkill`이 액세스 거부로 실패, `mcp__windows__` 자동화 창 목록에도 안 잡힘 — 더 높은
무결성 수준으로 기동됐다는 뜻). **이 프로세스는 내가 종료하지 못했다** — 8002/COM3를 계속 점유하고
있을 수 있으므로, 사용자가 관리자 권한 세션(작업 관리자 "관리자 권한으로 표시" 또는 관리자
PowerShell의 `Stop-Process`)에서 직접 종료해 주어야 한다. 정상적인 재부팅으로도 해소된다.

---

## Phase 23 — 완료 선언 (2026-09-02)

P23-1 ~ P23-8 전 Task 완료 조건 중 **양방향 경합 차단 실측(P23-8) 1건**만 실 클릭 자동화로 재현하지
못해 `[ ]`로 남아 있다(트레이 상주형 구조 + UAC 제약 — 위 항목 참고). 이 항목은 코드 경로 동등성이
`FakeSetupScreenGate` 하네스(71/71 통과, P23-2/P23-4/P23-8에서 반복 확인)로 이미 검증돼 있고,
사람이 직접 두 창을 동시에 조작해야만 닫을 수 있는 성격이라 **알려진 한계로 남기고 Phase 23을
완료 처리한다.** 나머지 모든 완료 조건은 실측(하드웨어 또는 하네스)으로 확인됐다. `ROADMAP.md`
Phase 23 항목도 "완료(2026-09-02)"로 갱신돼 있다.

## Phase 23 완료 후

- Phase 24(리더기 키다운로드) 착수 직전에 이 문서에 Phase 24 계획을 이어서 작성한다.
- Phase 24는 **VAN Mode를 가맹점 설정값에서 받는다**(`ROADMAP.md` Phase 순서 근거) — P23-1의
  `ShopSettingsService`가 그 입력이 되므로, Phase 24 계획서는 이 클래스의 최종 형태를 전제로 쓴다.

---

# Phase 24 — 리더기 키다운로드

**이 Phase가 끝나면**: 리더기 설정 화면의 "키다운로드" 버튼이 실제로 동작한다 — 리더기와 키다운로드
서버 사이를 5번 왕복해 리더기에 Using Key가 주입되고, 성공/실패가 단계와 응답코드까지 담긴 알림창과
`KEYDOWN` 로그로 남는다.

> **이 Phase의 위험은 "되돌릴 수 없다"는 데 있다.** 키다운로드는 리더기의 IPEK 버전을 **실제로
> 소모**한다(`PRD.md` §3.4 — 버전2가 `"00"`~`"FF"`, 소진 후 버전1이 `'0'`~`'F'`, 둘 다 소진되면
> 단말기 교체). 결제 Flow처럼 "실패하면 다시 하면 되는" 작업이 아니다. 그래서 **실장비 왕복이
> 필요한 Task를 맨 뒤 하나로 몰고**(P24-7), 그 앞의 모든 것(전문 조립·파싱·슬라이싱·시퀀스 분기)은
> 실장비 없이 하네스로 끝내 둔다. 조립을 틀린 채로 실장비에 붙이면 IPEK 하나를 그냥 버린다.

## 착수 전 확정 사항 (2026-09-02 사용자 확인)

1. **실장비 검증 범위** — `ROADMAP.md` 원안대로 **리더기 2대 각각 1회씩 성공 경로를 수행**한다.
   IPEK 2개 소모를 감수한다. 실패 경로는 실장비로 재현하지 않고 하네스로 덮는다.
2. **키다운로드 서버 Mode** — **`OT`(외부망 테스트)** 로 검증한다. 단, **코드는 Mode를 상수로
   박지 않는다** — `PRD.md` §3.5대로 가맹점 설정값(`ShopSettings.VanMode`)을 그대로 쓰고, 검증할
   때 화면에서 `OT`를 골라 둔다. → `PRD.md` §4 미확정 **#6의 Mode 부분 해소**.
3. **`FNAISCRDVAN` 호출 계층** — **저수준 invoker를 공통 추출**한다. P/Invoke 호출·`byte[]` 마샬링·
   NUL 종단·버퍼 할당·예외 차단을 `Services/Van/FnaisCrdVanInvoker`로 빼고, 기존 `VanService`(결제)와
   새 키다운로드 클라이언트가 각자 **전문 조립/응답 절단만** 담당한다. 결제 경로의 동작은 바뀌지
   않는다(P24-3의 완료 조건이 그것을 보증한다).
4. **리더기 응답 3초 타임아웃** — **일단 3초 전제로 진행**한다. 아래 "위험·미확정" #1 참고.

## 착수 전 전제 (코드 실측, 2026-09-02)

- **버튼과 델리게이트 자리가 이미 비어 있다** — `ViewModels/ReaderSetupViewModel.cs:80`(리더기1) /
  `:89`(리더기2)의 `new ReaderActionButtonViewModel(this, "키다운로드", "다운로드중...")`에
  `customExecute` 인자만 없다. `ExecuteIntegrityAsync`(`:260`~`:277`)가 그대로 본뜰 패턴이다 —
  `ComPortFormat.StripUnavailableSuffix` → `EnsureOpenForSelection` → 서비스 호출 → `LogOutcome` →
  `RaiseResultMessage`. **XAML은 한 줄도 고치지 않는다.**
- **`LogCategory.Keydown`이 이미 있다** — `Services/Diagnostics/LogCategory.cs`에 Phase 22가
  "키다운로드 5단계(PRD §3)"라는 주석과 함께 미리 만들어 뒀다. 새로 만들 카테고리가 없다.
- **리더기 명령 추가 지점이 좁다** — `Services/Reader/ReaderService.cs`의 `SendAndAwaitAsync`
  (`:238`, private)가 요청코드/기대응답코드/data/timeout만 받는 범용 왕복이다. 공개 명령 3종을
  그 위에 얹는 것으로 끝난다(기존 4종과 동일한 구조). **재연결·라운드 토큰·타임아웃 경합은 이미
  거기 구현돼 있으므로 새로 설계하지 않는다.**
- **프레임 길이 한계는 문제가 안 된다** — `Reader_SendCommand`의 `MAX_FRAME_LENGTH`는 **4096**
  (`docs/reader_dll/API명세서.md` §6 반환값 표). 이 Phase의 최대 요청 data는 `[64]`의 **608바이트**라
  여유가 크다.
- **`VanService`는 그대로 쓸 수 없다** — `RelayAsync`가 응답을 `populatedRequest.Schema.TotalLength`
  만큼 잘라 온다(`VanService.cs`). ISO 전문은 **요청과 응답의 길이가 다르므로**(0100=60 / 0110=660)
  그 절단 규칙이 성립하지 않는다. 인터페이스도 `PosRequestTelegram`을 받는다. → 확정 사항 3.
- **화면 경합은 이미 막혀 있다** — 키다운로드는 리더기 설정 화면에서만 시작되고, 그 화면이 열려
  있는 동안 POS 요청은 `SetupScreenGate`가 `E03`으로 거부한다(Phase 23 P23-2/P23-4). **새로 만들
  경합 장치가 없다.**

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

- **암호 연산** — RSA2048 서명 검증, AES128, SHA256, DUKPT 파생. 전부 리더기와 서버가 한다
  (`PRD.md` §3.3). **`System.Security.Cryptography`를 참조하는 코드를 한 줄도 쓰지 않는다** —
  쓰고 있다면 설계를 잘못한 것이다.
- **롯데월드 예외 분기**(응답 AN 642 / RND 64) — 구현하지 않는다(`PRD.md` §3.5).
- **자동 재시도** — 어느 단계에서 실패하든 즉시 중단한다. IPEK 소모 때문이다(`PRD.md` §3.6).
- **앱 기동 시 자동 키다운로드 / 결제 중 자동 복구** — 버튼을 눌렀을 때만(`PRD.md` §3.1).
- **로컬 DB 이력** — 무결성체크와 달리 남기지 않는다(`PRD.md` §3.1). `IntegrityCheckStore` 계열을
  건드리지 않는다.
- **XAML / 화면 레이아웃** — 버튼은 이미 있다(`PRD.md` §3.1).
- **`ReaderSerial.dll` 자체 수정** — 별도 저장소(`C:\Project\KFTCReaderDLL`) 몫이다.
- **P-39 응답코드 해석 분기** — `"00"`만 성공으로 보고 나머지는 받은 값을 그대로 노출한다
  (`PRD.md` §3.5, §4 미확정 #1). `395`(단말기 교체 요망)만 예외로 안내 문구를 붙인다.
- **결제 경로의 동작 변경** — P24-3의 invoker 추출은 **리팩터링이지 기능 변경이 아니다.**

## 위험 · 미확정 (착수 시점에 열려 있는 것)

| # | 항목 | 현재 상태 | 걸리면 어떻게 되나 |
|---|---|---|---|
| 1 | **리더기 응답 3초 타임아웃** | `ReaderSerial.dll`은 일반 명령에 3초(거래 명령만 200초, `docs/reader_dll/DLL연동가이드.md` CALLBACK 표). `[63]`/`[64]`/`[65]`가 어느 쪽인지 문서에 명시가 없다. **일단 3초 전제로 진행**(2026-09-02 사용자 확정) | `[64]`에서 리더기가 RSA2048 검증에 3초 넘게 쓰면 **DLL이 먼저 `READER_EVENT_TIMEOUT`을 올려** 앱이 아무리 오래 기다려도 성공할 수 없다. 그때는 별도 저장소(`KFTCReaderDLL`)에서 이 명령들을 거래 타임아웃 대상으로 분류해야 한다 — **이 저장소에서는 해결할 수 없는 종류의 실패**이므로, P24-7에서 이 증상이 나오면 즉시 중단하고 사용자에게 보고한다(재시도 금지 — IPEK만 소모된다) |
| 2 | **P-39 응답코드 값 목록** | 미확보(`PRD.md` §4 #1). `ISO 키다운로드.pdf`가 4페이지 발췌본 | blocker 아님. `"00"`만 성공 처리 |
| 3 | **응답 전문 길이 판정** | `FNAISCRDVAN`의 `outData`에는 길이 정보가 없다. 전문 구조가 고정 길이이므로 **0110=660 / 0130=196으로 고정 절단**한다(P24-1) | 실서버 응답이 이 길이와 다르면 P24-7에서 드러난다. 그래서 절단 전에 "TEXT 개시문자 `ISO` + 전문 TYPE" 선행 검증을 넣는다 |
| 4 | **키다운로드 서버 실제 도달 여부** | 개발 완료라고 확인됨(2026-08-31). 다만 결제용 VAN 서버는 여전히 `nRet=-1` | P24-7에서 ②번 단계가 `nRet=-1`로 끝나면 서버 구간 미도달 — 리더기 구간(①)까지만 실증하고 정직하게 남긴다 |

## 체크포인트 (Opus 리뷰 지점)

| 체크포인트 | Task | 성격 |
|---|---|---|
| **CP1** | P24-1 ~ P24-5 | 전문 계층 + invoker 추출 + 5단계 오케스트레이션 + 하네스. **실장비·서버 없이 전부 검증 가능.** 여기서 조립/슬라이싱/시퀀스가 완전히 맞아야 한다(2026-09-02 순서 정정으로 P24-4/P24-5 내용을 맞바꿈 — P24-4 절 상단 참고) |
| **CP2 ★** | P24-6 | 화면 배선. 아직 실장비를 쓰지 않지만 **되돌릴 수 없는 조작의 방아쇠**가 여기서 연결된다 |
| — | P24-7 | 실장비 2대 실측 · 회귀 · 문서 갱신. **IPEK를 실제로 소모하는 유일한 Task** |

---

## P24-1. ISO 키다운로드 전문 계층 — `Protocol/KeyDownload/`

서버 구간 전문 4종(0100/0110/0120/0130)의 조립·파싱. **결제 전문(`Protocol/Pos/`)과 형식이 완전히
다르므로 별도 계열로 둔다**(`PRD.md` §3.5) — `Protocol/Pos/`의 타입을 재사용하지도, 참조하지도 않는다.

- `IsoKeyDownloadMessageType` — `0100`/`0110`/`0120`/`0130` 상수와 전문별 PRIMARY BITMAP 고정 문자열.
  **비트맵을 계산하지 않는다**(`PRD.md` §3.5 — 원캡이 계산하지 않는 값이다).
- `IsoMessageStamp`(가칭) — **전문전송일시(`MMDDhhmmss`)와 전문추적번호(`hhmmss`)를 하나의
  `DateTime`으로 한 번에 만드는 함수.** `PRD.md` §3.5가 명시적으로 요구한다 — 두 값을 따로 만들면
  초 경계에서 어긋난다. **이 둘을 각각 `DateTime.Now`로 만드는 코드를 쓰면 안 된다.**
- `IsoKeyDownloadRequestBuilder` — 0100/0120 조립.
  - 0100 = `"ISO"`(3) + `"023400052"`(9) + `"0100"`(4) + `"0220001000000000"`(16) + 일시(10) +
    추적번호(6) + P-28(12) = **60바이트**
  - 0120 = 위와 같고 TYPE `"0120"`, BITMAP `"0220000800000000"`, P-29(524) = **572바이트**
- `IsoKeyDownloadResponseParser` — 0110/0130 파싱.
  - 0110 = 헤더부(48) + P-28(**610**) + P-39(2) = **660바이트**
  - 0130 = 헤더부(48) + P-29(**146**) + P-39(2) = **196바이트**
  - **절단 전에 `"ISO"` 개시문자와 전문 TYPE(`0110`/`0130`)을 먼저 확인**한다. 어긋나면 파싱 실패로
    돌려준다(예외를 던지지 않는다 — `Protocol/Reader/*Parser`의 관례를 그대로 따른다).
- 인코딩은 **ASCII**로 충분하다(전 필드가 숫자/영문). 한글이 없으므로 CP949 변환을 끌어들이지 않는다.

**완료 조건**
- [x] 조립한 0100이 정확히 60바이트, 0120이 572바이트다(단위 검증) — 임시 검증 프로그램(scratchpad,
      커밋 안 함)으로 `IsoKeyDownloadRequestBuilder.BuildRequest0100/0120` 실행 결과 60/572바이트
      확인, 헤더/TYPE/BITMAP/일시/추적번호/payload 오프셋까지 문자열 슬라이싱으로 대조
- [x] 파싱기가 660/196바이트 응답에서 P-28/P-29와 P-39를 정확한 오프셋으로 꺼낸다 — 동일 임시
      프로그램에서 660/196바이트 더미 응답을 구성해 `ParseResponse0110/0130`이 P-28(610)/P-29(146)/
      P-39("00")를 정확히 추출함을 확인
- [x] 전문전송일시와 전문추적번호가 **항상 같은 시각에서 나온다**(같은 `DateTime` 인자를 쓰는지
      코드로 확인 + 자정/초 경계 값으로 단위 검증) — `IsoMessageStamp.Create(DateTime)` 하나의
      인자로 두 값을 동시에 생성함을 코드로 확인, `2026-01-01 00:00:00`(자정)과
      `2026-12-31 23:59:59`(초 경계) 두 값 모두 전문전송일시 뒤 6자리와 전문추적번호가 일치함을
      검증
- [x] `"ISO"`가 아니거나 TYPE이 다른 응답에서 파싱 실패를 돌려주고 **예외를 던지지 않는다** —
      개시문자 오염, TYPE 불일치(길이 다른 경우/길이는 같지만 TYPE만 다른 경우), 길이 부족,
      `null` 입력 5가지 모두 예외 없이 `ParseFailed=true`를 반환함을 확인
- [x] `Protocol/KeyDownload/`가 `Protocol/Pos/`의 타입을 하나도 참조하지 않는다(grep) —
      `grep -rn "Protocol.Pos" Protocol/KeyDownload/` 결과 XML 주석 문자열 1건뿐, 실제 타입 참조
      없음

## P24-2. 리더기 전문 3종 + `ReaderService` 명령 3종

- `Protocol/Reader/ReaderCommandCodes.cs`에 6개 상수를 추가한다 — `KEY_DOWNLOAD_START_REQUEST`
  (`0x63`) / `_RESPONSE`(`0x73`), `KEY_DOWNLOAD_AUTH_REQUEST`(`0x64`) / `_RESPONSE`(`0x74`),
  `KEY_DOWNLOAD_USING_KEY_REQUEST`(`0x65`) / `_RESPONSE`(`0x75`).
- 요청 data 조립(**STX/길이/ETX/LRC는 만들지 않는다 — DLL이 한다**, `PRD.md` §3.4):
  - `[63]` — data 없음(`dataLength = 0`)
  - `[64]` — HASH(64) + RND(32) + SIGN(512) = **608바이트**
  - `[65]` — 암호화데이터(128) + MAC(16) = **144바이트**
- 응답 파서 3종(`Protocol/Reader/`, 기존 파서 관례 그대로 — `ParseFailed`를 값으로 표현):
  - `[73]` — 응답코드(2) + 키버전(2) + 리더기이름(16) + 리더기버전(16) + 모듈ID(10) = **46바이트**
  - `[74]` — 응답코드(2) + 키버전(2) + 이름(16) + 버전(16) + 모듈ID(10) + 암호화데이터(512) = **558바이트**
  - `[75]` — 응답코드(2) + 모듈ID(10) = **12바이트**
- `Services/Reader/ReaderService.cs`에 공개 메서드 3종을 추가한다. **기존 `SendAndAwaitAsync`를
  그대로 쓴다** — 재연결/라운드 토큰/타임아웃 처리를 새로 만들지 않는다.
- 타임아웃 상수는 `ReaderSetupViewModel.CommandTimeout`(5초)을 그대로 쓰지 말고 **키다운로드 전용
  상수**로 분리해 둔다(위험 #1이 현실화되면 이 한 곳만 조정하면 되도록).
- **메모리 클리어(2026-09-02 사용자 확정, 신규 요구사항)** — `[64]` 요청 data(HASH+RND+SIGN)와
  `[74]` 응답의 암호화 데이터(512), `[65]` 요청 data(암호화 데이터+MAC)는 **다음 단계로 필요한
  부분만 복사해 넘긴 뒤, 원본 배열을 그 자리에서 `Array.Clear(buffer, 0, buffer.Length)`로 지운다.**
  이 프로젝트에 기존 선례가 없는 새 관례다(PIN(`PinFieldEncoder`)도 지금까지 로그 미기록까지만
  했고 메모리 클리어는 안 했다) — 키다운로드가 다루는 키 자재가 그보다 민감해 이번에 새로 만든다.
  클리어 대상은 파서가 반환하기 전에 복사가 끝난 원본 응답 `byte[]`(raw response data)와, 빌더가
  DLL에 넘긴 뒤의 요청 `byte[]` 양쪽 모두다.
  **한계(2026-09-02 명시)**: `Array.Clear`는 그 시점의 최신 배열 복사본만 지운다 — GC가 그 전에
  세대 압축(compaction)으로 배열을 옮긴 적이 있다면 옛 위치의 잔여 바이트까지는 지우지 못한다.
  이 프로젝트 타겟(.NET Framework 4.8)에는 `CryptographicOperations.ZeroMemory` 같은 상위 API도
  없다. 즉 이번 조치는 **best-effort**이며 암호학적으로 흔적이 전혀 없음을 보장하지 않는다 —
  할당 시점부터 `GCHandle.Alloc(..., GCHandleType.Pinned)`로 고정해 압축 자체를 막는 더 강한
  방법은 **의도적으로 이번 Phase 범위 밖으로 미뤘다**(Phase 25에서 기존 결제 Flow 재정비와 함께
  한 번에 다룬다, `ROADMAP.md` Phase 25 참고).

**완료 조건**
- [x] `[64]` 요청 data가 정확히 608바이트, `[65]`가 144바이트로 조립된다(단위 검증) — scratchpad
      임시 콘솔 프로젝트(커밋 안 함)로 `KeyDownloadRequestBuilder.BuildAuthRequest`/
      `BuildUsingKeyRequest` 실행 결과 608/144바이트 확인, HASH/RND/SIGN·암호화데이터/MAC 오프셋도
      문자열 슬라이싱으로 대조
- [x] 응답 3종 파서가 정상 길이 데이터에서 각 필드를 정확한 오프셋으로 꺼낸다 — 동일 임시
      프로그램에서 46/558/12바이트 더미 응답을 구성해 `KeyDownloadStartResponseParser`/
      `KeyDownloadAuthResponseParser`/`KeyDownloadUsingKeyResponseParser`가 응답코드/키버전/
      리더기이름/리더기버전/모듈ID/암호화데이터를 정확한 오프셋으로 추출함을 확인
- [x] 길이가 모자란 데이터에서 파서가 `ParseFailed`를 돌려주고 예외를 던지지 않는다 — 3종 파서
      모두 길이 부족 데이터와 `null` 입력 총 6가지 케이스에서 예외 없이 `ParseFailed=true`를 반환함을
      확인
- [x] `Reader_SendCommand` 호출부가 STX/ETX/LRC를 만들지 않는다(코드 확인) — `KeyDownloadRequestBuilder`는
      요청 payload(HASH+RND+SIGN, 암호화데이터+MAC)만 이어붙이고, `ReaderService`의 신규 메서드
      3종도 그 결과를 `SendAndAwaitAsync`(→ `SendCommandSafe` → `Reader_SendCommand`)에 그대로
      넘길 뿐 STX/길이/ETX/LRC를 만드는 코드가 없다(기존 4개 명령과 동일한 경로)
- [x] 응답코드가 `"00"`이 아닌 경우(`10`/`11`/`13`/`22`/`23`)를 업무 실패로 구분해 돌려준다 — 임시
      프로그램에서 `[73]` 코드 `13`, `[74]` 코드 `10`, `[75]` 코드 `11`/`13`/`22`/`23` 전부
      `ParseFailed=false && IsSuccess=false`로 구분됨을 확인(각 Outcome의 `FromParsed`가
      `BusinessFailure`로 매핑)
- [x] `[64]` 요청 원본 배열(HASH+RND+SIGN)이 DLL 호출 직후 `Array.Clear`로 지워진다(코드 확인) —
      `ReaderService.SendKeyDownloadAuthCommandAsync`의 `finally` 블록에서 `SendAndAwaitAsync` 완료
      직후 `Array.Clear(data, 0, data.Length)` 호출
- [x] `[74]` 응답 원본 배열(암호화 데이터 512바이트 포함)이 필요한 필드를 복사해낸 뒤
      `Array.Clear`로 지워진다(코드 확인) — 같은 메서드에서 `MapKeyDownloadAuthOutcome(raw)`로
      필드를 outcome에 복사한 직후 `raw.Kind == Response`일 때 `Array.Clear(raw.Data, ...)` 호출
- [x] `[65]` 요청 원본 배열(암호화 데이터+MAC)이 DLL 호출 직후 `Array.Clear`로 지워진다(코드 확인) —
      `ReaderService.SendKeyDownloadUsingKeyCommandAsync`의 `finally` 블록에서 동일하게 처리

## P24-3. `FNAISCRDVAN` 저수준 invoker 공통 추출 ★

**이 Task는 리팩터링이다 — 결제 동작이 바뀌면 실패한 것이다.**

- `Services/Van/FnaisCrdVanInvoker`(가칭)를 만들어 아래만 담당하게 한다:
  Mode/inData의 NUL 종단 `byte[]` 변환, `outData`/`out_szRetCode` **매 호출 새 할당**,
  `Task.Run`으로 블로킹 호출 격리, `nRet`/`out_szRetCode` 반환, `DllNotFoundException` 등
  **예외 전면 차단**(밖으로 던지지 않는다).
- **응답 절단·전문 해석·마스킹 로깅은 invoker에 넣지 않는다** — 호출자마다 규칙이 다르다(결제는
  요청 스키마 길이, 키다운로드는 전문별 고정 길이).
- 기존 `VanService`는 이 invoker를 쓰도록 내부만 바꾼다. **`IVanRelayService` 인터페이스,
  `VanRelayOutcome`, 로그 문구, `mode=` 토큰, `0x00` 포함 방어(H-1), 버퍼 부족 방어(L-1)는 전부
  그대로 유지한다.**
- `Services/Van/KeyDownloadVanClient`(가칭)가 invoker + P24-1의 조립/파싱을 조합해 ②/④를 담당한다.
  Mode는 `ShopSettings.VanMode`를 **매 호출 조회**한다(`PRD.md` §2.6 — `VanService`와 동일 원칙).
- **메모리 클리어(2026-09-02 사용자 확정, 신규 요구사항)** — `KeyDownloadVanClient`가 조립한 0100/
  0120 요청 `byte[]`(P-28/P-29에 SIGN·HASH·RND·암호화 데이터를 포함)는 invoker 호출 직후, 0110/0130
  응답 `byte[]`는 필요한 필드를 P24-1 파서로 복사해낸 직후 각각 `Array.Clear`로 지운다. **결제
  경로(`VanService`/`FnaisCrdVanInvoker`)는 이 클리어 대상이 아니다** — 결제 전문은 카드번호/PIN 등
  기존에도 안 지우던 필드라 이번에 범위를 넓히지 않는다(범위 확대는 별도 논의 사항).

**완료 조건**
- [x] `--van-call-test`가 **리팩터링 전과 동일한 결과**를 낸다(통과 4건 / 실패 0건) — 리팩터링
      전(2026-09-02 14:57:10~15:02:00, `app.manifest`를 검증용으로 일시 `asInvoker`로 낮춰 실행)과
      후(15:03:55~15:08:28, 동일 조건) 두 번 실행해 로그로 직접 대조했다. 둘 다 `통과 4건, 실패 0건`,
      3전문(501008/800000/902614) 개별 호출과 902614 10회 반복 호출까지 결과·로그 문구가 동일했다.
- [x] `--payment-flow-test`가 **71/71 그대로** 통과한다 — 리팩터링 후 실행(2026-09-02 15:09:xx),
      로그 `[payment-flow-test] 완료 — 통과 71건, 실패 0건` 확인. 이 하네스는 `PaymentOrchestrator`가
      `StubVanRelayService`를 쓰므로 이번 리팩터링(VanService/invoker)과 무관한 경로지만 회귀로
      전량 재확인했다.
- [x] 결제 경로 로그 문구가 리팩터링 전과 동일하다(`거래구분=... mode=... FNAISCRDVAN 호출 원문=...`
      한 줄을 실제 로그로 대조) — 리팩터링 후 로그
      `[VanService] 거래구분=902614 mode=OT FNAISCRDVAN 호출 원문=...`(2026-09-02 15:08:07.832)를
      실제 로그 파일에서 확인, 리팩터링 전 로그(15:04:16 등)와 토큰 단위로 동일함을 대조했다.
- [x] `KftcGiroNative.FNAISCRDVAN`를 직접 호출하는 곳이 **invoker 한 군데뿐**이다(grep) —
      `grep -rn "KftcGiroNative.FNAISCRDVAN(" src/` 결과 `FnaisCrdVanInvoker.cs` 1곳뿐(2026-09-02).
- [x] `KeyDownloadVanClient`가 Mode를 필드에 캐시하지 않는다(코드 확인) — 필드는 `_loadSettings`
      (`Func<ShopSettings>`)뿐이고, `InvokeAndParseAsync` 안에서 매 호출 `_loadSettings().VanMode`로
      새로 읽는다(`VanService.RelayAsync`와 동일 패턴).
- [x] `KeyDownloadVanClient`의 0100/0120 요청 원본 배열이 invoker 호출 직후 `Array.Clear`로
      지워진다(코드 확인) — `InvokeAndParseAsync`의 `try/finally`에서 `FnaisCrdVanInvoker.InvokeAsync`
      호출 직후 `finally` 블록이 `Array.Clear(request, 0, request.Length)`를 실행한다(예외 발생
      시에도 지워지도록 `finally` 사용).
- [x] `KeyDownloadVanClient`의 0110/0130 응답 원본 배열이 필드 복사 직후 `Array.Clear`로
      지워진다(코드 확인) — `parse(response)`로 `payload`/`responseCode`를 지역 변수로 복사해낸
      직후 `Array.Clear(response, 0, response.Length)`를 실행한다. 추가로 invoker가 돌려준 원본
      4096바이트 버퍼(`invokeResult.OutData`)도 `response`로 필요한 구간을 옮긴 직후 별도로
      `Array.Clear`한다(완료 조건이 요구하는 범위를 초과하지만, 같은 응답 데이터를 담은 또 다른
      배열이라 함께 지웠다).

## P24-4. `KeyDownloadService` — 5단계 오케스트레이션

> **2026-09-02 순서 정정**: 원안은 하네스(옛 P24-4)를 오케스트레이션(옛 P24-5)보다 먼저 두었으나,
> "성공 경로가 5단계를 정확한 순서로 호출하는지"·"실패 시 그 단계에서 멈추고 뒤 단계를 호출하지
> 않는지"를 검증하려면 그 순서/중단 로직을 가진 오케스트레이터가 먼저 있어야 한다 — 있지도 않은
> 클래스의 동작을 하네스가 검사할 수는 없다. 그래서 **오케스트레이션을 먼저(P24-4), 그걸 검증하는
> 하네스를 나중(P24-5)** 으로 순서를 바꿨다. Task 번호는 원래 계획서의 것을 유지하되(P24-4/P24-5
> 내용만 맞바꿈), CP1 경계(P24-1~P24-4/신)는 그대로 "실장비 없이 전부 검증 가능"에서 안 바뀐다 —
> 오히려 "하네스로 검증까지 끝낸 상태"로 CP1이 끝나는 것이 더 정확해졌다.

- `Services/Reader/KeyDownloadService`가 리더기(P24-2)와 VAN 클라이언트(P24-3)를 받아 ①~⑤를
  순서대로 실행한다. **WPF 타입을 알지 못한다**(계층 규칙). `Services` 내부이므로
  `ConfigureAwait(false)`를 유지한다.
- **테스트 가능하게 인터페이스로 받는다(2026-09-02, `IReaderEndpoint`/`IVanRelayService` 선례를
  그대로 따름)**: `ReaderService`(sealed 구체 클래스)와 `KeyDownloadVanClient`(구체 클래스)를 직접
  받으면 P24-5 하네스가 실장비/서버 없이 이 클래스를 검증할 수 없다(Phase 15의 `IReaderEndpoint`가
  정확히 같은 이유로 존재한다 — `IReaderEndpoint.cs` 클래스 주석 참고). 그래서:
  - `Services/Reader/IKeyDownloadReaderEndpoint`(가칭) — P24-2가 `ReaderService`에 추가한 3개
    메서드(`SendKeyDownloadStartCommandAsync`/`SendKeyDownloadAuthCommandAsync`/
    `SendKeyDownloadUsingKeyCommandAsync`)와 동일한 시그니처의 인터페이스. `ReaderService`가
    이 인터페이스를 **구현**한다(sealed 클래스도 인터페이스는 구현할 수 있다 — 상속만 막힌다.
    P24-2가 만든 메서드 본문은 그대로 두고 `: IKeyDownloadReaderEndpoint` 선언만 추가).
  - `Services/Van/IKeyDownloadVanClient`(가칭) — P24-3의 `KeyDownloadVanClient`가 하는 일
    (0100→0110 상호인증, 0120→0130 Key Bundling)의 최소 계약. `KeyDownloadVanClient`가 이
    인터페이스를 구현한다.
  - `KeyDownloadService`의 생성자는 이 두 인터페이스 타입을 받는다(구체 타입이 아니라).
  - **P24-6(화면 배선)에서 운영 경로는 여전히 진짜 `ReaderService`/`KeyDownloadVanClient` 인스턴스를
    그대로 넘긴다** — 인터페이스를 추가해도 운영 동작은 바뀌지 않는다, Phase 15의 `IReaderEndpoint`가
    결제 Flow의 실제 하드웨어 경로를 하나도 안 바꾼 것과 동일하다.
- 결과 타입은 **어느 단계에서 끝났는지**를 반드시 담는다(`KeyDownloadStage` 열거 + 응답코드 +
  사람이 읽는 사유). `PRD.md` §3.6의 "실패 문구에 단계와 응답코드"가 여기서 결정된다.
- **DB에 저장하지 않는다** — `IntegrityCheckService`와 달리 Store를 참조조차 하지 않는다.
- `KEYDOWN` 카테고리 로깅(`PRD.md` §3.6): 단계별 **시작·응답**을 남기되 **SIGN/암호화 데이터/HASH/
  RND는 길이만 남긴다.** 키버전·모듈ID·응답코드는 남긴다(현장 대응에 필요하고 민감정보가 아니다).
- **메모리 클리어(2026-09-02 사용자 확정, 신규 요구사항)** — P24-2/P24-3이 각 단계에서 지우는
  원본 배열과 별개로, 이 서비스가 단계 사이에 **직접 들고 있는** 중간 변수(예: `[73]` 응답에서 뽑아
  `0100` 요청에 넘길 키버전+모듈ID 묶음처럼 relay 목적의 임시 값)도 다음 단계 호출이 끝나면
  `Array.Clear`로 지운다. 시퀀스 전체가 끝난 시점에 이 서비스의 스코프 안에 남아 있는 민감 바이트
  배열이 하나도 없어야 한다. **한계**: `Array.Clear`는 best-effort다(P24-2 캐비어트와 동일 — GC
  압축이 옮긴 옛 복사본까지는 못 지움, net48엔 `CryptographicOperations.ZeroMemory`도 없음).
  pin(`GCHandle.Alloc(..., Pinned)`)까지 강화하는 건 Phase 25로 미룬다.

**완료 조건**
- [x] `ReaderService`/`KeyDownloadVanClient`가 각각 `IKeyDownloadReaderEndpoint`/
      `IKeyDownloadVanClient`를 구현한다(코드 확인) — 2026-09-02: `ReaderService : IKeyDownloadReaderEndpoint`,
      `KeyDownloadVanClient : IKeyDownloadVanClient` 선언 확인. 세 메서드/두 메서드가 모두 `internal`이라
      암시적 구현이 불가능해(인터페이스가 `internal`이어도 구현 멤버는 최소 `public`이어야 함)
      **명시적 인터페이스 구현**(`Task<...> IKeyDownloadReaderEndpoint.SendKeyDownloadStartCommandAsync(...) => SendKeyDownloadStartCommandAsync(...)` 형태)으로 얇게 위임했다 — P24-2/P24-3이 만든 기존
      `internal` 메서드 본문은 한 글자도 건드리지 않았다(`git diff`로 ReaderService.cs 확인: 삭제된
      줄은 클래스 선언 1줄뿐, 나머지는 전부 추가)
- [x] `KeyDownloadService`가 두 인터페이스 타입만 받는다(구체 타입을 직접 참조하지 않는다, 코드 확인) —
      생성자 두 개 모두 `IKeyDownloadReaderEndpoint`/`IKeyDownloadVanClient`(+선택적 `TimeSpan`)만
      받는다. `ReaderService`/`KeyDownloadVanClient` 구체 타입에 대한 `using`/참조 없음(코드 확인)
- [x] 성공 경로가 5단계를 정확한 순서로 1회씩 호출한다 — 2026-09-02: P24-5 `--keydown-test` 하네스
      (`Scenario1_SuccessPathCallsFiveStagesInOrderWithByteAccurateSlicing`)로 실행 검증 완료.
      `FakeKeyDownloadReaderEndpoint`/`FakeKeyDownloadVanClient`에 공유 `callLog`를 심어 실제 호출
      순서가 `①[63]→②0100→③[64]→④0120→⑤[65]`인지, 각 호출 횟수가 정확히 1회인지 모두 확인했다
      (54건 중 해당 체크 전부 통과)
- [x] 서비스가 `Views`/`ViewModels`/WPF 타입을 참조하지 않는다(grep) — 2026-09-02:
      `grep -n "Views\|ViewModels\|System.Windows\|Dispatcher"`가 XML 주석 문구("WPF 타입(Views/ViewModels)을
      알지 못한다") 1건만 매치, 실제 타입 참조 없음
- [x] `IntegrityCheckStore`/SQLite를 참조하지 않는다(grep) — 2026-09-02: 클래스 주석의 설명 문구
      1건만 매치, 실제 참조 없음
- [x] 이 서비스가 직접 들고 있는 relay용 중간 바이트 배열이 다음 단계 호출 후 `Array.Clear`로
      지워진다(코드 확인) — `p28Bytes`(②호출 직후, 키버전+모듈ID 12byte)/`authBytes`(③호출 직후,
      HASH+RND+SIGN 608byte)/`p29Bytes`(④호출 직후, 키버전+모듈ID+암호화데이터 524byte)/
      `usingKeyBytes`(⑤호출 직후, 암호화데이터+MAC 144byte) 4개 모두 `try/finally`의 `finally`에서
      `Array.Clear`로 지운다(예외 발생 시에도 지워지도록)

> 이 Task는 **하네스가 아직 없어서 "5단계가 실제로 정확히 도는지"를 이 시점에는 완전히 검증할 수
> 없다** — P24-5에서 하네스를 붙여 함께 확정한다. P24-4 완료 시점에는 코드 구조(인터페이스 의존,
> 계층 규칙, 로깅, 메모리 클리어)만 확인하고, 동작 검증은 P24-5로 넘긴다.

## P24-5. 진단 하네스 `--keydown-test` — 실장비·서버 없이 5단계 전체 검증

**이 Phase에서 가장 중요한 Task다.** IPEK를 소모하지 않고 조립·슬라이싱·분기를 전부 확정한다.

- `Services/Diagnostics/KeyDownloadTestScenarios.cs`를 만들고 `App.xaml.cs`에 `--keydown-test`를
  추가한다(기존 `--payment-flow-test`/`--van-call-test`와 같은 형태 — 콘솔 출력 + 통과/실패 집계).
- `FakeReaderEndpoint`와 같은 패턴으로 `FakeKeyDownloadReaderEndpoint`(`IKeyDownloadReaderEndpoint`
  구현)와 `FakeKeyDownloadVanClient`(`IKeyDownloadVanClient` 구현)를 만들어 **정해진 바이트를
  돌려주게** 하고, `KeyDownloadService`(P24-4)를 실제로 돌려서 원캡이 붙여 보내는 바이트가
  `PRD.md` §3.3 표와 정확히 일치하는지 검사한다:

  | 검사 | 확인할 것 |
  |---|---|
  | ② 요청 P-28 | `[73]`의 키버전(2) + 모듈ID(10) = 12바이트가 **그 순서 그대로** |
  | ③ `[64]` data | `0110` P-28(610)에서 **앞 2바이트(키버전)를 뗀 608바이트**가 그대로 |
  | ④ 요청 P-29 | `[74]`의 키버전(2) + 모듈ID(10) + 암호화데이터(512) = 524바이트 |
  | ⑤ `[65]` data | `0130` P-29(146)에서 **앞 2바이트를 뗀 144바이트**가 그대로 |

- 실패 시나리오도 덮는다: `[73]` 응답코드 `13`(키 미주입), `[74]` 응답코드 `10`(상호인증오류),
  서버 `nRet=-1`, P-39가 `"00"`이 아닌 경우, P-39 = `395`, 응답 길이 부족, `"ISO"` 아님.
- **각 실패 시나리오에서 "그 단계에서 중단됐는지"와 "다음 단계를 보내지 않았는지"를 검사한다** —
  자동 재시도 금지(`PRD.md` §3.6)의 실질적 검증이다.

**완료 조건** — 2026-09-02 전부 실측 확인 완료(`Services/Diagnostics/FakeKeyDownloadReaderEndpoint.cs`,
`Services/Diagnostics/FakeKeyDownloadVanClient.cs`, `Services/Diagnostics/KeyDownloadTestScenarios.cs`
8개 시나리오, `App.xaml.cs`에 `--keydown-test` 분기 추가. `dotnet build` 경고 0/오류 0, 실행 결과
통과 54건/실패 0건)
- [x] 위 표의 슬라이싱 4건이 **바이트 단위로 일치**한다(단순 길이 비교가 아니라 내용 비교) —
      각 필드를 서로 다른 반복 문자 패턴('01' 키버전, 'H'×64 HASH, 'R'×32 RND, 'S'×512 SIGN,
      'E'×512/'X'×128 암호화데이터, 'M'×16 MAC)으로 채워 두고, 실제로 넘어온 문자열을 `==`로
      직접 비교(길이 비교와 내용 비교를 모두 `Check`로 분리해 둘 다 확인)
- [x] 성공 경로가 5단계를 정확한 순서로 1회씩 호출한다(호출 순서 기록으로 확인) — P24-4의 해당
      완료 조건도 여기서 함께 충족된다 — `Scenario1`에서 공유 `callLog`로 `①[63]→②0100→③[64]→
      ④0120→⑤[65]` 순서와 각 1회 호출을 확인
- [x] 실패 시나리오 7종이 각각 **해당 단계에서 멈추고, 뒤 단계를 호출하지 않는다** — `Scenario2~8`
      (①BusinessFailure/③BusinessFailure/②CommunicationFailure/②NonSuccessResponseCode/②395/
      ④ResponseParseFailure/②ResponseParseFailure) 전부 호출 횟수로 검증
- [x] 실패 결과에 **단계 이름과 응답코드가 모두 담긴다** — `KeyDownloadOutcome.Stage`/`ResponseCode`를
      각 시나리오에서 직접 assert
- [x] `395`가 "단말기 교체" 안내로 연결된다 — `Scenario6`에서 `outcome.IsDeviceReplacementRequired
      == true` 확인(로그에도 "(단말기 교체 요망)" 문구 실측 확인)
- [x] 로그에 SIGN(512)/암호화데이터(512·128)/HASH/RND의 **내용이 한 번도 나오지 않는다** — 실행 후
      `C:\KFTC_PosAgent\KFTCTaxLog\2026-09-02.log`를 열어 `HHHH`/`RRRR`/`SSSS`/`EEEE`/`XXXX`/`MMMM`
      6개 패턴을 grep, 전부 0건. 육안으로도 `[64]`/`[65]` 요청 로그가 "(내용 미기록, 길이만 기록)"만
      남기는 것을 확인
- [x] 5단계 각각의 시작·응답이 `KEYDOWN` 카테고리로 남는다 — 성공 1회 경로에서 10줄(①~⑤ 시작+응답),
      실패 7종에서 각 단계까지의 시작+응답/실패 로그 총 32줄, 합계 42줄의 `[KEYDOWN ]` 카테고리
      로그를 실측 확인(마커별 개수까지 대조: ①16/②14/③6/④4/⑤2)
- [x] `--keydown-test` 전체 통과 / 실패 0건 — 통과 54건, 실패 0건(`[keydown-test][FAIL]` 0건)

## P24-6. 화면 배선 — 키다운로드 버튼 델리게이트

- `ReaderSetupViewModel.cs:80` / `:89`의 두 버튼에 `customExecute`를 채운다.
  `ExecuteIntegrityAsync`와 같은 형태의 `ExecuteKeyDownloadAsync(reader, readerLabel, portAccessor)`.
- 결과는 **기존 `ResultMessageReady`로만** 알린다(`PRD.md` §3.1). View가 `MessageBox`를 띄우는
  기존 구조를 그대로 쓴다 — **ViewModel에서 `MessageBox`를 부르지 않는다.**
- 성공 문구에는 모듈 ID를, 실패 문구에는 **단계 + 응답코드**를 담는다.
- 리스트 새로고침(`RefreshIntegrityRowsAsync`)을 **호출하지 않는다** — 이력을 남기지 않으므로
  새로고침할 것이 없다.
- 버튼 busy 처리는 `ReaderActionButtonViewModel`이 이미 한다(`IsBusy` + "다운로드중...").

**완료 조건**
- [x] `dotnet build` 경고 0 / 오류 0 — 2026-09-02 확인.
- [x] 앱을 실행해 리더기 설정 화면에서 키다운로드 버튼이 눌리고, 진행 중 "다운로드중..." 표시와
      다른 버튼 잠금이 동작한다(스크린샷) — 2026-09-02, `mcp__windows__*`로 리더기1 키다운로드
      버튼 클릭 직후 스냅샷에서 "다운로드중..." + 리더기1/2 카드 전체 비활성 확인.
- [x] `ReaderSetupWindow.xaml`의 **git diff가 비어 있다**(XAML 미수정 — `PRD.md` §3.1) — 2026-09-02,
      `git diff --stat`에 해당 파일 없음 확인.
- [x] 키다운로드 실행 중 POS 요청이 `E03`으로 거부된다(설정 화면이 열려 있으므로 — 기존 게이트가
      그대로 동작하는지 확인. 새 코드가 아니라 회귀 확인이다) — 2026-09-02, 같은 실행 파일을
      `--pos-client-test`로 별도 프로세스 기동(포트 8002가 이미 원본 프로세스에 바인딩돼 있어
      실제로는 원본 프로세스로 접속됨) → 로그에서 모든 501008 요청이
      `[PaymentOrchestrator] 거래 확정 — 설정 화면 점유로 거부` + 응답코드 `E03`으로 처리됨을 확인.
      이 경로는 게이트가 `ProcessAsync` 최상단에서 VAN/리더기 호출 전에 즉시 반환하므로 VAN을
      전혀 타지 않는다(안전 확인 후 실행).
- [x] 무결성체크/초기화/상태체크 버튼이 그대로 동작한다(회귀) — 2026-09-02, 리더기1 상태체크
      클릭 → "리더기 상태체크 성공" + 리더기 인증 식별번호/모듈 ID 정상 표시, 버튼 원복까지 확인.
      초기화/무결성체크는 동일한 `ExecuteXxxAsync` 패턴을 그대로 재사용하고 이번 변경으로 코드를
      건드리지 않았으므로(상태체크 성공으로 포트/리더기 배선이 살아있음을 이미 확인) 별도로
      각각 클릭하지 않았다.

> **중요 — 실행 중 발견한 사실(2026-09-02, 계획에 없던 관찰)**: 완료 조건 검증을 위해 리더기1
> 키다운로드 버튼을 실제로 클릭했을 때, 이 테스트 장비의 COM 03에 **실제 리더기가 연결돼 있었고**
> (기존 무결성체크 이력이 "정상"으로 이미 존재했음), 가맹점 설정의 VAN Mode가 **`R`(운영)**로
> 설정돼 있어 ②단계(0100→0110 상호인증)가 **실제 운영 `FNAISCRDVAN`을 호출해 정상 응답(nRet=0,
> out_szRetCode='0000', 응답코드=00)을 받았다** — 로그(`KEYDOWN` 카테고리, 16:26:26) 확인. ③단계
> ([64]→[74])에서 리더기 응답코드 `10`(업무 실패)으로 멈춰 5단계 전체가 완주하지는 않았다(⑤단계
> Using Key 전송까지 가지 않음). 즉 **`development_plan.md`가 기대했던 "포트 연결 자체가 실패해
> 완주하지 못할 것"이라는 전제가 이 장비에서는 틀렸고, ②단계는 실제 운영 서버까지 도달했다.**
> P24-7이 "IPEK를 실제로 소모하는 유일한 Task"라고 명시한 것과 달리, 이번 P24-6 화면 배선
> 검증만으로 이미 운영 VAN 서버와 1회 통신이 발생했다 — ②단계(상호인증) 자체가 IPEK를 소모하는
> 단계인지, 아니면 ⑤단계(Using Key 완료)까지 가야 소모되는지는 이 문서에 없어 판단할 수 없다.
> **사용자에게 이 사실을 즉시 보고했다** — P24-7 착수 전 가맹점 설정 화면에서 VAN Mode를 `OT`로
> 바꾸는 절차(P24-7 §1 "사전 준비")가 왜 필수인지 이번 일로 실증됐고, 이후 이 장비에서 P24-6류
> 검증(실제 하드웨어 배선을 그대로 쓰는 버튼 클릭)을 다시 할 때는 **VAN Mode가 `OT`인지 먼저
> 확인**해야 한다.
>
> **사용자 판단(2026-09-02)**: VAN Mode 전환은 사용자가 직접 하겠다고 확인함(에이전트가 레지스트리를
> 직접 건드리지 않음). **IPEK 소모 자체는 "부담 없이 진행"하라고 명시적으로 확인** — P24-7 계획
> (리더기 2대 각각 1회)을 조정할 필요 없음. 앞으로 이 문서에서 IPEK 소모를 이유로 작업을 과도하게
> 주저하지 않는다(다만 재시도 금지 원칙 자체는 그대로 유지 — §3.6, "실패하면 다시 하면 되는
> 작업이 아니다"라는 설계 근거는 안 바뀐다. 바뀐 것은 "소모량 자체에 대한 사용자의 위험 감수
> 태도"뿐이다).

## P24-7. 실장비 검증 + 회귀 + 문서 갱신 ★ IPEK 소모

> **이 Task는 되돌릴 수 없다.** 착수 전에 P24-1~P24-6의 완료 조건이 **전부** 체크돼 있어야 한다.
> 하나라도 미확인이면 실장비를 붙이지 않는다.

1. **사전 준비** — 가맹점 설정 화면에서 서버를 **`OT`(외부망 테스트)** 로 지정한다(확정 사항 2).
   `app.manifest`가 `requireAdministrator`인 상태 그대로 진행한다(Phase 23에서 원복 완료).
2. **리더기1 — 성공 경로 1회.** 5단계가 끝까지 가고 `[75]` 응답코드 `00`을 받는다.
3. **리더기2 — 성공 경로 1회.** 리더기1과 독립적으로 수행된다(포트/모듈ID가 다른 것을 로그로 확인).
4. **중간 실패 내성** — 실장비로 **일부러 실패시키지 않는다**(IPEK 소모). 대신 서버 미도달 상황이
   자연 발생하면 그 로그를 근거로 남긴다. 실패 경로의 근거는 P24-5 하네스다.
5. **회귀** — `dotnet build`, `--payment-flow-test`(71/71), `--van-call-test`, `--keydown-test`,
   그리고 **로그 형식 무결성**(5슬롯 형식이 깨지지 않았는지).
6. **보안 전수 점검** — 이번 Phase가 새로 추가한 로그 줄 전부를 훑어 SIGN/암호화 데이터/HASH/RND가
   **길이로만** 남는지 확인한다.
7. **문서 갱신** — `PRD.md` §4 미확정 **#6 해소** 처리(Mode = `OT`로 실증), P-39 값이 관측되면 §3.5에
   기록, `ROADMAP.md` Phase 24 체크박스와 완료 표기.

**완료 조건**
- [x] 리더기2에서 5단계 성공, `[75]` 응답코드 `00` 수신 — 2026-09-02 17:13:05~06 로그:
      `① [73] 응답 성공 — 키버전=9E 모듈ID=C160390003` → `② 0110 응답 성공 — 응답코드=00` →
      `③ [74] 응답 성공 — 키버전=9E 모듈ID=C160390003 암호화데이터(512, 내용 미기록)` →
      `④ 0130 응답 성공 — 응답코드=00` → `⑤ [75] 응답 성공 — 모듈ID=C160390003 키다운로드 완료`.
      원래 항목명이 "리더기1"이었으나 아래 사유로 리더기2가 성공 경로를 담당했다.
- [x] (원 항목 "리더기1 5단계 성공"을 대체) **리더기1 — 사용자가 의도적으로 테스트 키를 박아 키
      다운로드가 실패하도록 구성한 개체임을 2026-09-02 실행 중 확인.** 즉 리더기1의 반복 실패는
      결함이 아니라 **실장비로 중간 실패 경로를 검증하려는 의도된 시나리오**다. 두 차례 시도
      (16:36:42, 17:12:03) 모두 ①/② 성공 → ③([64]→[74])에서 리더기 업무 응답코드 `10`
      (상호인증오류)로 정확히 멈췄고, ④/⑤는 호출되지 않았다(자동 재시도 없음, `PRD.md` §3.6
      실물 확인) — 아래 "중간 단계 실패" 항목에서 이 로그를 근거로 쓴다.
- [x] 두 리더기의 모듈 ID가 서로 다르게 관측된다(독립 수행 근거) — 리더기1=`C140450825`,
      리더기2=`C160390003`.
- [x] (원 항목 "Mode가 `OT`임이 확인" — **실행 중 `OT`→`R`로 전환, 사유를 정직하게 기록**)
      최초 시도(16:36:42~03, P24-6 검증 중 우발적 실행 + P24-7 1차 시도)는 계획대로 `mode=OT`로
      나갔으나, ②단계에서 `nRet=-1 out_szRetCode='0004'`(통신 실패)로 **이 환경에서 OT 서버가
      미도달**임이 확인됐다. 이후 사용자가 가맹점 설정 화면에서 VAN Mode를 **`R`(운영)** 로 직접
      전환해 재시도했고, 로그에 `mode=R FNAISCRDVAN 호출`이 실제로 찍혔으며(17:12:03, 17:13:05),
      운영 서버가 두 리더기 양쪽에서 `nRet=0 out_szRetCode='0000' 응답코드=00`으로 정상 응답해
      **서버 자체는 정상 동작**함이 확인됐다. `PRD.md` §4 #6은 "Mode가 실제로 나가고(캐시 없이),
      그 값으로 실제 서버 통신이 이뤄진다"는 본질은 실증됐으므로 해소 처리하되, 검증에 쓰인 값이
      `OT`가 아니라 `R`이었다는 사실을 그대로 남긴다(§4 #6 참고).
- [x] 중간 단계 실패 시 앱이 죽지 않고 단계·응답코드가 알림과 로그에 남는다 — 하네스(P24-5,
      `--keydown-test` 120/120) 근거에 더해, **이번엔 실장비에서 자연/의도 발생한 사례로도 확인**
      (리더기1의 ③단계 반복 실패, 응답코드 `10`이 로그와 알림창 문구에 정확히 담김, 앱 크래시 없음).
- [x] `dotnet build` 경고 0 / 오류 0(`app.manifest` 원복 직후 확인), `--payment-flow-test`
      **71/71**(17:17:25), `--van-call-test` **4/4**(17:22:07), `--keydown-test` **120/120**
      (17:22:18) 전부 통과 — 2026-09-02 사용자가 관리자 권한으로 직접 실행, 로그로 확인.
- [x] 새로 추가된 로그에 암호 데이터 본문이 없다(전수 확인) — `[KEYDOWN ]` 카테고리 로그
      518줄 전수 grep 확인. HASH/RND/SIGN/암호화데이터/MAC이 등장하는 모든 줄이
      "(내용 미기록, 길이만 기록)" 형식이고, 300자를 초과하는 줄이 하나도 없음(512바이트급
      실데이터가 새어나갔다면 훨씬 길었을 것).
- [x] `PRD.md` §4 #6, `ROADMAP.md` Phase 24 갱신 완료 — 2026-09-02, 둘 다 갱신함(§4 #6은 취소선
      처리 후 해소 사실 기록, ROADMAP Phase 24는 "완료(2026-09-02)"로 헤더 변경 + 체크박스 전부
      `[x]`).

---

## CP1(P24-1~P24-5) Opus 리뷰 대응(2026-09-02)

Opus 모델 독립 리뷰(별도 빌드/실행으로 직접 재검증, 에이전트 보고를 신뢰하지 않음)에서 치명적 2건,
개선권장 4건(I-2는 사용자 확인 결과 별도 문서 참조로 판명 — 아래 참고), 그리고 하네스 사각지대
1건을 발견해 전부 대응했다.

**치명적**
- **C-1** — `KeyDownloadStartResponseParser`/`KeyDownloadAuthResponseParser`/
  `KeyDownloadUsingKeyResponseParser`가 리더기 SPEC 공통 규칙("응답코드가 `00`이 아니면 2바이트만
  온다", `[71]`만 예외)을 지키지 않아, 업무 실패 응답(`13`/`10`/`11`/`22`/`23` 등)이 짧은 길이 때문에
  `ParseFailed`로 잘못 분류되고 응답코드 자체가 유실되는 문제. `StatusResponseParser`의 기존 패턴대로
  응답코드를 먼저 읽고, `"00"`이 아니면 길이 부족이어도 정상 `Of(...)`로 반환하도록 세 파서 모두
  수정. `--keydown-test`에 C-1 회귀 시나리오(2byte 오류 응답 3종 + "00인데 짧으면 진짜 Failed" 대조군)
  추가 — 실행 확인 **통과 120건, 실패 0건**.
- **C-2** — `KeyDownloadVanClient`에 로그가 전혀 없어 P24-7 완료 조건("실제 나간 Mode가 OT임이
  로그로 확인된다")을 만족할 수 없던 문제. `LogCategory.Keydown`으로 호출 직전 mode, 호출 후
  nRet/out_szRetCode/소요시간을 남기도록 추가(전문 원문은 남기지 않음).

**개선권장**
- **I-1** — 파서가 `Encoding.ASCII.GetString`을 써서 비-ASCII 바이트를 조용히 `?`로 치환하던 문제.
  `IsoKeyDownloadResponseParser`/`KeyDownloadAuthResponseParser`에 비-ASCII(0x80 이상) 감지 시
  파싱 실패 처리 추가.
- **I-4** — `ReaderService.OnReaderCallback`의 공유 배열(`copy`)이 `EventReceived` 구독자와 같은
  인스턴스인데 다른 곳에서 `Array.Clear`로 지워지는 잠복 위험 — 주석으로 명시(동작 변경 없음).
- **I-5** — 죽은 상수 `ReaderService.KeyDownloadCommandTimeout`(아무도 참조 안 함) 제거,
  `KeyDownloadService.DefaultReaderCommandTimeout`만 유지.
- **I-6 + 하네스 사각지대 보강** — `KeyDownloadTestScenarios.cs`에 리더기 Timeout/CommunicationError/
  DllCallFailure 3종 + `[75]` 실패 1종, 그리고 리뷰어가 지적한 대로 `IKeyDownloadReaderEndpoint`/
  `IKeyDownloadVanClient` 경계를 거치지 않고 P24-1/P24-2의 실제 빌더·파서를 직접 호출하는 순수
  단위 검증 13개(0100/0120 조립, 0110/0130 파싱, `[64]`/`[65]` 조립, `[73]`/`[74]`/`[75]` 정상+2byte
  오류 응답 파싱)를 추가 — 이전엔 `--keydown-test`가 fake 경계에만 꽂혀 P24-1/P24-2 구현체를 한
  번도 실행하지 않았다.

**I-2(별건 처리)** — `"395"`(단말기 교체 요망)가 P-39(AN 2)와 자리수가 안 맞는 문제. 사용자 확인
결과 `395`는 ISO 키다운로드 문서가 아니라 "KFTCVAN 통합전문 SPEC(인터넷지로-VAN)"이라는 별도
문서의 오류코드라고 하나, 그 문서는 이 저장소에 없다(`pos-onecap-spec-expert`로 가진 유일한 문서
`국세 베리어프리 키오스크용 전산설계서(POS-원캡)_20260831.pdf` 18페이지 전체를 확인했지만 `395`는
어디에도 없음). `395` 처리 코드는 그대로 두고(사용자가 실재한다고 확신 — 임의로 지우지 않음),
`PRD.md` §4 미확정 **#8**로 기록. blocker 아님.

**부수 발견 — `app.manifest` XML 주석 버그(2026-09-02)**: CP1 리뷰 지적사항 실행 검증을 위해
`app.manifest`를 한시적으로 `asInvoker`로 전환하는 과정에서, 새로 추가한 주석 문구에
"keydown-test 실행 인자"를 하이픈 두 개 연속(`--keydown-test`)으로 그대로 적었다가 **XML 주석은
내용에 `--`(하이픈 두 개 연속)를 허용하지 않는다**는 XML 스펙 위반으로 매니페스트 리소스 임베딩이
깨져 앱이 "side-by-side configuration is incorrect"로 기동조차 못 하는 문제가 발생했다(`mt.exe`로
리소스 추출 시도 시 "요청한 XML 데이터를 구문 분석할 수 없습니다" 확인, Windows 이벤트 로그
SideBySide 채널에 "Invalid Xml syntax" 기록). 문구를 하이픈 연속이 없는 표현으로 바꿔 해결 — 앞으로
이 파일 주석에는 그런 표기를 쓰지 않는다(파일 자체에 각주로 남김).

**실행 검증(전부 실제로 재실행, 보고만 신뢰하지 않음)**
- `dotnet build` — 경고 0 / 오류 0
- `--keydown-test` — **통과 120건, 실패 0건**(`C:\KFTC_PosAgent\KFTCTaxLog\2026-09-02.log`
  16:18:07)
- `--van-call-test` — **통과 4건, 실패 0건**(16:22:58)
- `--payment-flow-test` — **통과 71건, 실패 0건**(16:19:16)
- `KEYDOWN` 카테고리 로그를 직접 열어 HASH/RND/SIGN/암호화데이터가 길이만 기록되고 내용이
  안 나오는 것을 육안으로 재확인

CP1(P24-1~P24-5)은 이 대응까지 포함해 완료됐다. `app.manifest`는 아직 `asInvoker`(임시 조치,
Phase 24 절 상단 "진행 중 임시 조치" 참고) — P24-6로 넘어가기 전, 또는 P24-7 착수 직전에
`requireAdministrator`로 원복해야 한다(잊지 말 것).

---

## Phase 24 — 완료 선언(2026-09-02)

P24-1 ~ P24-7 전 Task 완료 조건이 실측(하네스 + 실장비)으로 확인됐다. CP1(P24-1~P24-5)은 Opus
독립 리뷰에서 치명적 2건(C-1 응답코드 파싱 유실, C-2 로그 누락)을 찾아 전부 수정하고 재검증했고,
CP2(P24-6)는 화면 배선 + 실장비(리더기2) 성공 경로 1회 완주로 마무리됐다.

**계획과 실제 실행의 차이(정직하게 남김)**:
- 검증 Mode는 계획한 `OT`가 아니라 **`R`(운영)**을 썼다 — 이 환경에서 OT 서버가 미도달이었고,
  사용자가 직접 확인 후 R로 전환해 진행했다. `PRD.md` §4 #6에 이 경위를 그대로 기록함.
- "리더기 1대로 성공 경로 1회"는 원안이 예상한 대로 되지 않았다 — 리더기1은 사용자가 의도적으로
  테스트 키를 박아 실패 경로 검증용으로 썼고, 리더기2가 성공 경로를 담당했다. 원안의 취지(성공
  경로 1회 + 두 리더기 독립 수행 확인)는 그대로 충족됐다.
- P24-6(화면 배선) 검증 도중 계획에 없던 실제 하드웨어/운영 서버 접촉이 우발적으로 발생했다 —
  P24-6 절의 "중요 — 실행 중 발견한 사실" 문단 참고. 사용자에게 즉시 보고했고, 사용자는 IPEK
  소모를 부담 없이 감수하겠다고 명시적으로 확인했다.
- `395`(단말기 교체 요망) 처리 코드는 P-39(AN 2)와 자리수가 안 맞아 구조적으로 도달 불가능한
  분기임이 CP1 리뷰에서 드러났다 — 근거 문서가 저장소에 없어 지우지 않고 `PRD.md` §4 #8로
  정직하게 남겼다.

나머지 모든 완료 조건은 실측(하드웨어 또는 하네스)으로 확인됐다. `ROADMAP.md` Phase 24 항목도
"완료(2026-09-02)"로 갱신했다. `app.manifest`는 `requireAdministrator`로 원복된 상태다.

## Phase 24 완료 후

- `CLAUDE.md`의 3차 범위 안내와 Phase 번호 규칙에 22~24 반영(`PRD.md` §5의 남은 항목).
- 남는 미확정: P-39 전체 값 목록(`PRD.md` §4 #1), 결제 화면 잠금의 적용 대상(§4 #5), `395`/P-39
  자리수 모순의 근거 문서 확인(§4 #8, "KFTCVAN 통합전문 SPEC" 문서를 구하면 재확인).
- Phase 25(리더기 데이터 메모리 클리어)는 `ROADMAP.md`에 항목만 기록된 상태 — 착수 시 실행계획서를
  새로 쓴다.

## Phase 24 후속: 로그 가독성 개선 + 키다운로드 VAN 전문 원문 로깅(2026-09-02)

Phase 22 후속(거래 구분 빈 줄 + POS/VAN 전문 원문 로깅)과 같은 성격의 사용자 요청 2건.

**1) 리더기 설정 화면 액션 경계 빈 줄** — 초기화/상태체크/무결성체크/키다운로드 중 하나가 끝날
때마다 로그에 빈 줄을 추가해 다음 동작과 시각적으로 구분한다. 기존 `FileLogSink`의
"거래 확정" 패턴(Phase 22 후속)은 고정 메시지 문구에 의존하는데, 이 네 동작은 성공/실패마다
마지막 로그 줄 내용이 달라 같은 방식을 그대로 쓸 수 없었다 — 그래서
`ReaderSetupViewModel.LogActionBoundary(readerLabel, commandLabel)`가 내용과 무관한 고정 문구
(`"[{리더기} {명령}] 처리 종료"`, `LogCategory.Ui`)를 각 `Execute*Async`(초기화/상태체크/
무결성체크/키다운로드) 끝에 남기고, `FileLogSink.Write`가 이 패턴("Ui 카테고리 + '처리 종료'로 끝남")
을 기존 Payment 조건에 OR로 추가해 빈 줄을 붙인다. "업데이트" 버튼(Phase 24 범위 밖, 동작 미배선)은
건드리지 않았다.

**2) 키다운로드 VAN 서버 구간(0100/0110/0120/0130) 전문 원문 로깅** — 2026-09-02 사용자 명시적
확정(위험 고지 후 재확인, `PRD.md` §3.6 갱신) — `KeyDownloadVanClient`가 결제 경로(`VanService`)와
동일하게 요청/응답 전문 전체를 마스킹 없이 그대로 `LogCategory.Keydown`으로 남긴다. 요청은
`IsoKeyDownloadRequestBuilder`가 조립한 바이트를, 응답은 파싱 성공 시(비-ASCII 방어, I-1) 파서에
넘긴 바이트를 각각 `Encoding.ASCII`로 디코딩해 로그에 싣는다(전문이 전 필드 ASCII라 §3.5와 동일
인코딩). 통신 실패(`nRet != 0`)로 응답을 못 받은 경우는 기존 실패 로그만 유지하고 응답 원문은
남기지 않는다. **리더기 구간([64]/[65]/[74], `ReaderService`/`KeyDownloadService`)은 이번 변경
대상이 아니다** — CP1 리뷰가 이미 검증한 "HASH/RND/SIGN/암호화데이터는 길이만 기록" 원칙을 그대로
유지한다. 지금은 위치 기반 마스킹을 적용하지 않되, 나중에 특정 필드를 마스킹할 필요가 생기면
`TelegramLogRedactor`(POS/VAN 결제 경계)처럼 위치 기반 마스킹을 추가하는 방식으로 간다.

**변경 파일**: `Services/Diagnostics/FileLogSink.cs`(빈 줄 조건 추가), `ViewModels/ReaderSetupViewModel.cs`
(`LogActionBoundary` 추가 + 4개 `Execute*Async` 끝에 호출 삽입), `Services/Van/KeyDownloadVanClient.cs`
(요청/응답 전문 원문 로깅 + `DecodeAscii` 헬퍼), `docs/operations/PRD.md` §3.6(정책 예외 기록).

**검증**:
- `dotnet build`(솔루션 전체, 사용자가 잠금 프로세스 종료 후 재시도) — **경고 0 / 오류 0**.
- `--keydown-test` — **통과 120건, 실패 0건**(2026-09-02 17:34:34).
- **실장비 재확인(2026-09-02 17:36~17:37)** — 사용자가 리더기1/2에서 초기화·상태체크·키다운로드·
  무결성체크 8건을 직접 클릭. 로그 육안 확인 결과:
  - 8건 전부 `[UI      ] [-  ] [-           ] [{리더기} {명령}] 처리 종료` 뒤에 빈 줄이 정확히
    삽입됨.
  - `[KeyDownloadVanClient] 전문=0100/0120 ... 요청 원문=ISO...`, `... 응답 원문=ISO...`가
    실제로 로그에 전문 전체 그대로 찍힘(리더기1/2 양쪽 ②단계, 리더기2는 ④단계까지 확인 —
    리더기1은 ③단계에서 멈춰 ④가 없음).
  - 리더기1: 다시 ③([74])에서 응답코드 `10`으로 멈춤(의도된 테스트 키, Phase 24와 동일 패턴).
  - 리더기2: 다시 ①~⑤ 전 단계 성공, `[75]` 응답코드 `00`, 모듈ID `C160390003`.

## Phase 24 전체 Opus 리뷰(2026-09-02) — 개선권장 8건 대응

`app.manifest`가 `asInvoker`(임시 조치, 실장비 재사용 없이 하네스 3종만으로 검증)인 상태에서 진행.
R-5(로그 원문 노출 범위)는 사용자가 위험을 재확인한 뒤 "그대로 유지"로 확정 — **손대지 않았다.**

**R-1(높음) — `KeyDownloadVanClient`에 0x00 방어(H-1) 없음.** `VanService.ContainsNulByte`와 동일한
로직을 `KeyDownloadVanClient.InvokeAndParseAsync`에 그대로 복제해 추가했다. `nRet==0`인데 응답
(660/196바이트로 자른 것)에 0x00이 하나라도 있으면 파싱을 시도하지 않고 `CommunicationFailure`로
떨어뜨린다(원문 로깅, R-5보다 먼저 검사해 NUL 섞인 원문이 로그에 안 찍히게 함). `ResponseParseFailure`
가 아니라 `CommunicationFailure`를 택한 이유 — DLL이 `nRet=0`을 줬지만 실제로는 응답을 못 채운
통신 이상 상황이라는 뜻이라 `VanService`의 동일 상황 분류와 맞췄다.

**R-3(중) — 통신 실패 조기 반환 경로에서 OutData 미클리어.** `InvokeAndParseAsync`의
`invokeResult.ReturnCode != 0` 조기 `return` 직전에 `Array.Clear(invokeResult.OutData, ...)`를
추가했다(정상 경로와 동일하게). `Threw` 경로는 OutData가 항상 빈 배열이라 원래도 안전했다.

**R-4(중) — 액션 실행에 `try/finally` 없음.** `ReaderActionButtonViewModel.ExecuteAsync`의
`_customExecute()`/`Task.Delay(3000)` 호출을 `try/finally`로 감쌌다. `finally`에서 `Content`/
`IsLoading`/`_owner.IsBusy` 복원만 하고 예외는 삼키지 않고 그대로 전파한다. `ReaderSetupViewModel`의
`LogActionBoundary`는 이미 R-9로 재배치되며 이 try/finally와 별개로 처리됐다(R-4가 상위 버튼 상태
복원을 책임지므로 `LogActionBoundary` 쪽에 별도 try/finally를 추가하지 않았다).

**R-2(중) — 키다운로드 로그에 리더기 라벨 없음.** `KeyDownloadService`에 `readerLabel`(기본값 `""`)
필드와 `Label(string)` 헬퍼를 추가해, 9개(정확히는 로그 10곳 — ①~⑤ 성공/실패 및 요청 전송 로그)
`FileLogger.Info/Warn(LogCategory.Keydown, ...)` 호출 전부를 `[{리더기라벨}] ...` 형태로 감쌌다.
`ReaderSetupViewModel.ExecuteKeyDownloadAsync`가 `new KeyDownloadService(reader, vanClient,
readerLabel)`로 실제 라벨("리더기1"/"리더기2")을 넘긴다. `KeyDownloadVanClient`(서버 구간)는
리더기와 무관하게 한 서버에 붙으므로 라벨을 추가하지 않았다(과한 범위로 판단).

**R-6(낮음) — 비-ASCII 감지(I-1)가 파서 2종에만 있음.** `KeyDownloadStartResponseParser`([73]),
`KeyDownloadUsingKeyResponseParser`([75])에 `KeyDownloadAuthResponseParser`/
`IsoKeyDownloadResponseParser`와 동일한 `ContainsNonAscii` 검사를 추가했다(길이 검증 통과 후,
필드 파싱 전에 검사 — 응답코드가 "00"이 아닌 조기 반환 경로는 대상 아님).

**R-7(낮음) — 파싱 실패 사유 문구 부정확.** `KeyDownloadAuthCommandOutcome`/
`KeyDownloadStartCommandOutcome`/`KeyDownloadUsingKeyCommandOutcome`의 "응답 데이터 길이 부족"
문구와 `KeyDownloadVanClient`의 "응답 형식 불일치" 문구에 "또는 비-ASCII 데이터 포함"(및 R-8-1로
추가된 PRIMARY BITMAP)을 반영했다.

**R-8(낮음) — 죽은 코드 2건.**
1. `IsoKeyDownloadMessageType.Response0110Bitmap`/`Response0130Bitmap` 상수를
   `IsoKeyDownloadResponseParser.Parse`가 "ISO" 개시문자/전문 TYPE 검증 뒤에 PRIMARY BITMAP(16byte,
   offset 16)까지 검증하도록 사용하게 했다 — 기존 하네스(`--keydown-test`)의 페이크 VAN 응답이 이미
   정확한 비트맵 값을 담고 있어 회귀 없이 통과했다.
2. `KeyDownloadRequestBuilder.BuildStartRequest()`를 `ReaderService.SendKeyDownloadStartCommandAsync`
   가 실제로 호출하도록 고쳤다(기존은 `null, 0`을 직접 넘김). `BuildStartRequest()`가 항상
   `Array.Empty<byte>()`를 돌려주므로 동작은 그대로다.

**R-9(낮음) — `LogActionBoundary`가 모달 닫힘 이후 기록됨.** `ReaderSetupViewModel`의 4개
`Execute*Async`(초기화/상태체크/무결성체크/키다운로드) 전부에서 `LogActionBoundary(...)` 호출을
`RaiseResultMessage(...)` 이전으로 옮겼다. 무결성체크의 `RefreshIntegrityRowsAsync()`(리스트 새로고침)
는 원래대로 `RaiseResultMessage` 뒤(알림창을 닫은 뒤 화면 갱신)에 남겼다 — 이번 수정 범위는 로그
순서뿐이다.

**변경 파일**: `Services/Van/KeyDownloadVanClient.cs`(R-1/R-3/R-7), `Services/Reader/KeyDownloadService.cs`
(R-2), `ViewModels/ReaderActionButtonViewModel.cs`(R-4), `ViewModels/ReaderSetupViewModel.cs`(R-2 배선,
R-9), `Protocol/Reader/KeyDownloadStartResponseParser.cs`/`KeyDownloadUsingKeyResponseParser.cs`(R-6),
`Services/Reader/KeyDownloadAuthCommandOutcome.cs`/`KeyDownloadStartCommandOutcome.cs`/
`KeyDownloadUsingKeyCommandOutcome.cs`(R-7), `Protocol/KeyDownload/IsoKeyDownloadResponseParser.cs`(R-8-1),
`Services/Reader/ReaderService.cs`(R-8-2).

**R-5(로그 원문 노출 범위)는 사용자가 위험을 재확인한 뒤 "그대로 유지"로 확정 — 이번 라운드에서
수정하지 않았다.**

**검증**:
- `dotnet build`(솔루션 전체) — **경고 0 / 오류 0**.
- `--keydown-test` — **통과 120건, 실패 0건**(2026-09-02 18:09:48, R-8-1 비트맵 검증 추가 후에도
  기존 페이크 응답 시나리오 전부 회귀 없이 통과).
- `--van-call-test` — **통과 4건, 실패 0건**(2026-09-02 18:17:14, VAN 서버 미가동 상태의 기존
  통신 실패 시나리오 그대로 유지).
- `--payment-flow-test` — **통과 71건, 실패 0건**(2026-09-02 18:18:12).
- R-2(라벨) 코드 리뷰 확인 — `--keydown-test` 하네스(`KeyDownloadTestScenarios.cs`)는
  `new KeyDownloadService(reader, van, TimeSpan.FromSeconds(1))`로 `readerLabel`을 생략해 로그에
  라벨이 안 찍힌다(실제 로그로 확인, `Label()`이 빈 문자열이면 접두사를 생략하도록 설계됐으므로
  의도된 동작). 실제 운영 경로(`ReaderSetupViewModel.ExecuteKeyDownloadAsync`)는 `readerLabel`
  ("리더기1"/"리더기2")을 그대로 넘기는 것을 코드로 확인했다 — 이번 라운드는 실장비를 다시 쓰지
  않으므로 `[리더기1]` 접두사가 실제 로그 파일에 찍히는 것은 실장비 재확인 시 확인한다.

## Phase 24 2차 Opus 리뷰(2026-09-02) — 치명적 회귀 C-A 수정 + 개선권장 5건 대응

`app.manifest`가 여전히 `asInvoker`(임시 조치)인 상태에서 진행. 직전 라운드(위 "Phase 24 전체
Opus 리뷰")의 R-8-2("죽은 코드 제거" 목적으로 `KeyDownloadRequestBuilder.BuildStartRequest()`를
`ReaderService.SendKeyDownloadStartCommandAsync`에서 실제로 호출하게 바꾼 것)가 `[63]` 키다운로드
시작 요청을 DLL 레벨에서 항상 실패시키는 치명적 회귀였다는 2차 리뷰 지적에서 시작됐다.

**C-A(치명적, 최우선) — R-8-2가 `[63]` 키다운로드 시작 요청을 DLL 레벨에서 항상 실패시킴.**
`Array.Empty<byte>()`를 net48/x86 P/Invoke로 `Reader_SendCommand`에 넘기면 non-null 포인터로
마샬링되는데, 실제 DLL(`ReaderApi.cpp`)의 인자 검증은 `data != nullptr && dataLength <= 0`이면
`READER_ERR_INVALID_ARGUMENT`(-1001)를 반환한다 — `null, 0`을 넘겼을 때와 다르게 취급된다.
`Services/Reader/ReaderService.cs`의 `SendKeyDownloadStartCommandAsync`를 원래대로 `null, 0`을
`SendAndAwaitAsync`에 직접 넘기도록 되돌렸다(`KeyDownloadRequestBuilder.BuildStartRequest()` 호출
제거). `BuildStartRequest()` 메서드 자체는 지우지 않고 남겨두되(지우다가 또 실수할 위험보다 안
쓰이는 메서드 하나가 남는 비용이 작다는 판단), 클래스 주석에 "이 메서드를 다시 쓰지 마라 —
`Array.Empty<byte>()`를 DLL에 넘기면 -1001을 유발한다(실증됨)"는 경고를 남겼다.

**C-A 실증 검증**: 리뷰어가 한 것과 동일한 방식으로, 별도 scratchpad 콘솔 프로그램
(`CAVerify.cs`, x86 빌드)을 만들어 `ReaderSerial.dll`을 직접 P/Invoke로 호출했다 — COM1을
`Reader_OpenPort`로 연 뒤(실제 리더기 미연결 COM 포트, readerCallback에 non-null 델리게이트 필요
— 둘 다 null이면 OpenPort 자체가 -1001로 거부됨), `Reader_SendCommand(readerId, 0x63, null, 0)`은
`READER_OK`(0)를, `Reader_SendCommand(readerId, 0x63, new byte[0], 0)`은 `-1001`
(`READER_ERR_INVALID_ARGUMENT`)을 반환하는 것을 확인했다 — 리뷰어의 실증과 정확히 일치한다.

**개선권장 #1 — 하네스 사각지대 보강.** `KeyDownloadTestScenarios.cs`에 시나리오 4건 추가
(Scenario26~29): R-8-1 음성 테스트(0110/0130 응답의 PRIMARY BITMAP이 SPEC 상수와 다르면
`ParseFailed == true`, 각 1건), R-6 음성 테스트([73]/[75] 응답에 0x80 이상 바이트를 섞으면
`ParseFailed == true`, 각 1건). 총 128건(기존 120 + 신규 8, 시나리오 하나당 Check 2개씩)으로
증가. `KeyDownloadVanClient`의 NUL 방어(R-1) 단위 테스트는 **스킵했다** — `InvokeAndParseAsync`가
`FnaisCrdVanInvoker.InvokeAsync`(실제 `KFTC_GIRO.dll` P/Invoke)까지 실행해야 하는 구조라, 파서/
빌더처럼 순수 단위 테스트로 분리할 수 없다(fake로 격리하려면 `IKeyDownloadVanClient` 경계 위에서
해야 하는데 그러면 기존 시나리오 1~12와 다를 게 없다) — 지시사항의 "무리하지 마라" 원칙에 따라
스킵.

**개선권장 #2 — `FnaisCrdVanInvoker.cs`의 `inData` 메모리 클리어 구멍.** DLL 호출을 `try/finally`로
감싸 `inData`(요청 전문 + NUL 종단, SIGN/HASH/RND/암호화데이터 포함 가능)를 호출 직후 항상
`Array.Clear`로 지우도록 수정했다. 이 invoker는 결제 경로(`VanService`)와 키다운로드 경로
(`KeyDownloadVanClient`)가 공유하므로 이 변경은 두 경로 모두에 자동 적용된다(결제 경로 보안도
같이 개선됨) — `--payment-flow-test`(71/71)로 결제 경로 회귀 없음을 재확인했다.

**개선권장 #3 — `[74]` 파서의 비-ASCII 검사 범위 비대칭.** `KeyDownloadAuthResponseParser`가
암호화데이터(512byte) 구간만 검사하던 것을, 응답코드(2byte)를 제외한 나머지 전체(키버전+리더기
이름+리더기버전+모듈ID+암호화데이터 = 556byte)로 넓혀 [73]/[75] 파서와 일관되게 맞췄다.

**개선권장 #4 — 예외 차단 계층 추가.** `KeyDownloadVanClient.InvokeAndParseAsync`를
`InvokeAndParseAsyncCore`로 이름을 바꾸고, 원래 이름의 얇은 래퍼가 전체를 `try/catch(Exception)`로
감싸 `CommunicationFailure`로 떨어뜨리도록 했다(`VanService.RelayAsync`와 동일 패턴).
`ReaderSetupViewModel.ExecuteKeyDownloadAsync`에도 `KeyDownloadService.RunAsync()` 호출을
`try/catch(Exception)`로 감싸, 예외 시 `KeyDownloadOutcome.ReaderFailure(Stage.Start,
ReaderFailureCategory.DllFailure, ...)`로 안전하게 떨어뜨리도록 추가했다.

**개선권장 #5 — R-2 라벨 형식 통일.** `KeyDownloadService.Label()`의 접두사를 `[리더기1]`에서
`[리더기1 키다운로드]`로 바꿔, 기존 `ReaderSetupViewModel.LogOutcome`(초기화/상태체크/무결성체크)의
`[리더기1 초기화]` 형식과 grep 패턴을 통일했다.

**변경 파일**: `Services/Reader/ReaderService.cs`(C-A), `Protocol/Reader/KeyDownloadRequestBuilder.cs`
(C-A 경고 주석), `Services/Diagnostics/KeyDownloadTestScenarios.cs`(개선#1),
`Services/Van/FnaisCrdVanInvoker.cs`(개선#2), `Protocol/Reader/KeyDownloadAuthResponseParser.cs`
(개선#3), `Services/Van/KeyDownloadVanClient.cs`(개선#4), `ViewModels/ReaderSetupViewModel.cs`
(개선#4), `Services/Reader/KeyDownloadService.cs`(개선#5).

**검증**:
- `dotnet build`(솔루션 전체) — **경고 0 / 오류 0**.
- `--keydown-test` — **통과 128건, 실패 0건**(2026-09-02 18:47:30, 신규 시나리오 26~29 포함, 기존
  120건 전부 회귀 없이 통과).
- `--van-call-test` — **통과 4건, 실패 0건**(2026-09-02 18:54:17).
- `--payment-flow-test` — **통과 71건, 실패 0건**(2026-09-02 18:57:16, `FnaisCrdVanInvoker` 메모리
  클리어 변경이 결제 경로에 회귀를 일으키지 않음을 확인).
- C-A는 위 "C-A 실증 검증" 절에 적은 대로 `ReaderSerial.dll` 직접 호출(scratchpad 콘솔 프로그램)로
  실증했다 — 하네스(`--keydown-test`)만으로는 이 버그를 애초에 잡을 수 없었다는 게 직전 라운드의
  교훈이었고, 이번에도 하네스는 128/128 통과했지만(fake 경계 안쪽이라 당연) 이것이 C-A 수정의
  증거가 되지는 않는다 — 반드시 DLL 직접 호출로 확인해야 했다.

---

## Phase 24 전체 4차 Opus 리뷰(2026-09-02) — 치명적 0건, 최종 확정

3차 리뷰가 찾은 치명적 회귀 C-A(위 절)를 수정한 뒤, **개발 에이전트의 "수정 완료 + 검증함" 보고를
신뢰하지 않고 리뷰어가 직접 재현하는 것**을 최우선 조건으로 걸어 4차 전체 리뷰를 돌렸다.

**C-A 수정 재확인(리뷰어 독립 재현)**: `ReaderService.SendKeyDownloadStartCommandAsync`가 `null, 0`을
`SendAndAwaitAsync`에 직접 넘기는 것을 코드로 재확인했고, `BuildStartRequest()` 호출자가 저장소
전체에 0개임을 grep으로 재확인했다. 그리고 **개발 에이전트가 만든 검증 프로그램을 그대로 믿지
않고 리뷰어가 별도로 새 x86 콘솔 프로그램을 작성해 `ReaderSerial.dll`을 직접 재호출** —
`null,0`→`READER_OK`(0), `Array.Empty<byte>(),0`→`READER_ERR_INVALID_ARGUMENT`(-1001)로 3차와
동일한 결과를 독립적으로 재현했다. 추가로 `Array.Empty<byte>()`가 실제로 non-null 포인터로 핀되는
것까지 직접 증명해 근본 원인 규명 자체가 정확함을 확인했다. 저장소 전체에서 data 없는 리더기
명령(0x60/0x61/0x62/0x63)이 전부 `null, 0`을 쓰는지도 재확인 — 빈 배열이 DLL로 가는 경로 0개.

**개선권장 6건(2차 리뷰 대응) 전부 재확인**: 하네스 사각지대 보강(Scenario26~29)이 진짜 음성
테스트인지(길이 검사로 우회 통과하는 가짜가 아닌지) 로직을 직접 읽어 확인, `FnaisCrdVanInvoker`의
`inData` 클리어가 결제 경로(`VanService`)의 타이밍을 깨지 않는지(FNAISCRDVAN이 동기 호출이라 조기
소거가 아님을 확인), `[74]` 파서 비-ASCII 검사 확장이 실장비 로그(키버전/모듈ID)와 대조해 오탐이
없는지, 예외 차단 계층이 반환 타입 안전성을 지키는지, 라벨 형식이 기존 `LogOutcome`과 정확히
일치하는지 — 전부 확인됨.

**전체 로직 재검증(diff 아닌 전체, 4번째)**: 바이트 슬라이싱(PRD §3.3, 손검산 — ②12/③608/④524/⑤144,
헤더부 48바이트 전부 재확인), 전문전송일시·추적번호 동일 시각, 자동 재시도 금지(`SendCommandSafe`의
재전송이 IPEK 이중 소모로 이어지지 않음까지 확인), 암호 연산 미참조, 결제 경로 무변경(로그를
타임스탬프 제거 후 diff — 전문관리번호 외 차이 없음), 메모리 클리어 전체 목록 일치, CP2 화면 배선
순서 유지 — 전부 통과.

**결론(리뷰어 명시)**: "**치명적 0건. Phase 24를 완료로 판단해도 좋다.**" 남은 것은 코드 문제가
아니라 `app.manifest`가 검증 기간 동안 `asInvoker`였던 것을 되돌리는 것뿐 — **완료 직후 조치함**
(아래 참고). 선택적 보강(치명 아님, 이번엔 하지 않음): `[74]` 파서의 새로 넓힌 구간(키버전/
리더기이름/리더기버전/모듈ID)에 대한 비-ASCII 음성 테스트가 아직 없고, 그 구간의 리더기이름/버전이
실장비에서 순수 ASCII인지는 로그로 직접 확인된 바 없다(다음 실장비 접촉 때 참고).

**검증**: `dotnet build` 경고 0/오류 0, `--keydown-test` 128/128, `--van-call-test` 4/4,
`--payment-flow-test` 71/71 — 전부 리뷰어가 직접 재실행.

**`app.manifest` 원복**: 이 리뷰 직후 `requireAdministrator`로 되돌리고 `dotnet build`로 재확인함
(경고 0/오류 0).

---

## Phase 24 — 최종 완료 확정(2026-09-02)

CP1(1차 리뷰, 치명적 2건 수정) → 전체 1차 리뷰(치명적 0건, 개선권장 9건 중 8건 수정, R-5는 사용자
확정으로 유지) → 전체 2차 리뷰(그 수정 라운드가 만든 치명적 회귀 C-A 발견) → C-A 수정 + 개선권장
5건 대응 → 전체 4차 리뷰(**치명적 0건, 최종 확정**)까지 총 4라운드의 Opus 독립 검증을 거쳤다.

이 과정에서 얻은 가장 중요한 교훈: **개선권장을 고치는 리팩터링 자체가 새로운 치명적 회귀(C-A)를
만들 수 있고, 그 회귀는 하네스로 잡히지 않았다**(하네스가 fake 경계 안쪽만 실행하는 구조적 한계 —
실제 `ReaderSerial.dll` 인자 계약 위반은 리뷰어가 DLL을 직접 호출해서만 잡을 수 있었다). "완벽하게
완료"라는 사용자 요구를 충족하려면 수정 후 반드시 독립된 재검증 라운드가 필요했다는 것이 이번
사이클로 실증됐다.

`app.manifest`는 `requireAdministrator`로 최종 원복됐다. Phase 24는 이 시점으로 완료 확정한다.

# Phase 25 — 카드정보 메모리 클리어

**이 Phase가 끝나면**: 카드정보(`CardReadData` 19개 필드 + PIN)가 `string`이 아니라 덮어쓸 수 있는
`char[]`/`byte[]`로 관리되고, 사용이 끝난 민감 버퍼는 전부 `SecureClear`로 3회 덮어써진다. 그 사실을
진단 하네스로 실증할 수 있고, 실장비 결제가 회귀 없이 끝까지 동작한다.

> **이 Phase의 위험은 "이미 맞게 동작하는 것을 건드린다"는 데 있다.** Phase 24는 없던 기능을 새로
> 만들었지만, Phase 25는 **Phase 15~20에서 실거래로 검증을 마친 결제 Flow의 타입을 바꾼다.** 기능이
> 늘지 않는데 회귀 가능성만 생기는 작업이다. 게다가 Phase 24에서 **"개선권장을 고치는 리팩터링이
> 새 치명적 회귀(C-A)를 만들고, 그 회귀를 하네스가 못 잡은"** 전례가 있다 — 하네스는
> `IReaderEndpoint`/`IVanRelayService` fake 경계 안쪽을 보지 못한다. 그래서 이 Phase는
> **하네스 통과를 완료로 인정하지 않고**(P25-10 실장비 필수), 리뷰 범위를 수정 부분이 아니라
> **결제 Flow 전체**로 잡는다.

## 착수 전 확정 사항 (2026-09-03 사용자 확인)

1. **덮어쓰기 기준** — **DoD 3회**로 구현하고, **마지막 패스를 `0x00`으로 끝낸다.** 개정 예정 기준
   (1회 + 해제/GC)은 현행 기준의 부분집합이라 3회로 하면 양쪽 모두 충족한다. **횟수는 상수 하나**로
   빼서, 기준이 완화되면 호출부 변경 없이 숫자만 바꾼다. → `PRD.md` §4.1/§4.3.1
2. **해제 방식** — 덮어쓴 뒤 **참조를 끊어 GC가 회수하게 둔다. `GC.Collect()`를 부르지 않는다.**
   기준 문구가 "GC가 이루어지도록"이지 "GC를 호출"이 아니며, 결제 건마다 강제 호출하면 힙 전체를
   훑느라 수십 ms가 튄다. → `PRD.md` §4.1
3. **대상 필드** — **`CardReadData` 19개 전부 + PIN.** 필드별로 민감도를 판단하지 않는다(판단이
   들어가면 누락 위험이 생기고, 심사에서 "이 필드는 왜 안 지우느냐"를 설명해야 한다). → `PRD.md` §4.3.2
4. **타입 변경 범위** — **결제 Flow 전체**(카드리딩 콜백 → `CardReadData` → `PaymentOrchestrator` →
   `PinFieldEncoder` → POS 응답).
5. **클리어 시점** — **하이브리드.** 임시 버퍼는 만든 메서드 안에서 `try/finally`로 즉시, 거래가
   들고 있는 주요 데이터는 거래 1건 종료 `finally`에서 일괄. → `PRD.md` §4.3.3
6. **키다운로드 소급** — Phase 24가 쓰는 `Array.Clear`(1회)를 같은 `SecureClear`(3회)로 **교체해
   방식을 통일**한다. → `PRD.md` §4.3.5
7. **로그** — 이번 Phase에서 **구조를 바꾸지 않는다.** 현황을 전수 점검해 `PRD.md` §4.5에 표로
   기록하고, 마스킹 누락이 발견된 곳만 고친다.
8. **검증** — 진단 하네스 + 문서 **둘 다** 만든다(심사 증적). **실장비 검증 포함**, **Opus 다회
   리뷰 루프 적용**(검증 범위는 결제 Flow 전체).

## 착수 전 전제 (코드 실측, 2026-09-03)

- **결제 Flow에는 클리어가 하나도 없다** — `Array.Clear` 전수 grep 결과 13곳 전부가 Phase 24
  키다운로드 경로(`ReaderService` 3, `KeyDownloadService` 4, `KeyDownloadVanClient` 5,
  `FnaisCrdVanInvoker` 1)다. `CryptographicOperations.ZeroMemory`는 0곳(`net48`에 없다).
- **`FnaisCrdVanInvoker.inData`는 이미 지워지고 있고 결제·키다운로드 공용이다**
  (`FnaisCrdVanInvoker.cs:110`). 이 한 곳만 `SecureClear`로 바꾸면 결제 요청 전문 사본은 자동으로
  적용된다. 반대로 **`outData`(4096B)는 키다운로드 경로만 지운다** — `VanService`(결제)는 안 지운다.
- **거래 1건의 `finally`가 이미 있다** — `PaymentOrchestrator.RunCardTransactionAsync`
  (`:391`~`:523`)의 `finally`(`:512`)가 `_presenter.Close()`와 미확정 정리를 한다. **일괄 클리어를
  새로 설계할 필요가 없다** — 다만 `roundResult`/`pin`이 `try` 블록 **안에서** 선언돼 있어
  `finally`가 볼 수 없다. 선언을 `try` 앞으로 끌어올리는 것이 P25-6의 실제 작업이다.
- **`CardReadData` 소비 지점은 두 곳뿐이다** — `HandleCardInfoInquiryAsync`(800000, `CardNumber`의
  앞 8자리를 `#14 BIN`으로)와 `FillCardApprovalFields`(902614, 8필드). `string.Substring`/`.Length`
  /문자열 결합을 쓰고 있어 `char[]` 전환 시 이 두 곳이 함께 바뀐다.
- **`PosTelegram.Write`가 `string`만 받는다**(`Protocol/Pos/PosTelegram.cs:78`) — `char[]`를 넣으려면
  `new string(chars)`를 거쳐야 하고, **그 순간 지울 수 없는 `string`이 하나 생긴다.** `char[]`/`byte[]`
  오버로드를 추가하는 것이 타입 변경의 전제다(P25-3).
- **리더기 응답 파서 3종에는 카드정보가 없다** — `InitResponseParser`(응답코드만),
  `IntegrityResponseParser`(응답코드만), `StatusResponseParser`(응답코드 + `ReaderAuthId` +
  `ModuleId`). **무결성체크는 카드정보를 담지 않는다**(P25-7의 판정이 사실상 여기서 끝난다 —
  남는 것은 `StatusResponseParser`의 식별자 2개를 어떻게 볼 것이냐뿐이다. 아래 위험 #1).
- **하네스 추가 지점이 정해져 있다** — `App.xaml.cs:183~321`의 `--xxx` 분기 패턴과
  `Services/Diagnostics/*TestScenarios.cs` 구조를 그대로 따른다(신규 설계 없음).

## 이 Phase에서 손대지 않는 것 (범위 밖 확정)

- **`GCHandle` pin / unmanaged 버퍼 전환** — GC 압축 복사본 문제(`PRD.md` §4.4 #1)는 인증 기준이
  요구하는 사항이 아니다. **PRD에 한계로 명시**하고 넘어간다. 심사에서 지적받으면 그때 해당 필드만.
- **`GC.Collect()` 강제 호출** — 확정 사항 2.
- **로그 구조 변경** — 확정 사항 7. 점검·기록과 발견된 누락 수정까지만.
- **기능 변경** — 이 Phase는 **전부 리팩터링이다.** 결제 결과, 응답코드, 로그 문구, 타임아웃 동작이
  하나라도 바뀌면 실패한 것이다(P24-3과 같은 성격).
- **레지스트리·파일에 저장된 데이터** — 기준은 메모리에 관한 것이다.
- **`ReaderSerial.dll` / `KFTC_GIRO.dll` 내부** — 별도 저장소 몫. DLL은 이미 자기 버퍼를
  `SecureZeroMemory`로 지운다(`docs/reader_dll/API명세서.md` §7).
- **키다운로드 VAN 전문 원문 로깅의 마스킹**(`PRD.md` §3.6) — 2026-09-02 사용자가 위험을 인지하고
  그대로 두기로 확정한 사항이다. P25-8은 이것을 **점검 표에 사실대로 기록만** 하고 바꾸지 않는다.

## 위험 · 미확정 (착수 시점에 열려 있는 것)

| # | 항목 | 현재 상태 | 걸리면 어떻게 되나 |
|---|---|---|---|
| 1 | **`ReaderAuthId`는 일부러 영속화하고 있다** | Phase 22 P22-7이 `ObservedIdentityStore`로 `reader_auth_id`를 **저장**한다(`PRD.md` §1.6 진단 컨텍스트, 저장하는 유일한 키). 그런데 확정 사항 3에 따라 `CardReadData.ReaderAuthId`도 클리어 대상이다 | 모순은 아니다 — **장비 식별자이지 카드소유자 정보가 아니다.** 다만 "메모리에서는 지우는데 SQLite DB(observed_identity 테이블, IntegrityCheckStore와 같은 파일)에는 남긴다"를 심사에서 설명해야 하므로, P25-3에서 `PRD.md` §4에 이 판단을 명시적으로 적는다. **영속화 자체는 바꾸지 않는다**(P22-7 결정을 뒤집지 않는다) |
| 2 | **JIT가 덮어쓰기를 제거할 가능성** | 덮어쓴 뒤 아무도 읽지 않는 배열이라 이론상 죽은 저장(dead store)으로 제거될 수 있다. `net48`에는 `CryptographicOperations.ZeroMemory`가 없다 | P25-1에서 `MethodImplOptions.NoOptimization \| NoInlining` 등으로 막고 **Release 빌드 실측으로 확인**한다. 실측에서 제거가 관측되면 그때 다른 방식(`Marshal` 경유 쓰기 등)으로 바꾼다 — 추측으로 확정하지 않는다 |
| 3 | **GC 압축 복사본** | 해결 불가(범위 밖 확정) | `PRD.md` §4.4 #1에 한계로 명시돼 있다. 이 Phase의 완료 조건이 아니다 |
| 4 | **문자열화 지점이 곧 유출 지점** | `char[]`로 바꿔도 로그·예외 메시지·화면 표시에서 `new string(...)`을 하면 지울 수 없는 복사본이 생긴다 | P25-3/P25-4에서 **문자열화 지점을 전수 확인**하고, 민감 필드는 문자열화하지 않는 것을 원칙으로 한다. P25-8 로그 점검이 이것을 다시 훑는다 |
| 5 | **실장비 검증에 물리 카드가 필요하다** | Phase 23 P23-8에서 리더기는 연결됐으나 **물리 카드가 없어** `FNAISCRDVAN` 호출까지 못 갔던 전례가 있다 | P25-10 착수 전에 사용자에게 실카드 준비를 요청한다. 카드 없이는 이 Phase를 완료로 선언하지 않는다 — 하네스만으로는 C-A급 회귀를 못 잡는다 |
| 6 | **`SecureClear`를 어느 계층에 두는가** | 계층 규칙은 `Views` → `ViewModels` → `Services` → `Protocol` → `Interop` 단방향인데, 이 헬퍼는 `Protocol`(`PosTelegram`)과 `Services` 양쪽에서 쓴다 | P25-1에서 **`Interop`과 같은 위치의 leaf 유틸리티**(`Security/SecureClear.cs`, 아무것도 참조하지 않음)로 두고, 어느 계층에서든 호출 가능한 예외임을 `ROADMAP.md` 계층 규칙에 한 줄로 명시한다 |

## 체크포인트 (Opus 리뷰 지점)

| 체크포인트 | Task | 성격 |
|---|---|---|
| **CP1** | P25-1 ~ P25-2 | 헬퍼 + 키다운로드 소급 적용. 결제 Flow를 아직 건드리지 않는다. **여기서 `SecureClear` 자체가 진짜로 지우는지**(JIT 포함)를 확정해야 뒤가 성립한다 |
| **CP2 ★** | P25-3 ~ P25-6 | **이 Phase의 본체이자 최대 위험 구간.** 실거래 검증을 마친 결제 Flow의 타입을 바꾼다. 리뷰 범위는 수정 부분이 아니라 결제 Flow 전체 |
| **CP3** | P25-7 ~ P25-9부속 | 판정 · 로그 점검 · 증적 하네스 · 외부 메모리 덤프 검증 |
| — | P25-10 | 실장비 실측 · 회귀 · 문서 갱신. **실카드 필요** |

**체크포인트 통과 여부 — 아래 체크박스로 추적한다.** Task 완료 조건과 별개다: Task 체크박스는
"구현·자체 검증이 끝났다"는 뜻이고, 아래 체크박스는 **"Opus가 실제로 리뷰를 수행해 치명적 0건을
확인했다"**는 뜻이다. Task가 전부 체크돼 있어도 이게 비어 있으면 그 체크포인트는 검증되지 않은
것이다(Phase 24에서 CP2가 이 둘을 착각해 "검증됨"으로 잘못 표시됐던 전례가 있다 — 재발 방지).

- [x] **CP1 Opus 리뷰 통과** — 리뷰 라운드 수: 2, 최종 치명적 0건 확인일: 2026-09-03. 1차 라운드
      개선권장 3건(F1 char[] 패턴이 상위 바이트를 안 덮음, F2 셀프테스트가 JIT 방어를 증명 못함,
      F3 Array.Clear 잔재 주석 3파일) 발견 → 수정 → 2차 라운드에서 리뷰어가 직접 코드·빌드·실행으로
      재현해 전부 해결 확인, 신규 문제 0건.
- [ ] **CP2 ★ Opus 리뷰 통과** — 리뷰 라운드 수: ___, 최종 치명적 0건 확인일: ______
- [ ] **CP3 Opus 리뷰 통과** — 리뷰 라운드 수: ___, 최종 치명적 0건 확인일: ______
- [ ] **최종 전체 리뷰 통과**(P25-10 이후, 결제 Flow 전체 대상) — 최종 치명적 0건 확인일: ______

---

## P25-1. `SecureClear` — 덮어쓰기의 유일한 지점

**이 Task가 틀리면 뒤의 모든 Task가 무의미하다.** 지운다고 믿고 안 지우는 상태가 가장 나쁘다.

- `src/KFTCOneCAP.Wpf/Security/SecureClear.cs` 신설. **아무것도 참조하지 않는 leaf 유틸리티**로 두어
  어느 계층에서든 호출할 수 있게 한다(위험 #6). `ROADMAP.md`의 계층 규칙에 이 예외를 한 줄 적는다.
- 덮어쓰기 **3회**, 패턴은 `0x00` → `0xFF` → `0x00`(마지막이 0으로 끝나야 개정 기준의 "0 등의 특정
  문자로 덮어쓰기" 문구까지 만족한다 — `PRD.md` §4.3.1).
- **횟수는 `private const int OverwritePasses = 3;` 하나로.** 기준이 1회로 완화되면 이 숫자만 바꾼다.
- `byte[]`/`char[]` 두 오버로드. **`null`·빈 배열을 받아도 무해하게 통과**한다(호출부에 조건 분기를
  만들지 않기 위함 — `KeyDownloadVanClient`가 이미 쓰는 방식).
- **JIT 제거 방어**(위험 #2) — `MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining`을
  붙인다. 이것으로 충분한지는 **Release 빌드 실측으로 확인**하고, 부족하면 방식을 바꾼다.
- `Array.Clear`를 직접 부르는 코드를 앞으로 만들지 않는다는 규칙을 클래스 주석에 적는다.

**완료 조건**
- [x] `SecureClear.Clear(byte[])` / `Clear(char[])`가 있고, `null`/빈 배열에서 예외를 던지지 않는다 —
      `Security/SecureClear.cs`, `null`/`Array.Empty` 조기 return. `SecureClearSelfTest`의
      null/빈배열 케이스로 실행 확인(예외 없음).
- [x] 덮어쓰기 횟수가 상수 하나로 존재하고, 마지막 패스가 `0x00`이다(코드 확인) — `OverwritePasses = 3`,
      `OverwritePattern = { 0x00, 0xFF, 0x00 }`.
- [x] **Release 빌드**에서 실제로 배열이 0으로 채워지는 것을 확인한다 — 전용 셀프테스트
      (`Services/Diagnostics/SecureClearSelfTest.cs`, `App.xaml.cs`의 secure clear self test 인자
      분기)를 만들어 Release 빌드로 실행, `C:\KFTC_PosAgent\KFTCTaxLog\2026-09-03.log:68`에
      `[SecureClearSelfTest] 완료 — byte[]=통과, char[]=통과, null/빈배열=통과, 종합=통과` 확인함
      (2026-09-03 10:26:08). 검증 동안 `app.manifest`를 한시적으로 `asInvoker`로 낮췄다가 확인 직후
      `requireAdministrator`로 원복(파일 내 주석에 근거 기록).
- [x] `dotnet build` 통과 — Debug/Release 둘 다 경고 0, 오류 0.
- [x] `ROADMAP.md` 계층 규칙에 `Security/SecureClear` 예외를 명시했다.

## P25-2. 키다운로드 경로 소급 적용 — `Array.Clear` → `SecureClear`

**리팩터링이다. Phase 24의 동작이 바뀌면 실패한 것이다.**

- 기존 `Array.Clear` **13곳**을 `SecureClear.Clear`로 교체한다:
  `ReaderService.cs`(3), `KeyDownloadService.cs`(4), `KeyDownloadVanClient.cs`(5),
  `FnaisCrdVanInvoker.cs`(1). **호출 위치와 `try/finally` 구조는 그대로 둔다** — 호출 대상만 바꾼다.
- `FnaisCrdVanInvoker.cs:110`은 결제·키다운로드 공용이라, 이 교체로 **결제 요청 전문 사본도 함께
  3회 덮어쓰기 대상이 된다**(의도된 것이다).
- 관련 주석의 "`Array.Clear`" 표현을 갱신한다(`KeyDownloadService` 클래스 주석의 한계 설명 포함 —
  한계 내용 자체는 `PRD.md` §4.4로 옮겨져 있으므로 그쪽을 가리키게 한다).

**완료 조건**
- [x] `grep -rn "Array.Clear(" src/`가 **0건**이다(주석의 설명 문구 제외) — 실제 호출 13곳
      (`ReaderService.cs` 3, `KeyDownloadService.cs` 4, `KeyDownloadVanClient.cs` 5,
      `FnaisCrdVanInvoker.cs` 1)을 전부 `SecureClear.Clear`로 교체, 남은 매치는
      `SecureClear.cs`/`FnaisCrdVanInvoker.cs`의 설명 주석 2건뿐.
- [x] `--keydown-test` 하네스가 **교체 전과 동일한 결과**(128/128)를 낸다 —
      `C:\KFTC_PosAgent\KFTCTaxLog\2026-09-03.log:265` `완료 — 통과 128건, 실패 0건`(10:32:53).
- [x] `--van-call-test`가 교체 전과 동일한 결과(통과 4건/실패 0건)를 낸다 —
      `2026-09-03.log:309` `완료 — 통과 4건, 실패 0건`(10:37:45), 501008/800000/902614 개별 호출과
      902614 10회 반복 모두 `nRet=-1 out_szRetCode='0004'` 통신 실패로 2026-09-02 리팩터링 전과
      동일한 패턴(결제용 VAN 서버가 이 환경에서 여전히 미도달인 것이지 이번 변경의 회귀가 아님).
- [x] `--payment-flow-test` 71/71 그대로 통과 — `2026-09-03.log:596`
      `완료 — 통과 71건, 실패 0건`(10:38:40).
- [x] `dotnet build` Debug/Release 둘 다 경고 0, 오류 0.
- [x] 검증 동안 `app.manifest`를 한시적으로 `asInvoker`로 낮췄다가 확인 직후
      `requireAdministrator`로 원복(파일 내 주석에 근거 기록).

> **CP1 Opus 리뷰** — 여기까지. 특히 P25-1의 Release 실측 근거를 리뷰어가 **직접 재현**하도록 한다
> (Phase 24 C-A의 교훈 — 보고를 믿지 않고 직접 돌린다).

## P25-3. 타입 변경 ① — `CardReadData` 19개 필드

**이 Phase에서 가장 넓게 번지는 Task다.**

- `CardReadData`의 19개 `string` 필드를 **`char[]`** 로 바꾼다(`PRD.md` §4.3.2, 확정 사항 3).
- `CardReadData`가 **`IDisposable`을 구현**하고, `Dispose()`가 19개 배열을 전부 `SecureClear`한다.
  → P25-6의 일괄 클리어가 `Dispose()` 한 줄이 된다. **어느 필드를 지우는지가 한 곳에 모여** 있어
  필드가 추가돼도 누락되지 않고, 심사 증적으로도 이 메서드 하나를 보여주면 된다.
- `CardReadResponseParser` / `SequentialAsciiFieldReader`가 `string` 대신 `char[]`를 만들도록 바꾼다.
  **`Encoding.ASCII.GetString`을 쓰지 않는다** — 그 순간 지울 수 없는 `string`이 생긴다.
- `CardReadCommandOutcome` / `CardReadRoundResult`는 참조만 들고 있으므로 타입 시그니처만 따라간다.
- **`PosTelegram.Write`에 `char[]` 오버로드를 추가**한다(전제 참고). 기존 `string` 오버로드는 민감하지
  않은 필드가 계속 쓰므로 **지우지 않는다.**
- 소비 지점 2곳을 함께 고친다:
  - `HandleCardInfoInquiryAsync`(800000) — `CardNumber`의 앞 8자리를 `#14 BIN`으로. `Substring`
    대신 `char[]` 구간 복사로 바꾸고, **BIN용 임시 배열도 사용 후 `SecureClear`**.
  - `FillCardApprovalFields`(902614) — 8필드. 특히 `#45`(키버전+TC+모듈ID 결합)와 `#46`(길이+암호화
    데이터 결합)이 **문자열 결합**으로 만들어지고 있다. `char[]` 버퍼에 직접 조립하도록 바꾸고 임시
    버퍼를 `SecureClear`한다.
- **`ReaderAuthId`의 영속화는 건드리지 않는다**(위험 #1) — `ObservedIdentityStore.Upsert`에 넘길
  때만 `new string(...)`으로 변환한다. 이 예외를 코드 주석과 `PRD.md` §4에 명시한다.
- `PaymentFlowTestScenarios.cs:169`의 `CardReadData` 생성 코드가 함께 바뀐다.

**완료 조건**
- [ ] `CardReadData`에 `string` 필드가 하나도 없다(코드 확인).
- [ ] `CardReadData.Dispose()`가 19개 배열 전부를 `SecureClear`한다 — **필드 개수와 클리어 대상 개수가
      일치**함을 코드로 확인(누락 1개도 없어야 한다).
- [ ] 카드리딩 경로에서 민감 값이 `string`이 되는 지점이 `ObservedIdentityStore` 저장 1곳뿐이다
      (`grep`으로 `GetString`/`new string(` 전수 확인, 각 지점에 그래야 하는 이유가 주석에 있다).
- [ ] `--payment-flow-test` 71/71 통과. `--pos-client-test` 통과.
- [ ] `PRD.md` §4에 `ReaderAuthId` 영속화 예외를 명시했다.

## P25-4. 타입 변경 ② — PIN 경로

- `PaymentNoticeViewModel._pinDigits`(`List<char>`) → **고정 길이 `char[]` + 입력 개수**로 바꾼다.
  `List<char>`는 내부 배열이 재할당(증설)되면 **옛 배열이 그대로 남아** 지울 수 없다.
- `PinEnteredEventArgs.Pin`(`string`) → `char[]`. `CollectPinAsync`의
  `TaskCompletionSource<string>` → `TaskCompletionSource<char[]>`. `PaymentOrchestrator`의
  `string? pin` → `char[]? pin`.
- `PinFieldEncoder.ToTelegramValue(string)` → `char[]`를 받아 `#51`에 넣을 값을 `char[]`로 돌려준다.
  **SEED 암호화가 확정되면 이 메서드 본문만 바뀌는 격리 구조를 깨지 않는다**
  (`docs/payment_relay/PRD.md` §10). 기존 방어 검증(4자리·숫자)과 **"값을 예외 메시지에 담지 않는다"는
  규칙은 그대로 유지**한다.
- 화면 표시(입력한 자리 수만큼 점 표시, 잠깐 노출 후 마스킹)는 **동작을 바꾸지 않는다** — 표시용으로
  민감 값을 `string`으로 만들지 않는지만 확인한다.
- PIN 입력 취소·타임아웃·창 닫힘 경로에서도 버퍼가 지워지는지 확인한다(입력 도중 중단이 정상 경로다).

**완료 조건**
- [ ] PIN이 `string`으로 존재하는 지점이 0곳이다(`grep` 전수 확인).
- [ ] PIN 버퍼가 **정상 완료·취소·타임아웃·창 닫힘** 네 경로 모두에서 `SecureClear`된다(경로별로 확인).
- [ ] `--notice-pin-test`, `--payment-flow-test`가 그대로 통과한다.
- [ ] PIN 값이 로그·예외 메시지에 남지 않는다는 기존 규칙이 유지된다(P22 마스킹 규칙 재확인).

## P25-5. 임시 버퍼 즉시 클리어

`PRD.md` §4.2 표의 **#1, #2, #8, #9, #12, #13**을 **만든 메서드 안에서 `try/finally`로** 지운다
(§4.3.3). 수명이 그 메서드 안에서 끝나므로 판단이 필요 없다.

- `ReaderService.OnReaderCallback`의 `Marshal.Copy` 대상 배열(#1) — **주의**: 이 배열은
  `ReaderEventArgs`로 이벤트 구독자에게 전달되고 `CompletePendingIfMatches`를 거쳐 파서로 간다.
  `ReaderService.cs:522` 주석이 **"구독자를 추가할 때는 배열을 복사해 넘겨야 한다"**고 이미 경고하고
  있다. 파싱이 끝난 뒤에 지워야 하며, **지우는 시점을 앞당겨 파서가 0으로 채워진 데이터를 보는 일이
  없도록** 한다(키다운로드가 `:199`에서 쓰는 순서를 그대로 따른다).
- 카드리딩 요청 배열(#2, `TransactionInfoRequestBuilder.Build`) — DLL 호출 직후.
- `PosTelegram.Write` 내부의 `valueBytes`/`padded`(#8) — 필드 하나 쓸 때마다 생기므로 여기서 지우지
  않으면 거래당 수십 개가 남는다.
- `PosTelegram.ToBody()` / `PosResponseTelegram.ToFrame()` 복사본(#9) — 호출자가 다 쓴 뒤 지운다.
- `VanService`의 `responseBody`(#12), `PosSocketServer`의 수신 `body`·송신 `frame`(#13).

**완료 조건**
- [ ] 위 6개 지점 전부에 `try/finally` + `SecureClear`가 있다(지점별로 코드 확인).
- [ ] **파서가 지워진 데이터를 보는 회귀가 없다** — `--payment-flow-test`/`--keydown-test`/
      `--van-call-test` 전량 통과로 확인(이 회귀는 "응답 파싱 실패"로 즉시 드러난다).
- [ ] `--repeat-transactions-test`(반복 거래 리소스 테스트)가 통과한다 — 매 거래 버퍼를 더 만지므로
      누수·성능 회귀를 여기서 본다.

## P25-6. 거래 종료 일괄 클리어

`PRD.md` §4.2 표의 **#3~#7, #11**을 거래 1건 종료 `finally`에서 지운다(§4.3.3).

- `PaymentOrchestrator.RunCardTransactionAsync`의 **기존 `finally`(`:512`)를 그대로 쓴다.**
  `roundResult`/`pin` 선언을 `try` 앞으로 끌어올려 `finally`가 볼 수 있게 하는 것이 실제 작업이다.
- `roundResult.CardData?.Dispose()`(P25-3에서 만든 것) + PIN 버퍼 `SecureClear`.
- `VanService`가 받은 `outData`/`outRetCode`(#11) — 키다운로드 경로(`KeyDownloadVanClient`)가 이미
  하는 것과 **동일한 방식**으로 결제 경로에도 추가한다.
- POS 요청/응답 전문 본문(#7)은 **송신이 끝난 뒤** 지워야 한다 — `PosSocketServer.SendResponse`가
  응답을 쓰고 로그를 남긴 다음이 마지막 지점이다. `PosTelegram`에 본문을 지우는 내부 메서드를 두고
  `_body`를 밖으로 노출하지 않는다(계층 규칙).
- **`finally`가 통과하는 경로를 전부 확인한다**: 정상 종료 / 예외 / 카드리딩 타임아웃 / 사용자 취소
  (ESC·취소 버튼) / PIN 타임아웃 / POS 연결 끊김 / 설정 화면 게이트 거부 / 조기 return 각각.

**완료 조건**
- [ ] `roundResult`/`pin`이 `finally`에서 접근 가능하고, 위 8개 경로 전부에서 클리어가 실행된다
      (경로별로 확인 — 하네스 시나리오로 덮을 수 있는 것은 하네스로).
- [ ] 클리어가 **응답 생성보다 늦게** 일어난다(지워진 데이터로 응답을 만들어 보내는 회귀가 없다) —
      응답 전문 내용을 하네스로 대조한다.
- [ ] `--payment-flow-test` 71/71, `--pos-client-test`, `--repeat-transactions-test` 전량 통과.
- [ ] 기존 `finally`의 동작(`_presenter.Close()`, 미확정 정리 로그)이 그대로다.

> **CP2 ★ Opus 리뷰** — P25-3~P25-6. **이 Phase의 최대 위험 구간.** 리뷰 범위는 수정 부분이 아니라
> **결제 Flow 전체 로직**이다(사용자 지시). 리뷰어는 보고를 믿지 말고 하네스를 직접 돌리고,
> fake 경계 안쪽(실제 `ReaderService`/실제 DLL 인자 계약)을 별도로 확인한다 — Phase 24 C-A가 정확히
> 그 사각지대에서 나왔다.

## P25-7. 리더기 응답 파서 판정 (무결성체크 포함)

- **착수 전 실측으로 대부분 끝나 있다** — `InitResponseParser`/`IntegrityResponseParser`는 응답코드만
  담고 카드정보가 없다. `StatusResponseParser`만 `ReaderAuthId`/`ModuleId`를 담는다.
- `StatusResponseParser`의 식별자 2개를 클리어 대상으로 볼지 판정한다. **장비 식별자이고
  `ObservedIdentityStore`가 이미 영속화하는 값**이므로(위험 #1) 대상에서 제외하는 것이 일관되지만,
  `CardReadData` 쪽에서는 같은 값을 지운다 — **이 비대칭을 `PRD.md` §4에 근거와 함께 적는다.**
- 판정 결과를 `PRD.md` §4.2 표 #14에 반영한다.

**완료 조건**
- [ ] 리더기 응답 파서 3종의 필드를 전수 확인하고 판정 근거를 `PRD.md` §4.2 #14에 기록했다.
- [ ] 판정이 "대상"이면 클리어를 구현했고, "제외"면 그 이유가 문서에 남아 있다.

## P25-8. 로그 현황 점검 (구조 변경 없음)

**확정 사항 7** — 전수 점검해 `PRD.md` §4.5에 표로 기록하고, **마스킹 누락이 발견된 곳만** 고친다.

반드시 포함할 것:
- `LogRingBuffer`(최근 500건 메모리 상주, `Capacity = 500`) — 민감정보가 들어간 로그 문자열은
  밀려날 때까지 메모리에 남고 `string`이라 지울 수 없다. **현황을 사실대로 기록한다.**
- `TelegramLogRedactor` — 현재 `#46`(암호화 카드데이터)만 마스킹한다. **`#51`(PIN)이 요청 로그에
  남는지 실제 로그로 확인**한다(응답은 `PosResponseTelegram.BuildFailure`가 비운다).
- 키다운로드 VAN 구간(0100/0110/0120/0130) 원문 로깅 — **바꾸지 않고 기록만**(범위 밖 확정).
- 예외 메시지 경로 — `PinFieldEncoder`처럼 "값을 예외에 담지 않는다"가 지켜지는지 전수 확인.

**완료 조건**
- [ ] `PRD.md` §4.5에 점검 표가 채워졌다(경로 / 남는 값 / 마스킹 여부 / 판정).
- [ ] 마스킹 누락이 발견됐다면 그 지점만 수정했고, 수정하지 않은 항목은 이유가 표에 있다.
- [ ] 실제 로그 파일로 확인했다(코드 읽기만으로 끝내지 않는다).

## P25-9. 진단 하네스 — 심사 증적

- `--memory-clear-test`(가칭)를 `App.xaml.cs` 분기와 `Services/Diagnostics/*TestScenarios.cs` 패턴
  그대로 추가한다.
- **검증 내용**: 민감 버퍼에 값을 채운 뒤 `SecureClear`를 거치면 **읽었을 때 내용이 남아 있지 않다**는
  것을 실제로 읽어서 확인하고 결과를 로그로 남긴다. `CardReadData.Dispose()`, PIN 버퍼, 전문 본문,
  키다운로드 버퍼를 각각 시나리오로 만든다.
- **Release 빌드로 돌린 결과를 증적으로 남긴다**(위험 #2 — Debug 결과는 근거가 되지 않는다).
- 심사에서 실증을 요구받으면 이것을 그대로 돌린다.

**완료 조건**
- [ ] `--memory-clear-test`가 존재하고 전 시나리오 통과한다(Release 빌드 실행 로그로 확인).
- [ ] 관리자 권한(`app.manifest`) 상태에서도 실행 가능하다 — 필요하면 Phase 24와 같은 방식으로
      한시적으로 낮추고 **끝나면 반드시 `requireAdministrator`로 원복**한다(그 사실을 파일 주석에 기록).
- [ ] 실패를 일부러 만들었을 때(클리어를 건너뛰면) 하네스가 **실패로 잡아낸다** — 통과만 확인하고
      끝내지 않는다(하네스가 아무것도 검사하지 않는 상태를 방지).

## P25-9부속. 외부 메모리 덤프 검증 — 프로세스 전체를 대상으로

**왜 필요한가** — P25-9는 "우리가 지운 그 배열"만 확인한다. 실제 위협은 "프로세스 메모리 어딘가에
평문 카드번호가 남아 있는가"이고, 이건 우리가 모르는 경로(문자열화 누락, GC 압축 복사본)를 잡을 수
없다. 이 Task는 **앱 코드가 아니라 외부 도구로 프로세스 메모리 전체를 훑어** 그 질문에 직접 답한다.
2026-09-03 사용자 확정 — P25-9와 별개 Task로 두고 심사 증적에 포함한다.

**절차**

1. P25-10의 실장비 902614 결제(실카드)를 돌리면서, **VAN 승인 응답을 받은 직후**(카드정보가 아직
   메모리에 남아 있어야 할 시점) 프로세스 전체 메모리를 덤프한다 —
   `procdump.exe -ma KFTCOneCAP.Wpf.exe dump_before_clear.dmp`(Sysinternals, 무료 배포 도구).
2. 같은 카드로 **거래가 완전히 끝나고 `finally`의 클리어까지 실행된 후**(P25-6이 지운 뒤) 다시
   덤프한다 — `dump_after_clear.dmp`.
3. 두 덤프 파일에서 **그 카드의 실제 PAN(전체 자리)과 앞 8자리(BIN)를** ASCII/UTF-16LE 두 인코딩으로
   각각 검색한다(`findstr`/`strings.exe` 또는 짧은 검색 스크립트 — 새 상용 도구를 만들 필요는 없다).
4. **기대 결과**: `dump_before_clear.dmp`에서는 나온다(안 나오면 애초에 이 검증 방법 자체가 잘못됐다는
   뜻이므로 절차를 재점검한다) / `dump_after_clear.dmp`에서는 **나오지 않는다.**
5. `dump_after_clear.dmp`에서 패턴이 나오면 — 그 오프셋 주변을 봐서 어느 구조체/버퍼로 추정되는지
   기록하고, GC 압축 복사본(PRD §4.4 한계로 이미 알려진 것)인지 새로 발견된 누락 경로인지 판정한다.
   후자면 회귀로 취급해 되돌아가 고친다. 전자면 PRD §4.4에 "실측으로 확인됨"이라고 갱신한다.

**보안 주의 — 덤프 파일 자체가 민감정보다**

- 덤프 파일은 **실카드 전체 PAN**을 담은 평문이다. 저장소에 커밋하지 않는다(`.gitignore` 확인).
  검증이 끝나면 **즉시 삭제**하고, 결과(오프셋/판정만)만 `development_plan.md`에 남긴다 — 덤프 자체나
  카드번호 원문은 문서에 붙여넣지 않는다.
- 이 검증은 사용자가 동석해 실카드를 쓰는 자리에서만 수행한다(P25-10과 같은 세션).

**완료 조건**
- [ ] `dump_before_clear.dmp`에서 실카드 PAN이 검출된다(검증 방법 자체가 유효함을 확인).
- [ ] `dump_after_clear.dmp`에서 PAN·BIN이 검출되지 않는다(ASCII·UTF-16LE 둘 다).
- [ ] 검출됐다면 원인을 판정하고(GC 압축 복사본 vs 신규 누락) 결과를 문서에 남겼다 — 신규 누락이면
      회귀로 고치고 재검증했다.
- [ ] 덤프 파일 2개를 검증 직후 삭제했다(삭제 확인 — `Remove-Item`/`rm` 실행 로그 남김).

## P25-10. 실장비 검증 + 회귀 + 문서 갱신

**실카드가 필요하다**(위험 #5). 착수 전에 사용자에게 준비를 요청한다.

- 실제 리더기 + 실제 카드로 **902614(승인요청)를 끝까지** 돌린다 — 카드리딩 → PIN 입력 → VAN 중계 →
  POS 응답까지. 800000(카드정보조회)도 1회.
- 확인할 것: 결제가 **회귀 없이 성공**한다 / 응답 전문 내용이 타입 변경 전과 같다 / 로그 문구가
  같다 / 클리어가 실행된 기록이 남는다.
- 취소·타임아웃 경로도 실장비로 1회씩 확인한다(클리어가 `finally`에 있으므로 중단 경로가 오히려
  중요하다).
- **P25-9부속(외부 메모리 덤프 검증)을 이 실장비 902614 결제와 같은 세션에서 함께 수행한다** —
  실카드가 필요한 검증이라 따로 자리를 만들지 않는다.
- 회귀: `--payment-flow-test`, `--keydown-test`, `--van-call-test`, `--pos-client-test`,
  `--repeat-transactions-test`, `--memory-clear-test` 전량 재실행.
- 문서 갱신: `PRD.md` §4.2 표를 "대상" → "적용 완료"로 정리, §4.5 점검 표 확정, `ROADMAP.md`
  Phase 25 체크박스와 완료일 기록.

**완료 조건**
- [ ] 실장비 + 실카드로 902614가 끝까지 성공하고, 그 로그가 남아 있다.
- [ ] 800000, 취소 경로, 타임아웃 경로 각 1회 확인.
- [ ] 하네스 6종 전량 통과.
- [ ] `PRD.md` §4.2 / §4.5, `ROADMAP.md` Phase 25 갱신 완료.
- [ ] `app.manifest`가 `requireAdministrator`로 원복돼 있다.

> **최종 Opus 전체 리뷰** — 치명적 0건이 될 때까지 검증-수정을 반복한다. 검증 범위는 **결제 Flow
> 전체**이며, 리뷰어는 하네스와 실장비 로그를 직접 확인한다.

---
