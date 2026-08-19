// ReaderSerialNative.cs — ReaderSerial.dll P/Invoke 바인딩 (Phase 9, P9-1)
//
// vendor/ReaderSerial/CSharpSample/ReaderSerialNative.cs를 그대로 포팅한 것이다. 새로 설계하지
// 않았다 — development_plan.md P9-1 지시대로 네임스페이스/접근 제한자만 이 프로젝트에 맞춰 조정했고,
// 시그니처·특성([UnmanagedFunctionPointer(StdCall)], CallingConvention.StdCall)·enum 값은 한 글자도
// 바꾸지 않았다.
//
// vendor/ReaderSerial/ReaderSerial.h와의 1:1 대조 결과(P9-1 완료 조건, 2026-08-19 직접 대조):
//   - ReaderEventType: RESPONSE=0/TIMEOUT=1/LRC_ERROR=2/RECEIVE_ERROR=3/UNSOLICITED=4/FRAME_STALL=5
//     — 헤더 enum 선언 순서·값과 정확히 일치.
//   - PinpadEventType: RESPONSE=0/TIMEOUT=1/NAK=2/LRC_ERROR=3/TAMPER=4/SEND_FAIL=5/RECEIVE_ERROR=6/
//     FRAME_STALL=7 — 헤더와 정확히 일치.
//   - PinpadCommandCode: INIT=0xA0/PIN_PASSWORD=0xA1/PIN_NUMBER=0xA2/PIN_DES=0xA3/PIN_SEED=0xA4
//     — 헤더와 정확히 일치.
//   - READER_CALLBACK(int readerId, int eventType, unsigned char commandCode,
//     const unsigned char* data, int dataLength, void* userContext) — ReaderCallback 델리게이트가
//     동일 순서로 int/int/byte/IntPtr/int/IntPtr을 선언, [UnmanagedFunctionPointer(StdCall)] 부착.
//   - PINPAD_CALLBACK — READER_CALLBACK과 동일한 파라미터 목록(3번째가 commandCode, resultCode
//     없음) — PinpadCallback 델리게이트와 일치.
//   - Reader_OpenPort(int portNumber, int baudRate, READER_CALLBACK, PINPAD_CALLBACK,
//     void* userContext, int* outReaderId) — 5인자 + out(6번째)인 최신 시그니처. DllImport 선언이
//     동일한 순서·타입(ReaderCallback/PinpadCallback/IntPtr userContext/out int outReaderId)으로 일치.
//   - Reader_ClosePort(int readerId) — 일치.
//   - Reader_IsPortOpen(int readerId) — 일치.
//   - Reader_SendCommand(int readerId, unsigned char commandCode, const unsigned char* data,
//     int dataLength) — commandCode가 byte(헤더의 unsigned char와 대응, 과거 int 아님) — 일치.
//   - Pinpad_SendCommand(int readerId, unsigned char commandCode, const unsigned char* data,
//     int dataLength) — commandCode가 byte — 2026-08-13 변경 반영, 일치.
//   => 5개 함수·2개 CALLBACK·3개 enum 모두 헤더와 선언이 정확히 일치함을 확인했다.
using System;
using System.Runtime.InteropServices;

namespace KFTCOneCAP.Wpf.Interop
{
    // CALLBACK eventType 값. ReaderSerial.h의 ReaderEventType과 값이 완전히
    // 동일해야 한다 — 외부 연동 프로그램과의 계약이므로 순서/값을 바꾸지 않는다.
    internal enum ReaderEventType
    {
        READER_EVENT_RESPONSE = 0,
        READER_EVENT_TIMEOUT = 1,
        READER_EVENT_LRC_ERROR = 2,
        READER_EVENT_RECEIVE_ERROR = 3,
        READER_EVENT_UNSOLICITED = 4,
        READER_EVENT_FRAME_STALL = 5,
    }

    // ReaderErrors.h의 ReaderResult(DLL 오류 코드, docs/reader_dll/DLL연동가이드.md §4)를 그대로
    // 옮긴 것이다. SPEC 업무 응답 코드(00~23, CALLBACK의 data 첫 2byte에 실려 옴)와는 완전히 별개
    // 체계다 — 이 enum과 혼동하지 않는다.
    internal enum ReaderResult
    {
        READER_OK = 0,

        READER_ERR_INVALID_ARGUMENT = -1001,
        READER_ERR_MAX_READER_COUNT = -1002,
        READER_ERR_INVALID_READER_ID = -1003,
        READER_ERR_BUSY = -1004,
        READER_ERR_COMMAND_NOT_ALLOWED = -1005,

        READER_ERR_PORT_NOT_FOUND = -1100,
        READER_ERR_PORT_OPEN_FAIL = -1101,
        READER_ERR_PORT_ALREADY_OPEN = -1102,
        READER_ERR_PORT_NOT_OPEN = -1103,
        READER_ERR_PORT_CONFIG_FAIL = -1104,
        READER_ERR_PORT_CLOSING = -1105,

