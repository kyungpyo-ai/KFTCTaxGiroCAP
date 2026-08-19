namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// SendInitCommandAsync 결과 종류. "전문 응답코드에 의한 실패"와 "DLL 연동 실패"를 구분하는
    /// 원칙(PRD §4.6/§4.7, Phase 10 P10-6에서 정식 타입으로 확장 예정)을 파일럿 범위에서 최소
    /// 형태로 먼저 반영한다 — 이번 Phase는 0x60/0x70 1종뿐이라 별도 계층을 새로 만들지 않는다.
    /// </summary>
    internal enum InitOutcomeKind
    {
        /// <summary>0x70 응답코드가 "00" — 리더기 업무 레벨 성공.</summary>
        Success,

        /// <summary>0x70 응답코드가 "00"이 아님 — 리더기가 정상 응답했지만 업무적으로 실패.</summary>
        BusinessFailure,

        /// <summary>Reader_SendCommand 자체가 실패(포트 미오픈/BUSY 등) — DLL 연동 레벨 실패.</summary>
        DllCallFailure,

        /// <summary>READER_EVENT_TIMEOUT — 응답 없이 시간 초과.</summary>
        Timeout,

        /// <summary>READER_EVENT_LRC_ERROR/RECEIVE_ERROR/FRAME_STALL 등 통신 오류로 응답을 받지 못함.</summary>
        CommunicationError,
    }

    /// <summary>SendInitCommandAsync의 결과. ResponseCode는 Success/BusinessFailure일 때만 채워진다.</summary>
    internal sealed class InitCommandOutcome
    {
        internal InitOutcomeKind Kind { get; }
        internal string ResponseCode { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }
        internal string Detail { get; }

        private InitCommandOutcome(InitOutcomeKind kind, string responseCode, int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            ResponseCode = responseCode;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        internal static InitCommandOutcome Success(string responseCode) =>
            new InitCommandOutcome(InitOutcomeKind.Success, responseCode, 0, string.Empty, string.Empty);

        internal static InitCommandOutcome BusinessFailure(string responseCode) =>
            new InitCommandOutcome(InitOutcomeKind.BusinessFailure, responseCode, 0, string.Empty, string.Empty);

        internal static InitCommandOutcome DllCallFailure(int dllResult, string dllResultName, string detail) =>
            new InitCommandOutcome(InitOutcomeKind.DllCallFailure, string.Empty, dllResult, dllResultName, detail);

        internal static InitCommandOutcome Timeout() =>
            new InitCommandOutcome(InitOutcomeKind.Timeout, string.Empty, 0, string.Empty, "응답 대기 시간 초과");

        internal static InitCommandOutcome CommunicationError(string detail) =>
            new InitCommandOutcome(InitOutcomeKind.CommunicationError, string.Empty, 0, string.Empty, detail);
    }
}
