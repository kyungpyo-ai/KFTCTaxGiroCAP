using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using KFTCOneCAP.Wpf.ViewModels;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 리더기 설정 화면.
/// Phase 5(ROADMAP.md "리더기 설정: 비즈니스 로직(스텁) + 레지스트리 저장/dirty-check"):
/// 액션 버튼 5종/조회 버튼의 로딩→완료 스텁, 멀티패드 정보 팝오버, 레지스트리 저장, 스냅샷
/// 기반 dirty-check를 배선한다. AOP 제약(PRD 4.11)·TRANSINFO_AOP 저장 차단·포트 열기 토글
/// (PRD 4.8)은 명시적으로 이번 Phase 범위에서 제외 — docs/home_reader_setup/ROADMAP.md Phase 5 상단 안내 참고.
///
/// Phase 7(MVVM 전환, docs/payment_relay/development_plan.md P7-2/P7-3): 레지스트리 로드/저장,
/// dirty-check, busy 상태, "미사용" 비활성 판정, 조회 결과는 전부 <see cref="ReaderSetupViewModel"/>로
/// 이관됐고, 버튼 Content/스피너/리스트 Visibility/ItemsSource는 XAML 바인딩으로 대체됐다.
/// 이 코드비하인드에 남은 것은 아래 세 가지뿐이다 — 전부 창 핸들/렌더링/시각 요소 배치처럼
/// View·OS 고유 책임이라 ViewModel로 옮기면 오히려 나빠진다(계층 규칙, ROADMAP.md "계층 구조"):
/// 1) DWM 타이틀바(SourceInitialized) — 창 핸들(HWND)이 있어야만 가능한 순수 OS 호출.
/// 2) 멀티패드 info Popup의 PlacementTarget 지정/열기·닫기 — Popup은 시각 요소 배치이며 데이터가
///    아니다.
/// 3) IntegrityScrollViewer_ScrollChanged의 헤더 padding 보정 — 스크롤바 실제 렌더링 폭에 맞추는
///    순수 렌더링 보정으로, ViewModel이 알 수 없는 값(스크롤바 두께)에 의존한다.
/// 여기에 더해 ConfirmButton.Focus()(초기 포커스, PRD 4.2)와 확인/취소 버튼의 Window.Close()/
/// DialogResult/MessageBox 호출도 Window 타입 자체를 다루는 동작이라 View에 남아 있다 — dirty-check
/// "판단"과 레지스트리 "저장"은 ViewModel(IsDirty()/Save())에 위임하고, 그 결과로 창을 어떻게
/// 닫을지만 여기서 결정한다.
/// </summary>
public partial class ReaderSetupWindow : Window
{
    // Phase 12(P12-1): ReaderConnectionManager는 App.xaml.cs(OnStartup)가 앱 수명 동안 하나만
    // 만든다 — 이 창이 열렸다 닫혔다 할 때마다 ViewModel이 새로 생성되지만, 포트 자체는 그 사이
    // 계속 열려 있어야 한다(PRD §2.2.2). 여기서는 그 인스턴스를 ViewModel 생성자에 전달만 한다
    // (이 창/ViewModel 둘 다 ReaderConnectionManager를 소유/생성하지 않는다).
    public ReaderSetupViewModel ViewModel { get; } = new(App.ReaderConnections!);

    /// <summary>
    /// 2026-08-20 추가(Opus 리뷰 후속) — <c>HomeWindow.WarmUpReaderSetupWindow</c>가 화면 밖에
    /// 만들었다가 자신의 <c>Loaded</c> 직후 바로 닫는 워밍업 인스턴스를 표시한다. 이 인스턴스는
    /// 사용자가 실제로 조작한 적이 없으므로(dirty할 수 없다) <see cref="ReaderSetupWindow_Closing"/>의
    /// dirty-check + <see cref="ReaderSetupViewModel.DiscardPortChanges"/>를 실행할 이유가
    /// 없다 — 그대로 실행해도 기능상 안전(항상 not-dirty 경로)하지만, 매 기동마다
    /// <see cref="Services.Reader.ReaderConnectionManager.EnsureOpenForSelection"/>을 2번 불필요하게
    /// 호출하는 낭비가 있었다. 워밍업 생성 쪽(HomeWindow)이 이 값을 true로 설정해 알린다.
    /// </summary>
    internal bool IsWarmupInstance { get; set; }