        READER_ERR_SEND_FAIL = -1200,

        READER_ERR_INVALID_LENGTH = -1300,
        READER_ERR_BUFFER_OVERFLOW = -1301,

        READER_ERR_INTERNAL = -1900,

        READER_ERR_PINPAD_NOT_SUPPORTED = -1400,
    }

    // ReaderSerial.h의 PinpadEventType과 값이 완전히 동일해야 한다.
    internal enum PinpadEventType
    {
        PINPAD_EVENT_RESPONSE = 0,
        PINPAD_EVENT_TIMEOUT = 1,
        PINPAD_EVENT_NAK = 2,
        PINPAD_EVENT_LRC_ERROR = 3,
        PINPAD_EVENT_TAMPER = 4,
        PINPAD_EVENT_SEND_FAIL = 5,
        PINPAD_EVENT_RECEIVE_ERROR = 6,
        PINPAD_EVENT_FRAME_STALL = 7,
    }

    // ReaderSerial.h의 PinpadCommandCode와 값이 완전히 동일해야 한다.
    internal enum PinpadCommandCode
    {
        PINPAD_CMD_INIT = 0xA0,
        PINPAD_CMD_PIN_PASSWORD = 0xA1,
        PINPAD_CMD_PIN_NUMBER = 0xA2,
        PINPAD_CMD_PIN_DES = 0xA3,
        PINPAD_CMD_PIN_SEED = 0xA4,
    }

