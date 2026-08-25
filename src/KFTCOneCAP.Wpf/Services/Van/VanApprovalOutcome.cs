namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-5) — <see cref="IVanService.RequestApprovalAsync"/>의
/// 결과. 이 프로젝트의 다른 Outcome 타입들(<c>CardReadCommandOutcome</c>,
/// <c>IntegrityCheckSequenceOutcome</c> 등)과 같은 모양 — private 생성자 + 정적 팩터리로 잘못된 조합
/// (예: <see cref="VanApprovalOutcomeKind.Approved"/>인데 <see cref="ResponseCode"/>가 채워짐)이
/// 만들어지지 않게 한다.
/// </summary>
internal sealed class VanApprovalOutcome
{
    internal VanApprovalOutcomeKind Kind { get; }

    /// <summary>VAN 서버 응답코드. <see cref="VanApprovalOutcomeKind.Declined"/>일 때만 채워진다.</summary>
    internal string? ResponseCode { get; }

    /// <summary>사람이 읽는 사유 — 로그용. Declined/CommunicationFailure일 때만 채워진다(Approved는
    /// 빈 문자열).</summary>
    internal string Detail { get; }

    private VanApprovalOutcome(VanApprovalOutcomeKind kind, string? responseCode, string detail)
    {
        Kind = kind;
        ResponseCode = responseCode;
        Detail = detail;
    }

    internal static VanApprovalOutcome Approved() =>
        new VanApprovalOutcome(VanApprovalOutcomeKind.Approved, null, string.Empty);

    internal static VanApprovalOutcome Declined(string responseCode, string detail) =>
        new VanApprovalOutcome(VanApprovalOutcomeKind.Declined, responseCode, detail);

    internal static VanApprovalOutcome CommunicationFailure(string detail) =>
        new VanApprovalOutcome(VanApprovalOutcomeKind.CommunicationFailure, null, detail);
}
