// CommandFieldSpecs.cs — SPEC 명령별 Data 필드 스펙 테이블.
//
// src/ReaderSerialTestUI/CommandFieldSpecs.cpp(GetCommandFieldSpecs, 71~304줄
// 부근)를 그대로 C#으로 포팅한 것이다. 필드 폭/기본값/note는 이미
// reader-spec-expert로 SPEC 원문을 대조해 확인된 값이므로 여기서 다시
// SPEC을 확인하지 않는다 — MFC 버전과 값이 달라지지 않도록 그대로 옮긴다.
//
// 필드 종류:
//   FIXED           — 고정 폭 바이트. width만큼 Pad로 채우거나 자른다.
//   LENGTH_PREFIXED — "길이(width자리 숫자, 왼쪽 '0' 패딩) + 가변 payload".
//                     사용자는 payload만 입력하고, 프리픽스는 전송 시 payload
//                     의 CP949 인코딩 byte 길이로부터 자동 계산한다.
// (이 19개 명령 범위에는 HEX_BINARY 필드가 실제로 쓰이지 않는다 — 0x64/0x65는
//  SPEC상 hex 값이 ascii 텍스트로 그대로 전송되는 FIXED 필드다.)
using System.Collections.Generic;

namespace ReaderSerialCSharpSample
{
    internal enum FieldKind
    {
        FIXED,
        LENGTH_PREFIXED,
    }

    internal enum FieldPad
    {
        NONE,        // 폭 초과 시 앞부분만 유지, 부족 시 뒤에 공백으로 채움(테스트 편의, SPEC 명시 아님)
        LEFT_ZERO,   // 숫자 필드: 왼쪽 '0' 패딩 (SPEC 명시)
        RIGHT_SPACE, // 메시지류 필드: 오른쪽 Space 패딩 (SPEC 명시)
    }

    internal sealed class FieldSpec
    {
        internal string Label;
        internal FieldKind Kind;
        internal int Width;   // FIXED: 바이트 폭. LENGTH_PREFIXED: 프리픽스 자릿수.
        internal FieldPad Pad; // FIXED에서만 사용
        internal string DefaultValue; // LENGTH_PREFIXED는 payload만
        internal string Note;
    }

    internal static class CommandFieldSpecs
    {
        private static FieldSpec Fixed(string label, int width, FieldPad pad, string defaultValue, string note = "")
        {
            return new FieldSpec { Label = label, Kind = FieldKind.FIXED, Width = width, Pad = pad, DefaultValue = defaultValue, Note = note };
        }

        private static FieldSpec LengthPrefixed(string label, int prefixWidth, string defaultPayload, string note = "")
        {
            return new FieldSpec { Label = label, Kind = FieldKind.LENGTH_PREFIXED, Width = prefixWidth, Pad = FieldPad.NONE, DefaultValue = defaultPayload, Note = note };
        }

