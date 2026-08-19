using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using KFTCOneCAP.Wpf.ViewModels;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 홈 화면.
/// Phase 2: 정적 레이아웃. Phase 3(ROADMAP.md "홈 화면: 인터랙션 &amp; 트레이"): 카드 호버/눌림
/// 애니메이션(Themes/Buttons.xaml HomeCardButtonStyle에 배선), 카드 클릭 시 서브 화면 오픈,
/// 최소화 버튼 → 시스템 트레이 이동, 트레이 우클릭 메뉴/더블클릭 복원을 이번 Phase에서 배선한다.
/// Phase 7(MVVM 전환, docs/payment_relay/development_plan.md P7-5): 카드 클릭이 "무엇을 할지"만
/// HomeViewModel의 Command/이벤트로 옮겨졌다. 트레이 아이콘/DWM 타이틀바/창 워밍업/눌림 애니메이션
/// 프레임 확보(Dispatcher.BeginInvoke)는 전부 View/OS 책임이라 그대로 이 코드비하인드에 남아 있다
/// (HomeViewModel 상단 주석 참고 — ViewModel로 옮기면 Window/WinForms 타입을 알아야 해서 계층
/// 규칙이 깨진다).
/// </summary>
public partial class HomeWindow : Window
{
    /// <summary>
    /// WPF에는 네이티브 트레이 아이콘 API가 없어 PRD 3.6 지침대로 WinForms interop을 사용한다
    /// (csproj UseWindowsForms=true). net48이라 Windows 7까지 문제없이 동작.
    /// </summary>
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public HomeViewModel ViewModel { get; } = new();

    public HomeWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.ReaderSetupRequested += OnReaderSetupRequested;
        ViewModel.NotImplementedCardRequested += OnNotImplementedCardRequested;
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
    // Phase 7(P7-5): 카드 Click 핸들러는 XAML의 Command 바인딩(HomeViewModel의 [RelayCommand])으로
    // 대체됐다. 아래 두 핸들러는 ViewModel이 "무엇을 할지" 알려온 이벤트를 받아 실제 Window/타이밍
    // 처리를 담당한다(ViewModel이 Window 타입을 몰라야 하므로 이 부분은 View 책임으로 남는다).

    /// <summary>
    /// 리더기 설정 카드. Phase 4(ROADMAP.md)부터 실제 ReaderSetupWindow를 모달로 연다
    /// (PRD 3.7/4.2). 확인/취소 버튼의 실제 저장/검증 로직은 Phase 5~6에서 배선 예정 —
    /// 지금은 창을 닫는 동작만 있다.
    ///
    /// 2026-08-14 수정(사용자 피드백: "리더기 버튼 눌렀을때만 살짝 끊기는것처럼 보임"): 클릭
    /// 즉시(Click 핸들러 안에서) 무거운 ReaderSetupWindow를 생성+ShowDialog()하면, 카드의
    /// 눌림 애니메이션(HomeCardButtonStyle, PRD 3.4)이 아직 재생 중인데 UI 스레드가 창 생성으로
    /// 막혀 그 프레임이 스킵되어 끊김으로 보인다 — PRD 3.4가 이미 예상한 케이스("클릭 즉시
    /// ShowDialog() 호출 전 짧은 딜레이 또는 Dispatcher.BeginInvoke로 근사 재현"). 창 생성/오픈을
    /// 한 프레임 뒤(Input 우선순위)로 미뤄 눌림 애니메이션이 먼저 렌더링을 마치도록 한다.
    /// </summary>
    private void OnReaderSetupRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(OpenReaderSetup));

    /// <summary>
    /// 가맹점 설정/결제/전표 설정 카드는 본 프로젝트 범위 밖 화면(PRD 1.3 비범위, PRD 6장 미확정
    /// 사항 #5)이다. 임의로 실동작을 만들지 않고 "준비 중" 안내만 표시한다(카드 자체를 비활성화하지
    /// 않은 이유: 원본 화면에서 카드가 눌리지 않는 것처럼 보이는 것도 임의 판단이라 UX상 더 이상하다고
    /// 판단 — PM 확인 시 이 처리 방식은 재검토 필요).
    /// </summary>
    private void OnNotImplementedCardRequested(object? sender, string cardName) => ShowNotImplementedCard(cardName);

    private void OpenReaderSetup()
    {
        var dialog = new ReaderSetupWindow { Owner = this };
        dialog.ShowDialog();
    }

    /// <summary>
    /// 홈 화면이 다 뜬 뒤 유휴 시간에 ReaderSetupWindow를 한 번 미리 만들었다가 바로 닫아
    /// "워밍업"한다(화면에 실제로 보이지 않도록 화면 밖에 배치 + 작업표시줄 숨김).
    ///
    /// 2026-08-14 추가(사용자 요청: "리더기설정창이 띄어질때 자체의 렉을 줄일방법은없어?"):
    /// 사용자가 실제로 카드를 처음 클릭하는 시점에 ReaderSetupWindow를 생성하면, 이 창의
    /// XAML(BAML) 최초 로드, 관련 .NET 타입(ItemsControl/Popup 등)의 최초 JIT, 리소스
    /// 딕셔너리 스타일의 최초 lookup/캐싱 비용이 전부 "그 클릭 순간"에 한꺼번에 발생해
    /// 체감 렉의 원인이 된다. 앱이 유휴 상태일 때 미리 한 번 만들어 버리면 이 비용들이
    /// 대부분 캐시/JIT되어, 실제 사용자가 여는 시점에는 새 인스턴스를 만들더라도 훨씬 빠르다.
    /// 워밍업 인스턴스는 화면 밖 좌표에 표시했다가 Loaded 직후 바로 닫아 사용자 눈에 보이지
    /// 않게 한다. 실패해도 앱 동작에 영향 없도록 전체를 try/catch로 감싼다.
    /// </summary>
    private void HomeWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(WarmUpReaderSetupWindow));
    }

    private void WarmUpReaderSetupWindow()
    {
        try
        {
            var warmup = new ReaderSetupWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            warmup.Loaded += (_, _) => warmup.Close();
            warmup.Show();
        }
        catch
        {
            // 워밍업은 순수 최적화 목적 — 실패해도 실제 카드 클릭 시 정상 경로(OpenReaderSetup)로
            // 창이 열리므로 조용히 무시한다.
        }
    }

    private void ShowNotImplementedCard(string name)
    {
        MessageBox.Show(
            this,
            $"{name} 화면은 이 프로젝트의 구현 범위 밖입니다.\n(docs/home_reader_setup/PRD_WPF.md 1.3 비범위 참고)",
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
            OpenReaderSetup();
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
