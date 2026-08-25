using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-10) 검증용 가짜 <see cref="IReaderSetupGate"/>.
/// **최종 산출물이 아니다.** 실제 <see cref="Views.ReaderSetupWindowGate"/>(<c>App.ReaderSetupGate</c>)를
/// 검증 하네스가 직접 건드리면 앱 전역 상태를 오염시킬 위험이 있어(하네스가 중간에 실패해도 카운터가
/// 남는 등), 시나리오마다 독립된 가짜를 쓴다.
/// </summary>
internal sealed class FakeReaderSetupGate : IReaderSetupGate
{
    internal bool IsOpen { get; set; }

    public bool IsReaderSetupOpen => IsOpen;
}
