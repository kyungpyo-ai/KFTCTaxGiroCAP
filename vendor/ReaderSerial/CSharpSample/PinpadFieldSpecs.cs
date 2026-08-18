// PinpadFieldSpecs.cs — 핀패드 명령별 Data 필드 스펙 테이블 (P17-2).
//
// src/ReaderSerialTestUI/PinpadFieldSpecs.h/.cpp(MFC, P17-1, DLL 실제 파싱
// 코드 PinpadPinCommands.cpp와 대조해 확정됨)를 그대로 C#으로 포팅한 것이다.
// 리더기 필드(CommandFieldSpecs.cs)의 FieldKind.LENGTH_PREFIXED는 ASCII 숫자
// 프리픽스를 뜻하지만, 핀패드 필드는 완전히 다른 바이트 표현이 필요해 별도
// PinpadFieldKind를 둔다.
//
// 2026-08-10 사용자 요청: Line1/Line2(TEXT_LINE) 입력 필드를 완전히 제거했다 -
// DLL이 항상 명령별 기본 문구만 사용하도록 바뀌었으므로(PinpadMessageText.h
// 참조) POS가 입력할 필드 자체가 없어졌다.
using System.Collections.Generic;

namespace ReaderSerialCSharpSample
{
    internal enum PinpadFieldKind
    {
        DECIMAL_BYTE,  // 10진 숫자 텍스트(예: "6") -> 그 값을 그대로 1byte로 저장(MaxPinLength 등).
        HEX_BYTE,      // hex 2문자 -> 1byte. 2026-08-07 기준 이 kind를 쓰는 필드 스펙은 없다
                       // (PIN_DES의 TMKID가 유일한 사용처였으나 POS 입력 필드에서 제거됨).
        HEX_BINARY,    // hex 문자열 -> width byte로 변환(WorkingKey/ACN/RNUM 등).
    }

    internal sealed class PinpadFieldSpec
    {
        internal string Label;
        internal PinpadFieldKind Kind;
        internal int Width;   // HEX_BINARY: byte 폭. 그 외 항상 1byte.
        internal string DefaultValue;
        internal string Note;
    }

    internal static class PinpadFieldSpecs
    {
        private static PinpadFieldSpec DecimalByte(string label, string defaultValue, string note = "")
        {
            return new PinpadFieldSpec { Label = label, Kind = PinpadFieldKind.DECIMAL_BYTE, Width = 1, DefaultValue = defaultValue, Note = note };
        }

        private static PinpadFieldSpec HexBinaryField(string label, int byteWidth, string defaultValue, string note = "")
        {
            return new PinpadFieldSpec { Label = label, Kind = PinpadFieldKind.HEX_BINARY, Width = byteWidth, DefaultValue = defaultValue, Note = note };
        }

        internal static List<PinpadFieldSpec> GetPinpadCommandFieldSpecs(PinpadCommandCode commandCode)
        {
            switch (commandCode)
            {
                case PinpadCommandCode.PINPAD_CMD_INIT:
                    // PRD_핀패드.md §7.3.1: Data 없음.
                    return new List<PinpadFieldSpec>();

                case PinpadCommandCode.PINPAD_CMD_PIN_PASSWORD:
                    // Data = MaxPinLength(1), 정확히 1byte
                    // (PinpadPinCommands.h/.cpp, PRD_핀패드.md §7.3.2).
                    return new List<PinpadFieldSpec>
                    {
                        DecimalByte("MaxPinLength(1) — 최대 PIN 자릿수, 기본 6", "6"),
                    };

                case PinpadCommandCode.PINPAD_CMD_PIN_NUMBER:
                    // Data 구조는 PASSWORD와 동일 — 내부 KIND(0x02)만 다르다 (PRD_핀패드.md §7.3.3).
                    return new List<PinpadFieldSpec>
                    {
                        DecimalByte("MaxPinLength(1) — 최대 PIN 자릿수, 기본 6", "6"),
                    };

                case PinpadCommandCode.PINPAD_CMD_PIN_DES:
                    // Data = MaxPinLength(1) + WorkingKey(8) + ACN(8), 정확히 17byte (PRD_핀패드.md §7.3.4).
                    // TMKID는 POS 입력 필드가 아니다 - DLL이 항상 0x00 고정으로 싣는다
                    // (2026-08-07 확정, PinpadPinCommands.cpp 참조).
                    return new List<PinpadFieldSpec>
                    {
                        DecimalByte("MaxPinLength(1) — 최대 PIN 자릿수, 기본 6", "6"),
                        HexBinaryField("WorkingKey(8) — hex 16자리", 8, "0000000000000000"),
                        HexBinaryField("ACN(8) — hex 16자리", 8, "0000000000000000"),
                    };

                case PinpadCommandCode.PINPAD_CMD_PIN_SEED:
                    // Data = MaxPinLength(1) + RNUM(12), 정확히 13byte (PRD_핀패드.md §7.3.5).
                    // MaxPinLength는 6 초과 시 DLL이 READER_ERR_INVALID_ARGUMENT로 거부한다(SPEC case 6 상한).
                    return new List<PinpadFieldSpec>
                    {
                        DecimalByte("MaxPinLength(1) — 최대 6", "6",
                            "SPEC case 6 상한: 6 초과 입력은 DLL이 READER_ERR_INVALID_ARGUMENT로 거부"),
                        HexBinaryField("RNUM(12) — hex 24자리", 12, "000000000000000000000000"),
                    };

                default:
                    return new List<PinpadFieldSpec>();
            }
        }

        internal static List<PinpadCommandCode> GetAllPinpadCommandCodes()
        {
            return new List<PinpadCommandCode>
            {
                PinpadCommandCode.PINPAD_CMD_INIT,
                PinpadCommandCode.PINPAD_CMD_PIN_PASSWORD,
                PinpadCommandCode.PINPAD_CMD_PIN_NUMBER,
                PinpadCommandCode.PINPAD_CMD_PIN_DES,
                PinpadCommandCode.PINPAD_CMD_PIN_SEED,
            };
        }
    }

    internal static class PinpadCommandNames
    {
        internal static string KoreanName(PinpadCommandCode code)
        {
            switch (code)
            {
                case PinpadCommandCode.PINPAD_CMD_INIT: return "핀패드 초기화";
                case PinpadCommandCode.PINPAD_CMD_PIN_PASSWORD: return "PIN 비밀번호 입력";
                case PinpadCommandCode.PINPAD_CMD_PIN_NUMBER: return "PIN 번호 입력";
                case PinpadCommandCode.PINPAD_CMD_PIN_DES: return "PIN DES 암호화 입력";
                case PinpadCommandCode.PINPAD_CMD_PIN_SEED: return "PIN SEED 암호화 입력";
                default: return "알 수 없는 핀패드 명령";
            }
        }

        internal static string DisplayName(PinpadCommandCode code)
        {
            return $"{KoreanName(code)}(0x{(int)code:X2})";
        }
    }
}
