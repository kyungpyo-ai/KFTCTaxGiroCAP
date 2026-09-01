using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// 검증 하네스(<see cref="PaymentFlowTestScenarios"/>) 전용 — <see cref="StubVanRelayService"/>를
/// 감싸 가장 최근 요청 전문을 <see cref="LastRequest"/>로 노출한다(docs/payment_relay/
/// development_plan.md Phase 21 P21-1).
///
/// <b>왜 이 클래스가 따로 있는가</b>: 예전에는 이 "가장 최근 요청 캡처" 기능이
/// <see cref="StubVanRelayService"/> 자신에게 있었다 — 그런데 그 클래스는 `App.xaml.cs`가 지금도
/// 실제로 배선해 쓰는 프로덕션 구현체다(Phase 20 결정 1). 검증 하네스만 필요로 하는 필드가
/// 프로덕션 경로에 얹혀 있어, 이전 거래의 요청 전문(카드번호·PIN 포함)이 다음 거래까지 메모리에
/// 남는 PRD §8.4 위반이었다(체크포인트 1에서 발견). 이 래퍼로 분리해 프로덕션 구현체는 전문을
/// 전혀 붙들지 않게 하고, 캡처는 테스트 전용 경로에만 남긴다.
/// </summary>
internal sealed class CapturingVanRelayService : IVanRelayService
{
    private readonly StubVanRelayService _inner = new();

    /// <summary>가장 최근 <see cref="RelayAsync"/> 호출의 인자 — 검증 하네스가 "원캡 필드가 실제로
    /// VAN 요청까지 채워져 도달했는가"를 확인하는 용도(P15-5의 <c>LastRequest</c> 패턴을 이 테스트
    /// 전용 클래스로 계승).</summary>
    internal PosRequestTelegram? LastRequest { get; private set; }

    /// <summary>다음 호출이 반환할 결과를 미리 지정한다(검증 하네스 전용). <see cref="StubVanRelayService.SetNextOutcome"/>
    /// 로 그대로 위임한다.</summary>
    internal void SetNextOutcome(VanRelayOutcome outcome) => _inner.SetNextOutcome(outcome);

    public async Task<VanRelayOutcome> RelayAsync(PosRequestTelegram populatedRequest)
    {
        LastRequest = populatedRequest;
        return await _inner.RelayAsync(populatedRequest).ConfigureAwait(false);
    }
}
