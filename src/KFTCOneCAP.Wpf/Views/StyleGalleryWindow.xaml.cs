using System;
using System.Threading.Tasks;
using System.Windows;
using KFTCOneCAP.Wpf.Services.Payment;
using KFTCOneCAP.Wpf.ViewModels;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// Phase 1 디자인 시스템 검증용 개발 도구 창. 최종 산출물 아님.
/// </summary>
public partial class StyleGalleryWindow : Window
{
    // 스크린샷 검증용으로 상태를 수동 전환할 수 있도록 창/뷰모델을 붙잡아 둔다(2026-08-21 배경 이미지
    // 자산 구조 재작업 검증 편의). 최종 산출물 아님 — Phase 15에서 재검토.
    private PaymentNoticeViewModel? _demoViewModel;
    private PaymentNoticeWindow? _demoWindow;

    public StyleGalleryWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Phase 13(development_plan.md P13-7) 개발용 임시 트리거 — 결제 Flow(Phase 15)가 아직 없어
    /// 실제 결제 요청으로 알림창을 띄울 수 없으므로 여기서 상태 전환을 데모한다. 최종 산출물 아님,
    /// Phase 15에서 실제 Flow가 연결되면 이 버튼/핸들러의 제거 여부를 재검토한다.
    /// </summary>
    private async void PaymentNoticeDemoButton_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = new PaymentNoticeViewModel();
        var noticeWindow = new PaymentNoticeWindow(viewModel);
        noticeWindow.Show();

        await Task.Delay(TimeSpan.FromSeconds(2));
        viewModel.State = PaymentNoticeState.FallbackCardRequest;

        await Task.Delay(TimeSpan.FromSeconds(2));
        viewModel.State = PaymentNoticeState.VanProcessing;
    }

    /// <summary>수동 상태 전환 트리거 3종 — 스크린샷 검증 편의용(2026-08-21). 최종 산출물 아님.</summary>
    private void PaymentNoticeIcButton_Click(object sender, RoutedEventArgs e) => SetDemoState(PaymentNoticeState.IcCardRequest);

    private void PaymentNoticeFallbackButton_Click(object sender, RoutedEventArgs e) => SetDemoState(PaymentNoticeState.FallbackCardRequest);

    private void PaymentNoticeProcessingButton_Click(object sender, RoutedEventArgs e) => SetDemoState(PaymentNoticeState.VanProcessing);

    private void SetDemoState(PaymentNoticeState state)
    {
        if (_demoWindow is null)
        {
            _demoViewModel = new PaymentNoticeViewModel();
            _demoWindow = new PaymentNoticeWindow(_demoViewModel);
            _demoWindow.Closed += (_, _) => { _demoWindow = null; _demoViewModel = null; };
            _demoWindow.Show();
        }

        _demoViewModel!.State = state;
    }
}
