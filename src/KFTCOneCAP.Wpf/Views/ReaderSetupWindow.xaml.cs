using System;
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
    public ReaderSetupViewModel ViewModel { get; } = new();

    public ReaderSetupWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.ResultsUpdated += ViewModel_ResultsUpdated;
        SourceInitialized += ReaderSetupWindow_SourceInitialized;
    }

    private void ReaderSetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // PRD 4.2: 초기 포커스는 확인(OK) 버튼. 포커스는 시각 트리가 구성된 뒤(Loaded)에만 가능한
        // 순수 View 동작이라 ViewModel로 옮기지 않는다.
        ConfirmButton.Focus();
    }

    /// <summary>
    /// ViewModel이 조회 결과를 갱신했을 때 목록 스크롤을 맨 위로 되돌린다(원본 QueryButton_Click의
    /// IntegrityScrollViewer.ScrollToTop()과 동일 동작). 스크롤 위치는 View 전용 상태라 ViewModel이
    /// 직접 다루지 않고 이벤트로만 알려온다.
    /// </summary>
    private void ViewModel_ResultsUpdated(object? sender, EventArgs e) => IntegrityScrollViewer.ScrollToTop();

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

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
            return;

        ViewModel.Save();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
            return;

        // PRD 4.12 취소 흐름 1: 열려있는 팝오버 먼저 닫기.
        if (MultipadInfoPopup.IsOpen)
            MultipadInfoPopup.IsOpen = false;

        // PRD 4.12 취소 흐름 2~4: 변경사항 판단은 ViewModel.IsDirty()에 위임하고, 확인창 표시
        // 여부만 여기서 결정한다.
        if (ViewModel.IsDirty())
        {
            var result = MessageBox.Show(
                this,
                "변경된 내용이 있습니다.\n저장하지 않고 종료하시겠습니까?",
                "리더기 설정",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
                return; // 창 유지
        }

        DialogResult = false;
        Close();
    }
}
