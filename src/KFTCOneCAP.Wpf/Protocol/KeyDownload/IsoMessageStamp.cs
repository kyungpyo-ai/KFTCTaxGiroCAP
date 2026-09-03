using System;
using System.Globalization;

namespace KFTCOneCAP.Wpf.Protocol.KeyDownload
{
    /// <summary>
    /// 전문전송일시(P-7, `MMDDhhmmss`)와 전문추적번호(P-11, `hhmmss`)를 하나의 `DateTime`에서
    /// 한 번에 만든 결과. `PRD.md` §3.5 "공통 필드 생성 규칙" — 전문추적번호는 같은 전문의
    /// 전문전송일시에서 `hhmmss` 6자리를 그대로 쓴다. 두 값을 각각 `DateTime.Now`로 따로 만들면
    /// 초 경계에서 어긋날 수 있으므로, 반드시 <see cref="Create"/>가 받은 단일 `DateTime` 인자
    /// 하나로만 두 값을 생성한다.
    /// </summary>
    internal readonly struct IsoMessageStamp
    {
        /// <summary>전문전송일시(N 10) — `MMDDhhmmss`.</summary>
        internal string TransmissionDateTime { get; }

        /// <summary>전문추적번호(N 6) — 전문전송일시의 `hhmmss`.</summary>
        internal string TraceNumber { get; }

        private IsoMessageStamp(string transmissionDateTime, string traceNumber)
        {
            TransmissionDateTime = transmissionDateTime;
            TraceNumber = traceNumber;
        }

        /// <summary>단일 `DateTime` 인자 하나로 전문전송일시와 전문추적번호를 동시에 만든다.</summary>
        internal static IsoMessageStamp Create(DateTime timestamp)
        {
            string transmissionDateTime = timestamp.ToString("MMddHHmmss", CultureInfo.InvariantCulture);
            string traceNumber = timestamp.ToString("HHmmss", CultureInfo.InvariantCulture);
            return new IsoMessageStamp(transmissionDateTime, traceNumber);
        }
    }
}