        // commandCode(요청 코드)에 대한 필드 스펙 목록을 반환한다. Data가 없는
        // 명령(0x60/0x61/0x62/0x63)이나 정의되지 않은 코드는 빈 목록을 반환한다.
        internal static List<FieldSpec> GetCommandFieldSpecs(byte commandCode)
        {
            const string kEmpty = "";

            if (commandCode == CommandCodes.INTEGRITY_CHECK_REQUEST || commandCode == CommandCodes.KEY_DOWNLOAD_START_REQUEST)
            {
                // SPEC SS3.3(p.13)/SS3.4(p.14): Data 없음.
                return new List<FieldSpec>();
            }

            if (commandCode == CommandCodes.KEY_DOWNLOAD_AUTH_REQUEST)
            {
                // SPEC SS3.5(p.15). 세 필드 모두 hex->ascii 2byte expanding, 고정폭.
                return new List<FieldSpec>
                {
                    Fixed("HASH X(64) — RND의 SHA256, hex→ascii", 64, FieldPad.NONE, kEmpty),
                    Fixed("RND X(32) — hex→ascii", 32, FieldPad.NONE, kEmpty),
                    Fixed("SIGN X(512) — hex→ascii", 512, FieldPad.NONE, kEmpty,
                        "SPEC SS3.5 요청 필드표 LRC/ETX 순서 이상 — FrameBuilder는 표준 순서(ETX 뒤 LRC) 유지"),
                };
            }

            if (commandCode == CommandCodes.USING_KEY_SEND_REQUEST)
            {
                // SPEC SS3.6(p.16).
                return new List<FieldSpec>
                {
                    Fixed("암호화 데이터 X(128) — hex→ascii", 128, FieldPad.NONE, kEmpty),
                    Fixed("MAC 값 X(16) — hex→ascii", 16, FieldPad.NONE, kEmpty),
                };
            }

            if (commandCode == CommandCodes.IC_TRANSACTION_REQUEST)
            {
                // SPEC SS3.8(p.20).
                return new List<FieldSpec>
                {
                    Fixed("거래 일시 X(14) YYYYMMDDHHmmSS", 14, FieldPad.NONE, "20260715140851"),
                    Fixed("거래 금액 X(18)", 18, FieldPad.LEFT_ZERO, "1004"),
                    Fixed("AID 인덱스 X(1) '0'~'9'", 1, FieldPad.NONE, "0"),
                    Fixed("RF 사용 여부 X(1) 'Y'/'N'", 1, FieldPad.NONE, "Y"),
                    Fixed("RF 거래 구분 X(1) '1'~'4'", 1, FieldPad.NONE, "3"),
                    LengthPrefixed("RF 거래 순서", 2, "00",
                        "SPEC: RF거래구분≠'2'일 때 처리방식 명시 없음 — 프리픽스만 \"00\"으로 전송(0x2B의 동일 필드 처리 방식과 동일하게 추정)"),
                };
            }

            if (commandCode == CommandCodes.IC_TRANSACTION_COMPLETE_REQUEST)
            {
                // SPEC SS3.9(p.25). EMV 인코딩 데이터는 BASE64 payload — 길이 프리픽스 폭 4.
                return new List<FieldSpec>
                {
                    Fixed("거래 일시 X(14)", 14, FieldPad.NONE, kEmpty),
                    Fixed("거래 금액 X(18)", 18, FieldPad.LEFT_ZERO, kEmpty),
                    Fixed("EMV 인코딩 방식 X(1) \"B\" 고정", 1, FieldPad.NONE, "B"),
                    LengthPrefixed("EMV 인코딩 데이터 (길이 4 + BASE64 가변)", 4, kEmpty,
                        "BASE64 내부는 Escape 인코딩된 TAG+LENGTH+VALUE(GS 구분) — 샘플 미제공, 임의로 채우지 않음"),
                };
            }

            if (commandCode == CommandCodes.IC_TRANSACTION_CANCEL_REQUEST)
            {
                // SPEC SS3.10(p.27).
                return new List<FieldSpec>
                {
                    Fixed("거래 일시 X(14)", 14, FieldPad.NONE, "20260715141221"),
                    Fixed("거래 금액 X(18)", 18, FieldPad.LEFT_ZERO, "1004"),
                    Fixed("AID 인덱스 X(1)", 1, FieldPad.NONE, "0"),
                    Fixed("PIN 블록 입력 여부 X(1) '0'/'1'", 1, FieldPad.NONE, "1"),
                    Fixed("RF 사용 여부 X(1) 'Y'/'N'", 1, FieldPad.NONE, "Y"),
                    Fixed("RF 거래 구분 X(1) '1'~'4'", 1, FieldPad.NONE, "3"),
                    LengthPrefixed("RF 거래 순서", 2, "00",
                        "SPEC: RF거래구분≠'2'일 때 처리방식 명시 없음 — 프리픽스만 \"00\"으로 전송"),
                };
            }

            if (commandCode == CommandCodes.FALLBACK_TRANSACTION_REQUEST)
            {
                // SPEC SS3.11(p.31).
                return new List<FieldSpec>
                {
                    Fixed("거래 일시 X(14)", 14, FieldPad.NONE, "20260715141132"),
                    Fixed("PIN 블록 입력 여부 X(1) '0'/'1'", 1, FieldPad.NONE, "0"),
                };
            }

            if (commandCode == CommandCodes.MS_TRANSACTION_REQUEST)
            {
                // SPEC SS3.12(p.33).
                return new List<FieldSpec>
                {
                    Fixed("거래 일시 X(14)", 14, FieldPad.NONE, "20260715141723"),
                    Fixed("암호화 여부 X(1) '0'~'8'", 1, FieldPad.NONE, "2"),
                    Fixed("PIN 블록 입력 여부 X(1) '0'/'1'", 1, FieldPad.NONE, "0"),
                };
            }

            if (commandCode == CommandCodes.KEYIN_NUMBER_ENCRYPT_REQUEST)
            {
                // SPEC SS3.13(p.36). SPEC 원문이 동일 코드(0x6C)에 서로 다른
                // Data 구조 2종(일반형 / 멀티패드 구분자형)을 정의하고 판별
                // 필드가 없다 — 이 예제는 MFC 버전과 동일하게 "일반형"만 지원한다.
                return new List<FieldSpec>
                {
                    Fixed("거래 일시 X(14)", 14, FieldPad.NONE, "20260715141723"),
                    LengthPrefixed("카드 번호 (길이 2 + 가변, 유효기간 포함)", 2, "1111222233334444=1212",
                        "SPEC이 동일 코드에 멀티패드용 \"[[SB+시각+길이]]\" 구조도 정의하나 판별 필드가 없어 일반형만 지원"),
                };
            }

            if (commandCode == CommandCodes.LOCKTYPE_DEVICE_CONTROL_REQUEST)
            {
                // SPEC SS3.15(p.40).
                return new List<FieldSpec>
                {
                    LengthPrefixed("제어 데이터 (길이 2 + 가변, S/V/M/E/B/C/R/I/P)", 2, "E"),
                };
            }

            if (commandCode == CommandCodes.VOICE_VIDEO_OUTPUT_REQUEST)
            {
                // SPEC SS3.17(p.44).
                return new List<FieldSpec>
                {
                    Fixed("요청 구분 X(1) '0'/'1'/'2'", 1, FieldPad.NONE, "0"),
                    Fixed("기능 구분 X(1) '0'~'3'", 1, FieldPad.NONE, "0"),
                    Fixed("실행 파일 번호 X(2)", 2, FieldPad.LEFT_ZERO, "81"),
                };
            }

            if (commandCode == CommandCodes.CARD_INFO_CONFIRM_REQUEST)
            {
                // SPEC SS3.20(p.50).
                return new List<FieldSpec>
                {
                    Fixed("처리 구분 X(2) \"01\"(멀티패드 카드선택)/\"02\"(POS 응답)", 2, FieldPad.NONE, "01"),
                    Fixed("메시지 1 X(16)", 16, FieldPad.RIGHT_SPACE, kEmpty),
                    Fixed("메시지 2 X(16)", 16, FieldPad.RIGHT_SPACE, kEmpty),
                    Fixed("메시지 3 X(16)", 16, FieldPad.RIGHT_SPACE, kEmpty),
                    Fixed("메시지 4 X(16)", 16, FieldPad.RIGHT_SPACE, kEmpty),
                };
            }

            if (commandCode == CommandCodes.READER_SETTING_REQUEST)
            {
                // SPEC SS3.36(p.79).
                return new List<FieldSpec>
                {
                    LengthPrefixed("설정 데이터 (길이 2 + 가변: RE/ET/EF/CKMOD/CB/SM120 등)", 2, "ET",
                        "\"SM\"+시간(3자리)의 패딩 문자는 SPEC 미명시 — 예시 \"SM120\"처럼 자리수 그대로 입력"),
                };
            }

            if (commandCode == CommandCodes.PLAIN_PIN_INPUT_REQUEST)
            {
                // SPEC SS3.38(p.83~84).
                return new List<FieldSpec>
                {
                    Fixed("유형 및 마스킹 여부 X(2) \"00\"~\"03\",\"81\"~\"99\"", 2, FieldPad.NONE, "01"),
                    Fixed("Display 시간 9(2), 초기값 30", 2, FieldPad.LEFT_ZERO, "30"),
                    Fixed("메시지 인코딩 방식 X(1) \"B\" 고정", 1, FieldPad.NONE, "B"),
                    LengthPrefixed("메시지 (길이 3 + 가변, EPSON 제어코드)", 3,
                        "G2EwMTIzNDU2NzgKGyEIG2ExQUJDREVGRwobISAbYTIhQCMkJV4m",
                        "\"메시지 인코딩 방식=B\"가 메시지 필드 자체를 BASE64로 감싸라는 뜻인지 SPEC에 명시적 연결 서술 없음 — 샘플을 그대로 payload로 사용"),
                };
            }

            if (commandCode == CommandCodes.TRANSACTION_INFO_REQUEST)
            {
                // SPEC SS3.39(p.86~88).
                return new List<FieldSpec>
                {
                    Fixed("거래 일시 X(14)", 14, FieldPad.NONE, "20260715152310"),
                    Fixed("거래 금액 X(18)", 18, FieldPad.LEFT_ZERO, "1004"),
                    Fixed("AID 인덱스 X(1)", 1, FieldPad.NONE, "0"),
                    LengthPrefixed("거래구분 (길이 2 + 가변, A/C/F/M/H/P/R/Q/q/o 나열)", 2, "RQA"),
                    Fixed("RF 리딩 방식 X(1) '0'~'4' (거래구분에 'R' 없으면 '0')", 1, FieldPad.NONE, "3"),
                    LengthPrefixed("RF 거래 순서", 2, "00",
                        "SPEC이 명시적으로 그 외에는 \"00\"(프리픽스만 0) 규정"),
                    Fixed("PIN 블록 입력 여부 X(1) '0'/'1'", 1, FieldPad.NONE, "1"),
                    Fixed("FILLER X(16) — 예비필드, Space 고정", 16, FieldPad.RIGHT_SPACE, kEmpty),
                    Fixed("메시지 1 X(16)", 16, FieldPad.RIGHT_SPACE, "1-----승인------"),
                    Fixed("메시지 2 X(16)", 16, FieldPad.RIGHT_SPACE, "2 카드를        "),
                    Fixed("메시지 3 X(16)", 16, FieldPad.RIGHT_SPACE, "3    넣어주세요."),
                    Fixed("메시지 4 X(16)", 16, FieldPad.RIGHT_SPACE, "4  IC  INSERT   "),
                    Fixed("payOn Key정보 X(32) — RF카드종류='C'일 때만 값, 그 외 Space", 32, FieldPad.RIGHT_SPACE, kEmpty),
                };
            }

            if (commandCode == CommandCodes.ENCRYPTED_ACN_REQUEST)
            {
                // SPEC SS3.40(p.93).
                return new List<FieldSpec>
                {
                    Fixed("암호화 방식 X(1) \"S\"(SEED) 고정", 1, FieldPad.NONE, "S"),
                };
            }

            return new List<FieldSpec>();
        }

