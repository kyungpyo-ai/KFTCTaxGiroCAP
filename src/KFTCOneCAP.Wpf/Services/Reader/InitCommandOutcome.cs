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

        /// <summary>2026-09-15 사용자 요청 — DLL이 직접 보고한 타임아웃인지 앱이 로컬 타이머로
        /// 포기한 것인지(<see cref="ReaderService"/>의 두 <c>RawReaderCommandResult.Timeout</c>
        /// 호출 지점 참고) <paramref name="detail"/>로 구분해서 받는다.</summary>
        internal static InitCommandOutcome Timeout(string detail) =>
            new InitCommandOutcome(ReaderCommandOutcomeKind.Timeout, string.Empty, 0, string.Empty, detail);

        internal static InitCommandOutcome CommunicationError(string detail) =>
            new InitCommandOutcome(ReaderCommandOutcomeKind.CommunicationError, string.Empty, 0, string.Empty, detail);
    }
}
