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
        if (!IsCancelAllowed)
        {
            return;
        }

        _canceled = true;
        OnPropertyChanged(nameof(IsCancelAllowed));
        CancelCommand.NotifyCanExecuteChanged();
        Canceled?.Invoke(this, EventArgs.Empty);
    }
}