    public ReaderSetupWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.ResultsUpdated += ViewModel_ResultsUpdated;
        ViewModel.ResultMessageReady += ViewModel_ResultMessageReady;
        SourceInitialized += ReaderSetupWindow_SourceInitialized;
        Closed += ReaderSetupWindow_Closed;
    }

    /// <summary>
    /// Phase 15(P15-4) — 이 창이 (워밍업이 아니게) 실제로 열려 있는 동안 결제 Flow가 카드 리딩을
    /// 시도하지 않도록 <see cref="App.SetupScreenGate"/>에 등록한다(가맹점 설정 화면과 카운터를 공유,
    /// PRD.md §2.7). <see cref="IsWarmupInstance"/>는
    /// 객체 초기화 구문(<c>new ReaderSetupWindow { IsWarmupInstance = true, ... }</c>)으로 설정되므로
    /// 생성자 시점에는 아직 반영되지 않는다 — 이 값이 이미 확정된 뒤에 실행되는 <c>Loaded</c>에서
    /// 판정해야 정확하다(<c>Loaded</c>는 <c>Show()</c> 이후에 발생하고, 객체 초기화는 <c>Show()</c>
    /// 호출보다 항상 먼저 끝난다).
    /// </summary>
    private bool _registeredInGate;

    private void ReaderSetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // PRD 4.2: 초기 포커스는 확인(OK) 버튼. 포커스는 시각 트리가 구성된 뒤(Loaded)에만 가능한
        // 순수 View 동작이라 ViewModel로 옮기지 않는다.
        ConfirmButton.Focus();

        // (2026-08-25, Opus 검증 리뷰 L-2 수정) _registeredInGate 가드: 현재 사용 경로에서는 Loaded가
        // 인스턴스당 정확히 1회만 발생해 재현되지 않지만, 언젠가 Loaded가 재진입하게 바뀌면
        // 이중 등록으로 카운터가 새고 — 그 실패 모드가 "결제가 영구히 거부됨"이라 예방 가치가 크다.
        if (!IsWarmupInstance && !_registeredInGate)
        {
            App.SetupScreenGate.Register();
            _registeredInGate = true;
        }
    }

    /// <summary>
    /// Phase 15(P15-4) — <see cref="ReaderSetupWindow_Loaded"/>에서 등록했으면 반드시 여기서 해제한다.
    /// <c>Closing</c>이 아니라 <c>Closed</c>에 두는 이유: <c>Closing</c>은 <see cref="CancelEventArgs.Cancel"/>로
    /// 취소될 수 있어 "실제로 닫혔다"를 보장하지 못한다 — 등록/해제 카운트가 어긋나면 결제 Flow가
    /// 창이 이미 닫혔는데도 계속 거부하는 결함이 된다.
    /// </summary>
    private void ReaderSetupWindow_Closed(object? sender, EventArgs e)
    {
        if (_registeredInGate)
        {
            App.SetupScreenGate.Unregister();
            _registeredInGate = false;
        }
    }

    /// <summary>
    /// ViewModel이 조회 결과를 갱신했을 때 목록 스크롤을 맨 위로 되돌린다(원본 QueryButton_Click의
    /// IntegrityScrollViewer.ScrollToTop()과 동일 동작). 스크롤 위치는 View 전용 상태라 ViewModel이
    /// 직접 다루지 않고 이벤트로만 알려온다.
    /// </summary>
    private void ViewModel_ResultsUpdated(object? sender, EventArgs e) => IntegrityScrollViewer.ScrollToTop();

    /// <summary>
    /// P12-3 — ViewModel은 MessageBox를 직접 호출하지 않는다(계층 규칙, ReaderSetupViewModel 상단
    /// 주석 참고). 초기화/상태체크/무결성체크 결과 문구(PRD §6.1/§6.2/§6.4)가 준비되면 이 이벤트로
    /// 알려오고, 여기서만 모달로 보여준다.
    /// </summary>
    private void ViewModel_ResultMessageReady(object? sender, string message) =>
        MessageBox.Show(this, message, "리더기 설정", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>
    /// 흰색(라이트) 타이틀바 강제 적용. HomeWindow와 동일한 로직(중복이지만 이번 Phase 범위에서는
    /// 공용 헬퍼로 추출하지 않음 — 서브 창이 하나뿐이라 과도한 추상화로 판단, CLAUDE.md 원칙 참고).
    /// </summary>
    private void ReaderSetupWindow_SourceInitialized(object? sender, EventArgs e)
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

    /// <summary>
    /// 무결성 체크 테이블의 ScrollViewer에 스크롤바가 생기면 본문 데이터 영역의 너비가 스크롤바 폭만큼 줄어들어
    /// 헤더와 데이터 열의 정렬이 어긋나는 것을 방지하기 위해 헤더 Border의 우측 Padding을 스크롤바 너비만큼 동적 동기화한다.
    /// </summary>
    private void IntegrityScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var hasScrollbar = e.ExtentHeight > e.ViewportHeight;
        var scrollBarWidth = hasScrollbar ? SystemParameters.VerticalScrollBarWidth : 0;
        TableHeaderBorder.Padding = new Thickness(0, 0, scrollBarWidth, 0);
    }

    // ===================== 정보 팝오버(멀티패드, PRD 4.10) =====================
    // 리더기1/2 멀티패드 info 버튼이 단일 Popup을 공유한다. 같은 버튼 재클릭 시 닫히고,
    // 다른 버튼 클릭 시 PlacementTarget만 바뀌어 자동으로 이전 팝오버가 닫히고 새로 뜬다.
    // "포트 열기" info 버튼은 자리가 Hidden이라 이번 Phase에서 배선하지 않는다.
    // Popup의 PlacementTarget/IsOpen은 시각 요소 배치이며 ViewModel이 다룰 데이터가 아니므로
    // View에 남는다(클래스 상단 주석 참고).

    private void MultipadInfoButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (MultipadInfoPopup.IsOpen && ReferenceEquals(MultipadInfoPopup.PlacementTarget, button))
        {
            MultipadInfoPopup.IsOpen = false;
            return;
        }

        MultipadInfoPopup.PlacementTarget = button;
        MultipadInfoPopup.IsOpen = true;
    }

    // ===================== 확인 / 취소 (PRD 4.12) =====================
    // TODO(별도 단계, ROADMAP.md Phase 5 상단 안내): TRANSINFO_AOP 검증(포트 미지정 시 저장 차단)은
    // 이번 Phase 범위에서 제외됨. 여기서는 ViewModel의 dirty-check 판단(IsDirty)과 저장(Save)
    // 결과에 따라 창을 닫을지만 결정한다 — Window.Close()/DialogResult/MessageBox는 Window 타입
    // 자체를 다루는 동작이라 View에 남는다(계층 규칙).

    /// <summary>
    /// 2026-08-20 Opus 리뷰에서 발견 — X(제목표시줄 닫기)/Alt+F4로 창을 닫으면 취소 버튼 핸들러를
    /// 전혀 거치지 않아 dirty-check 확인창도, <see cref="ReaderSetupViewModel.DiscardPortChanges"/>도
    /// 실행되지 않았다(실장비로 재현: 콤보 변경 후 액션 버튼으로 저장 전 포트에 연결해본 뒤 X로
    /// 닫으면, 레지스트리는 옛 값인데 실제 연결은 새 포트로 남아 Phase 15 결제 Flow가 잘못된 포트를
    /// 쓰게 된다). 이 플래그는 확인/취소 버튼이 자신의 정상 경로로 이미 뒷정리(저장 또는
    /// DiscardPortChanges)를 마치고 <see cref="Window.Close"/>를 호출했음을 <see cref="ReaderSetupWindow_Closing"/>에
    /// 알려, 그 경로에서 같은 처리를 중복 실행하지 않도록 한다.
    /// </summary>
    private bool _closeHandled;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
            return;

        // 2026-08-20 Opus 리뷰에서 발견 — 리더기1/2에 같은 포트를 지정해도 경고 없이 저장됐다.
        // 포트는 배타적으로 열리므로 실제로는 한쪽이 항상 READER_ERR_PORT_ALREADY_OPEN으로
        // 실패한다(PRD에 이 케이스 규정이 없어 스펙 공백 — 저장 자체를 막기로 확정).
        if (ViewModel.IsDuplicatePortSelected())
        {
            MessageBox.Show(
                this,
                "리더기1과 리더기2에 같은 COM 포트를 지정할 수 없습니다.\n서로 다른 포트를 선택해주세요.",
                "리더기 설정",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return; // 창 유지, 저장하지 않음
        }

        ViewModel.Save();
        _closeHandled = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
            return;

        if (!ConfirmDiscardIfDirty())
            return; // 창 유지

        ViewModel.DiscardPortChanges();
        _closeHandled = true;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 2026-08-20 추가 — X/Alt+F4 등 취소 버튼을 거치지 않는 모든 닫기 경로를 여기서 가로챈다.
    /// 확인/취소 버튼이 이미 자신의 정상 경로로 처리를 마쳤다면(<see cref="_closeHandled"/>) 아무
    /// 것도 하지 않는다. 그 외의 경우(X 클릭 등)는 취소와 동일하게 dirty-check 확인 →
    /// <see cref="ReaderSetupViewModel.DiscardPortChanges"/>를 실행한다. 작업 중(IsBusy)에는 닫기
    /// 자체를 막는다 — 진행 중인 명령을 창 파괴로 끊으면 콜백이 이미 죽은 ViewModel을 참조하게 된다.
    /// <see cref="IsWarmupInstance"/>면 사용자가 조작한 적이 없어 항상 not-dirty이므로(기능상
    /// 안전) 이 로직 자체를 실행할 이유가 없다 — 매 기동마다 불필요한
    /// <see cref="Services.Reader.ReaderConnectionManager.EnsureOpenForSelection"/> 호출을 피하기
    /// 위해 곧바로 건너뛴다.
    /// </summary>
    private void ReaderSetupWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeHandled || IsWarmupInstance)
            return;

        if (ViewModel.IsBusy)
        {
            e.Cancel = true;
            return;
        }

        if (!ConfirmDiscardIfDirty())
        {
            e.Cancel = true;
            return;
        }

        ViewModel.DiscardPortChanges();
        _closeHandled = true;
    }

    /// <summary>
    /// PRD 4.12 취소 흐름: 열려있는 팝오버를 먼저 닫고, 변경사항 판단은
    /// <see cref="ReaderSetupViewModel.IsDirty"/>에 위임한다. dirty면 확인창을 띄우고 "아니오"
    /// 선택 시 false(창 유지)를 반환한다. <see cref="CancelButton_Click"/>과
    /// <see cref="ReaderSetupWindow_Closing"/> 둘 다 이 메서드 하나를 공유한다(중복 구현 금지).
    /// </summary>
    private bool ConfirmDiscardIfDirty()
    {
        if (MultipadInfoPopup.IsOpen)
            MultipadInfoPopup.IsOpen = false;

        if (!ViewModel.IsDirty())
            return true;

        var result = MessageBox.Show(
            this,
            "변경된 내용이 있습니다.\n저장하지 않고 종료하시겠습니까?",
            "리더기 설정",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }
}
