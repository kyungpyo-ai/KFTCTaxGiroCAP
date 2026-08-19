using System;
using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>FIXED 필드의 패딩 방향(vendor/ReaderSerial/CSharpSample/CommandFieldSpecs.cs의
    /// FieldPad와 동일한 개념).</summary>
    internal enum ReaderFieldPad
    {
        LeftZero,   // 숫자 필드: 왼쪽 '0' 패딩 (SPEC 명시)
        RightSpace, // 메시지류 필드: 오른쪽 Space 패딩 (SPEC 명시)
    }

    /// <summary>
    /// 0x2B 요청 필드를 SPEC Data 필드 byte로 변환하는 헬퍼.
    /// vendor/ReaderSerial/CSharpSample/FieldEncoding.cs(EncodeCp949/PadFixedFieldBytes/
    /// BuildLengthPrefixedFieldBytes)를 그대로 포팅한 것이다 — 새로 설계하지 않는다
    /// (development_plan.md "참조 구현이 있으면 새로 설계하지 않는다").
    /// </summary>
    internal static class ReaderFieldEncoding
    {
        // CP949(완성형)로 인코딩한다. ReaderSerial.dll(JohabConverter)이 메시지 필드 조합형 변환의
        // 입력을 CP949 byte로 가정하므로, .NET 기본 인코딩을 쓰면 한글이 깨진 채로 전송된다 —
        // 메시지 필드는 완성형 그대로 넘기고(이중 변환 금지, development_plan.md P10-2), 조합형
        // 변환 자체는 DLL(MessageFieldTransform/JohabConverter)이 담당한다.
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949);

        internal static byte[] EncodeCp949(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<byte>();

            return Cp949.GetBytes(text);
        }

        // FIXED 필드: widthBytes로 자르거나 pad로 채운다. width는 문자 개수가 아니라 CP949 인코딩
        // byte 개수 기준이다(한글 1글자=2byte).
        internal static byte[] PadFixedFieldBytes(string text, int widthBytes, ReaderFieldPad pad)
        {
            byte[] encoded = EncodeCp949(text);
            byte[] result = new byte[widthBytes];

            if (encoded.Length >= widthBytes)
            {
                int srcOffset = (pad == ReaderFieldPad.LeftZero) ? (encoded.Length - widthBytes) : 0;
                Array.Copy(encoded, srcOffset, result, 0, widthBytes);
                return result;
            }

            int padCount = widthBytes - encoded.Length;
            byte padByte = (pad == ReaderFieldPad.LeftZero) ? (byte)'0' : (byte)' ';
            if (pad == ReaderFieldPad.LeftZero)
            {
                for (int i = 0; i < padCount; ++i)
                    result[i] = padByte;
                Array.Copy(encoded, 0, result, padCount, encoded.Length);
            }
            else
            {
                Array.Copy(encoded, 0, result, 0, encoded.Length);
                for (int i = encoded.Length; i < widthBytes; ++i)
                    result[i] = padByte;
            }

            return result;
        }

        // LENGTH_PREFIXED 필드: payload를 CP949로 인코딩한 뒤 그 byte 길이를 prefixWidth자리
        // 숫자('0' 왼쪽 패딩)로 앞에 붙인다. 표현 가능한 최댓값을 넘으면 payload byte를 자른다.
        internal static byte[] BuildLengthPrefixedFieldBytes(string payload, int prefixWidth)
        {
            byte[] payloadBytes = EncodeCp949(payload);

            int maxLen = 1;
            for (int i = 0; i < prefixWidth; ++i)
                maxLen *= 10;
            maxLen -= 1;

            int payloadLen = payloadBytes.Length;
            if (payloadLen > maxLen)
                payloadLen = maxLen;

            string prefix = payloadLen.ToString().PadLeft(prefixWidth, '0');
            if (prefix.Length > prefixWidth)
                prefix = prefix.Substring(prefix.Length - prefixWidth);

            byte[] result = new byte[prefixWidth + payloadLen];
            for (int i = 0; i < prefixWidth; ++i)
                result[i] = (byte)prefix[i];
            Array.Copy(payloadBytes, 0, result, prefixWidth, payloadLen);
            return result;
        }
    }
}
