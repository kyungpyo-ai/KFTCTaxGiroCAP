using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using KFTCOneCAP.Wpf.ViewModels.Payment;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// Phase 29 P29-6(docs/payment_relay/development_plan.md "P29-6", PRD.md §12) — 결제 화면.
///
/// 이 코드비하인드에는 창 핸들(HWND)에 직접 묶인 DWM 타이틀바 설정 하나만 남아 있다(공통 규칙 —
/// 코드비하인드는 창 핸들 관련 코드만, ReaderSetupWindow.xaml.cs와 동일 패턴이라 중복이지만 서브 창이
/// 하나뿐이라 공용 헬퍼로 추출하지 않는다, CLAUDE.md 원칙). 그 외 모든 상태·커맨드·전송 로직은
/// <see cref="PaymentScreenViewModel"/>에 있다.
///
/// 홈 화면 "결제" 카드(<see cref="HomeWindow.OpenPaymentScreen"/>, P29-7)에서 연다. <c>SetupScreenGate</c>에는
/// 등록하지 않는다(PRD §12.4 — 이 화면은 애초에 그 게이트에 등록하지 않기로 확정됐다).
/// </summary>
public partial class PaymentScreenWindow : Window
{
    public PaymentScreenViewModel ViewModel { get; } = new();

    /// <summary>
    /// StartupUri(<c>--payment-screen-test</c> 진단 인자, App.xaml.cs)가 요구하는 공개 매개변수 없는
    /// 생성자 — WPF가 리플렉션으로 이 시그니처를 직접 호출하므로 없앨 수 없다. Owner 없이 열리는
    /// 경로이므로 <see cref="PaymentScreenWindow(Window)"/>에 <c>null</c>을 넘기는 것과 동일하다.
    /// </summary>
    public PaymentScreenWindow() : this(null)
    {
    }

    /// <summary>
    /// 홈 화면(<see cref="HomeWindow.OpenPaymentScreen"/>)이 이 창을 열 때 쓰는 생성자. Owner를
    /// 매개변수로 받는다.
    ///
    /// 2026-09-21 체크포인트 2 L-1 수정 — 이전에는 <c>new PaymentScreenWindow { Owner = this }</c>
    /// (객체 이니셜라이저)로 Owner를 대입했는데, 객체 이니셜라이저는 생성자 실행이 "끝난 뒤"에 속성을
    /// 대입한다. 그런데 WPF는 <c>WindowStartupLocation</c>에 따른 위치 계산을 <c>Show()</c> 내부에서
    /// <c>SourceInitialized</c>/<c>Loaded</c>가 발생하기도 전에 끝내버리므로, 생성자 본문에서
    /// <c>Owner == null</c>을 검사하면 홈 경로에서도 항상 참이었다(즉 아래 CenterScreen 대체가 항상
    /// 적용되고 XAML의 CenterOwner는 한 번도 실제로 쓰이지 못했다) — <c>Loaded</c> 핸들러로 옮겨도
    /// 이미 위치 계산이 끝난 뒤라 마찬가지로 소용없다. Owner를 생성자 매개변수로 받아 이 본문 안에서
    /// 대입하면, 아래 판정 시점에 Owner가 이미 최종값으로 확정돼 있다.
    /// </summary>
    public PaymentScreenWindow(Window? owner)
    {
        InitializeComponent();
        DataContext = ViewModel;

        if (owner != null)
            Owner = owner;

        // 2026-09-18 발견(사용자가 1024×768 컴팩트 해상도로 확인 중 발견) — XAML의
        // WindowStartupLocation="CenterOwner"는 Owner가 있을 때를 겨냥한 값이다. Owner 없이 이 창이
        // 단독 실행되면 WPF가 CenterOwner를 화면 중앙이 아니라 OS 기본 캐스케이드 위치(예: 104,104)로
        // 처리해, 컴팩트 창 폭(1000)이 1024 화면 폭을 넘어 오른쪽이 잘렸다. Owner가 없을 때만
        // CenterScreen으로 대체한다(HomeWindow.xaml과 동일 값) — Owner가 있는 정상 경로는 위에서 이미
        // 대입이 끝났으므로 그대로 CenterOwner를 쓴다.
        if (Owner == null)
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    /// <summary>흰색(라이트) 타이틀바 강제 적용 — HomeWindow/ReaderSetupWindow와 동일 로직.</summary>
    private void PaymentScreenWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (!IsWindows10Build17763OrGreater())
            return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int useImmersiveDarkMode = 0; // 0 = 라이트(흰색) 타이틀바
        int attribute = Environment.OSVersion.Version.Build >= 18985
            ? DWMWA_USE_IMMERSIVE_DARK_MODE
            : DWMWA_USE_IMMERSIVE_DARK_MODE_OLD;

        DwmSetWindowAttribute(hwnd, attribute, ref useImmersiveDarkMode, sizeof(int));
    }

    private static bool IsWindows10Build17763OrGreater()
    {
        var v = Environment.OSVersion.Version;
        return v.Major > 10 || (v.Major == 10 && v.Build >= 17763);
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
