using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KFTCOneCAP.Wpf.ViewModels;

/// <summary>
/// 홈 화면(Views/HomeWindow.xaml)의 ViewModel.
/// Phase 7(MVVM 전환, docs/payment_relay/development_plan.md P7-5): HomeWindow.xaml.cs(245줄)는
/// 대부분이 View/OS 책임(트레이 아이콘 WinForms interop, DWM 타이틀바, 창 워밍업, 눌림 애니메이션
/// 프레임 확보용 Dispatcher.BeginInvoke)이라 옮길 것이 적다. 이 ViewModel은 "카드 클릭 시 무엇을
/// 할지"만 Command로 노출하고, 그 결과를 이벤트로 알린다 — 실제 창 생성/타이밍/DWM 같은 WPF
/// Window·WinForms 타입을 다루는 코드는 전부 Views/HomeWindow.xaml.cs에 남아 있다. 이곳으로
/// 옮기면 ViewModel이 Window/WinForms 타입을 알게 되어 계층 규칙(ViewModels → Services → ...,
/// docs/payment_relay/ROADMAP.md "계층 구조")이 깨지기 때문이다.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    /// <summary>리더기 설정 카드 클릭(PRD 3.7/4.2) — 실제 창 생성/오픈은 View가 담당한다.</summary>
    public event EventHandler? ReaderSetupRequested;

    /// <summary>가맹점 설정 카드 클릭(Phase 23, docs/operations/development_plan.md P23-4) — 실제
    /// 창 생성/오픈은 View가 담당한다. <see cref="NotImplementedCardRequested"/>를 쓰던 시절의
    /// "준비 중" 안내에서 실제 화면 오픈으로 바뀌었다.</summary>
    public event EventHandler? ShopSetupRequested;

    /// <summary>범위 밖 카드(결제/전표 설정) 클릭 — 카드 이름을 실어 알린다.</summary>
    public event EventHandler<string>? NotImplementedCardRequested;

    [RelayCommand]
    private void OpenReaderSetup() => ReaderSetupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenShopSetup() => ShopSetupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenTrans() => NotImplementedCardRequested?.Invoke(this, "결제");

    [RelayCommand]
    private void OpenReceiptSetup() => NotImplementedCardRequested?.Invoke(this, "전표 설정");
}
