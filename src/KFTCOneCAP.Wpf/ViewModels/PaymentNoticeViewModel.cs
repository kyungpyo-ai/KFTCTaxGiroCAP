using CommunityToolkit.Mvvm.ComponentModel;
using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.ViewModels;

/// <summary>
/// 결제 알림창(Views/PaymentNoticeWindow.xaml)의 ViewModel.
/// Phase 13 시각 구현 범위(docs/payment_relay/development_plan.md P13-2 최소 범위)에서는
/// <see cref="State"/> 프로퍼티 하나만 노출한다 — View는 이 값에서 배경 이미지/문구/카드
/// 애니메이션을 전부 파생시킨다(<see cref="Views.PaymentNoticeWindow"/>의 "배경 소스 단일 지점" 규칙).
///
/// 이 클래스는 <c>Visibility</c> 등 WPF 타입을 다루지 않는다(P7-3에서 정립한 원칙 — UI 상태는
/// View/컨버터가 이 열거값에서 파생시킨다). 취소 1회 제한, VanProcessing 중 취소 차단,
/// <c>IPaymentNoticePresenter</c> 제어 진입점(P13-2/P13-6 전체 범위)은 이번 시각 구현 범위 밖이며,
/// Phase 13의 후속 작업(취소/ESC 훅/Presenter)에서 이 클래스를 확장한다.
/// </summary>
public sealed partial class PaymentNoticeViewModel : ObservableObject
{
    [ObservableProperty]
    private PaymentNoticeState _state = PaymentNoticeState.IcCardRequest;
}
