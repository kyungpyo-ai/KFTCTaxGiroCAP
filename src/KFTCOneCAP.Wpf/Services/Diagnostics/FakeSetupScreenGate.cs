using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-10) 검증용 가짜 <see cref="ISetupScreenGate"/>.
/// **최종 산출물이 아니다.** 실제 <see cref="Views.SetupScreenGate"/>(<c>App.SetupScreenGate</c>)를
/// 검증 하네스가 직접 건드리면 앱 전역 상태를 오염시킬 위험이 있어(하네스가 중간에 실패해도 카운터가
/// 남는 등), 시나리오마다 독립된 가짜를 쓴다.
///
/// 리더기 설정 화면과 가맹점 설정 화면 둘 다 이 게이트를 센다(PRD.md §2.7). Phase 23
/// (docs/operations/development_plan.md P23-2)에서 이전 이름(리더기 설정 화면만 가리키던 이름)에서 리네임했다 —
/// 순수 리네임이며 동작은 바뀌지 않았다.
/// </summary>
internal sealed class FakeSetupScreenGate : ISetupScreenGate
{
    internal bool IsOpen { get; set; }

    public bool IsSetupScreenOpen => IsOpen;
}
