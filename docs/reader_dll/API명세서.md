# API 명세서 — 리더기 시리얼 통신 제어 DLL

## 1. 문서 정보

- 대상 모듈: `ReaderSerial.dll`
- 근거 소스: `src/ReaderSerial/ReaderSerial.h`(공개 헤더, 정본), `src/ReaderSerial/ReaderApi.cpp`(공개 함수 실제 구현), `src/ReaderSerial/ReaderContext.h`, `src/ReaderSerial/ReaderTypes.h`(내부 상태 참고), `src/ReaderSerial/PinpadTypes.h`(핀패드 내부 상태 참고)
- 이 문서는 PRD(`DOC/개발문서/PRD.md`, 핀패드는 `DOC/개발문서/PRD_핀패드.md`) §8/§19에 기술된 "설계 의도"가 아니라, 위 소스 코드의 "실제 동작"을 기준으로 작성했다. PRD 문구와 실제 구현이 다른 부분을 발견한 경우 실제 구현을 우선 기술하고 각주로 차이를 남긴다.
- 오류 코드(`ReaderResult`) 각각의 값/의미/발생 상황은 `DOC/개발문서/오류코드정의서.md`를 참조한다. 이 문서에서는 각 함수가 "언제 어떤 오류 코드를 반환하는지"만 다룬다.
- **2026-08-11 갱신**: 2차 개발(핀패드 SPEC 연동)로 추가된 `Pinpad_SendCommand`/`PINPAD_CALLBACK`/`PinpadCommandCode`/`PinpadEventType`, 그리고 `Reader_OpenPort`에 추가된 `pinpadCallback` 파라미터를 반영했다(§3, §10~§12).
- **2026-08-13 갱신**: `Pinpad_SendCommand` 반환값 표(§10)에 누락돼 있던 4개(`READER_ERR_INVALID_LENGTH`/`READER_ERR_BUFFER_OVERFLOW`/`READER_ERR_PORT_CLOSING`/`READER_ERR_SEND_FAIL`)를 추가했다 — 코드상 `PinpadSequence_Begin`이 `Reader_SendCommand`와 동일한 프레임 빌드/물리 송신 인프라를 공유해 실제로 반환 가능한데 표에서 빠져 있었다(사용자 지적으로 확인).
- **2026-08-13 재번호**: `READER_ERR_RECEIVE_FAIL`(-1201)/`READER_ERR_TIMEOUT`(-1202)/`READER_ERR_FRAME_STALL`(-1203)/`READER_ERR_LRC_MISMATCH`(구 -1301)가 죽은 값으로 재확인되어 `ReaderResult`에서 제거됐다(`READER_ERR_BUFFER_OVERFLOW`를 -1302→-1301로 재번호) — §7 CALLBACK 시그니처 설명과 §6/§10 반환값 표에 반영. 상세는 `오류코드정의서.md` §3.4/§3.5 참조.
- **2026-08-13 시그니처 변경**: `Pinpad_SendCommand`의 `commandCode` 파라미터 타입을 `int`에서 `unsigned char`로 통일했다(§2, §10) — `PinpadCommandCode` 값(`0xA0`~`0xA4`)이 `Reader_SendCommand`의 `commandCode`와 마찬가지로 1byte 범위 안에 들어가는데도 타입이 달랐던 것은 근거 없는 우연한 불일치였다. MFC/C# 샘플, 테스트 전체 갱신.

---

## 2. 공개 함수 개요

`ReaderSerial.h`가 export하는 함수는 정확히 5개이며, 모두 C ABI + `__stdcall`이다. 1차 개발(리더기) 4종에 2차 개발(핀패드)에서 `Pinpad_SendCommand` 1종이 추가됐다.

```cpp
READER_API int __stdcall Reader_OpenPort(
    int portNumber,
    int baudRate,
    READER_CALLBACK readerCallback,
    PINPAD_CALLBACK pinpadCallback,
    void* userContext,
    int* outReaderId
);

READER_API int __stdcall Reader_ClosePort(
    int readerId
);

READER_API int __stdcall Reader_IsPortOpen(
    int readerId
);

READER_API int __stdcall Reader_SendCommand(
    int readerId,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength
);

READER_API int __stdcall Pinpad_SendCommand(
    int readerId,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength
);
```

`Reader_Initialize`/`Reader_Shutdown`/`Reader_GetLastError`/`Reader_SendCommandToAll` 등은 의도적으로 제공하지 않는다(PRD §8.5). 최초 `Reader_OpenPort()` 호출이 지연 초기화(lazy init) 역할을 겸한다. `Pinpad_SendCommand`는 핀패드 전용 명령이며, `readerId`는 `Reader_OpenPort`가 발급한 값을 그대로 재사용한다 — 핀패드는 별도의 Open 함수가 없다(§10 참조).

---

## 3. Reader_OpenPort

```cpp
int __stdcall Reader_OpenPort(
    int portNumber,
    int baudRate,
    READER_CALLBACK readerCallback,
    PINPAD_CALLBACK pinpadCallback,
    void* userContext,
    int* outReaderId
);
```

**2026-08-11 표기 정정**: 이 문서의 이전 버전은 이 시그니처를 3번째 인자 `callback` 하나만 있던 1차 개발 형태로 기술했다. 실제 코드(`ReaderSerial.h`)는 2차 개발(핀패드)에서 4번째 인자로 `PINPAD_CALLBACK pinpadCallback`을 추가했다 — 이하 본 절의 `callback`은 `readerCallback`으로 표기를 통일한다.

지정한 COM 포트 번호와 Baud Rate로 시리얼 포트를 열고, 리더기 1대에 대한 `ReaderContext`를 확보한 뒤 전용 수신 스레드를 시작한다. **동기(synchronous) 함수**다 — 반환 시점에 포트 HANDLE, 수신 스레드, 내부 수신 버퍼, 상태(`READER_PORT_OPEN`)까지 모두 준비가 끝나 있다.

### 인자

| 인자 | 의미/제약 |
|---|---|
| `portNumber` | COM 포트 번호(정수). 예: `3`을 넘기면 DLL 내부에서 `\\.\COM3` 장치 경로로 변환한다(`SerialPortPath.h`/`BuildSerialPortPath`). `1` 이상이어야 하며, `1` 미만이면 `Reader_OpenPort` 진입 직후 인자 검증에서 즉시 거부된다. |
| `baudRate` | 시리얼 통신 속도. 호출자가 지정한 값을 그대로 사용한다(`SerialWorker_OpenPort`의 `dcb.BaudRate`). PRD §5.1/§8.4의 SPEC 기본 권장값은 115200이지만 강제되지는 않는다 — `baudRate <= 0`이면 `READER_ERR_INVALID_ARGUMENT`로 거부된다(`ReaderApi.cpp:104`). 2026-08-05 이전에는 115200 외 모든 값을 하드 거부했으나, POS가 리더기 SPEC에 맞는 값을 자유롭게 설정할 수 있어야 한다는 요구로 이 제한을 해제했다.[^1] |
| `readerCallback` | 리더기 응답/이벤트 CALLBACK 함수 포인터(§7). `pinpadCallback`이 지정돼 있으면(핀패드 전용 장비) `nullptr`도 허용된다 — 두 콜백이 **동시에 모두** `nullptr`인 경우에만 거부된다(`ReaderApi.cpp:106`, PRD_핀패드.md §8.1). |
| `pinpadCallback` | 핀패드 응답/이벤트 CALLBACK 함수 포인터(§11). `nullptr`이면 이 포트에서 `Pinpad_SendCommand`는 항상 `READER_ERR_PINPAD_NOT_SUPPORTED`를 즉시 반환한다(§10). 리더기 전용 장비는 여기에 `nullptr`을 넘기면 된다. |
| `userContext` | POS가 CALLBACK(리더기/핀패드 공통) 호출 시 그대로 돌려받을 임의의 포인터. DLL은 이 값을 해석하지 않고 보관/전달만 한다. |
| `outReaderId` | 성공 시 발급된 `readerId`를 받을 출력 포인터. `nullptr`이면 거부된다. **실패 시에는 이 포인터에 아무 것도 쓰지 않는다** — 실패 반환값을 받은 호출자는 `*outReaderId`를 유효한 값으로 취급해서는 안 된다. |

### 반환값

