using System;
using System.Threading;
using System.Threading.Tasks;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>거래를 끝내는 사유. <see cref="TransactionOutcomeGate"/>가 이 중 정확히 하나만 확정한다.</summary>
internal enum TransactionOutcomeReason
{
    /// <summary>정상 흐름(카드 리딩 성공/실패, DLL 오류, 재요청 상한 초과 등)이 거래를 끝내는 결과를
    /// 스스로 만들었다.</summary>
    FlowResult,

    /// <summary>사용자 취소(취소 버튼/ESC, PRD §4.8)로 거래가 끝났다.</summary>
    UserCanceled,

    /// <summary>거래 데드라인 만료(PRD §4.9)로 거래가 끝났다.</summary>
    Timeout,
}

/// <summary>
/// Phase 16(docs/payment_relay/development_plan.md P16-1) — 거래 1건의 최종 결과를 **정확히 한 번만**
/// 확정하는 게이트. Phase 15가 남긴 세 갈래(<c>_canceled</c> 플래그, <c>_cancelSignal</c> TCS, 브로드캐스트
/// 완료 후 방어적 재확인)를 이 클래스 하나로 대체한다.
///
/// **리더기 계층의 게이트(<c>PendingReaderCommand</c>, P10-4)와는 층이 다르다** — 그 게이트는 "리더기
/// 1대의 명령 1회"를 대상으로 하고, 이 게이트는 "거래 1건"을 대상으로 한다. <c>ReaderService</c>는
/// "거래"라는 개념을 전혀 모르게 설계돼 있어(그 무지 덕분에 N=1 축약이 공짜로 성립, P10-4/P10-5 주석)
/// 그 계층에 이 개념을 밀어넣지 않는다 — 계층마다 확정 지점을 정확히 하나씩 둔다(development_plan.md
/// Phase 16 "두 계층의 게이트를 구분한다" 참고).
///
/// 확정 방식은 <see cref="Interlocked.CompareExchange(ref int, int, int)"/> 한 줄뿐이다(락을 쓰지 않는다 —
/// P10-4와 같은 이유, 임계구역을 CAS 한 줄로 명확히 한다). 경합 시 승패 규칙은 **선착순**이다(2026-08-25
/// 사용자 확정) — 특정 사유에 우선권을 주지 않는다.
/// </summary>
internal sealed class TransactionOutcomeGate
{
    private const int Unclaimed = 0;

    // TransactionOutcomeReason 값 자체가 아니라 "미확정(-1 대신 0)"과 구분하기 위해 (int)reason + 1을
    // 저장한다 — FlowResult == 0이라 그대로 쓰면 Unclaimed(0)과 구분이 안 된다.
    private int _claimedReasonPlusOne;

    private readonly TaskCompletionSource<bool> _interrupted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>확정된 사유. 아직 미확정이면 null.</summary>
    internal TransactionOutcomeReason? ClaimedReason
    {
        get
        {
            int value = Volatile.Read(ref _claimedReasonPlusOne);
            return value == Unclaimed ? null : (TransactionOutcomeReason)(value - 1);
        }
    }

    /// <summary><see cref="TransactionOutcomeReason.UserCanceled"/> 또는 <see
    /// cref="TransactionOutcomeReason.Timeout"/>으로 확정되면 완료되는 Task. 카드 리딩 라운드 루프가
    /// 리더기 응답 대기와 <see cref="Task.WhenAny(Task[])"/>로 경쟁시킨다. <see
    /// cref="TransactionOutcomeReason.FlowResult"/>로 확정될 때는 이 Task를 완료시키지 않는다 — 정상
    /// 흐름은 자기 자신을 깨울 필요가 없다.</summary>
    internal Task Interrupted => _interrupted.Task;

    /// <summary>
    /// 이 사유로 거래를 확정하려 시도한다. 최초 1회만 <c>true</c>를 반환하고, 그 뒤로는 이미 다른 사유가
    /// 선점했으므로 항상 <c>false</c>를 반환한다(선착순). 호출자는 <c>true</c>를 받았을 때만 그 사유에
    /// 해당하는 후속 처리(리더기 정리, POS 응답 생성)를 수행해야 한다.
    /// </summary>
    internal bool TryClaim(TransactionOutcomeReason reason)
    {
        int desired = (int)reason + 1;
        bool claimed = Interlocked.CompareExchange(ref _claimedReasonPlusOne, desired, Unclaimed) == Unclaimed;

        if (claimed && reason != TransactionOutcomeReason.FlowResult)
        {
            _interrupted.TrySetResult(true);
        }

        return claimed;
    }
}
