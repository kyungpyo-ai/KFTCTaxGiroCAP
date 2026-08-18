// ReaderSerialNative.cs — ReaderSerial.dll P/Invoke 바인딩 (P10-2)
//
// src/ReaderSerial/ReaderSerial.h(공개 API 4개)와 ReaderErrors.h(ReaderResult)를
// 그대로 옮긴 것이다. 두 헤더의 값/시그니처가 바뀌면 이 파일도 함께 고쳐야
// 한다(자동 동기화되는 단일 소스가 아니므로 헤더 변경 시 반드시 대조할 것).
using System;
using System.Runtime.InteropServices;

namespace ReaderSerialCSharpSample
{
    // CALLBACK eventType 값. ReaderSerial.h의 ReaderEventType과 값이 완전히
    // 동일해야 한다 — 외부 연동 프로그램과의 계약이므로 순서/값을 바꾸지 않는다.
    // 2026-08-05 재번호: READER_EVENT_CONNECTED/DISCONNECTED/SEND_ERROR는
    // DLL 어디에서도 실제로 발생시키지 않는 죽은 값으로 확인되어 제거되었다.
    // 2026-08-12 재번호: 1부터 시작하던 것을 0부터 시작하도록 변경(상대 순서는
    // 불변) — PinpadEventType과 시작 번호를 통일하기 위한 사용자 요청.
    internal enum ReaderEventType
    {
        READER_EVENT_RESPONSE = 0,
        READER_EVENT_TIMEOUT = 1,
        READER_EVENT_LRC_ERROR = 2,
        READER_EVENT_RECEIVE_ERROR = 3,
        READER_EVENT_UNSOLICITED = 4,
        READER_EVENT_FRAME_STALL = 5,
    }

    // ReaderErrors.h의 ReaderResult(PRD SS9 오류 코드)를 그대로 옮긴 것이다.
    // SPEC 업무 응답 코드(00~23, CALLBACK의 data 첫 2byte에 실려 옴)와는
    // 완전히 별개 체계다 — 이 enum과 혼동하지 않는다.
    // 2026-08-05 재번호: READER_ERR_PORT_CLOSED/FRAME_BUILD_FAIL/INVALID_FRAME/
    // UNEXPECTED_RESPONSE는 DLL 어디에서도 실제로 발생시키지 않는 죽은 값으로
    // 확인되어 제거되었다.
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

        // 2026-08-13 제거: READER_ERR_RECEIVE_FAIL(-1201)/READER_ERR_TIMEOUT(-1202)/
        // READER_ERR_FRAME_STALL(-1203)/READER_ERR_LRC_MISMATCH(구 -1301)는 resultCode가
        // READER_CALLBACK에서 제거된 2026-08-12 이후로 반환/전달 경로가 없는 죽은 값으로
        // 확인되어 ReaderErrors.h에서 제거됐다(BUFFER_OVERFLOW를 -1302에서 -1301로 재번호).