| 반환값 | 의미 | 발생 조건(코드 근거) |
|---|---|---|
| `READER_OK` (0) | 성공. `*outReaderId`에 유효한 식별자가 채워짐 | 포트 열기, DCB/COMMTIMEOUTS 설정, 종료 Event 생성, 수신 스레드 생성까지 전부 성공 |
| `READER_ERR_INVALID_ARGUMENT` (-1001) | 인자 검증 실패 | `portNumber < 1`, 또는 `readerCallback == nullptr && pinpadCallback == nullptr`(둘 다 없음), 또는 `outReaderId == nullptr`, 또는 `baudRate <= 0` (`ReaderApi.cpp:106`, 2026-08-05부터 — 이전엔 `baudRate != 115200`. 2차 개발에서 `readerCallback` 단독 nullptr 체크가 `pinpadCallback`과의 OR 조건으로 완화됨) |
| `READER_ERR_PORT_ALREADY_OPEN` (-1102) | 동일 COM 포트 번호가 이미 열려 있음 | `ReaderManager_Acquire`가 슬롯 목록에서 같은 `portNumber`를 찾은 경우(`ReaderManager.cpp:60`). CreateFileA가 `ERROR_ACCESS_DENIED`로 실패한 경우도 동일 코드로 매핑된다(`SerialWorker.cpp:117`, 다른 프로세스가 점유 중인 경우 등) |
| `READER_ERR_MAX_READER_COUNT` (-1002) | 이미 8개(`MAX_READER_COUNT`, 2026-08-06 2 → 8 확대, 리더기·핀패드 장비 종류 무관) 슬롯이 모두 사용 중 | `ReaderManager_Acquire`가 빈 슬롯을 찾지 못한 경우(`ReaderManager.cpp:78`) |
| `READER_ERR_PORT_NOT_FOUND` (-1100) | 지정한 COM 포트 장치가 존재하지 않음 | `CreateFileA`가 `ERROR_FILE_NOT_FOUND`로 실패(`SerialWorker.cpp:111`) |
| `READER_ERR_PORT_OPEN_FAIL` (-1101) | 그 외 포트 열기 실패 | `CreateFileA`가 위 두 경우 외의 오류로 실패(`SerialWorker.cpp:119`), 또는 종료 Event(`CreateEvent`) 생성 실패(`ReaderApi.cpp:64`), 또는 수신 스레드(`CreateThread`) 생성 실패(`ReaderApi.cpp:84`) |
| `READER_ERR_PORT_CONFIG_FAIL` (-1104) | 포트는 열렸으나 통신 파라미터 설정 실패 | `GetCommState`/`SetCommState`/`SetCommTimeouts` 중 하나라도 실패(`SerialWorker.cpp:130,152,174`) |
| `READER_ERR_INTERNAL` (-1900) | `ReaderManager_Acquire` 내부에서 위 두 오류(`PORT_ALREADY_OPEN`/`MAX_READER_COUNT`) 외의 경로로 실패가 보고된 경우에 대비한 기본값 | `ReaderApi.cpp:36`의 `acquireError` 초기값. 현재 `ReaderManager_Acquire` 구현상 이 기본값이 그대로 반환되는 실제 경로는 없다(안전망 성격의 초기값) |

### 실패 시 동작

- **CALLBACK은 절대 호출되지 않는다.** 실패는 오직 함수 반환값으로만 전달된다.
- 실패 시 그 시점까지 확보했던 내부 자원(슬롯/HANDLE/Event)은 모두 정리되고 슬롯은 반환되며(2026-08-03부터 내부적으로 `ReaderManager_BeginClose`+`ReaderManager_Unpin`을 곧바로 이어 호출하는 `ReleaseUnpublishedSlot`을 사용 — 이 시점은 아직 `outReaderId`가 호출자에게 공개되지 않아 경쟁이 원천적으로 없다), 포트 상태는 사실상 미사용(슬롯 반환) 상태로 복귀한다.
- `*outReaderId`는 쓰지 않는다.

---

## 4. Reader_ClosePort

```cpp
int __stdcall Reader_ClosePort(int readerId);
```

지정한 `readerId`의 포트와 관련 자원(진행 중인 I/O, 수신 스레드, 포트 HANDLE, 동기화 자원)을 정리한다.

### 인자

| 인자 | 의미/제약 |
|---|---|
| `readerId` | `Reader_OpenPort` 성공 시 발급받은 식별자. 범위를 벗어나거나(`< 0` 또는 `>= MAX_READER_COUNT`) 현재 사용 중이 아닌 슬롯이면 무효로 취급한다. |

### 반환값

| 반환값 | 의미 | 발생 조건 |
|---|---|---|
| `READER_ERR_INVALID_READER_ID` (-1003) | 무효한 `readerId` | `ReaderManager_BeginClose(readerId, ...)`가 `ReaderCloseBeginResult::InvalidReaderId`를 반환(`ReaderApi.cpp:142~146`) — 범위 밖이거나 현재 사용 중이 아닌 슬롯 |
| `READER_OK` (0) | 성공(포트가 이미 닫혀 있던 경우, 그리고 다른 스레드가 동시에 같은 `readerId`를 이미 닫는 중인 경우도 포함) | 그 외 모든 경로. `SerialWorker_ClosePort` 내부에서 `portState`가 이미 `CLOSED`/`CLOSING`이면 아무 작업도 하지 않고 즉시 `READER_OK`를 반환한다(`SerialWorker.cpp:497~501`) — 즉 **이미 닫힌 포트를 다시 닫아도 오류가 아니라 성공으로 처리된다.**[^2] 2026-08-03부터는 동일 `readerId`에 대한 **동시** `Reader_ClosePort` 호출도 안전하다 — `ReaderManager_BeginClose`가 closing 플래그를 원자적으로 선점해 두 번째 이후 호출은 `ReaderCloseBeginResult::AlreadyClosing`을 받으며, 이 경우도 관례대로 `READER_OK`를 반환한다(내부적으로는 참조 카운트가 0이 될 때까지 실제 정리를 미루는 방식으로 이중 `CloseHandle`/`DeleteCriticalSection`을 방지한다 — P9-12 참조). |

`Reader_ClosePort`는 이 두 가지 반환값만 가진다(다른 `ReaderResult` 값을 반환하는 경로가 코드상 없음).

### 실패 시 동작

- 실패(`READER_ERR_INVALID_READER_ID`)는 CALLBACK 없이 반환값으로만 전달된다.
- 성공 경로에서는 `SerialWorker_ClosePort`가 수신 스레드 종료까지 완전히 대기(`WaitForSingleObject(..., INFINITE)`)한 뒤 반환하므로, `Reader_ClosePort`가 반환된 시점 이후에는 해당 리더기에서 CALLBACK이 더 이상 발생하지 않는다. 함수 자신은 CALLBACK을 호출하지 않는다(닫기 완료 자체를 알리는 CALLBACK 이벤트는 없다).

---

## 5. Reader_IsPortOpen

```cpp
int __stdcall Reader_IsPortOpen(int readerId);
```

지정한 리더기의 **현재** 포트 상태를 조회한다. 상태를 변경하지 않는 순수 조회(query) 함수다.

### 인자

| 인자 | 의미/제약 |
|---|---|
| `readerId` | 조회 대상 리더기 식별자 |

### 반환값

| 반환값 | 의미 | 발생 조건 |
|---|---|---|
| `1` | 포트 열림 | 내부 `ReaderPortState`가 `READER_PORT_OPEN`인 경우에만(`ReaderApi.cpp:138`) |
| `0` | 포트 닫힘 또는 송수신 불가 | `READER_PORT_CLOSED`/`OPENING`/`CLOSING`/`ERROR` 5종 중 `OPEN`이 아닌 나머지 전부를 하나로 뭉뚱그려 `0`으로 반환한다. 내부 5종 세부 상태는 POS에 노출하지 않는다. |
| `READER_ERR_INVALID_READER_ID` (-1003) | 무효한 `readerId` | `ReaderManager_Pin(readerId)`가 `nullptr`(`ReaderApi.cpp:180`) |

### 주의(CLAUDE.md 확정 사항)

`Reader_IsPortOpen()`은 `Reader_SendCommand()` 호출 전 필수 사전 점검이 **아니다**. `Reader_SendCommand`가 이미 포트 상태를 원자적으로 자체 검증하므로, `IsPortOpen`을 먼저 호출해도 그 값과 실제 `Reader_SendCommand` 호출 사이에 상태가 바뀔 수 있는 경합(race)이 구조적으로 존재한다. 올바른 사용 패턴은 상태 표시(UI 표시 등) 목적으로만 이 함수를 쓰고, 명령 송신 게이트로 쓰지 않는 것이다.

---

## 6. Reader_SendCommand

```cpp
int __stdcall Reader_SendCommand(
    int readerId,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength
);
```

