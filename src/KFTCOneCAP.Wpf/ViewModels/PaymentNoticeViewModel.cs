using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.ViewModels;

/// <summary>
/// 결제 알림창(Views/PaymentNoticeWindow.xaml)의 ViewModel.
/// <see cref="State"/>에서 화면의 이미지/문구/카드 애니메이션을 전부 파생시킨다
/// (<see cref="Views.PaymentNoticeWindow"/>의 "배경 소스 단일 지점" 규칙).
///
/// 이 클래스는 <c>Visibility</c> 등 WPF 타입을 다루지 않는다(P7-3에서 정립한 원칙 — UI 상태는
/// View/컨버터가 이 열거값에서 파생시킨다).
///
/// (docs/payment_relay/development_plan.md P13-2) 취소는 정확히 한 번만 나가야 한다 — 취소 버튼
/// 연타/ESC 연타(P13-5에서 연결)/버튼+ESC 동시 입력 어느 경우에도 <see cref="Canceled"/>는 1회만
/// 발생한다. <c>IPaymentNoticePresenter</c> 제어 진입점(P13-6)은 Phase 15에서 이어서 다룬다.
/// </summary>
public sealed partial class PaymentNoticeViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCancelAllowed))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private PaymentNoticeState _state = PaymentNoticeState.IcCardRequest;

    private bool _canceled;

    /// <summary>취소가 정확히 1회 발생했을 때 통지된다. Phase 15가 결제 워커에 중계한다.</summary>
    public event EventHandler? Canceled;

    /// <summary>
    /// 취소 가능 여부를 판정하는 유일한 지점(development_plan.md P13-2). 이미 취소했거나
    /// <see cref="PaymentNoticeState.VanProcessing"/> 상태면 취소할 수 없다 — VAN 요청이 나간 뒤
    /// 취소를 받으면 VAN 승인과 POS 응답이 어긋날 수 있기 때문(PRD §4.8/§5.3).
    /// </summary>
    public bool IsCancelAllowed => !_canceled && State != PaymentNoticeState.VanProcessing;

    [RelayCommand(CanExecute = nameof(IsCancelAllowed))]
    private void Cancel()
    {
        if (TryMarkCanceled())
        {
            RaiseCanceledEvent();
        }
    }

    /// <summary>
    /// (Opus 검증 리뷰 2026-08-24, H-3) 취소 가능 여부 판정과 <see cref="_canceled"/> 확정을
    /// 동기·원자적으로 수행한다(이벤트 통지는 하지 않음). ESC 전역 훅처럼 "지금 이 순간 삼킬지"를
    /// 결정하는 동시에 취소를 확정해야 하는 호출자를 위한 것이다.
    ///
    /// 원래는 훅 콜백이 삼킬지 여부만 동기로 정하고 실제 취소 실행은
    /// <see cref="System.Windows.Threading.Dispatcher.BeginInvoke(Delegate)"/>(Normal 우선순위)로
    /// 미뤘는데, 그 사이 Phase 15 워커가 <c>ChangeState(VanProcessing)</c>을
    /// <see cref="System.Windows.Threading.Dispatcher.Invoke(Delegate)"/>(Send 우선순위, Normal보다
    /// 먼저 처리됨)로 부르면 먼저 처리되어, 뒤늦게 실행된 취소가 이미 VanProcessing으로 바뀐
    /// <see cref="IsCancelAllowed"/>를 보고 조용히 무시되는 결함이 있었다 — ESC는 이미 삼켜져
    /// POS에도 전달되지 않았는데 취소는 일어나지 않는, 결제 시스템에서 가장 위험한 무증상 실패였다.
    /// 상태 전환(플래그 확정)은 여기서 동기로 끝내고, 무거울 수 있는 외부 구독자 통지
    /// (<see cref="RaiseCanceledEvent"/>)만 호출자가 별도로 지연시킨다.
    /// </summary>
    internal bool TryMarkCanceled()
    {
        if (!IsCancelAllowed)
        {
            return false;
        }

        _canceled = true;
        OnPropertyChanged(nameof(IsCancelAllowed));
        CancelCommand.NotifyCanExecuteChanged();
        return true;
    }

    /// <summary>
    /// <see cref="TryMarkCanceled"/>로 취소가 이미 확정된 뒤 구독자에게 통지한다 — 반드시 먼저
    /// <see cref="TryMarkCanceled"/>가 true를 반환한 경우에만 호출한다.
    /// </summary>
    internal void RaiseCanceledEvent() => Canceled?.Invoke(this, EventArgs.Empty);
}