    // ReaderSerial.h의 READER_CALLBACK과 동일한 시그니처. StdCall 지정이
    // 없으면 기본값(Winapi, 이 플랫폼에서는 사실상 stdcall)에 기대게 되어
    // 위험하므로 명시한다 — DLL이 실제로 stdcall로 호출하는데 여기서
    // 어긋나면 스택이 깨진다.
    //
    // data는 unsigned char* 그대로 IntPtr로 받는다(byte[]로 선언하면 마샬러가
    // 그 시점에 배열 전체를 자동 복사하긴 하지만, "언제 복사가 일어나는지"를
    // 코드에서 명시적으로 통제하기 위해 일부러 IntPtr + Marshal.Copy를 쓴다 —
    // CALLBACK 데이터 수명 규칙: data는 이 호출이 실행되는 동안에만 유효하고,
    // DLL이 호출 직후 내부 버퍼를 0으로 지우므로 반드시 콜백 안에서 즉시
    // 복사해야 한다는 점을 코드로도 드러내기 위함 — Services/Reader/ReaderService 참고).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void ReaderCallback(
        int readerId,
        int eventType,
        byte commandCode,
        IntPtr data,
        int dataLength,
        IntPtr userContext);

    // ReaderSerial.h의 PINPAD_CALLBACK과 동일한 시그니처. ReaderCallback과
    // 마찬가지로 data는 IntPtr + Marshal.Copy로 받아 복사 시점을 코드로
    // 명시한다. 이번 Phase(9)에서는 핀패드를 쓰지 않지만(PRD §10), Reader_OpenPort
    // 시그니처와 헤더를 맞추기 위해 선언 자체는 함께 가져온다.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void PinpadCallback(
        int readerId,
        int eventType,
        byte commandCode,
        IntPtr data,
        int dataLength,
        IntPtr userContext);

    internal static class ReaderSerialNative
    {
        private const string DllName = "ReaderSerial.dll";

        // readerCallback/pinpadCallback의 `?`도 위 data 파라미터와 동일한 이유의 nullable 참조
        // 형식 주석이다(ABI 영향 없음) — DLL연동가이드.md §1.1: "둘 다 동시에 nullptr인 경우에만
        // 거부됨", 이번 Phase(9)는 pinpadCallback에 항상 null을 넘긴다(PRD §2.2.1/§10, 핀패드 미사용).
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Reader_OpenPort(
            int portNumber,
            int baudRate,
            ReaderCallback? readerCallback,
            PinpadCallback? pinpadCallback,
            IntPtr userContext,
            out int outReaderId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Reader_ClosePort(int readerId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Reader_IsPortOpen(int readerId);

        // data는 dataLength==0일 때 null을 그대로 넘길 수 있다(마샬러가
        // null 배열을 IntPtr.Zero로 전달함 — ReaderSerial.h의 data==nullptr
        // 허용 규칙과 일치).
        // data 파라미터의 `?`는 이 프로젝트(<Nullable>enable</Nullable>)의 C# nullable 참조 형식
        // 주석일 뿐이다 — vendor 샘플은 Nullable을 켜지 않아 이 표기가 없었다. 마샬링/ABI에는
        // 아무 영향이 없다(P/Invoke 시그니처 자체는 샘플과 완전히 동일) — dataLength==0일 때 null
        // 배열을 그대로 넘길 수 있다는 기존 동작도 그대로다.
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Reader_SendCommand(
            int readerId,
            byte commandCode,
            byte[]? data,
            int dataLength);

        // 2026-08-13: commandCode를 int에서 byte로 통일했다 — PinpadCommandCode
        // 값(0xA0~0xA4)이 Reader_SendCommand의 commandCode(byte)와 마찬가지로
        // 1byte 범위 안에 들어가는데도 타입이 달랐던 것은 근거 없는 불일치였다.
        // 이번 Phase(9)에서는 호출하지 않지만 헤더 대조를 위해 선언은 유지한다.
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Pinpad_SendCommand(
            int readerId,
            byte commandCode,
            byte[]? data,
            int dataLength);
    }

    // vendor/ReaderSerial/CSharpSample의 ReaderResultToString/ReaderEventTypeToString을 그대로
    // 포팅한 것 — 로그에 정수값과 심볼릭 이름을 함께 병기하기 위함이다.
    internal static class ReaderNames
    {
        internal static string ReaderResultToString(int result)
        {
            switch ((ReaderResult)result)
            {
                case ReaderResult.READER_OK: return nameof(ReaderResult.READER_OK);
                case ReaderResult.READER_ERR_INVALID_ARGUMENT: return nameof(ReaderResult.READER_ERR_INVALID_ARGUMENT);
                case ReaderResult.READER_ERR_MAX_READER_COUNT: return nameof(ReaderResult.READER_ERR_MAX_READER_COUNT);
                case ReaderResult.READER_ERR_INVALID_READER_ID: return nameof(ReaderResult.READER_ERR_INVALID_READER_ID);
                case ReaderResult.READER_ERR_BUSY: return nameof(ReaderResult.READER_ERR_BUSY);
                case ReaderResult.READER_ERR_COMMAND_NOT_ALLOWED: return nameof(ReaderResult.READER_ERR_COMMAND_NOT_ALLOWED);
                case ReaderResult.READER_ERR_PORT_NOT_FOUND: return nameof(ReaderResult.READER_ERR_PORT_NOT_FOUND);
                case ReaderResult.READER_ERR_PORT_OPEN_FAIL: return nameof(ReaderResult.READER_ERR_PORT_OPEN_FAIL);
                case ReaderResult.READER_ERR_PORT_ALREADY_OPEN: return nameof(ReaderResult.READER_ERR_PORT_ALREADY_OPEN);
                case ReaderResult.READER_ERR_PORT_NOT_OPEN: return nameof(ReaderResult.READER_ERR_PORT_NOT_OPEN);
                case ReaderResult.READER_ERR_PORT_CONFIG_FAIL: return nameof(ReaderResult.READER_ERR_PORT_CONFIG_FAIL);
                case ReaderResult.READER_ERR_PORT_CLOSING: return nameof(ReaderResult.READER_ERR_PORT_CLOSING);
                case ReaderResult.READER_ERR_SEND_FAIL: return nameof(ReaderResult.READER_ERR_SEND_FAIL);
                case ReaderResult.READER_ERR_INVALID_LENGTH: return nameof(ReaderResult.READER_ERR_INVALID_LENGTH);
                case ReaderResult.READER_ERR_BUFFER_OVERFLOW: return nameof(ReaderResult.READER_ERR_BUFFER_OVERFLOW);
                case ReaderResult.READER_ERR_INTERNAL: return nameof(ReaderResult.READER_ERR_INTERNAL);
                case ReaderResult.READER_ERR_PINPAD_NOT_SUPPORTED: return nameof(ReaderResult.READER_ERR_PINPAD_NOT_SUPPORTED);
                default: return "UNKNOWN";
            }
        }

        internal static string ReaderEventTypeToString(int eventType)
        {
            switch ((ReaderEventType)eventType)
            {
                case ReaderEventType.READER_EVENT_RESPONSE: return nameof(ReaderEventType.READER_EVENT_RESPONSE);
                case ReaderEventType.READER_EVENT_TIMEOUT: return nameof(ReaderEventType.READER_EVENT_TIMEOUT);
                case ReaderEventType.READER_EVENT_LRC_ERROR: return nameof(ReaderEventType.READER_EVENT_LRC_ERROR);
                case ReaderEventType.READER_EVENT_RECEIVE_ERROR: return nameof(ReaderEventType.READER_EVENT_RECEIVE_ERROR);
                case ReaderEventType.READER_EVENT_UNSOLICITED: return nameof(ReaderEventType.READER_EVENT_UNSOLICITED);
                case ReaderEventType.READER_EVENT_FRAME_STALL: return nameof(ReaderEventType.READER_EVENT_FRAME_STALL);
                default: return "UNKNOWN";
            }
        }

        internal static string FormatResult(int result)
        {
            return $"{result} ({ReaderResultToString(result)})";
        }

        internal static string FormatEventType(int eventType)
        {
            return $"{eventType} ({ReaderEventTypeToString(eventType)})";
        }
    }
}
