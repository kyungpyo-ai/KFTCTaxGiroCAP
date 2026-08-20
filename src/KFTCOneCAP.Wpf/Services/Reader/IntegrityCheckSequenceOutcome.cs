namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 12(docs/payment_relay/development_plan.md P12-4) — 무결성 체크 2단계 시퀀스
    /// (0x61→0x71 상태체크 → 0x62→0x72 무결성)의 최종 결과. 호출자(리더기 설정 화면/Phase 15 결제
    /// Flow)는 이 값 하나로 성공/실패, 실패 원인 구분(<see cref="FailureCategory"/>), 표시에 필요한
    /// 리더기 인증 식별번호/모듈 ID를 모두 얻는다.
    /// </summary>
    internal sealed class IntegrityCheckSequenceOutcome
    {
        internal ReaderCommandOutcomeKind Kind { get; }

        internal bool IsSuccess { get; }

        /// <summary>0x72 응답의 업무 응답코드. 1단계(0x71)에서 실패해 2단계를 시도하지 못했다면
        /// null이다.</summary>
        internal string? ResponseCode { get; }

        /// <summary>0x71 응답에서 파싱된 값(PRD §4.2/§6.2) — 1단계가 DLL 연동 레벨로 실패해 응답
        /// 자체를 못 받았으면 null.</summary>
        internal string? ReaderAuthId { get; }

        internal string? ModuleId { get; }

        internal int DllResult { get; }

        internal string DllResultName { get; }

        internal string Detail { get; }

        internal ReaderFailureCategory FailureCategory => Kind.ToFailureCategory();

        private IntegrityCheckSequenceOutcome(ReaderCommandOutcomeKind kind, bool isSuccess, string? responseCode,
            string? readerAuthId, string? moduleId, int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            IsSuccess = isSuccess;
            ResponseCode = responseCode;
            ReaderAuthId = readerAuthId;
            ModuleId = moduleId;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        /// <summary>1단계(0x61→0x71 상태체크)에서 이미 실패한 경우. <see cref="StatusCommandOutcome"/>의
        /// Kind별 빈 문자열("") 필드를 null로 정규화한다 — DLL 연동 실패/타임아웃/통신 오류일 때는
        /// 응답 자체가 없어 리더기 인증 식별번호/모듈 ID도 없기 때문이다(BusinessFailure일 때만
        /// [71] 전용 예외로 필드가 채워져 있다, Protocol/Reader/StatusResponseParser 참고).</summary>
        internal static IntegrityCheckSequenceOutcome FromStatusFailure(StatusCommandOutcome status) =>
            new IntegrityCheckSequenceOutcome(
                status.Kind,
                isSuccess: false,
                responseCode: NullIfEmpty(status.ResponseCode),
                readerAuthId: NullIfEmpty(status.ReaderAuthId),
                moduleId: NullIfEmpty(status.ModuleId),
                dllResult: status.DllResult,
                dllResultName: status.DllResultName,
                detail: status.Detail);

        /// <summary>1단계(상태체크)가 성공해 2단계(0x62→0x72 무결성)까지 진행한 경우. 리더기 인증
        /// 식별번호/모듈 ID는 0x72가 아니라 1단계(0x71) 응답에서만 얻을 수 있으므로 status에서
        /// 가져온다.</summary>
        internal static IntegrityCheckSequenceOutcome FromIntegrityOutcome(StatusCommandOutcome status, IntegrityCommandOutcome integrity) =>
            new IntegrityCheckSequenceOutcome(
                integrity.Kind,
                isSuccess: integrity.Kind == ReaderCommandOutcomeKind.Success,
                responseCode: NullIfEmpty(integrity.ResponseCode),
                readerAuthId: NullIfEmpty(status.ReaderAuthId),
                moduleId: NullIfEmpty(status.ModuleId),
                dllResult: integrity.DllResult,
                dllResultName: integrity.DllResultName,
                detail: integrity.Detail);

        private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
