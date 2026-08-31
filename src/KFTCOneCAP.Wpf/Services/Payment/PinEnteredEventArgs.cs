using System;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// <see cref="IPaymentNoticePresenter.PinEntered"/>가 통지할 때 담아 보내는 값 —
/// 사용자가 알림창 키패드로 완성한 PIN 4자리(docs/payment_relay/development_plan.md P18-1).
/// WPF 타입은 참조하지 않는다(Services 계층 규칙, <see cref="PaymentNoticeState"/> 상단 주석 참고).
///
/// ★ 이 값을 로그로 남기지 않는다(P18-3/P18-5에서 확정될 규칙 — Phase 18 "확정된 설계 결정" #6).
/// </summary>
public sealed class PinEnteredEventArgs : EventArgs
{
    public PinEnteredEventArgs(string pin)
    {
        Pin = pin;
    }

    public string Pin { get; }
}
