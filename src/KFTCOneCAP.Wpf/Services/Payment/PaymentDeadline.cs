using System;
using System.Threading;
using System.Threading.Tasks;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// Phase 16(docs/payment_relay/development_plan.md P16-2) — 거래 1건의 POS 응답 시점을 결정하는
/// **단일 데드라인**(PRD §4.9). "리더기 명령 타임아웃"과 역할이 다르다 — 이 클래스가 Timeout 결과를
/// 확정하는 유일한 주체이고, 리더기 명령 타임아웃(<c>ReaderService.SendAndAwaitAsync</c>의 <c>timeout</c>
/// 인자)은 하드웨어가 영영 응답하지 않을 때 DLL 라운드를 회수하는 리더기 계층의 안전장치일 뿐 POS 응답을
/// 만들지 않는다.
///
/// 시작 120초(PRD §4.9) + <see cref="Extend"/> 호출마다 뒤로 밀리는 **하나의 데드라인**이다 — 카드 리딩
/// 라운드마다 새로 120초를 주지 않는다(그러면 FALLBACK·재요청이 겹칠 때 최악 360초까지 늘어난다,
/// development_plan.md Phase 16 착수 전 전제 참고).
///
/// <see cref="Expired"/>는 <see cref="Task.Delay(TimeSpan,CancellationToken)"/> 재확인 루프로 구현한다
/// (<see cref="System.Threading.Timer"/>를 쓰지 않는 이유: 연장할 때마다 재무장이 필요하고 해제를
/// 빠뜨리면 그대로 누수가 된다 — Phase 13에서 겪은 <c>DispatcherTimer</c> 누수와 같은 종류). 거래 종료
/// 시 반드시 <see cref="Dispose"/>를 호출해 루프를 끝낸다(P16-4 리소스 해제 목록).
/// </summary>
internal sealed class PaymentDeadline : IDisposable
{
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task<bool> _expiryTask;
    private DateTime _deadlineUtc;
    private bool _disposed;

    internal PaymentDeadline(TimeSpan initial)
    {
        _deadlineUtc = DateTime.UtcNow + initial;
        _expiryTask = RunAsync(_cts.Token);
    }

    /// <summary>남은 시간. 이미 지났으면 <see cref="TimeSpan.Zero"/>(음수를 반환하지 않는다 — 호출자가
    /// 그대로 리더기 명령 타임아웃에 넘겨도 안전하도록).</summary>
    internal TimeSpan Remaining
    {
        get
        {
            lock (_lock)
            {
                TimeSpan remaining = _deadlineUtc - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

    /// <summary>
    /// 이 데드라인이 끝날 때까지 기다린다. **실제로 만료됐으면 <c>true</c>**, 거래가 정상 종료돼
    /// <see cref="Dispose"/>로 감시가 끝났으면 <c>false</c>를 돌려준다. 예외를 던지지 않으므로
    /// 호출자가 <c>try/catch</c>를 두지 않아도 된다.
    ///
    /// 두 경우를 <b>반드시 구분해서</b> 돌려준다(2026-08-25, Phase 16 체크포인트 리뷰 M-2) — 예전엔
    /// 둘 다 그냥 "완료"로 합쳐져 있어 호출자가 정상 종료한 거래에까지 Timeout 확정을 시도했다.
    /// 게이트가 이미 확정돼 있어 결과적으로는 무해했지만, "확정을 시도하는 지점"의 목록에 정상
    /// 경로가 섞이면 그 목록으로 안전성을 논증할 수 없게 된다.
    /// </summary>
    internal Task<bool> WaitForExpiryAsync() => _expiryTask;

    /// <summary>데드라인을 <paramref name="extension"/>만큼 뒤로 민다. PRD §4.9 — 새 사용자 입력 단계가
    /// 시작될 때마다(현재는 FALLBACK/재요청, 추후 서명·PIN 입력도 동일) 호출한다. 연장 값 자체는 이
    /// 메서드가 정하지 않는다 — 호출자(<c>PaymentOrchestrator</c>)의 상수 한 곳에서만 정한다.</summary>
    internal void Extend(TimeSpan extension)
    {
        lock (_lock)
        {
            _deadlineUtc += extension;
        }
    }

    /// <summary>거래 종료 시 반드시 호출한다 — 재확인 루프를 끝내고 <see
    /// cref="CancellationTokenSource"/>까지 해제한다(P16-4 리소스 해제 목록). 거래마다 하나씩 만들어지는
    /// 객체라 <c>Cancel</c>만 하고 <c>Dispose</c>를 빠뜨리면 장시간 운용에서 그대로 누수가 된다
    /// (PRD §9, 2026-08-25 Phase 16 체크포인트 리뷰 M-1). 두 번 호출돼도 안전하다.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        // Cancel()이 반환된 시점엔 Task.Delay의 등록이 모두 처리된 뒤이고, RunAsync는 다음 루프
        // 진입에서 token.IsCancellationRequested를 보고 곧바로 빠져나가므로(=이미 취소된 토큰으로
        // Task.Delay를 다시 부르지 않으므로) 이어지는 Dispose()가 ObjectDisposedException을
        // 유발하지 않는다.
        _cts.Cancel();
        _cts.Dispose();
    }

    /// <summary>true = 실제로 만료됨, false = <see cref="Dispose"/>로 감시가 끝남.</summary>
    private async Task<bool> RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TimeSpan remaining = Remaining;
            if (remaining <= TimeSpan.Zero)
            {
                return true;
            }

            try
            {
                await Task.Delay(remaining, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }
}
