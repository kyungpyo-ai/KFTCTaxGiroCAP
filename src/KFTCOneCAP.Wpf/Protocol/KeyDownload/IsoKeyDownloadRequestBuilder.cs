using System;
using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.KeyDownload
{
    /// <summary>
    /// 서버 구간 ISO 요청 전문(0100/0120) 조립기. `PRD.md` §3.5 표를 그대로 이어붙인다 — TEXT
    /// 개시문자(3) + 전문 HEADER(9) + 전문 TYPE(4) + PRIMARY BITMAP(16) + 전문전송일시(10) +
    /// 전문추적번호(6) + P-28/P-29. 인코딩은 ASCII(전 필드 숫자/영문, 한글 없음, §3.5 지시대로
    /// CP949 변환을 끌어들이지 않는다).
    ///
    /// P-28/P-29 payload(키버전+모듈ID, HASH+RND+SIGN 등)는 리더기 응답을 그대로 잘라 붙이는 값
    /// 이라 이 계층에서 다시 패딩하지 않는다(`PRD.md` §3.3 — "받은 바이트열을 잘라 다른 쪽 전문에
    /// 붙이는 것뿐"). 길이가 SPEC과 다르면 상위 계층(P24-3)의 조립 실수이므로 예외로 알린다 —
    /// 이 값은 하드웨어에서 오는 응답이 아니라 호출자가 스스로 조립한 값이라, 파서의 "예외를
    /// 던지지 않는다" 관례(Protocol/Reader/*Parser)가 여기에는 적용되지 않는다.
    /// </summary>
    internal static class IsoKeyDownloadRequestBuilder
    {
        /// <summary>P-28(AN 12) — 키버전(2) + 모듈ID(10).</summary>
        internal const int Request0100PayloadLength = 12;

        /// <summary>P-29(AN 524) — 키버전(2) + 모듈ID(10) + 암호화데이터(512).</summary>
        internal const int Request0120PayloadLength = 524;

        /// <summary>0100 전문 전체 길이(byte) — 3+9+4+16+10+6+12 = 60.</summary>
        internal const int Request0100Length = 60;

        /// <summary>0120 전문 전체 길이(byte) — 3+9+4+16+10+6+524 = 572.</summary>
        internal const int Request0120Length = 572;

        /// <summary>Key Download 요청(0100) 조립. <paramref name="p28"/>은 정확히 12문자(AN 12)여야
        /// 한다.</summary>
        internal static byte[] BuildRequest0100(DateTime timestamp, string p28)
        {
            RequirePayloadLength(p28, Request0100PayloadLength, nameof(p28));
            return Build(IsoKeyDownloadMessageType.Request0100, IsoKeyDownloadMessageType.Request0100Bitmap, timestamp, p28);
        }

        /// <summary>Key Bundling 요청(0120) 조립. <paramref name="p29"/>은 정확히 524문자(AN 524)여야
        /// 한다.</summary>
        internal static byte[] BuildRequest0120(DateTime timestamp, string p29)
        {
            RequirePayloadLength(p29, Request0120PayloadLength, nameof(p29));
            return Build(IsoKeyDownloadMessageType.Request0120, IsoKeyDownloadMessageType.Request0120Bitmap, timestamp, p29);
        }

        private static byte[] Build(string messageType, string bitmap, DateTime timestamp, string payload)
        {
            IsoMessageStamp stamp = IsoMessageStamp.Create(timestamp);

            var text = new StringBuilder(IsoKeyDownloadMessageType.TextStartMarker.Length
                + IsoKeyDownloadMessageType.Header.Length
                + messageType.Length
                + bitmap.Length
                + stamp.TransmissionDateTime.Length
                + stamp.TraceNumber.Length
                + payload.Length);

            text.Append(IsoKeyDownloadMessageType.TextStartMarker);
            text.Append(IsoKeyDownloadMessageType.Header);
            text.Append(messageType);
            text.Append(bitmap);
            text.Append(stamp.TransmissionDateTime);
            text.Append(stamp.TraceNumber);
            text.Append(payload);

            return Encoding.ASCII.GetBytes(text.ToString());
        }

        private static void RequirePayloadLength(string payload, int expectedLength, string paramName)
        {
            if (payload == null || payload.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"payload length must be {expectedLength}, actual {(payload == null ? "null" : payload.Length.ToString())}.",
                    paramName);
            }
        }
    }
}
