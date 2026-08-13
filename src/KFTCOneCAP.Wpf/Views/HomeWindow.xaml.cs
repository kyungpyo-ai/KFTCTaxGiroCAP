using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 홈 화면.
/// Phase 2: 정적 레이아웃. Phase 3(ROADMAP.md "홈 화면: 인터랙션 &amp; 트레이"): 카드 호버/눌림
/// 애니메이션(Themes/Buttons.xaml HomeCardButtonStyle에 배선), 카드 클릭 시 서브 화면 오픈,
/// 최소화 버튼 → 시스템 트레이 이동, 트레이 우클릭 메뉴/더블클릭 복원을 이번 Phase에서 배선한다.
/// </summary>
public partial class HomeWindow : Window
{
    /// <summary>
    /// WPF에는 네이티브 트레이 아이콘 API가 없어 PRD 3.6 지침대로 WinForms interop을 사용한다
    /// (csproj UseWindowsForms=true). net48이라 Windows 7까지 문제없이 동작.
    /// </summary>
    private System.Windows.Forms.NotifyIcon? _trayIcon;

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

    // ===================== 카드 클릭 (PRD 3.7) =====================

    /// <summary>
    /// 리더기 설정 카드. 실제 리더기 설정 화면은 Phase 4(ROADMAP.md)에서 정적 레이아웃부터
    /// 새로 만든다 — 지금은 존재하지 않으므로 임시 플레이스홀더 안내창으로 대체한다.
    /// TODO(Phase 4): 여기를 실제 ReaderSetupWindow.ShowDialog(this) 호출로 교체할 것.
    /// </summary>
    private void ReaderSetupCardButton_Click(object sender, RoutedEventArgs e) => OpenReaderSetupPlaceholder();

    /// <summary>
    /// 가맹점 설정/결제/전표 설정 카드는 본 프로젝트 범위 밖 화면(PRD 1.3 비범위, PRD 6장 미확정
    /// 사항 #5)이다. 임의로 실동작을 만들지 않고 "준비 중" 안내만 표시한다(카드 자체를 비활성화하지
    /// 않은 이유: 원본 화면에서 카드가 눌리지 않는 것처럼 보이는 것도 임의 판단이라 UX상 더 이상하다고
    /// 판단 — PM 확인 시 이 처리 방식은 재검토 필요).
    /// </summary>
    private void ShopSetupCardButton_Click(object sender, RoutedEventArgs e) => ShowNotImplementedCard("가맹점 설정");

    private void TransCardButton_Click(object sender, RoutedEventArgs e) => ShowNotImplementedCard("결제");

    private void ReceiptSetupCardButton_Click(object sender, RoutedEventArgs e) => ShowNotImplementedCard("전표 설정");

    private void OpenReaderSetupPlaceholder()
    {
        MessageBox.Show(
            this,
            "리더기 설정 화면은 Phase 4(정적 레이아웃)부터 순차 구현될 예정입니다.\n(docs/ROADMAP.md 참고)",
            "리더기 설정",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowNotImplementedCard(string name)
    {
        MessageBox.Show(
            this,
            $"{name} 화면은 이 프로젝트의 구현 범위 밖입니다.\n(docs/PRD_WPF.md 1.3 비범위 참고)",
            name,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ===================== 최소화 / 종료 =====================

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => MinimizeToTray();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ShutdownApp();

    private void HomeWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 타이틀바 X 버튼(또는 Alt+F4)으로 닫는 경우에도 트레이 아이콘 잔상이 남지 않도록 정리.
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void ShutdownApp()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        Application.Current.Shutdown();
    }

    // ===================== 트레이 아이콘 (PRD 3.6) =====================

    private void MinimizeToTray()
    {
        EnsureTrayIcon();
        _trayIcon!.Visible = true;
        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_trayIcon != null)
            _trayIcon.Visible = false;
    }

    /// <summary>
    /// NotifyIcon 지연 생성. 아이콘은 별도 .ico 자산이 아직 없어(Phase 3 범위에 트레이 아이콘
    /// 그래픽 자산 준비가 포함되지 않음) 실행 파일에 내장된 기본 아이콘을 재사용한다.
    /// TODO: 전용 트레이 아이콘(.ico) 자산이 확보되면 교체할 것.
    /// </summary>
    private void EnsureTrayIcon()
    {
        if (_trayIcon != null)
            return;

        System.Drawing.Icon icon;
        try
        {
            icon = System.Drawing.Icon.ExtractAssociatedIcon(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? System.Drawing.SystemIcons.Application;
        }
        catch
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "KFTCOneCAP",
            Visible = false,
            ContextMenuStrip = BuildTrayContextMenu(),
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    /// <summary>
    /// PRD 3.6 트레이 우클릭 메뉴: 열기 / 리더기 설정 / 가맹점 설정 / 구분선 / 프로그램 종료.
    /// WinForms ContextMenuStrip을 사용(WPF ContextMenu를 NotifyIcon에 붙이려면 SetForegroundWindow
    /// 등 별도 포커스 트릭이 필요해 신뢰성이 떨어짐 — CLAUDE.md 서브에이전트 지침대로 과도한
    /// 추상화 없이 표준적이고 안정적인 방식을 택함). "완전히 동일한 커스텀 스타일"까지는 구현하지
    /// 않았고 항목 구성/동작만 PRD를 따름(작업 지시사항 명시적으로 허용).
    /// </summary>
    private System.Windows.Forms.ContextMenuStrip BuildTrayContextMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        menu.Items.Add("KFTCOneCAP 열기", null, (_, _) => RestoreFromTray());
        menu.Items.Add("리더기 설정", null, (_, _) =>
        {
            RestoreFromTray();
            OpenReaderSetupPlaceholder();
        });
        menu.Items.Add("가맹점 설정", null, (_, _) =>
        {
            RestoreFromTray();
            ShowNotImplementedCard("가맹점 설정");
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("프로그램 종료", null, (_, _) => ShutdownApp());

        return menu;
    }
}
