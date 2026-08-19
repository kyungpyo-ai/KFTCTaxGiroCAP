namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>SendInitCommandAsync의 결과. ResponseCode는 Success/BusinessFailure일 때만 채워진다.
    /// Kind는 ReaderCommandOutcomeKind(Services/Reader/ReaderCommandOutcomeKind.cs, Phase 10에서
    /// 명령 4종 공통으로 일반화됨 — 과거 이름 "InitOutcomeKind")를 그대로 쓴다.</summary>
    internal sealed class InitCommandOutcome
    {
        internal ReaderCommandOutcomeKind Kind { get; }
        internal string ResponseCode { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }
        internal string Detail { get; }

        /// <summary>P10-6: 호출자가 "전문 응답코드 실패" vs "DLL 연동 실패"를 이 값 하나로 분기할 수
        /// 있다.</summary>
        internal ReaderFailureCategory FailureCategory => Kind.ToFailureCategory();

        private InitCommandOutcome(ReaderCommandOutcomeKind kind, string responseCode, int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            ResponseCode = responseCode;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        internal static InitCommandOutcome Success(string responseCode) =>
            new InitCommandOutcome(ReaderCommandOutcomeKind.Success, responseCode, 0, string.Empty, string.Empty);

        internal static InitCommandOutcome BusinessFailure(string responseCode) =>
            new InitCommandOutcome(ReaderCommandOutcomeKind.BusinessFailure, responseCode, 0, string.Empty, string.Empty);

        internal static InitCommandOutcome DllCallFailure(int dllResult, string dllResultName, string detail) =>
            new InitCommandOutcome(ReaderCommandOutcomeKind.DllCallFailure, string.Empty, dllResult, dllResultName, detail);

        internal static InitCommandOutcome Timeout() =>
            new InitCommandOutcome(ReaderCommandOutcomeKind.Timeout, string.Empty, 0, string.Empty, "응답 대기 시간 초과");

        internal static InitCommandOutcome CommunicationError(string detail) =>
            new InitCommandOutcome(ReaderCommandOutcomeKind.CommunicationError, string.Empty, 0, string.Empty, detail);
    }
}