        // 명령 콤보박스에 나열할 순서 — MFC 버전 GetAllFieldCommandCodes()와 동일한
        // 순서(파일럿 2 + SPEC 17).
        internal static List<byte> GetAllFieldCommandCodes()
        {
            return new List<byte>
            {
                CommandCodes.INIT_REQUEST,
                CommandCodes.STATUS_REQUEST,
                CommandCodes.INTEGRITY_CHECK_REQUEST,
                CommandCodes.KEY_DOWNLOAD_START_REQUEST,
                CommandCodes.KEY_DOWNLOAD_AUTH_REQUEST,
                CommandCodes.USING_KEY_SEND_REQUEST,
                CommandCodes.IC_TRANSACTION_REQUEST,
                CommandCodes.IC_TRANSACTION_COMPLETE_REQUEST,
                CommandCodes.IC_TRANSACTION_CANCEL_REQUEST,
                CommandCodes.FALLBACK_TRANSACTION_REQUEST,
                CommandCodes.MS_TRANSACTION_REQUEST,
                CommandCodes.KEYIN_NUMBER_ENCRYPT_REQUEST,
                CommandCodes.LOCKTYPE_DEVICE_CONTROL_REQUEST,
                CommandCodes.VOICE_VIDEO_OUTPUT_REQUEST,
                CommandCodes.CARD_INFO_CONFIRM_REQUEST,
                CommandCodes.READER_SETTING_REQUEST,
                CommandCodes.PLAIN_PIN_INPUT_REQUEST,
                CommandCodes.TRANSACTION_INFO_REQUEST,
                CommandCodes.ENCRYPTED_ACN_REQUEST,
            };
        }
    }
}
