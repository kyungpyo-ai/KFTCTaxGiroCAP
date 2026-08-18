// CommandCodes.cs — src/ReaderSerial/CommandCodes.h, PilotCommands.h의 명령
// 코드 상수를 이 예제가 다루는 19개 명령(파일럿 2 + SPEC 17)만 옮긴 것이다.
// SPEC 원문 근거/범위 제외 사유는 원본 헤더(CommandCodes.h 주석)를 참조 —
// 이 파일은 값만 재사용하고 그 배경 설명은 중복해 옮기지 않는다.
namespace ReaderSerialCSharpSample
{
    internal static class CommandCodes
    {
        internal const byte INIT_REQUEST = 0x60;
        internal const byte STATUS_REQUEST = 0x61;

        internal const byte INTEGRITY_CHECK_REQUEST = 0x62;
        internal const byte KEY_DOWNLOAD_START_REQUEST = 0x63;
        internal const byte KEY_DOWNLOAD_AUTH_REQUEST = 0x64;
        internal const byte USING_KEY_SEND_REQUEST = 0x65;
        internal const byte IC_TRANSACTION_REQUEST = 0x67;
        internal const byte IC_TRANSACTION_COMPLETE_REQUEST = 0x68;
        internal const byte IC_TRANSACTION_CANCEL_REQUEST = 0x69;
        internal const byte FALLBACK_TRANSACTION_REQUEST = 0x6A;
        internal const byte MS_TRANSACTION_REQUEST = 0x6B;
        internal const byte KEYIN_NUMBER_ENCRYPT_REQUEST = 0x6C;
        internal const byte LOCKTYPE_DEVICE_CONTROL_REQUEST = 0x6E;
        internal const byte VOICE_VIDEO_OUTPUT_REQUEST = 0x80;
        internal const byte CARD_INFO_CONFIRM_REQUEST = 0x83;
        internal const byte READER_SETTING_REQUEST = 0x0D;
        internal const byte PLAIN_PIN_INPUT_REQUEST = 0x2A;
        internal const byte TRANSACTION_INFO_REQUEST = 0x2B;
        internal const byte ENCRYPTED_ACN_REQUEST = 0x2C;
    }
}
