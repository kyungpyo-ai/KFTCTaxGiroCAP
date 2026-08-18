# ReaderSerial.dll 연동 가이드

암호화 리더기 시리얼 통신 제어 DLL — POS 연동 개발자를 위한 안내서

문서 버전: 2.3 (2026-08-13) · 대상 DLL: `ReaderSerial.dll` (Win32/x86) · 대상 플랫폼: MFC / C·C++ / C#(P/Invoke)

샘플 프로젝트(MFC/C#)를 함께 제공합니다. 이 문서는 그 샘플과 함께 볼 최소한의 API 계약만 다룹니다.

**2026-08-13 갱신 (같은 날, 이어서)**: `Pinpad_SendCommand`의 `commandCode` 파라미터 타입이 `int`에서 `unsigned char`로 바뀌었습니다(§6.3) — `Reader_SendCommand`의 `commandCode`와 타입을 통일했습니다. `PinpadCommandCode` 값(`0xA0`~`0xA4`)은 원래도 1byte 범위였으므로 실질적인 값 범위 변경은 없습니다. C#에서 P/Invoke를 직접 선언한 경우 `int` → `byte`로 갱신하세요.

**2026-08-13 갱신**: `READER_ERR_RECEIVE_FAIL`(-1201)/`READER_ERR_TIMEOUT`(-1202)/`READER_ERR_FRAME_STALL`(-1203)/`READER_ERR_LRC_MISMATCH`(-1301)이 `ReaderResult`에서 제거됐습니다(§4) — `resultCode`가 `READER_CALLBACK`에서 빠진 2026-08-12 이후로 실제 반환/전달 경로가 없는 죽은 값이었습니다. 해당 상황은 항상 `eventType`(`READER_EVENT_RECEIVE_ERROR`/`TIMEOUT`/`FRAME_STALL`/`LRC_ERROR`)으로만 통지됩니다 — `READER_ERR_BUFFER_OVERFLOW`는 -1302에서 -1301로 재번호됐습니다.

**2026-08-12 갱신 (이번 세션 중 가장 큰 변경)**: (1) `READER_CALLBACK`의 `commandCode` 파라미터가 "그 자리는 항상 응답 코드"라는 원칙으로 통일됐습니다(§2) — `READER_EVENT_TIMEOUT`이 더 이상 요청 코드가 아니라 기다리던 응답 코드를 담습니다. (2) `PINPAD_CALLBACK` 시그니처가 전면 재설계됐습니다(§7) — 3번째 파라미터가 `result`에서 `commandCode`로 바뀌었고, `PinpadEventType`이 3종에서 8종으로 세분화되어 `failInfo`(3byte payload) 개념이 완전히 제거됐습니다. 기존에 이 콜백을 연동한 코드가 있다면 반드시 §7을 다시 읽고 갱신하세요.

**2026-08-11 갱신**: 2차 개발(핀패드 SPEC 연동)로 추가된 `Pinpad_SendCommand`/`PINPAD_CALLBACK`과, `Reader_OpenPort`에 추가된 `pinpadCallback` 인자를 반영했습니다(§1.1, §6~§8). 기존 리더기 전용 연동(핀패드 미사용)은 `pinpadCallback`에 `nullptr`을 넘기면 이전과 동일하게 동작합니다 — 이 갱신으로 인한 리더기 전용 연동 쪽 동작 변경은 없습니다.

**2026-08-11 재번호**: 핀패드 오류 코드 중 코드상 실제로 발생하지 않는 죽은 값 3종(`READER_ERR_PINPAD_NAK`, `READER_ERR_PINPAD_TAMPER`, `READER_ERR_FRAME_AMBIGUOUS`)을 제거하고, `READER_ERR_PINPAD_TIMEOUT`을 -1406에서 -1402로 재번호했습니다. 외부 배포 전이라 하위 호환 문제는 없습니다.

---

## 1. 공개 API 5종

모두 C ABI + `__stdcall`. 최초 `Reader_OpenPort()` 호출이 초기화를 겸하며, `Reader_Initialize`/`Shutdown`/`GetLastError` 류는 없습니다. 리더기 전용 4종(`Reader_OpenPort`/`Reader_ClosePort`/`Reader_IsPortOpen`/`Reader_SendCommand`)에 핀패드 전용 1종(`Pinpad_SendCommand`)이 더해졌습니다.

**기본 흐름(리더기)**: `OpenPort` → `SendCommand(0x60 초기화)` → CALLBACK으로 응답 수신 → 업무 명령 반복 → `ClosePort`.

**기본 흐름(핀패드)**: `OpenPort(pinpadCallback 등록)` → `Pinpad_SendCommand(PINPAD_CMD_INIT)` → `PINPAD_CALLBACK`으로 완료 수신 → PIN 입력 명령(§6) 반복 → `ClosePort`.

### 1.1 Reader_OpenPort — 포트 열기

```cpp
int __stdcall Reader_OpenPort(int portNumber, int baudRate,
    READER_CALLBACK readerCallback, PINPAD_CALLBACK pinpadCallback,
    void* userContext, int* outReaderId);
```

동기 함수입니다. 반환 시점에 포트/수신 스레드/내부 버퍼가 모두 준비 완료 상태입니다.

| 인자 | 의미 |
|---|---|
| `portNumber` | COM 포트 번호(예: `3` → `COM3`). `1` 이상이어야 함 |
| `baudRate` | 통신 속도. 리더기/핀패드 SPEC에 맞는 값을 그대로 설정(양수여야 함) — §6.1 "포트 구성" 참고 |
| `readerCallback` | 리더기 응답/이벤트 CALLBACK 함수 포인터. `pinpadCallback`을 지정했다면(핀패드 전용 장비) `nullptr`도 허용됨 |
| `pinpadCallback` | 핀패드 응답/이벤트 CALLBACK 함수 포인터(§7). 핀패드를 쓰지 않으면 `nullptr` |
| `userContext` | CALLBACK 호출 시 그대로 돌려받을 임의 포인터(DLL은 해석하지 않음). 두 CALLBACK 모두에 동일하게 전달됨 |
| `outReaderId` | 성공 시 발급되는 `readerId`를 받을 출력 포인터. `nullptr` 불가 |

`readerCallback`/`pinpadCallback`은 **둘 다 동시에 `nullptr`인 경우에만** 거부됩니다 — 리더기 전용 장비는 `pinpadCallback`에, 핀패드 전용 장비는 `readerCallback`에 `nullptr`을 넘기면 됩니다.

| 반환값 | 의미 |
|---|---|
| `READER_OK` | 성공. `*outReaderId`에 유효한 식별자 채워짐 |
| `READER_ERR_INVALID_ARGUMENT` | `portNumber`/`baudRate`/`outReaderId`가 유효하지 않거나, `readerCallback`/`pinpadCallback`이 둘 다 `nullptr` |
| `READER_ERR_PORT_NOT_FOUND` | 지정한 COM 포트 장치 없음 |
| `READER_ERR_PORT_ALREADY_OPEN` | 동일 포트가 이미 열려 있음(DLL 내부 또는 다른 프로세스) |
| `READER_ERR_PORT_OPEN_FAIL` / `READER_ERR_PORT_CONFIG_FAIL` | 그 외 포트 열기/통신 파라미터 설정 실패 |
| `READER_ERR_MAX_READER_COUNT` | 리더기 슬롯(최대 8개, 장비 종류 무관) 모두 사용 중 |

실패 시 CALLBACK은 호출되지 않고 `*outReaderId`도 쓰이지 않습니다 — 실패는 반환값으로만 확인하세요.

### 1.2 Reader_ClosePort — 포트 닫기

```cpp
int __stdcall Reader_ClosePort(int readerId);
```

포트와 관련 자원(수신 스레드, 핸들 등)을 정리합니다. 반환된 시점 이후로는 해당 리더기의 CALLBACK이 더 이상 발생하지 않습니다.

| 반환값 | 의미 |
|---|---|
| `READER_OK` | 성공. 이미 닫혀 있던 포트를 다시 닫아도 오류가 아니라 이 값(멱등) |
| `READER_ERR_INVALID_READER_ID` | 유효하지 않은 `readerId`(범위 밖 또는 사용 중이 아닌 슬롯) |

### 1.3 Reader_IsPortOpen — 포트 상태 조회

```cpp
int __stdcall Reader_IsPortOpen(int readerId);
```

상태를 변경하지 않는 순수 조회 함수입니다.

| 반환값 | 의미 |
|---|---|
| `1` | 포트 열림 |
| `0` | 그 외 모든 상태(닫힘/여는 중/닫는 중/오류) — 세부 상태는 구분해 노출하지 않음 |
| `READER_ERR_INVALID_READER_ID` | 유효하지 않은 `readerId` |

**주의**: `Reader_SendCommand()`가 포트 상태를 자체적으로 원자적 검증하므로, 이 함수를 명령 송신 전 사전 게이트로 쓰지 마세요 — 체크 시점과 실제 송신 시점 사이에 상태가 바뀔 수 있는 경합만 늘어납니다. UI 상태 표시 용도로만 사용하세요.

### 1.4 Reader_SendCommand — 명령 송신

```cpp
int __stdcall Reader_SendCommand(int readerId, unsigned char commandCode,
    const unsigned char* data, int dataLength);
```

전문을 조립해 송신합니다. **반환값은 "송신을 시작할 수 있었는지"에 대한 즉시 결과일 뿐, 리더기의 실제 업무 처리 결과가 아닙니다** — 업무 응답은 이후 CALLBACK(`READER_EVENT_RESPONSE`)으로 비동기 전달됩니다.

| 인자 | 의미 |
|---|---|
| `readerId` | 대상 리더기 식별자 |
| `commandCode` | 전문 구분 코드 1byte. `0x60`(초기화 요청)은 특별 취급됨(아래 참고) |
| `data` | 업무 Data 영역 포인터. `nullptr`이면 `dataLength`는 반드시 `0` |
| `dataLength` | `data`의 byte 길이(STX/Length/CommandCode/ETX/LRC 제외) |

| 반환값 | 의미 |
|---|---|
| `READER_OK` | 송신 성공(업무 처리 성공을 의미하지 않음) |
| `READER_ERR_BUSY` | 이미 다른 명령이 응답 대기 중. 멀티패드(리더기+핀패드가 같은 포트를 공유하는 장비) 구성에서는 핀패드 명령이 진행 중일 때도 이 코드로 거절됩니다(§6.4 "리더기/핀패드 크로스 BUSY" 참고) |
| `READER_ERR_COMMAND_NOT_ALLOWED` | 이미 초기화 진행 중에 또 초기화 요청 |
| `READER_ERR_PORT_NOT_OPEN` / `READER_ERR_PORT_CLOSING` | 포트가 열려 있지 않거나 닫히는 중 |
| `READER_ERR_SEND_FAIL` | 실제 송신 단계 실패(즉시 IDLE 복귀, §5 참고) |
| `READER_ERR_INVALID_ARGUMENT` / `READER_ERR_INVALID_LENGTH` / `READER_ERR_BUFFER_OVERFLOW` | 인자·프레임 길이 문제 |

**`0x60`(초기화 요청) 특별 취급**: `IDLE`/`WAITING_RESPONSE` 어느 상태에서든 항상 허용되며, 응답 대기 중이던 다른 명령이 있었다면 그 명령을 무효화합니다. `INITIALIZING` 상태에서만 거부됩니다.

### 핵심 제약

- 리더기·핀패드 슬롯 **최대 8개**(장비 종류 무관, 2026-08-06 2 → 8 확대), 서로 완전히 독립(한쪽 오류가 다른 쪽에 영향 없음).
- **자동 재연결 없음.** 케이블 분리 후에는 POS가 `Reader_ClosePort()` → `Reader_OpenPort()`를 명시적으로 다시 호출해야 합니다(§5).
- 핀패드를 함께 쓰는 경우의 추가 제약(크로스 BUSY, 응답 유실 트레이드오프)은 §6을 반드시 읽으세요.

---

## 2. CALLBACK

```cpp
typedef void (__stdcall *READER_CALLBACK)(
    int readerId, int eventType,
    unsigned char commandCode, const unsigned char* data,
    int dataLength, void* userContext);
```

> 2026-08-12: `resultCode` 파라미터를 제거했습니다 — `eventType`에서 항상
> 유도 가능한 중복 정보였기 때문입니다. `PINPAD_CALLBACK`의 3번째 파라미터는
> 같은 날 별도로 `result`에서 `commandCode`로 바뀌었습니다 — §7 참고.
>
> **2026-08-12 추가 — `commandCode`는 "그 자리는 항상 응답 코드"**: 과거
> `READER_EVENT_TIMEOUT`만 예외적으로 요청의 `commandCode`(예: `0x60`)를
> 담았으나, 이제 그 요청이 **기다리고 있던 응답 코드**(예: `0x70`)를 담도록
> 통일됐습니다. `READER_EVENT_FRAME_STALL`도 과거 항상 `0`이었으나, 정체된
> 프레임이 지금 기다리는 응답과 일치해 즉시 실패 처리된 경우에는 그 응답
> 코드가 실립니다(그 외에는 여전히 `0`).

| 파라미터 | 의미 |
|---|---|
| `readerId` | 이벤트를 유발한 리더기 식별자 |
| `eventType` | 아래 이벤트 표 참고 |
| `commandCode` | 응답 전문 구분 코드(예: `0x70`). 없으면 `0`. `TIMEOUT`은 기다리던 응답 코드, `FRAME_STALL`은 즉시 실패 매칭 시에만 그 응답 코드가 실립니다(위 안내 참고) |
| `data` | 응답 Data 시작 포인터. **첫 2byte가 SPEC 업무 응답 코드(00~23, ASCII)** — §3 참고 |
| `dataLength` | `data` byte 길이. 없으면 `0`이고 `data`는 `nullptr` |
| `userContext` | `Reader_OpenPort` 호출 시 넘긴 값 그대로 |

### 이벤트 종류 (`eventType`)

| 값 | 이름 | 발생 시점 |
|---:|---|---|
| 0 | `READER_EVENT_RESPONSE` | 명령의 최종 응답 정상 수신 |
| 1 | `READER_EVENT_TIMEOUT` | 응답 없이 타임아웃(일반 3초 / 거래 200초) — 즉시 IDLE 복귀. `commandCode`는 기다리던 응답 코드(2026-08-12부터) |
| 2 | `READER_EVENT_LRC_ERROR` | 수신 프레임 LRC 검증 실패. 손상된 프레임이 지금 기다리던 명령의 응답이 확실하면(2026-08-12부터) 명령 상태가 Timeout을 기다리지 않고 즉시 IDLE로 복귀하므로, 다음 명령을 곧바로 보내도 된다 — 무관한 프레임(예: 카드 감지 `0x76`이 손상된 경우)이면 기존처럼 원래 Timeout까지 대기한다 |
| 3 | `READER_EVENT_RECEIVE_ERROR` | 수신 오류(케이블 분리 등 포트 장애) |
| 4 | `READER_EVENT_UNSOLICITED` | 리더기가 자발적으로 보낸 전문(카드 감지 `0x76` 등) |
| 5 | `READER_EVENT_FRAME_STALL` | 미완성 프레임이 1초간 정체되어 폐기(내부 재동기화 목적). 정체된 프레임이 지금 기다리던 명령의 응답이 확실하면(2026-08-12부터) 명령 상태가 즉시 IDLE로 복귀하고 `commandCode`에 그 응답 코드가 실린다 — 그렇지 않으면 기존처럼 명령 상태에 영향 없고 `commandCode = 0` |

### 데이터 수명 규칙 (중요)

`data`는 **CALLBACK 함수가 실행되는 동안에만 유효**합니다. 반환 즉시 DLL이 버퍼를 0으로 지웁니다. 이후에도 필요하면 **콜백 내부에서 즉시 복사**하세요. CALLBACK은 리더기별 수신 스레드에서 동기 호출되므로, 내부에서 UI를 직접 건드리지 말고 복사한 데이터를 `PostMessage` 등으로 넘기세요.

---

## 3. 두 가지 응답 코드 체계 — 혼동 주의

| 구분 | `ReaderResult` (DLL 오류 코드) | SPEC 업무 응답 코드 |
|---|---|---|
| 전달 위치 | 함수 반환값, CALLBACK의 `eventType`(§2 참고) | `data`의 **첫 2byte** |
| 형식 | `int`, 음수(성공만 0) | ASCII 2문자(`"00"`~`"23"`) |
| 의미 | 포트 상태/BUSY 등 **통신 레벨** | 카드 오류/취소 등 **리더기 업무 레벨** |
| 해석 | DLL이 정의 | 리더기 SPEC — DLL은 그대로 전달만 함 |

`00`(정상) 외의 값이면 응답 Data에는 이 2byte만 담기고 나머지 업무 필드는 생략되는 것이 일반적입니다(상태확인 응답은 예외). 00~23 전체 코드 의미는 리더기 SPEC 문서를 참고하세요.

---

## 4. 오류 코드 (`ReaderResult`)

| 값 | 이름 | 의미 |
|---:|---|---|
| 0 | `READER_OK` | 성공 |
| -1001 | `READER_ERR_INVALID_ARGUMENT` | 함수 인자 검증 실패 |
| -1002 | `READER_ERR_MAX_READER_COUNT` | 리더기 슬롯(최대 8개) 모두 사용 중 |
| -1003 | `READER_ERR_INVALID_READER_ID` | 유효하지 않은 `readerId` |
| -1004 | `READER_ERR_BUSY` | 이미 다른 명령이 응답 대기 중 |
| -1005 | `READER_ERR_COMMAND_NOT_ALLOWED` | 초기화 진행 중 추가 명령 거부 |
| -1100 | `READER_ERR_PORT_NOT_FOUND` | 지정한 COM 포트 장치 없음 |
| -1101 | `READER_ERR_PORT_OPEN_FAIL` | 포트 열기 실패(그 외 사유) |
| -1102 | `READER_ERR_PORT_ALREADY_OPEN` | 동일 포트가 이미 열려 있음 |
| -1103 | `READER_ERR_PORT_NOT_OPEN` | 포트가 열려 있지 않음 |
| -1104 | `READER_ERR_PORT_CONFIG_FAIL` | 통신 파라미터 설정 실패 |
| -1105 | `READER_ERR_PORT_CLOSING` | 송신 시도 시점에 포트가 닫히는 중 |
| -1200 | `READER_ERR_SEND_FAIL` | 실제 송신 실패(즉시 IDLE 복귀) |
| -1300 | `READER_ERR_INVALID_LENGTH` | 프레임 길이 허용 범위 초과 |
| -1301 | `READER_ERR_BUFFER_OVERFLOW` | 프레임이 내부 버퍼 용량 초과 |
| -1400 | `READER_ERR_PINPAD_NOT_SUPPORTED` | 이 포트에 `pinpadCallback`이 등록되지 않음 |
| -1900 | `READER_ERR_INTERNAL` | 분류 외 내부 오류 |

> 수신 오류/응답 타임아웃/미완성 프레임 정체/LRC 검증 실패는 함수 반환값이 아니라
> 항상 `READER_CALLBACK`의 `eventType`(`READER_EVENT_RECEIVE_ERROR`/`TIMEOUT`/
> `FRAME_STALL`/`LRC_ERROR`, 위 이벤트 종류 표 참고)으로만 통지됩니다.

2026-08-12: `READER_ERR_PINPAD_STEP_FAILED`(-1401)/`READER_ERR_PINPAD_TIMEOUT`(-1402)는 `PINPAD_CALLBACK` 전면 재설계로 `Pinpad_SendCommand`가 실제로 반환하는 경로가 완전히 사라져 제거했습니다(재번호 불필요). 실패 원인은 §7의 `PinpadEventType`로 통지됩니다.

핀패드 오류 코드 전체(결번 처리된 값 포함)와 상세 발생 조건은 §7, `DOC/개발문서/오류코드정의서.md` §3.7을 참고하세요.

---

## 5. 권장 연동 패턴 — 포트 오류 시 자동 재연결

`Reader_SendCommand`를 직접 호출하는 대신, 다음 흐름의 래퍼로 감싸는 것을 권장합니다(샘플 프로젝트의 `SendCommandSafe` 구현 참고).

1. `readerId`가 없으면 `Reader_OpenPort()`로 먼저 확보
2. `Reader_SendCommand()` 시도
3. `READER_ERR_PORT_NOT_OPEN`이면 `Reader_ClosePort()` → `Reader_OpenPort()` → 재시도 1회. 재오픈 성공 시 **새 `readerId`로 상태를 반드시 덮어쓰기**(옛 id 재사용 금지)
4. `READER_ERR_BUSY`는 복구 대상이 아님 — 이미 정상 진행 중인 명령이 있다는 뜻이므로 그대로 반환(여기서 Close하면 그 명령을 강제로 죽임)

`Reader_IsPortOpen()`을 사전 체크로 쓰지 마세요 — 체크와 실제 송신 사이에도 경합이 있어 신뢰할 수 없습니다.

---

## 6. 핀패드 연동 (2차 개발)

핀패드는 리더기와 별도의 Open 함수가 없습니다 — `Reader_OpenPort()` 호출 시 `pinpadCallback`을 등록하면 그 `readerId`로 핀패드 명령도 함께 보낼 수 있습니다.

### 6.1 포트 구성 — 별도 핀패드 vs 멀티패드

이 DLL이 다루는 핀패드는 두 가지 물리 구성으로 연결될 수 있으며, **DLL 사용법은 동일**하지만 동작 특성(특히 §6.4의 크로스 BUSY/응답 유실)이 다릅니다.

| 구성 | 설명 | Baud Rate | 크로스 BUSY 영향 |
|---|---|---|---|
| **별도 핀패드** | 리더기와 핀패드가 서로 다른 COM 포트에 연결된 별개 장치. `Reader_OpenPort()`를 리더기용/핀패드용으로 각각 한 번씩 호출해 서로 다른 `readerId` 2개를 확보합니다(리더기 쪽은 `pinpadCallback = nullptr`, 핀패드 쪽은 `readerCallback = nullptr`로 열면 됩니다). | 별도 57,600 |
| **멀티패드** | 리더기와 핀패드 기능이 **같은 COM 포트**를 공유하는 하나의 물리 장치. `Reader_OpenPort()`를 한 번만 호출해 `readerCallback`/`pinpadCallback`을 **모두** 등록하고, 같은 `readerId`로 `Reader_SendCommand`/`Pinpad_SendCommand`를 둘 다 사용합니다. | 115,200 |

Baud Rate는 장비 SPEC에 따르며 DLL이 강제하지 않습니다(§1.1) — 실제 연결 장비의 매뉴얼을 따르세요. 위 값은 이 DLL 개발 시 검증에 쓰인 장비 기준 참고값입니다.

### 6.2 포트 오픈 시 `pinpadCallback` 등록

```cpp
READER_CALLBACK myReaderCallback = ...;
PINPAD_CALLBACK myPinpadCallback = ...;
int readerId = 0;
int rc = Reader_OpenPort(3, 115200, myReaderCallback, myPinpadCallback, nullptr, &readerId);
```

`readerCallback`/`pinpadCallback` 중 하나만 필요하면 다른 하나에 `nullptr`을 넘기면 됩니다. 둘 다 `nullptr`이면 `READER_ERR_INVALID_ARGUMENT`로 거부됩니다.

### 6.3 명령 5종 사용법

```cpp
int __stdcall Pinpad_SendCommand(int readerId, unsigned char commandCode,
    const unsigned char* data, int dataLength);
```

| `commandCode` | 값 | `dataLength` | `data` 레이아웃 |
|---|---|---:|---|
| `PINPAD_CMD_INIT` | `0xA0` | 0 | 없음 |
| `PINPAD_CMD_PIN_PASSWORD` | `0xA1` | 1 | `MaxPinLength(1)` |
| `PINPAD_CMD_PIN_NUMBER` | `0xA2` | 1 | `MaxPinLength(1)` |
| `PINPAD_CMD_PIN_DES` | `0xA3` | 17 | `MaxPinLength(1) + WorkingKey(8) + ACN(8)` |
| `PINPAD_CMD_PIN_SEED` | `0xA4` | 13 | `MaxPinLength(1, 0~6만 허용) + RNUM(12)` |

`dataLength`는 표의 값과 **정확히 일치**해야 하며(초과/부족 모두 `READER_ERR_INVALID_ARGUMENT`), 화면 표시 문구(Line1/Line2)는 POS가 지정할 수 없습니다 — DLL이 명령별 고정 기본 문구를 항상 사용합니다. `PINPAD_CMD_PIN_DES`의 TMKID도 POS 입력 필드가 아니며 DLL이 내부적으로 `0x00` 고정 전송합니다.

성공 시 `READER_OK`를 반환하지만, 이는 **조합 시퀀스가 시작됐다는 뜻일 뿐** 완료를 의미하지 않습니다 — 실제 완료/실패/타임아웃은 `PINPAD_CALLBACK`(§7)으로 비동기 통지됩니다.

### 6.4 리더기/핀패드 크로스 BUSY

멀티패드(같은 포트 공유) 구성에서는 리더기 명령과 핀패드 명령이 서로를 막습니다 — 한쪽이 진행 중이면 다른 쪽 명령은 `READER_ERR_BUSY`로 거절됩니다.

- **예외**: 두 초기화 명령(리더기 `0x60`, 핀패드 `PINPAD_CMD_INIT`)은 상대방에게 막히지 않고, 상대방의 걸려있는 잔여 상태를 조용히 정리한 뒤 항상 시작됩니다.
- 별도 핀패드 구성(서로 다른 COM 포트)에서는 두 장치가 완전히 독립된 상태를 가지므로 이 크로스 BUSY가 실질적으로 발동하지 않습니다.

### 6.5 핀패드 진행 중 리더기 프레임 유실 — 반드시 숙지할 제약

**멀티패드 구성에서, 핀패드 명령이 진행 중인 동안 도착하는 리더기 프레임은 유실됩니다.** 여기에는 다음이 모두 포함됩니다.

- 리더기가 자발적으로 보내는 비요청 이벤트(카드 감지 등)
- **핀패드 명령을 보내기 직전에 이미 전송해 둔 리더기 명령의 정상 응답** — 이 경우 리더기 쪽은 `READER_EVENT_RESPONSE` 대신 `READER_EVENT_TIMEOUT`을 받게 됩니다.

§6.4의 크로스 BUSY 덕분에 핀패드 진행 중 **새로** 시도하는 리더기 명령은 애초에 거절되어 이 문제의 영향을 받지 않지만, **핀패드 명령을 보내기 직전에 이미 보내둔 리더기 명령**의 응답은 여전히 유실될 수 있습니다.

> **경고**: 이 DLL은 "PIN 입력이 진행되는 동안에는 POS가 리더기 명령을 새로 보내지 않는다"는 운영 방침을 전제로 이 트레이드오프를 감수하도록 설계되었습니다. 이 방침을 지키지 않고 리더기 명령과 핀패드 명령을 실제로 동시에 사용하면, 리더기 명령의 응답을 조용히 잃을 수 있습니다(오류 CALLBACK 없이 단지 타임아웃만 발생). 멀티패드 구성을 사용하는 POS는 **핀패드 명령을 보내기 전에 진행 중인 리더기 명령이 없는지 반드시 확인**하고, 핀패드 시퀀스가 완료(`PINPAD_CALLBACK` 수신)될 때까지 새 리더기 명령을 보내지 마세요.

---

## 7. `PINPAD_CALLBACK`

**2026-08-12 전면 재설계** — 이전 버전(3번째 파라미터 `result`, 실패 원인을 `PINPAD_EVENT_ERROR` 하나로 뭉쳐 3byte `failInfo` payload로 `data`에 싣던 설계)은 완전히 폐기됐습니다. 이 절이 이 문서에서 가장 크게 바뀐 부분이니, 기존에 `failInfo`/`data[2]`를 파싱하던 연동 코드가 있다면 반드시 아래 새 방식으로 갱신하세요.

```cpp
typedef void (__stdcall *PINPAD_CALLBACK)(
    int readerId, int eventType, unsigned char commandCode,
    const unsigned char* data, int dataLength, void* userContext);
```

`READER_CALLBACK`과 별도 타입입니다. 데이터 수명 규칙(§2 "데이터 수명 규칙")은 동일하게 적용됩니다 — `data`는 콜백 실행 중에만 유효하며, 이후 필요하면 콜백 내부에서 즉시 복사하세요.

| 파라미터 | 의미 |
|---|---|
| `readerId` | 이벤트를 유발한 리더기/핀패드 식별자 |
| `eventType` | 아래 이벤트 표 참고 — **POS는 이제 `data`를 파싱할 필요 없이 `eventType`으로 바로 분기하면 됩니다** |
| `commandCode` | POS가 `Pinpad_SendCommand(readerId, commandCode, ...)`에 넘긴 그 `PinpadCommandCode` 값(`0xA0`~`0xA4`) 그대로. 내부적으로 몇 단계(0xF1→0xF3→0xF7 등)를 거치든, 그 시퀀스에서 발생하는 모든 CALLBACK은 항상 이 값이 동일합니다 — POS는 내부 SPEC Fc/subCode를 몰라도 "이 이벤트가 어떤 명령에 관한 것인지" 이 값으로 알 수 있습니다 |
| `data` | `PINPAD_EVENT_RESPONSE`일 때만 실제 완료 데이터(명령별 상이). 그 외 모든 이벤트는 항상 `nullptr` |
| `dataLength` | `data`의 byte 길이. `data == nullptr`이면 `0` |
| `userContext` | `Reader_OpenPort` 호출 시 넘긴 값 그대로 |

### 이벤트 종류 (`eventType`)

과거 `PINPAD_EVENT_ERROR` 하나로 뭉쳐 있던 실패 원인이 리더기 `ReaderEventType`과 동일하게 원인별 최상위 이벤트로 승격됐습니다 — `failInfo`/`PinpadFailReason` 개념은 완전히 제거됐습니다.

| 값 | 이름 | 발생 시점 |
|---:|---|---|
| 0 | `PINPAD_EVENT_RESPONSE` | 조합 명령 정상 완료 (`data`에 완료 데이터) |
| 1 | `PINPAD_EVENT_TIMEOUT` | 응답 대기 시간 초과(ACK 3초 / PIN 입력 200초), 내부 복구 완료 후 |
| 2 | `PINPAD_EVENT_NAK` | NAK 수신(ACK 대기 단계는 1회 재전송 후에도 실패, PIN 입력 단계는 재전송 없이 즉시) |
| 3 | `PINPAD_EVENT_LRC_ERROR` | 수신 전문 LRC 불일치 |
| 4 | `PINPAD_EVENT_TAMPER` | Tamper(물리적 파손) 감지 |
| 5 | `PINPAD_EVENT_SEND_FAIL` | 포트 쓰기(송신) 실패 |
| 6 | `PINPAD_EVENT_RECEIVE_ERROR` | 포트 물리 장애(케이블 분리 등) — 진행 중이던 명령이 있으면 즉시 실패 처리됨 |
| 7 | `PINPAD_EVENT_FRAME_STALL` | 미완성 프레임 정체 — 정체된 프레임이 지금 기다리던 응답임이 확실할 때만 즉시 실패 처리됨 |

2026-08-12: `READER_ERR_PINPAD_STEP_FAILED`(-1401)/`READER_ERR_PINPAD_TIMEOUT`(-1402)은 이 재설계로 `Pinpad_SendCommand`가 실제로 반환하는 경로가 완전히 사라져 `ReaderErrors.h`에서 제거됐습니다(§4 참고) — 더 이상 존재하지 않는 값이니 참조하지 마세요.

---

## 8. 알려진 제약

- 자동 재연결 없음(§1, §5) · 리더기·핀패드 슬롯 최대 8개(장비 종류 무관) · Win32(x86) 전용, x64 미제공
- 기본 로그 정책상 일부 응답 Data가 마스킹되어 기록되지만 완전한 무노출은 보장하지 않습니다 — 로그 파일 접근 권한 관리는 POS/운영 환경 책임입니다
- 핀패드 진행 중 리더기 응답이 유실될 수 있는 제약은 §6.5를 반드시 읽으세요
