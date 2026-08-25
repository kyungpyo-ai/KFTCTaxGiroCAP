namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-5) — PRD §4.10 "VAN DLL 통신 실패와 VAN 서버
/// 거절은 구분해서 처리한다"를 타입 수준에서 강제하는 3분기.
/// </summary>
internal enum VanApprovalOutcomeKind
{
    /// <summary>승인.</summary>
    Approved,

    /// <summary>DLL 호출은 성공(nRet==0)했지만 VAN 응답 전문의 거래 결과가 실패/거절.</summary>
    Declined,

    /// <summary>DLL 호출 자체가 실패(nRet==-1) — 서버 거절과 원인이 다르다.</summary>
    CommunicationFailure,
}
