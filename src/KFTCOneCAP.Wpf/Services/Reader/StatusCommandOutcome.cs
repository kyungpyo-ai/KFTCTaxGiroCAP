using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>SendStatusCommandAsync(0x61->0x71)의 결과. ReaderAuthId/ModuleId는 Success/
    /// BusinessFailure(=StatusResponseResult.ParseFailed가 아닌 모든 응답코드, StatusResponseParser
    /// 참고 — [71]은 응답코드와 무관하게 항상 두 필드가 함께 온다)일 때 채워진다.</summary>
    internal sealed class StatusCommandOutcome
    {
        internal ReaderCommandOutcomeKind Kind { get; }
        internal string ResponseCode { get; }
        internal string ReaderAuthId { get; }
        internal string ModuleId { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }
        internal string Detail { get; }

        internal ReaderFailureCategory FailureCategory => Kind.ToFailureCategory();

        private StatusCommandOutcome(ReaderCommandOutcomeKind kind, string responseCode, string readerAuthId,
            string moduleId, int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            ResponseCode = responseCode;
            ReaderAuthId = readerAuthId;
            ModuleId = moduleId;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        internal static StatusCommandOutcome Success(string responseCode, string readerAuthId, string moduleId) =>
            new StatusCommandOutcome(ReaderCommandOutcomeKind.Success, responseCode, readerAuthId, moduleId, 0, string.Empty, string.Empty);

        internal static StatusCommandOutcome BusinessFailure(string responseCode, string readerAuthId, string moduleId) =>
            new StatusCommandOutcome(ReaderCommandOutcomeKind.BusinessFailure, responseCode, readerAuthId, moduleId, 0, string.Empty, string.Empty);

        internal static StatusCommandOutcome DllCallFailure(int dllResult, string dllResultName, string detail) =>
            new StatusCommandOutcome(ReaderCommandOutcomeKind.DllCallFailure, string.Empty, string.Empty, string.Empty, dllResult, dllResultName, detail);

        internal static StatusCommandOutcome Timeout() =>
            new StatusCommandOutcome(ReaderCommandOutcomeKind.Timeout, string.Empty, string.Empty, string.Empty, 0, string.Empty, "응답 대기 시간 초과");

        internal static StatusCommandOutcome CommunicationError(string detail) =>
            new StatusCommandOutcome(ReaderCommandOutcomeKind.CommunicationError, string.Empty, string.Empty, string.Empty, 0, string.Empty, detail);

        /// <summary>Protocol/Reader/StatusResponseResult에서 그대로 매핑한다(계층 규칙 — Services는
        /// 바이트를 직접 다루지 않고 Protocol의 결과 객체만 받는다).</summary>
        internal static StatusCommandOutcome FromParsed(StatusResponseResult parsed)
        {
            if (parsed.ParseFailed)
                return CommunicationError("0x71 응답 데이터 길이 부족(28byte 미만)");

            return parsed.IsSuccess
                ? Success(parsed.ResponseCode, parsed.ReaderAuthId, parsed.ModuleId)
                : BusinessFailure(parsed.ResponseCode, parsed.ReaderAuthId, parsed.ModuleId);
        }
    }
}
