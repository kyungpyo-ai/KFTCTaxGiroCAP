using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// [74] 키 다운로드 상호 인증 응답 파싱 결과. "00"이 아니면 실패(§3.4/§3.6). ParseFailed는
    /// 데이터가 SPEC 형식(558byte)에 못 미칠 때만 true다(Protocol/Reader/StatusResponseParser와
    /// 동일한 관례). EncryptedData는 512byte 원본을 그대로 문자열로 옮긴 것뿐이다 — 이 구조체가
    /// 반환된 뒤 호출자(ReaderService)가 원본 raw byte[]를 Array.Clear로 지운다
    /// (development_plan.md P24-2 메모리 클리어 요구사항, 이 파서 자체는 원본 배열을 소유하지
    /// 않으므로 지우는 책임이 없다).
    /// </summary>
    internal readonly struct KeyDownloadAuthResponseResult
    {
        internal bool ParseFailed { get; }
        internal string ResponseCode { get; }
        internal string KeyVersion { get; }
        internal string ReaderName { get; }
        internal string ReaderVersion { get; }
        internal string ModuleId { get; }
        internal string EncryptedData { get; }
        internal bool IsSuccess => !ParseFailed && ResponseCode == "00";

        private KeyDownloadAuthResponseResult(bool parseFailed, string responseCode, string keyVersion,
            string readerName, string readerVersion, string moduleId, string encryptedData)
        {
            ParseFailed = parseFailed;
            ResponseCode = responseCode;
            KeyVersion = keyVersion;
            ReaderName = readerName;
            ReaderVersion = readerVersion;
            ModuleId = moduleId;
            EncryptedData = encryptedData;
        }

        internal static KeyDownloadAuthResponseResult Failed() =>
            new KeyDownloadAuthResponseResult(true, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        internal static KeyDownloadAuthResponseResult Of(string responseCode, string keyVersion, string readerName,
            string readerVersion, string moduleId, string encryptedData) =>
            new KeyDownloadAuthResponseResult(false, responseCode, keyVersion, readerName, readerVersion, moduleId, encryptedData);
    }

    /// <summary>[74](키 다운로드 상호 인증 응답) 전문 파서. 필드 순서/길이 출처: `PRD.md` §3.4
    /// `[74]` 표 — 응답코드(2) + 키버전(2) + 리더기이름(16) + 리더기버전(16) + 모듈ID(10) +
    /// 암호화 데이터(512) = 558byte.</summary>
    internal static class KeyDownloadAuthResponseParser
    {
        private const int ResponseCodeLength = 2;
        private const int KeyVersionLength = 2;
        private const int ReaderNameLength = 16;
        private const int ReaderVersionLength = 16;
        private const int ModuleIdLength = 10;
        private const int EncryptedDataLength = 512;

        internal const int TotalLength = ResponseCodeLength + KeyVersionLength + ReaderNameLength
            + ReaderVersionLength + ModuleIdLength + EncryptedDataLength;

        internal static KeyDownloadAuthResponseResult Parse(byte[] data)
        {
            if (data == null || data.Length < ResponseCodeLength)
                return KeyDownloadAuthResponseResult.Failed();

            string responseCode0 = Encoding.ASCII.GetString(data, 0, ResponseCodeLength);
            if (responseCode0 != "00")
            {
                // SPEC 공통 규칙(docs/reader_dll/API명세서.md:304, DLL연동가이드.md:180): 응답코드가
                // "00"이 아니면 나머지 업무 필드는 생략되고 응답코드 2byte만 온다. 558byte에 못 미쳐도
                // 통신 오류가 아니라 정상적인 업무 실패 응답이다.
                return KeyDownloadAuthResponseResult.Of(responseCode0, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            }

            if (data.Length < TotalLength)
                return KeyDownloadAuthResponseResult.Failed();

            // I-1(CP1 Opus 리뷰) + 개선권장 #3(Phase 24 2차 Opus 리뷰) — 처음엔 암호화 데이터
            // (512byte) 구간만 비-ASCII를 검사했는데, 그 앞의 키버전/리더기이름/리더기버전/모듈ID
            // (44byte) 구간은 빠져 있었다([73]/[75] 파서는 응답코드를 제외한 나머지 전체를 검사하는
            // 것과 비대칭이었다). 응답코드(2byte)를 제외한 나머지 전체(키버전+리더기이름+리더기버전+
            // 모듈ID+암호화데이터 = 556byte)로 검사 범위를 넓혀 다른 두 파서와 일관되게 맞춘다.
            if (ContainsNonAscii(data, ResponseCodeLength, TotalLength - ResponseCodeLength))
                return KeyDownloadAuthResponseResult.Failed();

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
            offset += ModuleIdLength;

            string encryptedData = Encoding.ASCII.GetString(data, offset, EncryptedDataLength);

            return KeyDownloadAuthResponseResult.Of(responseCode, keyVersion, readerName, readerVersion, moduleId, encryptedData);
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
