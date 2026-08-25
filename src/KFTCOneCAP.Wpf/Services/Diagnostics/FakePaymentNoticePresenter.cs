using System;
using System.Collections.Generic;
using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-10) 검증용 가짜 <see cref="IPaymentNoticePresenter"/>.
/// **최종 산출물이 아니다.** 실제 <see cref="Views.PaymentNoticePresenter"/>는 WPF 창을 실제로 띄워야
/// 해서 "언제 어떤 상태로 바뀌었는가"를 자동으로 검증하기 어렵고, 취소 클릭도 실제 UI 상호작용이
/// 필요하다 — 이 가짜는 호출 이력을 그대로 기록하고, <see cref="FireCanceled"/>로 검증 하네스가
/// 원하는 시점에 취소를 프로그램적으로 일으킬 수 있게 한다(P15-9 취소 우선순위 시나리오 검증에
/// 필수).
/// </summary>
internal sealed class FakePaymentNoticePresenter : IPaymentNoticePresenter
{
    private readonly object _lock = new();

    /// <summary>"Show:IcCardRequest", "ChangeState:VanProcessing", "Close" 같은 호출 이력.</summary>
    internal List<string> History { get; } = new();

    internal bool IsShown { get; private set; }

    public event EventHandler? Canceled;

    /// <summary>
    /// (2026-08-25, Opus 검증 리뷰 H-1 회귀 방지용) 실제 <see cref="Views.PaymentNoticePresenter.Show"/>는
    /// <c>Dispatcher.Invoke</c>로 동기 마샬링되므로, 반환 시점엔 이미 창이 떠서 취소 버튼이 활성
    /// 상태다 — <c>PaymentOrchestrator</c>가 <c>Canceled</c> 구독을 <see cref="Show"/> 호출 **뒤에**
    /// 걸면, 그 사이의 아주 짧은 창에 취소가 들어와도 구독자가 없어 통지가 사라지는 결함이 있었다
    /// (수정 전 재현됨). 이 플래그를 켜면 <see cref="Show"/> 자신이 기록 직후 즉시(=구독이 걸려
    /// 있어야만 통지가 도달하는 최악의 타이밍으로) <see cref="Canceled"/>를 발화해, 호출 순서가
    /// 실제로 "구독 → Show"인지를 결과로 증명한다 — 순서가 반대라면 이 취소는 유실되고 거래가
    /// 정상 진행돼 버린다.
    /// </summary>
    internal bool FireCanceledSynchronouslyOnShow { get; set; }

    public void Show(PaymentNoticeState state)
    {
        lock (_lock)
        {
            IsShown = true;
            History.Add($"Show:{state}");
        }

        if (FireCanceledSynchronouslyOnShow)
            FireCanceled();
    }

    public void ChangeState(PaymentNoticeState state)
    {
        lock (_lock)
        {
            History.Add($"ChangeState:{state}");
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            IsShown = false;
            History.Add("Close");
        }
    }

    /// <summary>검증 하네스가 원하는 시점에 취소를 일으킨다 — 실제 <see cref="Canceled"/> 구독자
    /// (<c>PaymentOrchestrator.OnCanceled</c>)가 그대로 통지받는다.</summary>
    internal void FireCanceled() => Canceled?.Invoke(this, EventArgs.Empty);

    /// <summary>현재 <see cref="Canceled"/>에 붙어 있는 구독자 수 — 거래 종료 후 구독 해제가
    /// 제대로 됐는지(Phase 13 Opus 리뷰 M-1과 같은 종류의 누수가 없는지) 검증하는 용도.</summary>
    internal int CanceledSubscriberCount => Canceled?.GetInvocationList().Length ?? 0;
}
