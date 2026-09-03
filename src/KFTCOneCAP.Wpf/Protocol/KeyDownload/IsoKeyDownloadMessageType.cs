namespace KFTCOneCAP.Wpf.Protocol.KeyDownload
{
    /// <summary>
    /// 리더기 키다운로드 서버 구간 ISO 전문(0100/0110/0120/0130) 고정 상수. `PRD.md` §3.5의
    /// "TEXT 개시문자"/"전문 HEADER"/"전문 TYPE"/"PRIMARY BITMAP" 열을 그대로 옮긴 것이다 —
    /// 비트맵은 전문별 고정 문자열이며 원캡이 계산하지 않는다(§3.5 "공통 필드 생성 규칙").
    /// 결제 전문(Protocol/Pos/)과 형식이 완전히 다른 별도 계열이므로 이 상수들은 오직 여기서만
    /// 쓰인다.
    /// </summary>
    internal static class IsoKeyDownloadMessageType
    {
        /// <summary>TEXT 개시문자(A 3) — 4전문 공통.</summary>
        internal const string TextStartMarker = "ISO";

        /// <summary>전문 HEADER(AN 9) — 4전문 공통 고정값.</summary>
        internal const string Header = "023400052";

        /// <summary>Key Download 요청 전문 TYPE(N 4).</summary>
        internal const string Request0100 = "0100";

        /// <summary>Key Download 응답 전문 TYPE(N 4).</summary>
        internal const string Response0110 = "0110";

        /// <summary>Key Bundling 요청 전문 TYPE(N 4).</summary>
        internal const string Request0120 = "0120";

        /// <summary>Key Bundling 응답 전문 TYPE(N 4).</summary>
        internal const string Response0130 = "0130";

        /// <summary>0100 PRIMARY BITMAP(AN 16) 고정 문자열.</summary>
        internal const string Request0100Bitmap = "0220001000000000";

        /// <summary>0110 PRIMARY BITMAP(AN 16) 고정 문자열.</summary>
        internal const string Response0110Bitmap = "0220001002000000";

        /// <summary>0120 PRIMARY BITMAP(AN 16) 고정 문자열.</summary>
        internal const string Request0120Bitmap = "0220000800000000";

        /// <summary>0130 PRIMARY BITMAP(AN 16) 고정 문자열.</summary>
        internal const string Response0130Bitmap = "0220000802000000";
    }
}
