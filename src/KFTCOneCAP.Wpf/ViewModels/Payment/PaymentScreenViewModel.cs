using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Services.Pos;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.ViewModels.Payment;

/// <summary>
/// Phase 29 P29-6(docs/payment_relay/development_plan.md "P29-6", PRD.md §12) — 국세 결제 화면
/// (Views/PaymentScreenWindow.xaml)의 최상위 ViewModel. 3전문(501008/800000/902614) 탭을 담는다.
///
/// <b>이번 단계는 홈 화면과 배선되지 않는다</b>(P29-7이 별도로 한다) — 이 클래스와 창은 독립적으로
/// 생성/표시할 수 있다(App.xaml.cs의 <c>--payment-screen-test</c> 진단 인자, 또는 P29-7이 나중에 붙일
/// 홈 카드 클릭 경로).
/// </summary>
public sealed partial class PaymentScreenViewModel : ObservableObject
{
    public PaymentScreenViewModel()
    {
        var shopSettingsService = new ShopSettingsService();

        Tabs = new ObservableCollection<PaymentTelegramTabViewModel>
        {
            new("501008 국고 상세 고지내역 조회", NoticeInquirySchema.Create(), () => PosClient.DefaultResponseTimeout),
            new("800000 카드 정보 조회", CardInfoInquirySchema.Create(), () => PosClient.DefaultResponseTimeout),
            new("902614 국고 신용카드 승인요청", CardApprovalSchema.Create(),
                () => PosClient.ComputeCardApprovalResponseTimeout(shopSettingsService.Load())),
        };
    }

    /// <summary>탭 3개(501008/800000/902614) 고정 목록 — 전문 종류가 늘어날 계획이 없어(PRD §12) 동적
    /// 등록 구조를 두지 않는다.</summary>
    public ObservableCollection<PaymentTelegramTabViewModel> Tabs { get; }

    [ObservableProperty]
    private int selectedTabIndex;
}
