// FieldEncoding.cs — 필드 텍스트를 SPEC Data 필드 byte로 변환하는 헬퍼.
// ReaderSerialTestUIDlg.cpp의 EncodeCp949/PadFixedFieldBytes/
// BuildLengthPrefixedFieldBytes를 그대로 포팅한 것이다.
using System;
using System.Text;

namespace ReaderSerialCSharpSample
{
    internal static class FieldEncoding
    {
        // CP949(완성형)로 인코딩한다. ReaderSerial.dll(JohabConverter)이 msg
        // 필드 조합형 변환의 입력을 CP949 byte로 가정하므로, .NET 기본
        // 인코딩(UTF-16 문자 단위 캐스팅/UTF-8)을 쓰면 한글이 깨진 채로
        // 전송된다 — 반드시 Encoding.GetEncoding(949)를 명시해야 한다.
        // (.NET Framework(net48) 타깃이므로 System.Text.Encoding.CodePages
        // NuGet 패키지 없이도 코드페이지 949를 바로 쓸 수 있다.)
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949);

        internal static byte[] EncodeCp949(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<byte>();
            }
            return Cp949.GetBytes(text);
        }

        // FIXED 필드: widthBytes로 자르거나 pad로 채운다. width는 문자 개수가
        // 아니라 CP949 인코딩 byte 개수 기준이다(한글 1글자=2byte).
        internal static byte[] PadFixedFieldBytes(string text, int widthBytes, FieldPad pad)
        {
            byte[] encoded = EncodeCp949(text);
            byte[] result = new byte[widthBytes];

            if (encoded.Length >= widthBytes)
            {
                int srcOffset = (pad == FieldPad.LEFT_ZERO) ? (encoded.Length - widthBytes) : 0;
                Array.Copy(encoded, srcOffset, result, 0, widthBytes);
                return result;
            }

            int padCount = widthBytes - encoded.Length;
            byte padByte = (pad == FieldPad.LEFT_ZERO) ? (byte)'0' : (byte)' ';
            if (pad == FieldPad.LEFT_ZERO)
            {
                for (int i = 0; i < padCount; ++i)
                {
                    result[i] = padByte;
                }
                Array.Copy(encoded, 0, result, padCount, encoded.Length);
            }
            else
            {
                Array.Copy(encoded, 0, result, 0, encoded.Length);
                for (int i = encoded.Length; i < widthBytes; ++i)
                {
                    result[i] = padByte;
                }
            }
            return result;
        }

        // LENGTH_PREFIXED 필드: payload를 CP949로 인코딩한 뒤 그 byte 길이를
        // prefixWidth자리 숫자('0' 왼쪽 패딩)로 앞에 붙인다. 표현 가능한
        // 최댓값을 넘으면 payload byte를 자른다.
        internal static byte[] BuildLengthPrefixedFieldBytes(string payload, int prefixWidth, out int usedPayloadBytes)
        {
            byte[] payloadBytes = EncodeCp949(payload);

            int maxLen = 1;
            for (int i = 0; i < prefixWidth; ++i)
            {
                maxLen *= 10;
            }
            maxLen -= 1;

            int payloadLen = payloadBytes.Length;
            if (payloadLen > maxLen)
            {
                payloadLen = maxLen;
            }
            usedPayloadBytes = payloadLen;

            string prefix = payloadLen.ToString().PadLeft(prefixWidth, '0');
            if (prefix.Length > prefixWidth)
            {
                // maxLen 계산상 이론적으로 발생하지 않지만, 방어적으로 앞에서 자른다.
                prefix = prefix.Substring(prefix.Length - prefixWidth);
            }

            byte[] result = new byte[prefixWidth + payloadLen];
            for (int i = 0; i < prefixWidth; ++i)
            {
                result[i] = (byte)prefix[i];
            }
            Array.Copy(payloadBytes, 0, result, prefixWidth, payloadLen);
            return result;
        }

        // hex 문자열(2문자=1byte)을 byteWidth byte로 변환한다. 길이가
        // byteWidth*2와 정확히 일치하지 않거나 유효하지 않은 hex 문자가
        // 하나라도 있으면 false를 반환한다(MFC HexStringToBytes와 동일한
        // 검증 강도 — 2026-08-07 실장비 테스트에서 발견된, 잘못된 hex 문자를
        // 조용히 0x00으로 치환해 전송하던 버그의 재발 방지). 호출자는 실패
        // 시 그 필드의 전송 자체를 중단해야 한다.
        internal static bool TryParseHexString(string hexText, int byteWidth, out byte[] outBytes, out string failReason)
        {
            outBytes = new byte[byteWidth];
            failReason = null;

            if (hexText == null || hexText.Length != byteWidth * 2)
            {
                failReason = $"길이가 맞지 않음(입력 {hexText?.Length ?? 0}자, 필요 {byteWidth * 2}자)";
                return false;
            }

            for (int i = 0; i < byteWidth; ++i)
            {
                if (!TryHexNibble(hexText[i * 2], out int hi) || !TryHexNibble(hexText[i * 2 + 1], out int lo))
                {
                    failReason = "잘못된 hex 문자";
                    return false;
                }
                outBytes[i] = (byte)((hi << 4) | lo);
            }
            return true;
        }

        private static bool TryHexNibble(char ch, out int value)
        {
            if (ch >= '0' && ch <= '9') { value = ch - '0'; return true; }
            if (ch >= 'a' && ch <= 'f') { value = ch - 'a' + 10; return true; }
            if (ch >= 'A' && ch <= 'F') { value = ch - 'A' + 10; return true; }
            value = 0;
            return false;
        }

        // CALLBACK data / 전송 미리보기를 raw ASCII 문자로 보여준다(hex dump
        // 아님, MFC 테스트 UI와 동일 취지). NUL(0x00)은 화면 표시용 대체
        // 기호로 바꾼다 — 그대로 두면 일부 컨트롤/서식이 null 종단으로
        // 취급해 이후 바이트가 잘려 보일 수 있다.
        internal static string BytesToDisplayAscii(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return "(none)";
            }

            StringBuilder sb = new StringBuilder(data.Length);
            foreach (byte b in data)
            {
                sb.Append(b == 0 ? '␀' : (char)b);
            }
            return sb.ToString();
        }
    }
}
