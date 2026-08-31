using System;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// 결제 알림창(<see cref="Views.PaymentNoticeWindow"/>)을 여닫는 제어 진입점
/// (docs/payment_relay/development_plan.md P13-6). Phase 15의 결제 워커는 UI 스레드가 아닌 곳에서
/// 이 인터페이스만 보고 알림창을 띄우고/상태를 바꾸고/닫는다 — 계층 규칙상 <c>Services/</c>는 WPF
/// 타입(<c>Window</c>/<c>Dispatcher</c>)을 알면 안 되므로, 이 인터페이스 자체에는 WPF 타입이 전혀
/// 등장하지 않는다. 구현체(<see cref="Views.PaymentNoticePresenter"/>)가 <c>Views/</c>에서 WPF 타입을
/// 다루며 이 계약을 만족시킨다.
///
/// ★ 모든 메서드는 **어느 스레드에서 호출돼도 안전**해야 한다 — 구현체가 내부적으로 UI 스레드로
/// 마샬링한다. 이미 닫힌 상태에서 <see cref="ChangeState"/>/<see cref="Close"/>가 들어와도 예외를
/// 던지지 않고 조용히 무시한다(취소와 Flow 진행이 겹치면 충분히 발생할 수 있는 순서이므로).
/// </summary>
public interface IPaymentNoticePresenter
{
    /// <summary>알림창을 지정한 상태로 띄운다. 이미 떠 있으면 상태만 갱신한다.</summary>
    void Show(PaymentNoticeState state);

    /// <summary>떠 있는 알림창의 상태를 바꾼다. 알림창이 없으면 조용히 무시한다.</summary>
    void ChangeState(PaymentNoticeState state);

    /// <summary>알림창을 닫는다. 이미 닫혀 있으면 조용히 무시한다.</summary>
    void Close();

    /// <summary>
    /// 취소가 발생했을 때 통지된다(알림창 ViewModel의 "취소는 정확히 한 번만 나간다" 규칙을 그대로
    /// 물려받는다 — P13-2). <c>CancellationToken</c> 방식은 채택하지 않는다: Phase 15의 취소·타임아웃·
    /// CALLBACK 중재 구조가 아직 확정되지 않아서다.
    /// </summary>
    event EventHandler? Canceled;

    /// <summary>
    /// PIN 4자리가 입력 완료됐을 때 정확히 한 번 통지된다(취소와 같은 "정확히 한 번" 규칙,
    /// docs/payment_relay/development_plan.md P18-1). <see cref="PaymentNoticeState.PinEntry"/> 상태에서만
    /// 발생한다. 여기서도 <c>Task&lt;string?&gt;</c> 방식은 쓰지 않는다 — <see cref="Canceled"/>와 같은
    /// 이유로, 취소·Timeout·PIN 완료 3자 경합의 결과 확정 주체를 하나(호출자 쪽 게이트)로 유지하기
    /// 위해서다.
    /// </summary>
    event EventHandler<PinEnteredEventArgs>? PinEntered;
}
