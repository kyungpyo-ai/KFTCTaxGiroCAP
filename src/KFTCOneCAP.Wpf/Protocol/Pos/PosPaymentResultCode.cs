namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-3) — 결제 Flow(<c>Services/Payment/
/// PaymentOrchestrator</c>)가 POS에 반환하는 결과 종류. PRD가 "구분해서 응답"을 요구하는 축(§4.2,
/// §4.6, §4.7, §4.8, §4.9, §4.10, §2.2.3, §6)을 열거형으로 확정한 것이며, 값 하나하나는 실제 SPEC이
/// 확정되지 않은 지금도 바뀌지 않는다 — 바뀌는 것은 <see cref="PosPaymentResponse.Create"/>가 이 값을
/// 실제 전문 코드 문자열로 바꾸는 매핑 하나뿐이다(SPEC 확정 시 그 매핑표만 교체).
///
/// Flow(<c>Services/Payment/</c>)는 이 열거형만 다루고 전문 코드 문자열("00", "10" 등)을 직접
/// 리터럴로 쓰지 않는다 — grep으로 점검 가능해야 한다(P15-3 완료 조건).
/// </summary>
internal enum PosPaymentResultCode
{
    /// <summary>승인(PRD §4.10).</summary>
    Approved,

    /// <summary>리더기가 정상 응답했지만 업무 응답코드가 00/07/12 외 — 카드 리딩 실패(PRD §4.6).</summary>
    ReaderResponseFailure,

    /// <summary>Reader DLL 호출/CALLBACK 처리 자체가 실패 — 응답코드 실패와 구분됨(PRD §4.7).</summary>
    ReaderDllFailure,

    /// <summary>참여 후보 리더기 전원이 무결성 체크에 실패 — 양쪽 다 실패했을 때만(PRD §4.2).</summary>
    IntegrityCheckFailure,

    /// <summary>설정된 리더기가 하나도 없음("미사용" 2개) — 카드 리딩을 시도하지 않음(PRD §2.2.3).</summary>
    NoReaderConfigured,

    /// <summary>리더기 설정 화면(모달)이 열려 있어 카드 리딩을 시도하지 않고 거부(2026-08-25 확정,
    /// P15-4).</summary>
    ReaderSetupInProgress,

    /// <summary>사용자 취소(PRD §4.8) — 카드 입력 대기 중에만 발생 가능.</summary>
    UserCanceled,

    /// <summary>카드 입력 대기 120초 초과(PRD §4.9).</summary>
    Timeout,

    /// <summary>VAN 서버가 거절 — DLL 통신 실패와 구분됨(PRD §4.10).</summary>
    VanDeclined,

    /// <summary>VAN DLL 통신 자체가 실패 — 서버 거절과 구분됨(PRD §4.10).</summary>
    VanCommunicationFailure,

    /// <summary>그 외 처리 중 예외 — 워커 최상위 try/catch의 안전판(PRD §9, Phase 14 P14-3부터 있던
    /// 폴백과 같은 값).</summary>
    InternalError,
}
