using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>SendKeyDownloadUsingKeyCommandAsync([65]-&gt;[75])의 결과.</summary>
    internal sealed class KeyDownloadUsingKeyCommandOutcome
    {
        internal ReaderCommandOutcomeKind Kind { get; }
        internal string ResponseCode { get; }
        internal string ModuleId { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }
        internal string Detail { get; }

        internal ReaderFailureCategory FailureCategory => Kind.ToFailureCategory();

        private KeyDownloadUsingKeyCommandOutcome(ReaderCommandOutcomeKind kind, string responseCode, string moduleId,
            int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            ResponseCode = responseCode;
            ModuleId = moduleId;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        internal static KeyDownloadUsingKeyCommandOutcome Success(string responseCode, string moduleId) =>
            new KeyDownloadUsingKeyCommandOutcome(ReaderCommandOutcomeKind.Success, responseCode, moduleId, 0, string.Empty, string.Empty);

        internal static KeyDownloadUsingKeyCommandOutcome BusinessFailure(string responseCode, string moduleId) =>
            new KeyDownloadUsingKeyCommandOutcome(ReaderCommandOutcomeKind.BusinessFailure, responseCode, moduleId, 0, string.Empty, string.Empty);

        internal static KeyDownloadUsingKeyCommandOutcome DllCallFailure(int dllResult, string dllResultName, string detail) =>
            new KeyDownloadUsingKeyCommandOutcome(ReaderCommandOutcomeKind.DllCallFailure, string.Empty, string.Empty, dllResult, dllResultName, detail);

        internal static KeyDownloadUsingKeyCommandOutcome Timeout() =>
            new KeyDownloadUsingKeyCommandOutcome(ReaderCommandOutcomeKind.Timeout, string.Empty, string.Empty, 0, string.Empty, "응답 대기 시간 초과");

        internal static KeyDownloadUsingKeyCommandOutcome CommunicationError(string detail) =>
            new KeyDownloadUsingKeyCommandOutcome(ReaderCommandOutcomeKind.CommunicationError, string.Empty, string.Empty, 0, string.Empty, detail);

        internal static KeyDownloadUsingKeyCommandOutcome FromParsed(KeyDownloadUsingKeyResponseResult parsed)
        {
            // R-6/R-7(Phase 24 전체 Opus 리뷰) — KeyDownloadUsingKeyResponseParser에 비-ASCII 감지(I-1)가
            // 추가되면서 ParseFailed가 길이 부족 외에 비-ASCII 데이터 포함으로도 발생할 수 있다.
            if (parsed.ParseFailed)
                return CommunicationError("[75] 응답 데이터 길이 부족(12byte 미만) 또는 비-ASCII 데이터 포함");

            return parsed.IsSuccess ? Success(parsed.ResponseCode, parsed.ModuleId) : BusinessFailure(parsed.ResponseCode, parsed.ModuleId);
        }
    }
}
