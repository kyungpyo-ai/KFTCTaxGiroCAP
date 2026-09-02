# 실행계획서: 운영 기능 (Phase 22~)

> `PRD.md`(무엇을) → `ROADMAP.md`(어떤 순서로) → **이 문서(Task 단위로 무엇을 어떻게, 어디까지 하면
> 끝인지)**. 실제 코드 작성은 이 문서의 Task를 순서대로 따라간다.
>
> **Phase 24는 아직 작성하지 않았다** — 2차 범위에서 확정한 방식대로(2026-08-20 사용자 확정)
> **한 Phase씩 착수 직전에 작성**한다. 앞 Phase의 결과에 따라 뒤쪽 계획이 조정될 여지가 있다.

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
