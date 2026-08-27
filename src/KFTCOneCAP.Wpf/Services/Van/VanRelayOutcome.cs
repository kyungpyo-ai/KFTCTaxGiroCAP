using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// <see cref="IVanRelayService.RelayAsync"/>의 결과(docs/payment_relay/development_plan.md P17-5).
/// 이 프로젝트의 다른 Outcome 타입들과 같은 모양 — private 생성자 + 정적 팩터리로 잘못된 조합(예:
/// <see cref="VanRelayOutcomeKind.Success"/>인데 <see cref="ResponseBody"/>가 비어 있음)을 막는다.
/// </summary>
internal sealed class VanRelayOutcome
{
    internal VanRelayOutcomeKind Kind { get; }

    /// <summary>VAN이 돌려준 응답 전문 바이트(본문만, 길이 헤더 제외). <see cref="VanRelayOutcomeKind.Success"/>
    /// 일 때만 채워진다.</summary>
    internal byte[]? ResponseBody { get; }

    /// <summary><see cref="VanRelayOutcomeKind.CommunicationFailure"/>일 때만 채워진다 — D01/D02 매핑에
    /// 쓰인다(<see cref="PosResultCodeMapper"/>).</summary>
    internal VanFailureKind? FailureKind { get; }

    /// <summary>사람이 읽는 사유 — 로그용.</summary>
    internal string Detail { get; }

    private VanRelayOutcome(VanRelayOutcomeKind kind, byte[]? responseBody, VanFailureKind? failureKind, string detail)
    {
        Kind = kind;
        ResponseBody = responseBody;
        FailureKind = failureKind;
        Detail = detail;
    }

    internal static VanRelayOutcome Success(byte[] responseBody) =>
        new(VanRelayOutcomeKind.Success, responseBody, null, string.Empty);

    internal static VanRelayOutcome CommunicationFailure(VanFailureKind failureKind, string detail) =>
        new(VanRelayOutcomeKind.CommunicationFailure, null, failureKind, detail);
}
