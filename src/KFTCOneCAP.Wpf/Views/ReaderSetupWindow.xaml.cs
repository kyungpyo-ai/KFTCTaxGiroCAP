using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 리더기 설정 화면.
/// Phase 4(ROADMAP.md "리더기 설정: 정적 레이아웃"): 정적 레이아웃만 구현. 버튼 클릭
/// (초기화/상태체크/키다운로드/무결성체크/업데이트/조회), AOP 제약, 포트 열기 토글, dirty-check 등
/// 실제 동작은 Phase 5~6에서 배선한다. 확인/취소 버튼은 이번 Phase에서는 그냥 창을 닫기만 한다.
/// </summary>
public partial class ReaderSetupWindow : Window
{
    public ReaderSetupWindow()
    {
        InitializeComponent();
        SourceInitialized += ReaderSetupWindow_SourceInitialized;
    }

    private void ReaderSetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // PRD 4.2: 초기 포커스는 확인(OK) 버튼.
        ConfirmButton.Focus();

        // 2026-08-14 추가(사용자 피드백): 콤보(COM 포트) 선택값에 따라 해당 리더기 카드의
        // 액션 버튼 5개 + 멀티패드 토글을 활성/비활성 연동한다. 정식 AOP/레지스트리 연동은
        // Phase 5~6 범위 — 여기서는 "미사용"이면 비활성, 아니면 활성이라는 가벼운
        // 코드비하인드 연동만 수행한다. "포트 열기" 토글은 아직 만들지 않았으므로(Hidden
        // 자리만 존재) 이 연동에서 제외한다.
        //
        // 핸들러를 XAML의 SelectionChanged 속성 대신 여기서(Loaded 이후) 붙이는 이유:
        // XAML에서 SelectedIndex="0"을 선언하면 InitializeComponent가 트리를 순서대로
        // 구성하는 도중 그 시점에 곧바로 SelectionChanged가 발생하는데, 이때 아직 이 콤보보다
        // 뒤에 선언된 액션 버튼 패널/토글의 x:Name 필드가 연결되지 않아 NullReferenceException이
        // 날 위험이 있다. Loaded 시점에는 전체 트리가 이미 구성되어 있어 안전하다.
        Reader1PortCombo.SelectionChanged += (_, _2) => ApplyReaderCardEnabled(Reader1PortCombo, Reader1ActionButtonsPanel, Reader1MultipadToggle);
        Reader2PortCombo.SelectionChanged += (_, _2) => ApplyReaderCardEnabled(Reader2PortCombo, Reader2ActionButtonsPanel, Reader2MultipadToggle);

        ApplyReaderCardEnabled(Reader1PortCombo, Reader1ActionButtonsPanel, Reader1MultipadToggle);
        ApplyReaderCardEnabled(Reader2PortCombo, Reader2ActionButtonsPanel, Reader2MultipadToggle);
    }

    private static void ApplyReaderCardEnabled(ComboBox portCombo, Panel actionButtonsPanel, UIElement multipadToggle)
    {
        var isPortSelected = (portCombo.SelectedItem as ComboBoxItem)?.Content as string != "미사용";
        actionButtonsPanel.IsEnabled = isPortSelected;
        multipadToggle.IsEnabled = isPortSelected;
    }

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

    // ===================== 확인 / 취소 =====================
    // TODO(Phase 5~6): TRANSINFO_AOP 검증, 레지스트리 저장(PRD 4.12), dirty-check 확인창(PRD 4.13)을
    // 여기에 배선할 것. 지금은 정적 레이아웃 검증 목적으로 단순히 창을 닫기만 한다.

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
