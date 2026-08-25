using System.Threading.Tasks;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-5) — 결제 Flow가 VAN 서버 승인 요청에 대해
/// 필요로 하는 것 전부. 실제 `FNAISCRDVAN`(PRD §2.3) 호출은 Phase 17에서 이 자리에 구현체
/// (`VanService`, `Interop/KftcGiroNative.cs` + `Protocol/Van/` 경유)를 꽂는다 — Phase 15는
/// <see cref="StubVanService"/>만 꽂는다.
///
/// 카드 데이터는 이 인터페이스를 거치는 동안만 살아 있어야 한다(PRD §8.4 "거래 종료 시 카드 데이터
/// 즉시 삭제") — 호출자(Orchestrator)가 호출 후 참조를 버린다.
/// </summary>
internal interface IVanService
{
    Task<VanApprovalOutcome> RequestApprovalAsync(VanApprovalRequest request);
}
