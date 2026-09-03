using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// [75] Using Key 전송 응답 파싱 결과. "00"이 아니면 실패(§3.4/§3.6). ParseFailed는 데이터가
    /// SPEC 형식(12byte)에 못 미칠 때만 true다(Protocol/Reader/StatusResponseParser와 동일한 관례).
    /// </summary>
    internal readonly struct KeyDownloadUsingKeyResponseResult
    {
        internal bool ParseFailed { get; }
        internal string ResponseCode { get; }
        internal string ModuleId { get; }
        internal bool IsSuccess => !ParseFailed && ResponseCode == "00";

        private KeyDownloadUsingKeyResponseResult(bool parseFailed, string responseCode, string moduleId)
        {
            ParseFailed = parseFailed;
            ResponseCode = responseCode;
            ModuleId = moduleId;
        }

        internal static KeyDownloadUsingKeyResponseResult Failed() =>
            new KeyDownloadUsingKeyResponseResult(true, string.Empty, string.Empty);

        internal static KeyDownloadUsingKeyResponseResult Of(string responseCode, string moduleId) =>
            new KeyDownloadUsingKeyResponseResult(false, responseCode, moduleId);
    }

    /// <summary>[75](Using Key 전송 응답) 전문 파서. 필드 순서/길이 출처: `PRD.md` §3.4 `[75]`
    /// 표 — 응답코드(2) + 모듈ID(10) = 12byte.</summary>
    internal static class KeyDownloadUsingKeyResponseParser
    {
        private const int ResponseCodeLength = 2;
        private const int ModuleIdLength = 10;

        internal const int TotalLength = ResponseCodeLength + ModuleIdLength;

        internal static KeyDownloadUsingKeyResponseResult Parse(byte[] data)
        {
            if (data == null || data.Length < ResponseCodeLength)
                return KeyDownloadUsingKeyResponseResult.Failed();

            string responseCode0 = Encoding.ASCII.GetString(data, 0, ResponseCodeLength);
            if (responseCode0 != "00")
            {
                // SPEC 공통 규칙(docs/reader_dll/API명세서.md:304, DLL연동가이드.md:180): 응답코드가
                // "00"이 아니면 나머지 업무 필드는 생략되고 응답코드 2byte만 온다. 12byte에 못 미쳐도
                // 통신 오류가 아니라 정상적인 업무 실패 응답이다.
                return KeyDownloadUsingKeyResponseResult.Of(responseCode0, string.Empty);
            }

            if (data.Length < TotalLength)
                return KeyDownloadUsingKeyResponseResult.Failed();

            // R-6(Phase 24 전체 Opus 리뷰) — I-1(CP1 Opus 리뷰)과 동일한 비-ASCII 방어(모듈ID 구간).
            if (ContainsNonAscii(data, ResponseCodeLength, ModuleIdLength))
                return KeyDownloadUsingKeyResponseResult.Failed();

            string responseCode = Encoding.ASCII.GetString(data, 0, ResponseCodeLength);
            string moduleId = Encoding.ASCII.GetString(data, ResponseCodeLength, ModuleIdLength);

            return KeyDownloadUsingKeyResponseResult.Of(responseCode, moduleId);
        }

        private static bool ContainsNonAscii(byte[] data, int offset, int length)
        {
            for (int i = offset; i < offset + length; i++)
            {
                if (data[i] >= 0x80)
                    return true;
            }

            return false;
        }
    }
}
