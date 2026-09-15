using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>SendIntegrityCommandAsync(0x62->0x72)의 결과.</summary>
    internal sealed class IntegrityCommandOutcome
    {
        internal ReaderCommandOutcomeKind Kind { get; }
        internal string ResponseCode { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }
        internal string Detail { get; }

        internal ReaderFailureCategory FailureCategory => Kind.ToFailureCategory();

        private IntegrityCommandOutcome(ReaderCommandOutcomeKind kind, string responseCode, int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            ResponseCode = responseCode;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        internal static IntegrityCommandOutcome Success(string responseCode) =>
            new IntegrityCommandOutcome(ReaderCommandOutcomeKind.Success, responseCode, 0, string.Empty, string.Empty);

        internal static IntegrityCommandOutcome BusinessFailure(string responseCode) =>
            new IntegrityCommandOutcome(ReaderCommandOutcomeKind.BusinessFailure, responseCode, 0, string.Empty, string.Empty);

        internal static IntegrityCommandOutcome DllCallFailure(int dllResult, string dllResultName, string detail) =>
            new IntegrityCommandOutcome(ReaderCommandOutcomeKind.DllCallFailure, string.Empty, dllResult, dllResultName, detail);

        /// <summary>2026-09-15 사용자 요청 — DLL 직접 보고 타임아웃 vs 앱 로컬 포기를 <paramref name="detail"/>로
        /// 구분한다(<see cref="InitCommandOutcome.Timeout"/> 주석 참고).</summary>
        internal static IntegrityCommandOutcome Timeout(string detail) =>
            new IntegrityCommandOutcome(ReaderCommandOutcomeKind.Timeout, string.Empty, 0, string.Empty, detail);

        internal static IntegrityCommandOutcome CommunicationError(string detail) =>
            new IntegrityCommandOutcome(ReaderCommandOutcomeKind.CommunicationError, string.Empty, 0, string.Empty, detail);

        internal static IntegrityCommandOutcome FromParsed(IntegrityResponseResult parsed)
        {
            if (parsed.ParseFailed)
                return CommunicationError("0x72 응답 데이터 길이 부족(2byte 미만)");

            return parsed.IsSuccess ? Success(parsed.ResponseCode) : BusinessFailure(parsed.ResponseCode);
        }
    }
}