        // P17-2: 핀패드 오류 코드(ReaderErrors.h, 2026-08-11 재번호). Pinpad_SendCommand의
        // 반환값이 이 값을 쓴다. 2026-08-12부터 PINPAD_CALLBACK에는 더 이상 실리지 않는다
        // (3번째 파라미터가 result에서 commandCode로 바뀜).
        // 같은 날, READER_ERR_PINPAD_STEP_FAILED(-1401)/READER_ERR_PINPAD_TIMEOUT(-1402)는
        // PINPAD_CALLBACK 재설계로 반환 경로가 완전히 사라져 ReaderErrors.h에서
        // 제거됐다 - 이 enum도 함께 삭제(재번호 불필요).
        READER_ERR_PINPAD_NOT_SUPPORTED = -1400,
    }

    // ReaderSerial.h의 PinpadEventType과 값이 완전히 동일해야 한다.
    // 2026-08-12 전면 재설계: 실패 원인이 PINPAD_EVENT_ERROR 하나로 뭉쳐
    // failInfo(3byte) payload로 실리던 것을, 리더기 ReaderEventType과 동일하게
    // 원인마다 최상위 eventType으로 승격했다 - PinpadFailReason/failInfo 개념은
    // 완전히 제거됐다(아래 있던 PinpadFailReason enum도 함께 삭제).
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
    // CLAUDE.md/PRD SS8.2 CALLBACK 데이터 수명 규칙: data는 이 호출이 실행되는
    // 동안에만 유효하고, DLL이 호출 직후 내부 버퍼를 0으로 지우므로 반드시
    // 콜백 안에서 즉시 복사해야 한다는 점을 코드로도 드러내기 위함).
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
    // 명시한다(CLAUDE.md CALLBACK 데이터 수명 규칙).
    // 2026-08-12: 3번째 파라미터가 result(ReaderResult, 항상 고정값이라
    // 정보량이 없었다)에서 commandCode(POS가 Pinpad_SendCommand에 넘긴 원래
    // PinpadCommandCode)로 바뀌었다.
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

        // P17-2: pinpadCallback을 실제 델리게이트 타입으로 받는다(과거
        // IntPtr.Zero 고정 시그니처는 핀패드 미구현 시기의 임시 조치였다).
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Reader_OpenPort(
            int portNumber,
            int baudRate,
            ReaderCallback readerCallback,
            PinpadCallback pinpadCallback,
            IntPtr userContext,
            out int outReaderId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Reader_ClosePort(int readerId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Reader_IsPortOpen(int readerId);

        // data는 dataLength==0일 때 null을 그대로 넘길 수 있다(마샬러가
        // null 배열을 IntPtr.Zero로 전달함 — ReaderSerial.h의 data==nullptr
        // 허용 규칙과 일치).
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Reader_SendCommand(
            int readerId,
            byte commandCode,
            byte[] data,
            int dataLength);

        // 2026-08-13: commandCode를 int에서 byte로 통일했다 — PinpadCommandCode
        // 값(0xA0~0xA4)이 Reader_SendCommand의 commandCode(byte)와 마찬가지로
        // 1byte 범위 안에 들어가는데도 타입이 달랐던 것은 근거 없는 불일치였다.
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int Pinpad_SendCommand(
            int readerId,
            byte commandCode,
            byte[] data,
            int dataLength);
    }

    // 테스트 UI(ReaderSerialTestUIDlg.cpp)의 ReaderResultToString/
    // ReaderEventTypeToString을 그대로 포팅한 것 — 로그에 정수값과 심볼릭
    // 이름을 함께 병기하기 위함이다.
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
                // 2026-08-12: READER_ERR_PINPAD_STEP_FAILED/READER_ERR_PINPAD_TIMEOUT는
                // 반환 경로가 사라져 enum에서 제거됐다 - 이 case들도 함께 삭제.
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

        internal static string PinpadEventTypeToString(int eventType)
        {
            switch ((PinpadEventType)eventType)
            {
                case PinpadEventType.PINPAD_EVENT_RESPONSE: return nameof(PinpadEventType.PINPAD_EVENT_RESPONSE);
                case PinpadEventType.PINPAD_EVENT_TIMEOUT: return nameof(PinpadEventType.PINPAD_EVENT_TIMEOUT);
                case PinpadEventType.PINPAD_EVENT_NAK: return nameof(PinpadEventType.PINPAD_EVENT_NAK);
                case PinpadEventType.PINPAD_EVENT_LRC_ERROR: return nameof(PinpadEventType.PINPAD_EVENT_LRC_ERROR);
                case PinpadEventType.PINPAD_EVENT_TAMPER: return nameof(PinpadEventType.PINPAD_EVENT_TAMPER);
                case PinpadEventType.PINPAD_EVENT_SEND_FAIL: return nameof(PinpadEventType.PINPAD_EVENT_SEND_FAIL);
                case PinpadEventType.PINPAD_EVENT_RECEIVE_ERROR: return nameof(PinpadEventType.PINPAD_EVENT_RECEIVE_ERROR);
                case PinpadEventType.PINPAD_EVENT_FRAME_STALL: return nameof(PinpadEventType.PINPAD_EVENT_FRAME_STALL);
                default: return "UNKNOWN";
            }
        }

        // 2026-08-12: PINPAD_CALLBACK의 3번째 파라미터가 commandCode로 바뀌면서
        // 추가된 헬퍼 - PinpadFailReasonToString(failInfo[2] 해석용)을 대체한다.
        internal static string PinpadCommandCodeToString(byte commandCode)
        {
            switch ((PinpadCommandCode)commandCode)
            {
                case PinpadCommandCode.PINPAD_CMD_INIT: return nameof(PinpadCommandCode.PINPAD_CMD_INIT);
                case PinpadCommandCode.PINPAD_CMD_PIN_PASSWORD: return nameof(PinpadCommandCode.PINPAD_CMD_PIN_PASSWORD);
                case PinpadCommandCode.PINPAD_CMD_PIN_NUMBER: return nameof(PinpadCommandCode.PINPAD_CMD_PIN_NUMBER);
                case PinpadCommandCode.PINPAD_CMD_PIN_DES: return nameof(PinpadCommandCode.PINPAD_CMD_PIN_DES);
                case PinpadCommandCode.PINPAD_CMD_PIN_SEED: return nameof(PinpadCommandCode.PINPAD_CMD_PIN_SEED);
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

        internal static string FormatPinpadEventType(int eventType)
        {
            return $"{eventType} ({PinpadEventTypeToString(eventType)})";
        }

        internal static string FormatPinpadCommandCode(byte commandCode)
        {
            return $"0x{commandCode:X2} ({PinpadCommandCodeToString(commandCode)})";
        }
    }
}
