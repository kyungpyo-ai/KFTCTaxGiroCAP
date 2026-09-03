using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.KeyDownload
{
    /// <summary>
    /// 서버 구간 ISO 응답 전문(0110/0130) 파싱 결과. ParseFailed는 데이터가 SPEC 형식(길이,
    /// "ISO" 개시문자, 전문 TYPE)에 못 미칠 때만 true다 — 서버에서 오는 데이터는 언제든 예상과
    /// 다를 수 있으므로 예외 대신 결과 값으로 표현한다(Protocol/Reader/InitResponseParser 등과
    /// 동일한 관례).
    /// </summary>
    internal readonly struct IsoKeyDownloadResponseResult
    {
        internal bool ParseFailed { get; }

        /// <summary>0110이면 P-28(AN 610), 0130이면 P-29(AN 146).</summary>
        internal string Payload { get; }

        /// <summary>응답코드(P-39, AN 2).</summary>
        internal string ResponseCode { get; }

        internal bool IsSuccess => !ParseFailed && ResponseCode == "00";

        private IsoKeyDownloadResponseResult(bool parseFailed, string payload, string responseCode)
        {
            ParseFailed = parseFailed;
            Payload = payload;
            ResponseCode = responseCode;
        }

        internal static IsoKeyDownloadResponseResult Failed() =>
            new IsoKeyDownloadResponseResult(true, string.Empty, string.Empty);

        internal static IsoKeyDownloadResponseResult Of(string payload, string responseCode) =>
            new IsoKeyDownloadResponseResult(false, payload, responseCode);
    }

    /// <summary>
    /// 0110(Key Download 응답)/0130(Key Bundling 응답) 파서. `PRD.md` §3.5 표대로 헤더부(48) +
    /// P-28/P-29 + P-39(2)로 구성된다. **절단 전에 "ISO" 개시문자와 전문 TYPE을 먼저 검증**한다
    /// (`development_plan.md` P24-1 지시) — 둘 중 하나라도 어긋나면 예외를 던지지 않고
    /// <see cref="IsoKeyDownloadResponseResult.Failed"/>를 돌려준다. 인코딩은 ASCII(§3.5 지시대로
    /// CP949 변환 없음).
    /// </summary>
    internal static class IsoKeyDownloadResponseParser
    {
        // 헤더부(48) = TEXT 개시문자(3) + 전문 HEADER(9) + 전문 TYPE(4) + PRIMARY BITMAP(16) +
        // 전문전송일시(10) + 전문추적번호(6).
        private const int HeaderLength = 48;
        private const int TextStartMarkerOffset = 0;
        private const int TextStartMarkerLength = 3;
        private const int MessageTypeOffset = 12;
        private const int MessageTypeLength = 4;
        private const int PrimaryBitmapOffset = 16;
        private const int PrimaryBitmapLength = 16;
        private const int ResponseCodeLength = 2;

        /// <summary>P-28(AN 610) — 0110 응답 전문 전체 길이(byte) — 48 + 610 + 2 = 660.</summary>
        internal const int Response0110PayloadLength = 610;
        internal const int Response0110Length = HeaderLength + Response0110PayloadLength + ResponseCodeLength;

        /// <summary>P-29(AN 146) — 0130 응답 전문 전체 길이(byte) — 48 + 146 + 2 = 196.</summary>
        internal const int Response0130PayloadLength = 146;
        internal const int Response0130Length = HeaderLength + Response0130PayloadLength + ResponseCodeLength;

        /// <summary>Key Download 응답(0110) 파싱.</summary>
        internal static IsoKeyDownloadResponseResult ParseResponse0110(byte[] data) =>
            Parse(data, Response0110Length, IsoKeyDownloadMessageType.Response0110,
                IsoKeyDownloadMessageType.Response0110Bitmap, Response0110PayloadLength);

        /// <summary>Key Bundling 응답(0130) 파싱.</summary>
        internal static IsoKeyDownloadResponseResult ParseResponse0130(byte[] data) =>
            Parse(data, Response0130Length, IsoKeyDownloadMessageType.Response0130,
                IsoKeyDownloadMessageType.Response0130Bitmap, Response0130PayloadLength);

        private static IsoKeyDownloadResponseResult Parse(
            byte[] data, int expectedLength, string expectedMessageType, string expectedBitmap, int payloadLength)
        {
            if (data == null || data.Length != expectedLength)
                return IsoKeyDownloadResponseResult.Failed();

            // I-1(CP1 Opus 리뷰) — HASH/RND/SIGN/암호화데이터/MAC은 hex→ascii expanding이라 정상
            // 상황에선 전부 ASCII 범위(0x00~0x7F) 안이다. 서버가 비-ASCII 바이트(0x80 이상)를 보내면
            // Encoding.ASCII.GetString이 조용히 '?'(0x3F)로 치환해버려 손상을 감지할 방법이 없으므로,
            // 여기서 먼저 걸러 파싱 실패로 처리한다(예외 대신 결과 값으로 표현하는 관례 유지).
            if (ContainsNonAscii(data))
                return IsoKeyDownloadResponseResult.Failed();

            string text = Encoding.ASCII.GetString(data);

            string marker = text.Substring(TextStartMarkerOffset, TextStartMarkerLength);
            if (marker != IsoKeyDownloadMessageType.TextStartMarker)
                return IsoKeyDownloadResponseResult.Failed();

            string messageType = text.Substring(MessageTypeOffset, MessageTypeLength);
            if (messageType != expectedMessageType)
                return IsoKeyDownloadResponseResult.Failed();

            // R-8-1(Phase 24 전체 Opus 리뷰) — IsoKeyDownloadMessageType.Response0110Bitmap/
            // Response0130Bitmap 상수가 지금까지 어디서도 검증에 안 쓰이던 죽은 코드였다. "ISO"
            // 개시문자와 전문 TYPE만 검증하던 것에 PRIMARY BITMAP까지 더해 응답 판별을 한 겹 더
            // 단단하게 한다(실장비 로그로 이 값이 상수와 일치하는 것을 확인했다).
            string bitmap = text.Substring(PrimaryBitmapOffset, PrimaryBitmapLength);
            if (bitmap != expectedBitmap)
                return IsoKeyDownloadResponseResult.Failed();

            string payload = text.Substring(HeaderLength, payloadLength);
            string responseCode = text.Substring(HeaderLength + payloadLength, ResponseCodeLength);

            return IsoKeyDownloadResponseResult.Of(payload, responseCode);
        }

        private static bool ContainsNonAscii(byte[] data)
        {
            foreach (byte b in data)
            {
                if (b >= 0x80)
                    return true;
            }

            return false;
        }
    }
}
