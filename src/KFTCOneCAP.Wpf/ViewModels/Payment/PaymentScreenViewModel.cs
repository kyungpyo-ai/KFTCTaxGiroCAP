using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KFTCOneCAP.Wpf.Protocol.Pos;
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
        // 탭별 IsSending은 자기 탭의 전송/재생성 버튼만 막고 다른 탭에서 또 전송 버튼을 누르는 것은
        // 막지 않았다. 그래서 한 탭(예: 902614, 카드 대기 최대 120초)이 전송 중일 때 다른 탭으로 넘어가
        // 그 탭의 전송을 또 누를 수 있었다 — TransactionQueue가 순차 처리하므로 안전(리더기/VAN 동시
        // 접근)에는 문제가 없지만, 두 번째 요청이 응답 타임아웃에 먼저 걸려 화면엔 "실패"로 보이는데
        // 실제로는 서버에서 정상 처리되는 상황이 생겨 사용자가 오인 재전송할 위험이 있었다.
        //
        // 최초 구현(TabControl 자체를 IsEnabled=false로 비활성화)은 탭 전환 자체가 막혀 버려 "다른 두
        // 탭이 아예 안 눌리는" 부작용이 났다(2026-09-21 사용자 실측 발견) — TabControl.IsEnabled=false는
        // 자식 전체(헤더 포함)의 입력을 막는데, 그 시각적 신호가 없어(PaymentTabItemStyle에 비활성 트
        // 리거가 없음) 고장처럼 보였다. 탭 전환 자체는 무해하므로(구경만 하는 것) 항상 열어 두고, 각
        // 탭의 전송/재생성 버튼만 개별적으로 막는 방식(PaymentTelegramTabViewModel.IsBlockedByOtherTab)
        // 으로 바꿨다.
        foreach (PaymentTelegramTabViewModel tab in Tabs)
            tab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaymentTelegramTabViewModel.IsSending))
            RecomputeBlockedFlags();
        else if (e.PropertyName == nameof(PaymentTelegramTabViewModel.HasResponse)
                 && sender is PaymentTelegramTabViewModel { HasResponse: true } sourceTab)
            ApplyChainMappingsFrom(sourceTab);
    }

    /// <summary>Phase 30 P30-4(PRD §13.3 "501008/800000 전송 성공 → 뒤 전문 탭 갱신") — <paramref
    /// name="sourceTab"/>에서 다른 전문(자기 자신 제외)으로 가는 연쇄 항목만 처리한다. 자기참조 항목
    /// (902614 #29 = #27+#28)은 <see cref="PaymentTelegramTabViewModel.RecomputeSelfReferencingChainTargets"/>
    /// 가 그 탭 내부에서 자동으로 처리하므로 여기서 건드릴 필요가 없다.
    ///
    /// <b>값을 직접 고친 뒤 앞 전문을 재전송하면 덮어쓴다</b>(2026-09-22 확정, PRD §13.3) — 연쇄가 최신
    /// 응답을 반영하는 것이 이 기능의 목적이라 의도된 동작이다.</summary>
    private void ApplyChainMappingsFrom(PaymentTelegramTabViewModel sourceTab)
    {
        foreach (TelegramFieldChainMap.ChainEntry entry in TelegramFieldChainMap.Entries)
        {
            if (entry.SourceTelegram != sourceTab.TransactionTypeCode || entry.SourceTelegram == entry.TargetTelegram)
                continue;

            PaymentTelegramTabViewModel? targetTab = FindTab(entry.TargetTelegram);
            if (targetTab is null)
                continue;

            var sourceValues = new List<string>(entry.SourceFieldNumbers.Count);
            bool allAvailable = true;
            foreach (int sourceFieldNumber in entry.SourceFieldNumbers)
            {
                string? value = sourceTab.TryReadResponseField(sourceFieldNumber);
                if (value is null) { allAvailable = false; break; }
                sourceValues.Add(value);
            }
            if (!allAvailable)
                continue;

            // TelegramFieldChainConverter.Convert(값 계산)는 ApplyChainedValue 호출보다 먼저 일어나므로,
            // 합산 자리수 초과 등으로 던지는 PosProtocolException이 ApplyChainedValue 내부의 기존
            // try/catch(OnRequestRowValueChanged)를 거치지 못하고 이 메서드 밖으로 그대로 전파될 수
            // 있다 — 이벤트 핸들러(OnTabPropertyChanged) 안에서 잡히지 않으면 창이 죽으므로 여기서
            // 직접 감싼다. 실패한 필드는 건너뛰고 다음 항목을 계속 처리한다.
            string computed;
            try
            {
                computed = TelegramFieldChainConverter.Convert(
                    entry.Conversion, sourceValues, targetTab.GetFieldLength(entry.TargetFieldNumber), entry.FixedValue);
            }
            catch (PosProtocolException)
            {
                continue;
            }

            targetTab.ApplyChainedValue(entry.TargetFieldNumber, computed);
        }
    }

    private PaymentTelegramTabViewModel? FindTab(string transactionTypeCode)
    {
        foreach (PaymentTelegramTabViewModel tab in Tabs)
            if (tab.TransactionTypeCode == transactionTypeCode)
                return tab;
        return null;
    }

    private void RecomputeBlockedFlags()
    {
        bool anySending = false;
        foreach (PaymentTelegramTabViewModel tab in Tabs)
        {
            if (tab.IsSending)
            {
                anySending = true;
                break;
            }
        }

        foreach (PaymentTelegramTabViewModel tab in Tabs)
            tab.IsBlockedByOtherTab = anySending && !tab.IsSending;
    }

    /// <summary>탭 3개(501008/800000/902614) 고정 목록 — 전문 종류가 늘어날 계획이 없어(PRD §12) 동적
    /// 등록 구조를 두지 않는다.</summary>
    public ObservableCollection<PaymentTelegramTabViewModel> Tabs { get; }

    [ObservableProperty]
    private int selectedTabIndex;
}
