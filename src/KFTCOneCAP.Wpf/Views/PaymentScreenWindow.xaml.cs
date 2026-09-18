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
/// <b>이 창은 아직 홈 화면과 배선되지 않는다</b>(P29-7이 별도로 연결) — <c>SetupScreenGate</c> 등록도
/// 이 Task 범위가 아니다(PRD §12.4 — 이 화면은 애초에 그 게이트에 등록하지 않기로 확정됐다).
/// </summary>
public partial class PaymentScreenWindow : Window
{
    public PaymentScreenViewModel ViewModel { get; } = new();

    public PaymentScreenWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
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
