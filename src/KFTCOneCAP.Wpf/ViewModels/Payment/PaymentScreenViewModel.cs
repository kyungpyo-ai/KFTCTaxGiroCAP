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
/// 홈 화면 "결제" 카드(P29-7)에서 연다. <c>--payment-screen-test</c> 진단 인자로 Owner 없이 단독
/// 생성/표시할 수도 있다(App.xaml.cs).
/// </summary>
public sealed partial class PaymentScreenViewModel : ObservableObject
{
    public PaymentScreenViewModel()
    {
        var shopSettingsService = new ShopSettingsService();

        // 탭 라벨에서 거래 구분 코드(501008/800000/902614) 숫자를 뺐다(2026-09-18 사용자 지시 — 균등폭
        // 탭에서 말줄임이 나던 것을 한글 문구만 남겨 줄여서 해결). 코드값 자체는 각 탭의
        // TransactionTypeCode 프로퍼티(PaymentTelegramTabViewModel)로 여전히 확인 가능하다.
        Tabs = new ObservableCollection<PaymentTelegramTabViewModel>
        {
            new("국고 상세 고지내역 조회", NoticeInquirySchema.Create(), () => PosClient.DefaultResponseTimeout),
            new("카드 정보 조회", CardInfoInquirySchema.Create(), () => PosClient.DefaultResponseTimeout),
            new("국고 신용카드 승인요청", CardApprovalSchema.Create(),
                () => PosClient.ComputeCardApprovalResponseTimeout(shopSettingsService.Load())),
        };

        // 체크포인트 2 M-2 수정(2026-09-21, 사용자 확정 "통신중일때는 다른 걸 못하게 하는게 맞아") —
        // 탭별 IsSending은 자기 탭의 전송/재생성 버튼만 막고 다른 탭으로의 전환은 막지 않았다. 그래서
        // 한 탭(예: 902614, 카드 대기 최대 120초)이 전송 중일 때 다른 탭으로 넘어가 그 탭의 전송을 또
        // 누를 수 있었다 — TransactionQueue가 순차 처리하므로 안전(리더기/VAN 동시 접근)에는 문제가
        // 없지만, 두 번째 요청이 응답 타임아웃에 먼저 걸려 화면엔 "실패"로 보이는데 실제로는 서버에서
        // 정상 처리되는 상황이 생겨 사용자가 오인 재전송할 위험이 있었다. 탭 중 하나라도 전송 중이면
        // TabControl 자체를 비활성화해(PaymentScreenWindow.xaml TelegramTabControl.IsEnabled) 탭 전환을
        // 막는다 — 전송 중인 탭의 버튼은 기존 IsSending 바인딩으로 이미 막혀 있으므로 이거 하나로 충분.
        foreach (PaymentTelegramTabViewModel tab in Tabs)
            tab.PropertyChanged += OnTabPropertyChanged;

        RecomputeIsAnyTabSending();
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaymentTelegramTabViewModel.IsSending))
            RecomputeIsAnyTabSending();
    }

    private void RecomputeIsAnyTabSending()
    {
        bool any = false;
        foreach (PaymentTelegramTabViewModel tab in Tabs)
        {
            if (tab.IsSending)
            {
                any = true;
                break;
            }
        }

        IsAnyTabSending = any;
    }

    /// <summary>탭 3개(501008/800000/902614) 고정 목록 — 전문 종류가 늘어날 계획이 없어(PRD §12) 동적
    /// 등록 구조를 두지 않는다.</summary>
    public ObservableCollection<PaymentTelegramTabViewModel> Tabs { get; }

    [ObservableProperty]
    private int selectedTabIndex;

    /// <summary>탭 중 하나라도 전송 중이면 true — <see cref="Views.PaymentScreenWindow"/>가 이 값으로
    /// TabControl을 비활성화해 탭 전환(과 그로 인한 중복 전송)을 막는다.</summary>
    [ObservableProperty]
    private bool isAnyTabSending;
}
