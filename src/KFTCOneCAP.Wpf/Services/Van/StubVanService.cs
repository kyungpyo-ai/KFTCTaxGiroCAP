using System;
using System.Threading.Tasks;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-5) — <see cref="IVanService"/>의 개발용 스텁.
/// 실제 `FNAISCRDVAN` 호출은 Phase 17이 이 자리에 진짜 구현을 꽂는다(P15-5 확정 사항: VAN 단계는
/// 스텁 + `PROCESSING` 전환까지만).
///
/// 고정 지연 후 <see cref="SetNextResult"/>로 미리 주입해 둔 결과를 반환한다 — 기본값은
/// <see cref="VanApprovalOutcome.Approved"/>. 검증 하네스(P15-10)가 승인/거절/통신실패 3종을
/// 스크립트할 수 있게 하는 것이 이 클래스의 유일한 존재 이유다.
///
/// <see cref="SetNextResult"/>는 검증 하네스 스레드(예: 소켓 accept 스레드)에서, <see
/// cref="RequestApprovalAsync"/>는 결제 워커 스레드에서 호출될 수 있어 서로 다른 스레드 접근이
/// 발생한다 — <see cref="_lock"/>으로 둘 다 감싼다.
///
/// (2026-08-25, Opus 검증 리뷰 M-1 수정) <see cref="SetNextResult"/>의 주석이 "**다음** 호출이
/// 반환할 결과"라고 명시하므로, 실제로도 한 번 소비되면 기본값(<see cref="VanApprovalOutcome.Approved"/>)으로
/// 되돌아간다 — 예전엔 소비하지 않고 계속 같은 값을 돌려줘서(sticky), 검증 하네스가 시나리오 N에서
/// 거절/통신실패를 주입한 뒤 다음 시나리오가 기본값(승인)을 기대하면 조용히 어긋날 수 있었다.
/// </summary>
internal sealed class StubVanService : IVanService
{
    private static readonly TimeSpan FixedDelay = TimeSpan.FromSeconds(1);

    private readonly object _lock = new();
    private VanApprovalOutcome _nextResult = VanApprovalOutcome.Approved();

    /// <summary>
    /// (2026-08-25, Opus 검증 리뷰 M-2 수정) 가장 최근 <see cref="RequestApprovalAsync"/> 호출의
    /// 인자를 보관한다 — 검증 하네스(P15-10)가 "카드 데이터·금액·거래일시가 VAN까지 실제로
    /// 전달됐는가"(PRD §4.3 "0x3B 응답 데이터를 파싱해 VAN 요청 데이터를 생성")를 확인할 방법이
    /// 이전에는 없었다(이 스텁이 request를 완전히 무시했음). 검증 전용이라 스레드 안전성은
    /// <see cref="_lock"/> 하나로 충분하다(운영 코드가 이 값을 읽지 않는다).
    /// </summary>
    internal VanApprovalRequest? LastRequest { get; private set; }

    /// <summary>다음 <see cref="RequestApprovalAsync"/> 호출이 반환할 결과를 미리 지정한다(검증
    /// 하네스 전용). 지정하지 않으면 <see cref="VanApprovalOutcome.Approved"/>가 기본값이며, 한 번
    /// 소비된 뒤에도 다시 기본값으로 돌아간다(위 클래스 주석 M-1 참고) — 시나리오마다 매번
    /// 명시적으로 지정하지 않아도 이전 시나리오의 값이 새어 들어오지 않는다.</summary>
    internal void SetNextResult(VanApprovalOutcome outcome)
    {
        lock (_lock)
        {
            _nextResult = outcome;
        }
    }

    public async Task<VanApprovalOutcome> RequestApprovalAsync(VanApprovalRequest request)
    {
        await Task.Delay(FixedDelay).ConfigureAwait(false);

        lock (_lock)
        {
            LastRequest = request;
            VanApprovalOutcome result = _nextResult;
            _nextResult = VanApprovalOutcome.Approved(); // 소비 후 기본값으로 복귀(M-1 수정).
            return result;
        }
    }
}
