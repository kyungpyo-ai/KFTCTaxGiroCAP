namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-4) — 리더기 설정 화면(<see
/// cref="Views.ReaderSetupWindow"/>, 모달)이 지금 열려 있는지 결제 Flow(<c>PaymentOrchestrator</c>)가
/// 판정할 수 있게 하는 최소 계약. <c>Services/</c>는 WPF <c>Window</c> 타입을 알 수 없으므로 이
/// 인터페이스로 경계를 긋는다(계층 규칙, ROADMAP.md "계층 구조"). 운영 구현은
/// <see cref="Views.SetupScreenGate"/>다.
///
/// 2026-08-25 사용자 확정: 설정 화면이 열려 있으면 결제 요청은 카드 리딩을 시도하지 않고 즉시 오류로
/// 거부한다(같은 COM 포트를 설정 화면과 결제 워커가 동시에 쓰는 상황 자체를 만들지 않는다). 판정
/// 기준은 이 값 하나뿐이다.
///
/// Phase 23(docs/operations/development_plan.md P23-2)에서 이전 이름(리더기 설정 화면만 가리키던 이름)에서 리네임했다 —
/// <b>리더기 설정 화면과 가맹점 설정 화면 둘 다 이 게이트를 센다</b>(카운터 공유, PRD.md §2.7). 이름이
/// "리더기 설정"만 가리키던 시절의 흔적을 없애기 위한 순수 리네임이며 동작은 바뀌지 않았다.
/// </summary>
internal interface ISetupScreenGate
{
    bool IsSetupScreenOpen { get; }
}
