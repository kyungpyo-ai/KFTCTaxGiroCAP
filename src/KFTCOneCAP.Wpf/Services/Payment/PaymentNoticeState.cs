namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// 결제 알림창(<see cref="ViewModels.PaymentNoticeViewModel"/>, PRD §5.2)이 표시할 수 있는 3가지 화면 상태.
/// Phase 15의 결제 워커(Services 계층)가 이 값을 넘겨 화면을 전환시키므로, ViewModels가 아니라
/// Services 아래에 둔다(계층 규칙: ViewModels → Services 단방향, Services는 ViewModels 타입을 모른다.
/// docs/payment_relay/development_plan.md P13-2). WPF 타입은 참조하지 않는 순수 열거형이다.
/// </summary>
public enum PaymentNoticeState
{
    /// <summary>IC 카드 삽입 요청 — "그림과 같이 카드를 넣어주세요." (PRD §5.2 BG_IMG_IC)</summary>
    IcCardRequest,

    /// <summary>MS(마그네틱, Fallback) 카드 리딩 요청 — "그림과 같이 카드를 긁어주세요." (BG_IMG_MS)</summary>
    FallbackCardRequest,

    /// <summary>VAN 서버 통신 중 — "거래중입니다." (BG_IMG_PROCESSING). 이 상태에서는 취소가 막힌다
    /// (development_plan.md P13-2 "취소 가능 구간" — 이 Phase 시각 데모 범위에서는 버튼 IsEnabled에만 반영).</summary>
    VanProcessing,
}