POS가 넘긴 `commandCode`/`data`/`dataLength`로 완성 프레임(`STX + Length + CommandCode + Data + ETX + LRC`)을 만들어 지정 리더기로 송신한다. **반환값은 "송신을 시작할 수 있었는지"에 대한 즉시 결과일 뿐, 리더기의 실제 업무 처리 결과가 아니다** — 업무 응답은 이후 CALLBACK(`READER_EVENT_RESPONSE`)으로 비동기 전달된다.

### 인자

| 인자 | 의미/제약 |
|---|---|
| `readerId` | 대상 리더기 식별자 |
| `commandCode` | 전문 구분 코드 1byte. 초기화 요청(`PilotCommands::INIT_REQUEST` = `0x60`)이면 특별 취급(아래 참조)된다. |
| `data` | 업무 Data 영역 포인터. `nullptr`이면 `dataLength`는 반드시 `0`이어야 한다. `nullptr`이 아니면 `dataLength`는 반드시 `0`보다 커야 한다(둘 중 하나라도 어긋나면 `READER_ERR_INVALID_ARGUMENT`). |
| `dataLength` | `data`의 실제 byte 길이(업무 Data 길이만 — STX/Length/CommandCode/ETX/LRC는 포함하지 않음). DLL이 이 값을 이용해 프레임 내부 Length 필드를 계산한다. |

### 반환값

| 반환값 | 의미 | 발생 조건 |
|---|---|---|
| `READER_ERR_INVALID_READER_ID` (-1003) | 무효한 `readerId` | `ReaderManager_Pin`이 `nullptr`(`ReaderApi.cpp:247`) |
| `READER_ERR_PORT_NOT_OPEN` (-1103) | 포트가 `OPEN` 상태가 아님 | `stateLock` 보호 하에 읽은 `portState != READER_PORT_OPEN`(`ReaderApi.cpp:203`). `OPENING`/`CLOSING`/`CLOSED`/`ERROR` 전부 이 코드로 뭉뚱그려진다 — `Reader_SendCommand`가 직접 반환하는 이 경로에서는 `READER_ERR_PORT_CLOSING`이 별도로 나오지 않는다(뒤의 `SerialWorker_SendFrame`은 `PORT_CLOSING`을 구분해서 반환하지만, 그 시점 이전에 이미 이 체크에서 걸러진다).[^3] |
| `READER_ERR_INVALID_ARGUMENT` (-1001) | `data`/`dataLength` 조합 불일치 | `data == nullptr && dataLength != 0`, 또는 `data != nullptr && dataLength <= 0`(`ReaderApi.cpp:206~215`) |
| `READER_ERR_INVALID_LENGTH` (-1300) | 프레임 생성 시 길이 초과 | `BuildFrame` 내부에서 `frameDataLength > 0xFFFF` 또는 `totalFrameLength > MAX_FRAME_LENGTH`(4096, `FrameBuilder.h`)(`FrameBuilder.cpp:35,42`) |
| `READER_ERR_BUFFER_OVERFLOW` (-1301) | 완성 프레임이 내부 작업 버퍼(`frameBuf[MAX_FRAME_LENGTH]`)보다 큼 | `BuildFrame`이 `outBufferCapacity`(=4096) 초과를 감지(`FrameBuilder.cpp:46`). 실무상 `frameBuf` 크기와 `MAX_FRAME_LENGTH` 검사가 동일 상수를 쓰므로, 위 `INVALID_LENGTH` 검사를 먼저 통과했다면 이 경로에 실제로 도달하지는 않는다(코드상 방어적으로 남아 있는 경로). |
| `READER_ERR_BUSY` (-1004) | 이미 다른 일반 명령이 `WAITING_RESPONSE` 중 | `TryBeginGeneralCommand`가 `operationState != READER_OP_IDLE`일 때 반환(`CommandStateManager.cpp:101`). `commandCode`가 초기화 요청(`0x60`)이면 이 검사 자체가 적용되지 않는다(아래 참조). |
| `READER_ERR_COMMAND_NOT_ALLOWED` (-1005) | 이미 `INITIALIZING` 중에 또 초기화(`0x60`)를 요청 | `TryBeginInitCommand`가 `operationState == READER_OP_INITIALIZING`일 때 반환(`CommandStateManager.cpp:154`) |
| `READER_ERR_SEND_FAIL` (-1200) | 실제 `WriteFile`/`GetOverlappedResult` 단계에서 송신 실패 | `SerialWorker_SendFrame` 내부: `CreateEvent` 실패, `WriteFile` 비동기 완료 대기(`GetOverlappedResult`) 실패, 또는 부분 송신(`bytesWritten != frameLength`)(`SerialWorker.cpp:455,472,478,486`) — 이 경로에 도달했다는 것은 이미 `operationState`가 `WAITING_RESPONSE`/`INITIALIZING`으로 전이된 **이후**라는 뜻이다(아래 "상태 전이 순서" 참조). |
| `READER_ERR_PORT_CLOSING` (-1105) | 송신 시도 시점에 포트가 `CLOSING`으로 바뀜 | `SerialWorker_SendFrame`이 `stateLock`으로 다시 `portState`를 확인할 때 `CLOSING`(`SerialWorker.cpp:439`). `Reader_SendCommand` 진입 시점의 앞선 `OPEN` 체크와 실제 송신 사이에 다른 스레드가 `Reader_ClosePort`를 호출한 경합 상황에서만 발생한다. |
| `READER_ERR_PORT_NOT_OPEN` (-1103) | (송신 단계에서 다시) 포트가 `OPEN`이 아님 | `SerialWorker_SendFrame` 내부에서도 동일 검사를 한 번 더 하며(`SerialWorker.cpp:443`), `CLOSING`이 아닌 다른 비-`OPEN` 상태로 바뀐 경우 이 코드가 나온다. |
| `READER_OK` (0) | 송신 성공(리더기의 업무 처리 성공을 의미하지 않음) | 프레임 생성/상태 전이/송신이 모두 성공 |

### 초기화 요청(`0x60`)의 특별 취급

- `commandCode == 0x60`이면 `TryBeginGeneralCommand` 대신 `TryBeginInitCommand`가 호출된다.
- `IDLE`/`WAITING_RESPONSE` 상태에서는 항상 허용되며, `WAITING_RESPONSE`였다면 진행 중이던 일반 명령을 **무효화**하고 `INITIALIZING`으로 전이한다. `INITIALIZING` 상태에서만 거부(`READER_ERR_COMMAND_NOT_ALLOWED`)된다.

### 상태 전이 순서와 반환값의 관계(중요)

`Reader_SendCommand`는 다음 순서로 처리한다(`ReaderApi.cpp` 실제 순서, PRD 문구가 아니라 구현 순서를 그대로 기술):

1. `readerId`/포트 상태(`OPEN` 여부)/`data`·`dataLength` 인자 검증
2. `BuildFrame()` 호출 — **`operationState` 전이보다 먼저** 수행한다(P5-6 설계 결정). 프레임 생성이 실패하면 상태를 건드리지 않고 그대로 실패를 반환한다.
3. `TryBeginGeneralCommand`/`TryBeginInitCommand` 호출 — 여기서 `READER_ERR_BUSY`/`READER_ERR_COMMAND_NOT_ALLOWED`가 날 수 있으며, 이 시점까지 `operationState`는 아직 바뀌지 않는다.
4. `SerialWorker_SendFrame()` 호출 — **이 시점에는 이미 `operationState`가 `WAITING_RESPONSE`/`INITIALIZING`으로 전이된 뒤**이므로, 여기서 `READER_ERR_SEND_FAIL`/`PORT_CLOSING`/`PORT_NOT_OPEN`이 나면 `AbortPendingCommand()`(`CommandStateManager.cpp`)가 즉시 호출되어 `operationState`를 `IDLE`로 되돌린다.[^6]

즉, `Reader_SendCommand`가 `READER_ERR_SEND_FAIL`/`READER_ERR_PORT_CLOSING`/`READER_ERR_PORT_NOT_OPEN` 등 4단계 송신 오류를 반환한 시점에는 `operationState`가 이미 `IDLE`로 복귀해 있으므로, 호출자는 Timeout을 기다리지 않고 바로 다음 명령을 보낼 수 있다(단, §6-1 "POS 연동 권장 패턴" 참조 — DLL 쪽 장부 정리와 리더기 실물의 프레임 재동기화는 별개다).

