using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// 결제 Flow가 VAN(인터넷지로/KFTCVAN) 중계에 필요로 하는 것 전부(docs/payment_relay/development_plan.md
/// P17-5). 실제 <c>FNAISCRDVAN</c>(PRD §2.3) 호출은 Phase 20에서 이 자리에 구현체(<c>VanService</c>,
/// <c>Interop/KftcGiroNative.cs</c> 경유)를 꽂는다 — Phase 17은 <see cref="StubVanRelayService"/>만 꽂는다.
///
/// <b>Phase 15 대비 설계 정정</b>: 원래 <c>IVanService.RequestApprovalAsync(VanApprovalRequest)</c>는
/// 902614(카드 승인) 전용 DTO를 받았다. SPEC 확보(Phase 17) 결과 POS↔원캡 구간과 원캡↔VAN 구간의
/// **전문 형식이 동일하다**는 것이 확인됐으므로(ROADMAP Phase 20), 이 인터페이스는 이제 **완성된
/// 요청 전문을 그대로** 받아 넘긴다 — 501008(조회, 카드 없음)·800000(카드정보조회)·902614(승인) 셋
/// 다 같은 방식으로 다룰 수 있다. VAN이 실제로 응답하면(성공/거절 구분 없이) 그 바이트를
/// <see cref="VanRelayOutcome.ResponseBody"/>로 돌려주고, Orchestrator는 그걸 그대로
/// <see cref="PosResponseTelegram.Relay"/>에 실어 POS에 전달한다 — 승인/거절 해석은 OneCAP이 하지
/// 않는다(P17-3 relay 원칙).
/// </summary>
internal interface IVanRelayService
{
    Task<VanRelayOutcome> RelayAsync(PosRequestTelegram populatedRequest);
}
