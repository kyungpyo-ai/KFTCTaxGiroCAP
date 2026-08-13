using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 홈 화면 (Phase 2: 정적 레이아웃만. 카드 클릭/애니메이션/트레이는 Phase 3에서 배선).
/// </summary>
public partial class HomeWindow : Window
{
    public HomeWindow()
    {
        InitializeComponent();
        SourceInitialized += HomeWindow_SourceInitialized;
    }

    /// <summary>
    /// 흰색(라이트) 타이틀바 강제 적용.
    /// DWMWA_USE_IMMERSIVE_DARK_MODE 은 Windows 10 1809(빌드 17763)+ 전용 API이므로
    /// CLAUDE.md/PRD 1.4 지침대로 OS 버전을 확인한 뒤에만 호출하고, 미지원 OS(Windows 7 등)에서는
    /// 원본 MFC 앱과 동일하게 아무 동작도 하지 않는다(no-op).
    /// </summary>
    private void HomeWindow_SourceInitialized(object? sender, EventArgs e)
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
