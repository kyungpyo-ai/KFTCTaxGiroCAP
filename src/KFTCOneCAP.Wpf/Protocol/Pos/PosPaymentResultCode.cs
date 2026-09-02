namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-3) — 결제 Flow(<c>Services/Payment/
/// PaymentOrchestrator</c>)가 POS에 반환하는 결과 종류. PRD가 "구분해서 응답"을 요구하는 축(§4.2,
/// §4.6, §4.7, §4.8, §4.9, §4.10, §2.2.3, §6)을 열거형으로 확정한 것이다.
///
/// **P15-3의 예상대로 SPEC 확정(Phase 17) 이후에도 이 열거형 자체는 그대로 살아남았고, 바뀐 것은
/// 전문 코드 문자열로 바꾸는 매핑 하나뿐이다** — 그 매핑은 이제
/// <c>Services/Payment/PosResultCodeMapper</c>에 있다(P17-4에서 <c>Protocol/Pos</c>가 리더기 DLL 오류
/// 종류를 알아서는 안 된다는 계층 규칙 때문에 옮겼다).
///
/// Flow(<c>Services/Payment/</c>)는 이 열거형만 다루고 전문 코드 문자열("E01", "R20" 등)을 직접
/// 리터럴로 쓰지 않는다 — grep으로 점검 가능해야 한다(P15-3/P17-4 완료 조건).
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

    /// <summary>902614(승인요청) 요청의 <c>#42</c>(키오스크 고유번호)가 설정값과 다름 — 빈 설정값도
    /// 동일하게 이 값을 쓴다(Phase 23, docs/operations/development_plan.md P23-7, PRD.md §2.3.1/
    /// §2.3.2). 카드 리딩을 시작하기 전에 판정한다.</summary>
    KioskIdMismatch,

    /// <summary>설정된 리더기가 하나도 없음("미사용" 2개) — 카드 리딩을 시도하지 않음(PRD §2.2.3).</summary>
    NoReaderConfigured,

    /// <summary>설정 화면(리더기 설정 화면 또는 가맹점 설정 화면, 모달)이 열려 있어 카드 리딩을
    /// 시도하지 않고 거부(2026-08-25 확정, P15-4). Phase 23(docs/operations/development_plan.md
    /// P23-2)에서 이전 이름(리더기 설정 화면만 가리키던 이름)에서 리네임 — 가맹점 설정 화면도 같은 게이트를 공유하며
    /// 같은 사유로 거부한다(PRD.md §2.7). 전문 코드(<c>E03</c>)는 바뀌지 않았다.</summary>
    SetupScreenInProgress,

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