**동시성 계약(2026-08-03, P9-14로 강화됨)**: 위 3번(상태 전이)과 4번(물리적 송신)은 **원자적**이다 — 내부적으로 `ctx.sendLock` 하나로 이 두 단계 전체를 감싸므로, 다른 스레드가 같은 `readerId`로 명령을 시작(특히 `0x60`으로 무효화)하려면 이 스레드가 상태 전이와 물리적 송신을 **모두 끝낸 뒤**에야 시작할 수 있다. 즉 "상태만 `WAITING_RESPONSE`로 바뀌었고 아직 리더기에 실제로 바이트가 나가지 않은" 중간 상태는 외부에서 관측되지 않는다 — 어떤 명령이 논리적으로(상태 머신 상) 무효화됐다면, 그 명령을 물리적으로 무효화하는 `0x60` 전문보다 그 명령 자신의 전문이 항상 먼저 리더기로 전송된 뒤라는 뜻이다. 이 보장은 `SerialWorker.cpp`의 거래 타임아웃 후 자동 재초기화(`TryAutoReinitAfterTradeTimeout`)에도 동일하게 적용된다(같은 `ctx.sendLock`을 사용). 이 보장이 없던 이전 버전에서는 두 단계가 별개의 크리티컬 섹션이라 이론상 순서가 역전될 수 있었다(실측된 사례는 없었으나 구조적으로 배제되지 않았음) — 상세 배경은 `DOC/개발문서/실행계획서.md` P9-14 참조.

[^6]: 2026-08-03 사용자 결정으로 변경됨. 기존에는 송신 실패 시에도 상태를 되돌리지 않고 Timeout(일반 3초/거래 200초)으로만 자연 복귀시켰으나, 이 DLL은 여러 POS 업체가 사용하는 공용 인프라라 모든 업체가 "권장 재시도 패턴"을 충실히 구현한다고 보장할 수 없다는 점이 문제였다 — 그런 업체가 `SEND_FAIL`을 받고 바로 다음 명령을 보내면 최대 200초 동안 `READER_ERR_BUSY`만 돌려받아, 리더기 실물은 멀쩡히 처리 가능한 상황에서도 DLL이 스스로 정상 요청을 막는 셈이었다. `READER_ERR_SEND_FAIL`의 발생 원인 중 실제로 바이트가 리더기에 도달했을 위험이 있는 경우(`GetOverlappedResult` 실패, 부분 송신)도 있으나, 검증되지 않은 이 잔여 위험보다 "DLL이 최대 200초 동안 정상 요청까지 막는" 확실한 비용 쪽이 크다고 판단해 즉시 복귀로 바꿨다. `AbortPendingCommand`는 DLL 내부 장부(`operationState` 등)만 정리할 뿐 리더기 쪽에 별도 전문을 보내지 않는다 — 리더기가 여전히 깨진 프레임을 붙잡고 있을 잔여 가능성에 대한 대응은 §6-1의 방어적 `0x60` 재동기화 권장 패턴을 참고할 것.

### 실패 시 CALLBACK 호출 여부

`Reader_SendCommand` 자신은 어떤 실패 경로에서도 CALLBACK을 직접 호출하지 않는다. 3번(상태 전이) 이후 실패한 경우(4단계 송신 오류)는 위 각주대로 즉시 `IDLE`로 복귀하며 CALLBACK 없이 반환값만으로 실패가 전달된다. 반면 4번을 통과해 송신 자체는 성공했지만 리더기가 응답하지 않는 경우는 여전히 Timeout 정책(PRD §13, `TimeoutPolicy.h`)에 따라 최대 `DEFAULT_RESPONSE_TIMEOUT_MS`/`TRADE_TIMEOUT_MS` 이후 `READER_EVENT_TIMEOUT` CALLBACK이 별도로 발생한다.

### 6-1. POS 연동 권장 패턴 (`SendCommandSafe`)

