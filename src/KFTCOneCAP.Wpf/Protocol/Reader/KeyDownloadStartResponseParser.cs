using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// [73] 키 다운로드 시작 응답 파싱 결과. "00"이 아니면 실패(§3.4/§3.6). ParseFailed는 데이터가
    /// SPEC 형식(46byte)에 못 미칠 때만 true다 — 하드웨어 데이터는 언제든 깨질 수 있으므로 예외
    /// 대신 결과 값으로 표현한다(Protocol/Reader/StatusResponseParser와 동일한 관례).
    /// </summary>
    internal readonly struct KeyDownloadStartResponseResult
    {
        internal bool ParseFailed { get; }
        internal string ResponseCode { get; }
        internal string KeyVersion { get; }
        internal string ReaderName { get; }
        internal string ReaderVersion { get; }
        internal string ModuleId { get; }
        internal bool IsSuccess => !ParseFailed && ResponseCode == "00";

        private KeyDownloadStartResponseResult(bool parseFailed, string responseCode, string keyVersion,
            string readerName, string readerVersion, string moduleId)
        {
            ParseFailed = parseFailed;
            ResponseCode = responseCode;
            KeyVersion = keyVersion;
            ReaderName = readerName;
            ReaderVersion = readerVersion;
            ModuleId = moduleId;
        }

        internal static KeyDownloadStartResponseResult Failed() =>
            new KeyDownloadStartResponseResult(true, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        internal static KeyDownloadStartResponseResult Of(string responseCode, string keyVersion, string readerName,
            string readerVersion, string moduleId) =>
            new KeyDownloadStartResponseResult(false, responseCode, keyVersion, readerName, readerVersion, moduleId);
    }

    /// <summary>[73](키 다운로드 시작 응답) 전문 파서. 필드 순서/길이 출처: `PRD.md` §3.4 `[73]`
    /// 표 — 응답코드(2) + 키버전(2) + 리더기이름(16) + 리더기버전(16) + 모듈ID(10) = 46byte.</summary>
    internal static class KeyDownloadStartResponseParser
    {
        private const int ResponseCodeLength = 2;
        private const int KeyVersionLength = 2;
        private const int ReaderNameLength = 16;
        private const int ReaderVersionLength = 16;
        private const int ModuleIdLength = 10;

        internal const int TotalLength =
            ResponseCodeLength + KeyVersionLength + ReaderNameLength + ReaderVersionLength + ModuleIdLength;

        internal static KeyDownloadStartResponseResult Parse(byte[] data)
        {
            if (data == null || data.Length < ResponseCodeLength)
                return KeyDownloadStartResponseResult.Failed();

            string responseCode0 = Encoding.ASCII.GetString(data, 0, ResponseCodeLength);
            if (responseCode0 != "00")
            {
                // SPEC 공통 규칙(docs/reader_dll/API명세서.md:304, DLL연동가이드.md:180): 응답코드가
                // "00"이 아니면 나머지 업무 필드는 생략되고 응답코드 2byte만 온다. 46byte에 못 미쳐도
                // 통신 오류가 아니라 정상적인 업무 실패 응답이다.
                return KeyDownloadStartResponseResult.Of(responseCode0, string.Empty, string.Empty, string.Empty, string.Empty);
            }

            if (data.Length < TotalLength)
                return KeyDownloadStartResponseResult.Failed();

            // R-6(Phase 24 전체 Opus 리뷰) — I-1(CP1 Opus 리뷰)과 동일한 비-ASCII 방어. 키버전/
            // 리더기이름/리더기버전/모듈ID는 정상 상황에선 전부 ASCII 범위(0x00~0x7F) 안이다. 리더기가
            // 비-ASCII 바이트(0x80 이상)를 보내면 Encoding.ASCII.GetString이 조용히 '?'(0x3F)로
            // 치환해버려 손상을 감지할 방법이 없으므로, 여기서 먼저 걸러 파싱 실패로 처리한다
            // (KeyDownloadAuthResponseParser.ContainsNonAscii와 동일 패턴).
            if (ContainsNonAscii(data, ResponseCodeLength, TotalLength - ResponseCodeLength))
                return KeyDownloadStartResponseResult.Failed();

            int offset = 0;
            string responseCode = Encoding.ASCII.GetString(data, offset, ResponseCodeLength);
            offset += ResponseCodeLength;
            string keyVersion = Encoding.ASCII.GetString(data, offset, KeyVersionLength);
            offset += KeyVersionLength;
            string readerName = Encoding.ASCII.GetString(data, offset, ReaderNameLength);
            offset += ReaderNameLength;
            string readerVersion = Encoding.ASCII.GetString(data, offset, ReaderVersionLength);
            offset += ReaderVersionLength;
            string moduleId = Encoding.ASCII.GetString(data, offset, ModuleIdLength);

            return KeyDownloadStartResponseResult.Of(responseCode, keyVersion, readerName, readerVersion, moduleId);
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
