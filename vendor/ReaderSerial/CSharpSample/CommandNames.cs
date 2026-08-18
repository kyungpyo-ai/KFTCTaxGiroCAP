// CommandNames.cs — src/ReaderSerial/CommandNames.h의 한글 명칭 표를 이
// 예제가 다루는 19개 명령만 옮긴 것이다. C# 소스는 컴파일러가 UTF-8로
// 해석하므로(C++ 쪽처럼 u8 접두어/코드페이지 문제가 없음) 일반 문자열
// 리터럴을 그대로 쓴다.
namespace ReaderSerialCSharpSample
{
    internal static class CommandNames
    {
        internal static string KoreanName(byte code)
        {
            switch (code)
            {
                case CommandCodes.INIT_REQUEST: return "초기화";
                case CommandCodes.STATUS_REQUEST: return "상태 확인";
                case CommandCodes.INTEGRITY_CHECK_REQUEST: return "무결성 체크";
                case CommandCodes.KEY_DOWNLOAD_START_REQUEST: return "키 다운로드 시작";
                case CommandCodes.KEY_DOWNLOAD_AUTH_REQUEST: return "키 다운로드 상호인증";
                case CommandCodes.USING_KEY_SEND_REQUEST: return "Using Key 전송";
                case CommandCodes.IC_TRANSACTION_REQUEST: return "IC 거래";
                case CommandCodes.IC_TRANSACTION_COMPLETE_REQUEST: return "IC 거래 완료";
                case CommandCodes.IC_TRANSACTION_CANCEL_REQUEST: return "IC 거래 취소";
                case CommandCodes.FALLBACK_TRANSACTION_REQUEST: return "Fallback 거래";
                case CommandCodes.MS_TRANSACTION_REQUEST: return "MS 거래";
                case CommandCodes.KEYIN_NUMBER_ENCRYPT_REQUEST: return "키인 번호 암호화";
                case CommandCodes.LOCKTYPE_DEVICE_CONTROL_REQUEST: return "LockType 장비 제어";
                case CommandCodes.VOICE_VIDEO_OUTPUT_REQUEST: return "음성출력/동영상";
                case CommandCodes.CARD_INFO_CONFIRM_REQUEST: return "카드 정보 확인";
                case CommandCodes.READER_SETTING_REQUEST: return "리더기 설정";
                case CommandCodes.PLAIN_PIN_INPUT_REQUEST: return "Plain PIN 입력";
                case CommandCodes.TRANSACTION_INFO_REQUEST: return "거래정보";
                case CommandCodes.ENCRYPTED_ACN_REQUEST: return "암호화 ACN";
                default: return "알 수 없음";
            }
        }

        // 콤보박스 표시용 "명칭(0xNN)" 문자열 — MFC 쪽 GetCommandDisplayName과 동일한 형식.
        internal static string DisplayName(byte code)
        {
            return $"{KoreanName(code)}(0x{code:X2})";
        }
    }
}