`ReaderSerialTestUI`(MFC)와 `ReaderSerialCSharpSample`(C#) 두 연동 예제가 공통으로 구현하는 `SendCommandSafe` 래퍼는 `Reader_SendCommand`를 직접 호출하는 대신 다음 흐름을 따르도록 권장한다(DOC/개발문서/실행계획서.md P10-1b에서 확정, 이 문서에는 2026-08-03 반영):

1. 유효한 `readerId`가 아직 없으면(최초 호출, 또는 이전 복구 시도가 완전히 실패한 상태) 먼저 `Reader_OpenPort()`로 확보한다. `Reader_IsPortOpen()`을 사전 체크로 쓰지 않는다 — 체크와 실제 Send 사이에도 레이스가 있어 신뢰할 수 없고, `Reader_SendCommand`가 이미 포트 상태를 원자적으로 검증하므로 중복 호출이 된다.
2. `Reader_SendCommand()`를 먼저 시도한다.
3. 반환값이 **포트 계열 에러**(`READER_ERR_PORT_NOT_OPEN`)이면 `Reader_ClosePort()` → `Reader_OpenPort()` → `Reader_SendCommand()` 재시도를 한 번 수행한다. 재오픈 성공 시 새로 발급된 `readerId`로 반드시 자신의 상태를 덮어써야 한다(옛 id 재사용은 항상 실패). 재오픈 자체가 실패하면 `readerId`를 "없음" 상태로 되돌려 다음 호출이 Open부터 다시 시작하게 한다.
4. 반환값이 **`READER_ERR_SEND_FAIL`**이면(위 §6 각주대로 DLL이 이미 `operationState`를 즉시 `IDLE`로 되돌려놓은 상태) `Reader_SendCommand(readerId, 0x60, nullptr, 0)`을 한 번 방어적으로 전송하되, 그 결과를 기다리지 않고 로그만 남긴 뒤 원래의 `SEND_FAIL`을 그대로 호출자에게 반환한다. 이 재동기화 전송은 DLL이 강제하는 필수 절차가 아니라, 리더기 실물이 여전히 깨진 프레임을 붙잡고 있을 잔여 가능성에 대비한 방어적 권장 조치일 뿐이다 — POS가 이 단계를 생략하고 곧바로 다음 명령을 보내도 DLL은 더 이상 막지 않는다.
5. `READER_ERR_BUSY` 등 포트/송신과 무관한 에러는 복구 대상이 아니다 — 이미 정상 진행 중인 다른 명령이 있다는 뜻이므로 여기서 Close를 걸면 그 명령을 강제로 죽이게 된다.

---

## 7. `READER_CALLBACK` 시그니처

```cpp
typedef void (__stdcall *READER_CALLBACK)(
    int readerId,
    int eventType,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength,
    void* userContext
);
```

> 2026-08-12: `resultCode` 파라미터를 제거했다 — `eventType`(§8 표)에서 100% 유도
> 가능한 중복 정보였기 때문이다. 필요하면 POS 쪽에서 `eventType`으로부터 직접
> 유도할 수 있다. `PINPAD_CALLBACK`의 `result` 파라미터는 이 변경과 무관했으나,
> 같은 날 별도로 `commandCode`로 교체됐다 — §11 참조. (2026-08-13: 당시 `resultCode`가
> 실어 보내던 `READER_ERR_TIMEOUT`/`READER_ERR_LRC_MISMATCH`/`READER_ERR_RECEIVE_FAIL`/
> `READER_ERR_FRAME_STALL` 자체가 죽은 값으로 확인되어 `ReaderResult`에서 제거됐다
> — 이제 `eventType`으로만 이 상황들을 식별한다.)
>
> **2026-08-12 추가 — `commandCode`는 "그 자리는 항상 응답 코드"라는 원칙으로
> 통일됨**: 과거 `READER_EVENT_TIMEOUT`만 예외적으로 **요청**의 `commandCode`
> (예: `0x60`)를 담았으나, 이제는 다른 이벤트와 동일하게 **그 요청이 기다리고
> 있던 응답 코드**(예: `0x70`)를 담는다. `READER_EVENT_FRAME_STALL`도 과거
> 항상 `commandCode = 0`이었으나, 정체된 프레임이 지금 기다리는 응답과
> 일치해 즉시 실패 처리된 경우에는 그 `expectedResponseCode`를 담는다(그
> 외에는 여전히 `0`).

| 파라미터 | 의미 |
|---|---|
| `readerId` | 이 CALLBACK을 유발한 리더기의 식별자(`Reader_OpenPort`가 발급한 값) |
| `eventType` | `ReaderEventType` 값(아래 §8 표 참조) |
| `commandCode` | **그 자리는 항상 "응답 코드"를 의미한다**(2026-08-12 원칙 확정). `RESPONSE`/`LRC_ERROR`/`UNSOLICITED`는 실제 수신한 프레임의 `CommandCode`, `TIMEOUT`은 그 요청이 기다리고 있던 응답 코드(예: `0x60` 요청이면 `0x70`), `FRAME_STALL`은 정체된 프레임이 기다리는 응답과 일치해 즉시 실패 처리된 경우 그 응답 코드, 그 외(아직 판단 근거가 없는 경우)는 `0`이 전달된다. |
| `data` | STX/Length/CommandCode/ETX/LRC를 제외한 응답 Data 영역의 시작 포인터. **§9 참조 — 첫 2byte가 SPEC 업무 응답 코드(00~23, ASCII)** |
| `dataLength` | `data`의 실제 byte 길이. Data가 없는 이벤트에서는 `0`이고 `data`는 `nullptr`이다. |
| `userContext` | `Reader_OpenPort` 호출 시 넘긴 값이 그대로 전달됨 |

### CALLBACK 데이터 수명 규칙(중요)

`CallbackHandler_Invoke`(`CallbackHandler.cpp`)가 모든 CALLBACK 호출을 담당하며, 다음과 같이 동작한다:

1. `ctx.callback(...)`을 호출한다(콜백은 이 호출을 수행한 수신 스레드에서 **동기적으로** 실행된다).
2. 콜백이 반환되면, `data != nullptr && dataLength > 0`인 경우 그 버퍼를 `SecureZeroMemory`로 즉시 0으로 덮어쓴다.

따라서 **`data` 포인터는 CALLBACK 함수 호출이 실행되는 동안에만 유효하다.** CALLBACK 반환 이후에도 데이터가 필요하면 CALLBACK 내부에서 자체 메모리로 복사해야 한다(예: `ReaderSerialTestUI`/`ReaderSerialCSharpSample`의 콜백 구현이 이 패턴을 따른다). 이는 CLAUDE.md의 "CALLBACK 데이터 수명 규칙"과 동일하다.

---

## 8. `ReaderEventType` 전체 값과 발생 조건

`ReaderSerial.h`에 선언된 순서 그대로이며, 정수값은 `0`부터 선언 순서대로 자동 부여된다. 2026-08-05 재번호로 실제 발생하지 않던 죽은 값(`READER_EVENT_CONNECTED`/`READER_EVENT_DISCONNECTED`/`READER_EVENT_SEND_ERROR`)은 enum에서 제거됐다. 2026-08-12 재번호로 `1`부터가 아니라 `0`부터 시작하도록 변경됐다(상대 순서는 불변, `PinpadEventType`과 시작 번호를 통일하기 위함).

| 값 | 이름 | 발생 조건(코드 근거) | 비고 |
|---:|---|---|---|
| 0 | `READER_EVENT_RESPONSE` | 일반 명령 또는 초기화 요청의 **최종 응답**이 정상 수신됐을 때. `commandCode`에 실제 수신된 응답 코드, `data`/`dataLength`에 응답 Data가 실린다(`SerialWorker.cpp:381`) | 거래 타임아웃 후 DLL이 내부적으로 자동 재전송한 초기화(`0x60`)의 응답(`0x70`)에 대해서는 이 이벤트가 **의도적으로 억제**된다(`suppressResponseCallback`, P9-1c) — `READER_EVENT_TIMEOUT`만으로 충분하다는 사용자 결정. POS가 직접 호출한 `0x60`에는 영향 없음. |
| 1 | `READER_EVENT_TIMEOUT` | 진행 중인 명령이 응답 없이 Response/Trade Timeout(PRD §13, `TimeoutPolicy.h`)을 초과했을 때. **2026-08-12부터**: `commandCode`에 (과거의 요청 `commandCode`가 아니라) 그 요청이 기다리고 있던 **응답 코드**(예: `0x60` 요청이면 `0x70`)가 실린다 — "그 자리는 항상 응답 코드" 원칙 통일. `data = nullptr`(`SerialWorker.cpp`) | 발생 즉시 `operationState`가 `IDLE`로 복귀한다. Trade Timeout 대상 명령이면 곧이어 내부 자동 재초기화(`0x60`)가 시도된다(그 결과는 별도 CALLBACK 없음, 응답 억제됨). |
| 2 | `READER_EVENT_LRC_ERROR` | 수신 프레임의 LRC 검증에 실패했을 때. `commandCode`에 수신된 `commandCode`, `data`/`dataLength`에 (검증에 실패한) 수신 Data(`FrameDispatcher.cpp`) | CALLBACK은 항상 그대로 발생한다(`OnFrameReceived`, 즉 정상 응답 판정 경로는 호출하지 않는다는 원칙은 불변). **2026-08-12부터**: `commandCode`(손상된 프레임의 `commandCode`)가 지금 기다리는 명령의 예상 응답 코드와 일치하면(=그 응답이 끊긴 것이 확실하면) `operationState`가 Timeout을 기다리지 않고 즉시 `IDLE`로 복귀한다. 코드가 다르면(비요청 이벤트 등과 무관) 기존처럼 `operationState`가 변경되지 않는다. |
| 3 | `READER_EVENT_RECEIVE_ERROR` | 수신 스레드의 `ReadFile`/`GetOverlappedResult`가 예기치 않게 실패했을 때(포트 HANDLE 오류, 장치 제거 등). `commandCode = 0`, `data = nullptr`(`SerialWorker.cpp:28`, `ReportReceiveError`) | 이 경우 `ReaderPortState`가 `READER_PORT_ERROR`로 전이되고 수신 스레드가 종료된다 — 사실상 포트 자체 장애를 알리는 이벤트로, PRD가 원래 `READER_EVENT_DISCONNECTED`(2026-08-05 제거됨)에 기대했을 법한 역할을 대신 수행하고 있다.[^4] |
| 4 | `READER_EVENT_UNSOLICITED` | POS 요청 없이 리더기가 자발적으로 보낸 전문(현재는 카드 감지 이벤트 `0x76` 한 종류)을 수신했을 때. `commandCode = 0x76`, `data`/`dataLength`에 이벤트 Data(`SerialWorker.cpp:351~353`) | `operationState`를 변경하지 않는다. |
| 5 | `READER_EVENT_FRAME_STALL` | 누적 수신 버퍼에 미완성 프레임이 남은 채로 `TimeoutPolicy::FRAME_STALL_TIMEOUT_MS`(1000ms, 사용자 확정값 — SPEC 파생 아님) 동안 추가 byte가 도착하지 않아 그 미완성 프레임을 버렸을 때(Inter-byte Timeout). `data = nullptr`(`SerialWorker.cpp`) | **2026-08-12부터**: `commandCode`는 기본적으로 `0`이지만, 그 정체된 미완성 프레임에서 (도착한 만큼만) 미리 엿본 `commandCode`가 지금 기다리는 명령의 예상 응답 코드와 일치하면 그 응답 코드가 `commandCode`에 실리고 `operationState`가 Timeout을 기다리지 않고 즉시 `IDLE`로 복귀한다. `commandCode`를 아직 엿볼 수 없었거나(< 4byte) 다른 명령과 무관하면 `commandCode = 0`이며 `operationState`/`CommandStateManager`도 독립적으로 동작(변경 없음) — 순수한 프레임 버퍼 재동기화 목적이라는 원래 설계는 유지된다. Phase 9(P9-9)에서 추가. |

---

## 9. `data` 첫 2byte = SPEC 업무 응답 코드(00~23) 요약

`READER_EVENT_RESPONSE`로 전달되는 `data`의 **첫 2byte는 SPEC이 정의한 업무 응답 코드(ASCII 2문자, `00`~`23`)**다. 이 값은 `commandCode`와 완전히 별개 체계이며, DLL은 이 값을 해석하지 않고 그대로 전달만 한다(SPEC §2.2, 문서 p.10). 응답코드가 `00`(정상)이 아니면 해당 응답 Data 영역에는 이 2byte만 담기고 나머지 업무 필드는 생략되는 것이 SPEC 공통 규칙이다(단, `[71]` 상태확인 응답은 예외 — 오류 시에도 리더기 식별번호/모듈ID를 항상 포함).

상세 표는 `src/ReaderSerial/CommandCodes.h` 하단 "P7-4: SPEC 업무 응답 코드(00~23) 참고표" 주석에 원문 그대로 있으며, 요약하면 다음과 같다.

| 코드 | 응답 내용 | 비고 |
|------|------------------------------------|------|
| 00 | 리더기 상태 정상 | |
| 01 | 리더기 무결성 오류 | 무결성 체크 오류 |
| 02 | Reader Error(IC카드를 넣어주세요) | 카드 리딩 도중 제거 |
| 03 | 사용자 취소 | 단말기/멀티패드 종료 버튼 |
| 04 | 거래요청 Timeout | |
| 05 | 금액 요청 IC | |
| 06 | IC 카드 거래 불가 | 카드매체 불량 |
| 07 | FallBack | MS가능한 거래 |
| 08 | IC 카드 삽입되어있음 | 카드제거 요청 |
| 09 | 상황에 맞지 않는 명령 | 2차검증 대기 중 부적절 요청 등 |
| 10 | 상호인증오류 | Key 상호인증 시 |
| 11 | 암호화/복호화오류 | Key 다운로드 시 |
| 12 | MS거래 불가! IC카드로 진행 | IC카드를 MS로 Swipe |
| 13 | 리더기 KEY 다운로드 요망 | |
| 14 | MS카드를 넣어주세요 | MS전용카드 시 |
| 15 | RF카드 리딩 에러 | |
| 16 | 비정상 RF카드 접촉 | |
| 17 | 음성/동영상 파일 번호 없음 | |
| 18 | 현금IC 카드 복수 계좌 거래 불가 | |
| 19 | 사용자 확인 | 입력 버튼 |
| 20 | 2차 검증 데이터 오류 | EMV 데이터 오류 |
| 21 | 정의되지 않은 전문 코드 | |
| 22 | 지원되지 않는 전문 코드 | 정의는 되어 있으나 해당 리더기 미지원 |
| 23 | 필드값 오류 | 2020.07.21 추가 |

---

## 10. Pinpad_SendCommand (2차 개발, 핀패드)

```cpp
READER_API int __stdcall Pinpad_SendCommand(
    int readerId,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength
);
```

핀패드 조합 명령(SPEC 여러 전문을 DLL이 대신 순서대로 주고받는 시퀀스)을 전송한다(`PinpadSequence.cpp`). `readerId`는 `Reader_OpenPort`가 발급한 값을 그대로 쓴다 — 핀패드 전용 Open 함수는 없다. 별도 포트 구성(핀패드 전용 COM 포트)과 멀티패드 구성(리더기와 같은 포트를 공유하는 장비) 둘 다 이 함수 하나로 다룬다.

### 인자

| 인자 | 의미/제약 |
|---|---|
| `readerId` | 대상 리더기/핀패드 식별자(`Reader_OpenPort` 발급 값) |
| `commandCode` | `PinpadCommandCode` 값(§12 표, `0xA0`~`0xA4`). `unsigned char`이며 `Reader_SendCommand`의 `commandCode`와 타입이 통일돼 있다(2026-08-13). |
| `data` | 명령별 고정폭 Data. 아래 "명령별 Data 레이아웃" 참조 |
| `dataLength` | `data`의 byte 길이. 명령별로 정확히 일치해야 하며(초과/부족 모두 거부), 가변 길이 명령은 없다 |

### 명령별 Data 레이아웃(고정폭, `PinpadPinCommands.cpp` 기준)

| `commandCode` | `dataLength` | 레이아웃 |
|---|---:|---|
| `PINPAD_CMD_INIT` (0xA0) | 0 | Data 없음(`data`가 `nullptr`이 아니면서 `dataLength != 0`이면 거부) |
| `PINPAD_CMD_PIN_PASSWORD` (0xA1) | 1 | `MaxPinLength(1)` |
| `PINPAD_CMD_PIN_NUMBER` (0xA2) | 1 | `MaxPinLength(1)` |
| `PINPAD_CMD_PIN_DES` (0xA3) | 17 | `MaxPinLength(1) + WorkingKey(8) + ACN(8)` |
| `PINPAD_CMD_PIN_SEED` (0xA4) | 13 | `MaxPinLength(1, 0~6만 허용) + RNUM(12)` |

**2026-08-10부터 표시 문구(Line1/Line2) 커스텀 지정 필드는 완전히 제거됐다** — PIN 입력 4종은 항상 DLL 내장 고정 기본 문구만 사용하며(`PinpadMessageText.cpp`), POS는 표시 문구를 지정할 수 없다. 사유는 CLAUDE.md "2026-08-10, 별도 사용자 요청" 절 참조(가변폭 패딩 규칙 부재로 인한 프레임 불일치 문제).

`PINPAD_CMD_PIN_DES`의 `TMKID`는 POS 입력 필드가 아니며 DLL이 내부적으로 항상 `0x00`으로 고정 전송한다(2026-08-07 사용자 요청 — SPEC 원문에 TMKID 의미/기본값 설명이 없어 실장비 실측 1건에 근거한 값, 복수 TMK 슬롯 장비에서의 유효성은 미확인).

### 반환값

**2026-08-13 갱신**: 이 표는 원래 6개 값만 실려 있었으나, 아래 4개(`INVALID_LENGTH`/`BUFFER_OVERFLOW`/`PORT_CLOSING`/`SEND_FAIL`)가 누락돼 있었다 — `PinpadSequence_Begin`이 첫 전문을 만들고 보내는 과정이 `Reader_SendCommand`와 정확히 같은 프레임 빌드/물리 송신 인프라(`PinpadFrameBuilder.cpp`가 `BuildFrame`과 동일한 방식으로 `INVALID_LENGTH`/`BUFFER_OVERFLOW`를 반환하고, `SendViaHookOrDefault`가 `Reader_SendCommand`와 동일한 `SerialWorker_SendFrame`을 호출)를 타므로, `Reader_SendCommand`뿐 아니라 `Pinpad_SendCommand`도 이 값들을 실제로 반환할 수 있다(사용자 지적으로 확인, 코드 대조 완료). 아래 표에 반영했다 — `Reader_SendCommand`가 이미 초기화 중(`READER_OP_INITIALIZING`)일 때만 반환하는 `READER_ERR_COMMAND_NOT_ALLOWED`(-1005)는 핀패드에 대응 상태가 없어 여전히 리더기 전용이다(§6 `Reader_SendCommand` 반환값 표 참조).

| 반환값 | 의미 | 발생 조건(코드 근거) |
|---|---|---|
| `READER_OK` (0) | 조합 시퀀스가 정상 시작됨(최종 완료를 의미하지 않음 — 완료는 `PINPAD_CALLBACK`으로 비동기 통지) | 프레임 생성/상태 전이/송신 모두 성공(`PinpadSequence_Begin`) |
| `READER_ERR_INVALID_READER_ID` (-1003) | 무효한 `readerId` | `ReaderManager_Pin`이 `nullptr` |
| `READER_ERR_PINPAD_NOT_SUPPORTED` (-1400) | 이 포트에 `pinpadCallback`이 등록되지 않음 | `ctx->pinpadCallback == nullptr`(`ReaderApi.cpp:406`) — 타임아웃 대기 없이 즉시 반환 |
| `READER_ERR_PORT_NOT_OPEN` (-1103) | 포트가 `OPEN` 상태가 아님 | `ReaderApi.cpp:419` |
| `READER_ERR_INVALID_ARGUMENT` (-1001) | `commandCode`가 정의되지 않은 값이거나, `data`/`dataLength`가 위 레이아웃 표와 다름(길이 초과/부족 모두 포함) | 명령별 `PinpadCommand_Build*Sequence`/`ReaderApi.cpp:519~525`(default 분기) |
| `READER_ERR_BUSY` (-1004) | **크로스 BUSY**(2026-08-10) — 리더기 명령이 이미 `WAITING_RESPONSE`/`INITIALIZING` 중이거나, 다른 핀패드 명령이 이미 `PINPAD_OP_RUNNING` 중 | `PinpadSequence_Begin`(`PinpadSequence.cpp:186,196`). 단, `PINPAD_CMD_INIT`은 예외 — 진행 중이던 다른 핀패드 시퀀스뿐 아니라 리더기 쪽에 남아있던 `WAITING_RESPONSE` 잔여 상태까지 함께 무효화하고 항상 시작된다(아래 "리더기/핀패드 크로스 BUSY" 참조). |
| `READER_ERR_INVALID_LENGTH` (-1300) | 조합 시퀀스 첫 단계 전문의 Data Length가 허용 범위를 초과 | `PinpadSequence_Begin`이 호출하는 `PinpadBuildFrame`(`PinpadFrameBuilder.cpp:41`) — `Reader_SendCommand`가 `BuildFrame`(리더기 전용 프레임 빌더)에서 이 값을 받는 것과 동일한 구조를 핀패드 전용 프레임 빌더가 그대로 따른다 |
| `READER_ERR_BUFFER_OVERFLOW` (-1301) | 완성된 전문이 내부 작업 버퍼 용량을 초과(방어적 검사, 실무상 도달 어려움) | `PinpadBuildFrame`(`PinpadFrameBuilder.cpp:48,93`) |
| `READER_ERR_PORT_CLOSING` (-1105) | 송신 시도 시점에 포트가 이미 `CLOSING` 상태로 전이됨(닫기 진행 중인 경합 상황) | `PinpadSequence_Begin`이 첫 전문을 보낼 때 호출하는 `SendViaHookOrDefault` → `SerialWorker_SendFrame`(`SerialWorker.cpp:439`) — `Reader_SendCommand`와 물리 송신 함수를 그대로 공유하므로 동일한 값을 반환할 수 있다 |
| `READER_ERR_SEND_FAIL` (-1200) | 실제 바이트 송신(`WriteFile`) 단계 실패 | 위와 동일하게 `SerialWorker_SendFrame`을 공유하므로 발생(`SerialWorker.cpp:455,472,478,486`) — 실패 시 `PinpadSequence_OnStepFailed(ctx, PINPAD_EVENT_SEND_FAIL)`도 함께 호출되어 `PINPAD_CALLBACK`으로도 통지된다(`PinpadSequence.cpp:304-307`) |

### 리더기/핀패드 크로스 BUSY (2026-08-10 사용자 요청, 중요)

멀티패드는 리더기와 핀패드가 같은 물리 포트를 공유하는 하나의 장치이므로, 한쪽 명령이 진행 중이면 다른 쪽 명령도 `READER_ERR_BUSY`로 거절된다 — Phase 12 원안("리더기와 핀패드는 서로 막지 않는다")을 뒤집은 결정이다.

- 리더기 일반 명령(`Reader_SendCommand`) 진행 중 → 핀패드 일반 명령(`Pinpad_SendCommand`, INIT 제외)은 `READER_ERR_BUSY`
- 핀패드 명령(`PINPAD_OP_RUNNING`) 진행 중 → 리더기 일반 명령(`Reader_SendCommand`)은 `READER_ERR_BUSY`(`CommandStateManager.cpp:109`)
- **예외**: 두 초기화 명령(리더기 `0x60`, 핀패드 `PINPAD_CMD_INIT`)은 상대방에게 막히지 않으며, 상대방의 걸려있는 잔여 상태(`WAITING_RESPONSE`/`PINPAD_OP_RUNNING`)를 조용히(CALLBACK 없이) `IDLE`로 되돌린 뒤 시작한다. 단 상대방이 이미 **자기 자신의** 초기화 중(`READER_OP_INITIALIZING`)인 경우는 건드리지 않는다(범위 밖).
- 별도 핀패드 포트(리더기와 물리 포트가 다른 구성)에서는 이 체크가 실질적으로 발동할 일이 없다 — 두 장치가 완전히 독립된 `ReaderContext`를 갖기 때문이다. 이 절은 **멀티패드(같은 포트 공유) 구성에서만** 의미가 있다.

### 핀패드 RUNNING 중 리더기 프레임 유실 (중요, 반드시 숙지)

핀패드가 `PINPAD_OP_RUNNING`인 동안 도착하는 **모든 리더기 프레임은 유실된다** — 비요청 이벤트(카드 감지 `0x76`)뿐 아니라, **핀패드 명령이 시작되기 직전에 이미 접수돼 물리적으로 전송까지 끝난 리더기 명령의 정상 응답도 포함**된다(`FrameDispatcher.cpp`가 핀패드 RUNNING 중엔 핀패드 해석만 시도하고 리더기 누적 버퍼는 그 배치의 바이트를 받지 않는다 — 두 해석이 상호 배타적이라는 2026-08-10 아키텍처 결정. 근거/트레이드오프 전문은 `DOC/개발문서/PRD_핀패드.md` §12·§20 항목 7 참조).

- 위 "크로스 BUSY" 덕분에, 핀패드 RUNNING 중 POS가 **새로** 보내려는 리더기 명령은 `Pinpad_SendCommand`/`Reader_SendCommand` 어느 쪽 진입 시점에도 `READER_ERR_BUSY`로 걸러져 애초에 전송되지 않으므로 이 유실의 영향을 받지 않는다.
- 다만 **핀패드 명령을 보내기 직전에 이미 전송해 둔 리더기 명령의 응답**은 크로스 BUSY와 무관하게 여전히 유실될 수 있다(`Reader_SendCommand`가 성공을 반환한 뒤, 그 응답이 오기 전에 `Pinpad_SendCommand`를 호출하는 경우) — 이 경우 리더기 쪽은 `READER_EVENT_RESPONSE` 대신 `READER_EVENT_TIMEOUT`을 받게 된다(실장비로 확인됨, `Test_Hardware_MultipadCOM7_ConcurrentReaderAndPinpad`).
- **다른 POS 벤더가 "PIN 입력 진행 중에는 리더기 명령을 새로 보내지 않는다"는 운영 방침을 지키지 않고 실제로 동시에 쓰면 리더기 응답을 조용히 잃을 수 있다.** 벤더용 문서(`DOC/DLL연동가이드.md`)에도 이 제약을 반드시 명시한다.

---

## 11. `PINPAD_CALLBACK` 시그니처

**2026-08-12 전면 재설계.** `package/`가 아직 외부 배포 전이라 API 시그니처를 자유롭게 재설계할 수 있었던 마지막 시점에, 사용자와의 논의(리더기 `resultCode` 제거 논의 중 "핀패드도 리더기처럼 맞추는 게 DLL 사용자에게 편하다"는 판단) 끝에 이 절 전체가 바뀌었다. 이전 버전(3번째 파라미터 `result`, `PINPAD_EVENT_ERROR`/`PINPAD_EVENT_TIMEOUT`이 3byte `failInfo` payload를 싣던 설계)은 완전히 폐기됐다.

```cpp
typedef void (__stdcall *PINPAD_CALLBACK)(
    int readerId,
    int eventType,
    unsigned char commandCode,
    const unsigned char* data,
    int dataLength,
    void* userContext
);
```

`READER_CALLBACK`(§7)과 파라미터 개수/순서는 비슷하지만 별도 타입이다 — POS가 리더기용/핀패드용 핸들러를 분리할 수 있도록 의도적으로 나눴다.

| 파라미터 | 의미 |
|---|---|
| `readerId` | 이 CALLBACK을 유발한 리더기/핀패드의 식별자(`Reader_OpenPort`가 발급한 값) |
| `eventType` | `PinpadEventType` 값(§12 표) — 7개 실패 원인이 각각 최상위 이벤트로 승격되어 있다(아래 참조) |
| `commandCode` | POS가 `Pinpad_SendCommand(readerId, commandCode, ...)`에 넘긴 그 `PinpadCommandCode` 값(`0xA0`~`0xA4`) 그대로. 내부적으로 몇 단계(0xF1→0xF3→0xF7 등)를 거치든, 그 시퀀스에서 발생하는 모든 CALLBACK(성공/실패 불문)은 항상 이 값이 동일하다 — DLL 사용자는 내부 SPEC Fc/subCode를 모르는 상황이므로, POS가 이해하는 공개 어휘(`PinpadCommandCode`)로 "이 이벤트가 어떤 명령에 관한 것인지" 답해준다(리더기 `commandCode`와 유사한 역할이나, 내부 구현 디테일 대신 공개 어휘를 쓴다는 점이 다르다). |
| `data` | `PINPAD_EVENT_RESPONSE`일 때만 실제 완료 데이터(명령별 상이). 그 외 모든 이벤트(`TIMEOUT`/`NAK`/`LRC_ERROR`/`TAMPER`/`SEND_FAIL`/`RECEIVE_ERROR`/`FRAME_STALL`)는 항상 `nullptr`이다(`READER_CALLBACK`과 동일한 패턴). |
| `dataLength` | `data`의 byte 길이. `data == nullptr`이면 `0`. |
| `userContext` | `Reader_OpenPort` 호출 시 넘긴 값이 그대로 전달됨 |

### 데이터 수명 규칙

`READER_CALLBACK`과 동일하다 — `data` 포인터는 CALLBACK 호출이 실행되는 동안에만 유효하며, 반환 즉시 DLL이 0으로 덮어쓰고 정리한다. 이후 필요하면 CALLBACK 내부에서 복사해야 한다.

### `failInfo`/`PinpadFailReason` 개념은 완전히 제거됨

과거 실패 이벤트는 `PINPAD_EVENT_ERROR` 하나로 뭉쳐 3byte `failInfo`(`fc`/`subCode`/`reason`) payload로 원인을 `data`에 실었으나, 이제 그 정보는 `eventType` 자체가 표현한다 — `PinpadFailReason` 내부 enum도 소스에서 완전히 삭제됐다. POS는 더 이상 `data[2]`를 파싱할 필요가 없고, `eventType`으로 바로 분기하면 된다(§12 표).

---

## 12. `PinpadCommandCode`/`PinpadEventType` 값 표

### `PinpadCommandCode` (POS가 `Pinpad_SendCommand`에 넘기는 값, `ReaderSerial.h`)

SPEC이 정의하는 값이 아니라 DLL이 조합 시퀀스를 대신 수행하기 위해 자체 부여한 값이며, SPEC의 Fc(`0xF0`~`0xFA`)/리더기 명령 코드(`0x60`~`0x7F`)와 시각적으로 겹치지 않도록 `0xA0` 대역을 쓴다.

| 값 | 이름 | 의미 |
|---|---|---|
| `0xA0` | `PINPAD_CMD_INIT` | 핀패드 초기화 |
| `0xA1` | `PINPAD_CMD_PIN_PASSWORD` | 비밀번호 핀입력 |
| `0xA2` | `PINPAD_CMD_PIN_NUMBER` | 번호 핀입력 |
| `0xA3` | `PINPAD_CMD_PIN_DES` | DES 암호화 핀입력 |
| `0xA4` | `PINPAD_CMD_PIN_SEED` | SEED 암호화 핀입력 |

### `PinpadEventType` (`PINPAD_CALLBACK`의 `eventType`, `ReaderSerial.h`)

**2026-08-12 전면 재설계**: 과거 3개(`RESPONSE`/`TIMEOUT`/`ERROR`)에서 7개 실패 원인을 각각 최상위 이벤트로 승격한 8개로 확장했다 — 리더기 `ReaderEventType`(원인마다 최상위 이벤트)과 동일한 패턴이다. `PINPAD_FAIL_TIMEOUT`이 옛 `failInfo`에서 `0x01`이었던 것과 `PINPAD_EVENT_TIMEOUT = 1`이 겹치는 것은 우연이며(선언 순서대로 번호가 매겨진 것뿐), 의미 있는 매핑이 아니다.

| 값 | 이름 | 의미 | `data` |
|---:|---|---|---|
| 0 | `PINPAD_EVENT_RESPONSE` | 조합 명령 정상 완료 | 완료 데이터(명령별 상이) |
| 1 | `PINPAD_EVENT_TIMEOUT` | 단계 응답 대기 시간 초과(ACK 3초 / PIN 입력 200초), 내부 복구 처리(`0xF1 0x03` 재전송) 완료 후 발생 | `nullptr` |
| 2 | `PINPAD_EVENT_NAK` | NAK 수신(0xF1/0xF3 단계는 1회 재전송 후에도 실패, 0xF7 단계는 재전송 없이 즉시) | `nullptr` |
| 3 | `PINPAD_EVENT_LRC_ERROR` | 수신 전문 LRC 불일치 | `nullptr` |
| 4 | `PINPAD_EVENT_TAMPER` | `0xFA` 응답 — Tamper(물리적 파손) 감지 | `nullptr` |
| 5 | `PINPAD_EVENT_SEND_FAIL` | 포트 쓰기(송신) 실패 | `nullptr` |
| 6 | `PINPAD_EVENT_RECEIVE_ERROR` | 포트 물리 장애(케이블 분리 등, 리더기 `READER_EVENT_RECEIVE_ERROR`와 동일 원인) — 진행 중이던 시퀀스가 있으면 매칭 조건 없이 무조건 즉시 실패 처리된다 | `nullptr` |
| 7 | `PINPAD_EVENT_FRAME_STALL` | 미완성 프레임 정체(리더기 `READER_EVENT_FRAME_STALL`과 동일 원칙) — 정체된 프레임이 지금 기다리던 그 응답이라고 확신할 수 있을 때만(Fc, STEP_RESPONSE 단계는 code까지 일치) 원래 타임아웃까지 기다리지 않고 즉시 실패 처리된다 | `nullptr` |

**폐지된 개념(2026-08-11/2026-08-12)**: 과거 `0x06`(`PINPAD_FAIL_NO_INPUT`, "고객 미입력" 전용 사유)은 2026-08-11에 제거됐다 — SPEC의 NAK 패킷에는 사유 데이터가 없어 "고객 미입력"과 "미지원 sub-code(예: SEED 미지원 모델)의 즉시 거부"를 프로토콜 레벨에서 구분할 수 없다는 게 실장비로 확인돼, 0xF7 단계 NAK도 0xF1/0xF3 단계와 동일하게 `PINPAD_EVENT_NAK`로 통합됐다. 진짜 "고객이 입력하지 않음"은 별도의 `PINPAD_EVENT_TIMEOUT`(200초)이 담당하므로 커버리지 공백은 없다. `PinpadFailReason` enum 자체는 2026-08-12에 완전히 삭제됐다(§11 참조).

---

## 14. 각주 — PRD 문구와 실제 구현 간 확인된 차이

[^1]: 2026-08-05 이전에는 `Reader_OpenPort` 구현이 `115200`이 아닌 모든 `baudRate` 값을 `READER_ERR_INVALID_ARGUMENT`로 즉시 거부하는 하드 체크였다(당시 근거: `ReaderApi.cpp`의 `SUPPORTED_BAUD_RATE` 상수). PRD §8.4가 "기본 권장값 115200"으로 표현한 것과도 실제로는 더 좁게 동작해 각주로 남겼던 항목이다. 이후 이 하드 체크를 제거하고 `baudRate > 0`인 값은 그대로 `SetCommState`에 전달하도록 변경했다(POS가 리더기 SPEC에 맞는 값을 자유롭게 설정할 수 있어야 한다는 요구).

[^2]: PRD §7.2는 "이미 닫힌 포트를 다시 닫아도 프로그램이 비정상 종료되지 않아야 한다"는 안전성만 요구하고 반환값을 명시하지 않는다. 실제 구현은 이 경우를 오류가 아니라 `READER_OK`(멱등, idempotent)로 처리한다 — 문서화 가치가 있는 구체적 동작이라 이 명세서에 명시했다.

[^3]: PRD §7.5 "API 반환값 의미"는 `READER_ERR_BUSY`/`READER_ERR_INVALID_ARGUMENT`/`READER_ERR_INVALID_LENGTH`/`READER_ERR_INTERNAL` 정도만 예시로 들고 있으나, 실제 구현은 이보다 세분화된 코드(`READER_ERR_PORT_NOT_OPEN`/`READER_ERR_PORT_CLOSING`/`READER_ERR_BUFFER_OVERFLOW`/`READER_ERR_SEND_FAIL`/`READER_ERR_COMMAND_NOT_ALLOWED`)를 반환한다. PRD는 예시 나열이라 이를 "누락"이나 "모순"으로 보지는 않지만, 실제 반환 가능한 전체 집합은 본 문서 §6 표가 정본이다.

[^4]: `READER_EVENT_CONNECTED`/`READER_EVENT_DISCONNECTED`/`READER_EVENT_SEND_ERROR` 3개 값은 `ReaderEventType`에 선언만 되어 있고 `CallbackHandler_Invoke` 호출부 어디에서도 발생시키지 않는 죽은 값으로 확인되어(2026-08-03, `src/ReaderSerial` 전체 grep으로 재확인), 2026-08-05 재번호 작업에서 enum 자체에서 제거됐다. 포트 열기 성공은 `Reader_OpenPort`의 반환값(`READER_OK`)으로만 알 수 있고, 포트/장치 단의 오류는 `READER_EVENT_RECEIVE_ERROR`가 대신 커버한다.

[^6]: 2026-08-03 사용자 결정으로 §6의 서술이 바뀌었다 — 자세한 배경은 §6 본문의 각주 위치를 참고. 요약하면, 이 DLL은 여러 POS 업체가 사용하는 공용 인프라라 모든 업체가 권장 재시도 패턴을 구현한다고 보장할 수 없으므로, 송신 실패가 확인된 즉시 DLL 스스로 `operationState`를 정리해 다음 정상 명령을 곧바로 받아들이도록 바꿨다. `AbortPendingCommand`(`CommandStateManager.h/.cpp`)는 `TryExpireCommandOnTimeout`과 동일한 필드 리셋을 수행하되 Timeout을 기다리지 않고 `Reader_SendCommand` 반환 직전에 호출된다는 점만 다르다.
