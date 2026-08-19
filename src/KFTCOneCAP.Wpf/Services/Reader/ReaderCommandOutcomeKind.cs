namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// 리더기 명령 1건(0x60/0x61/0x62/0x2B 공통)의 결과 종류. Phase 9(파일럿)에서는
    /// "InitOutcomeKind"라는 이름으로 초기화 명령 전용으로 정의됐으나, Phase 10(P10-6)에서 명령
    /// 4종 전체가 같은 결과 분류를 공유하도록 이름을 일반화했다(값의 의미는 그대로).
    /// </summary>
    internal enum ReaderCommandOutcomeKind
    {
        /// <summary>응답코드가 성공 값 — 리더기 업무 레벨 성공.</summary>
        Success,

        /// <summary>리더기가 정상 응답했지만 응답코드가 성공 값이 아님 — 업무적으로 실패.</summary>
        BusinessFailure,

        /// <summary>Reader_SendCommand(SendCommandSafe 경유) 자체가 실패(포트 미오픈/BUSY 등) —
        /// DLL 연동 레벨 실패.</summary>
        DllCallFailure,

        /// <summary>READER_EVENT_TIMEOUT — 응답 없이 시간 초과. DLL 연동 레벨 실패로 분류한다.</summary>
        Timeout,

        /// <summary>READER_EVENT_LRC_ERROR/RECEIVE_ERROR/FRAME_STALL 등 통신 오류로 응답을 받지
        /// 못함. DLL 연동 레벨 실패로 분류한다.</summary>
        CommunicationError,
    }

    /// <summary>
    /// P10-6 "실패 원인 구분" — PRD §4.6/§4.7·§6.6이 요구하는 "전문 응답코드 실패"와 "DLL 연동
    /// 실패"의 타입 수준 구분. 호출자(결제 Flow/설정 화면)는 이 값 하나만 보고 분기하면 되고,
    /// Kind별로 매번 switch를 반복하지 않아도 된다.
    /// </summary>
    internal enum ReaderFailureCategory
    {
        /// <summary>실패가 아님(Success).</summary>
        None,

        /// <summary>전문 응답코드에 의한 실패 — 리더기가 정상 응답했지만 업무적으로 실패
        /// (PRD §4.6/§6.6).</summary>
        ResponseCodeFailure,

        /// <summary>DLL 연동 실패 — 호출 자체 실패/포트 오류/타임아웃/통신 오류(PRD §4.7/§6.6).</summary>
        DllFailure,
    }

    internal static class ReaderCommandOutcomeKindExtensions
    {
        internal static ReaderFailureCategory ToFailureCategory(this ReaderCommandOutcomeKind kind) => kind switch
        {
            ReaderCommandOutcomeKind.Success => ReaderFailureCategory.None,
            ReaderCommandOutcomeKind.BusinessFailure => ReaderFailureCategory.ResponseCodeFailure,
            ReaderCommandOutcomeKind.DllCallFailure => ReaderFailureCategory.DllFailure,
            ReaderCommandOutcomeKind.Timeout => ReaderFailureCategory.DllFailure,
            ReaderCommandOutcomeKind.CommunicationError => ReaderFailureCategory.DllFailure,
            _ => ReaderFailureCategory.DllFailure,
        };
    }
}
