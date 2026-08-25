using System.Threading;
using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-4) — <see cref="IReaderSetupGate"/>의 운영
/// 구현. <see cref="ReaderSetupWindow"/>가 (워밍업이 아닌) 실제로 열려 있는 인스턴스 수를 센다.
///
/// 등록/해제 지점을 이 클래스가 아니라 <see cref="ReaderSetupWindow"/> 자신의 <c>Loaded</c>/
/// <c>Closed</c>에 두는 이유: 호출자가 "창을 열 때마다 잊지 않고 알려야 한다"는 규칙을 지킬 필요가
/// 없게 하기 위함이다(창 자신이 자기 생애주기를 보고한다) — 창 생성/종료 지점이 여러 곳(정상 오픈,
/// <c>HomeWindow.WarmUpReaderSetupWindow</c>의 워밍업 인스턴스)이라도 이 카운터를 건드리는 코드는
/// 늘지 않는다.
///
/// 워밍업 인스턴스(<see cref="ReaderSetupWindow.IsWarmupInstance"/>)는 화면 밖에서 만들어졌다가
/// <c>Loaded</c> 직후 바로 닫히는 순수 성능 최적화용 인스턴스라 사용자에게 보이지 않는다 — 이
/// 인스턴스를 카운트에 포함시키면 앱 기동 직후 짧은 순간 결제 요청이 스푸리어스하게 거부될 수
/// 있으므로, <see cref="ReaderSetupWindow"/>가 등록을 호출할 때 그 판단을 자신이 하고 워밍업이면
/// 아예 호출하지 않는다(이 클래스는 그 판단을 모른다 — 호출 여부만으로 카운트한다).
///
/// <see cref="Interlocked"/>로 여닫음: 창은 UI 스레드에서만 열리고 닫히므로 실제 경합은 없지만,
/// <see cref="IsReaderSetupOpen"/>은 결제 워커 스레드(별도 스레드)에서 읽으므로 그 읽기/쓰기 사이의
/// 메모리 가시성을 보장하기 위해 원자적 연산을 쓴다.
/// </summary>
internal sealed class ReaderSetupWindowGate : IReaderSetupGate
{
    private int _openCount;

    public bool IsReaderSetupOpen => Volatile.Read(ref _openCount) > 0;

    internal void Register() => Interlocked.Increment(ref _openCount);

    internal void Unregister() => Interlocked.Decrement(ref _openCount);
}
