namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-4) — 리더기 설정 화면(<see
/// cref="Views.ReaderSetupWindow"/>, 모달)이 지금 열려 있는지 결제 Flow(<c>PaymentOrchestrator</c>)가
/// 판정할 수 있게 하는 최소 계약. <c>Services/</c>는 WPF <c>Window</c> 타입을 알 수 없으므로 이
/// 인터페이스로 경계를 긋는다(계층 규칙, ROADMAP.md "계층 구조"). 운영 구현은
/// <see cref="Views.ReaderSetupWindowGate"/>다.
///
/// 2026-08-25 사용자 확정: 설정 화면이 열려 있으면 결제 요청은 카드 리딩을 시도하지 않고 즉시 오류로
/// 거부한다(같은 COM 포트를 설정 화면과 결제 워커가 동시에 쓰는 상황 자체를 만들지 않는다). 판정
/// 기준은 이 값 하나뿐이다.
/// </summary>
internal interface IReaderSetupGate
{
    bool IsReaderSetupOpen { get; }
}
