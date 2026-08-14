using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using KFTCOneCAP.Wpf.Controls;
using KFTCOneCAP.Wpf.Models;
using Microsoft.Win32;

namespace KFTCOneCAP.Wpf.Views;

/// <summary>
/// 리더기 설정 화면.
/// Phase 5(ROADMAP.md "리더기 설정: 비즈니스 로직(스텁) + 레지스트리 저장/dirty-check"):
/// 액션 버튼 5종/조회 버튼의 로딩→완료 스텁, 멀티패드 정보 팝오버, 레지스트리 저장, 스냅샷
/// 기반 dirty-check를 배선한다. AOP 제약(PRD 4.11)·TRANSINFO_AOP 저장 차단·포트 열기 토글
/// (PRD 4.8)은 명시적으로 이번 Phase 범위에서 제외 — docs/ROADMAP.md Phase 5 상단 안내 참고.
/// </summary>
public partial class ReaderSetupWindow : Window
{
    private const string RegistryKeyPath = @"Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT";

    /// <summary>
    /// PRD 4.7 "동시에 하나의 작업만 진행 가능(다른 버튼 클릭 무시)" — 화면 전체(리더기1/2
    /// 액션버튼 + 조회버튼 전부 포함)에서 하나의 비동기 작업이 진행 중이면 다른 클릭을 무시한다.
    /// </summary>
    private bool _isBusy;

    // PRD 4.13/4.12: 콤보1/2 선택값 + 멀티패드1/2 토글 상태 스냅샷(취소 시 dirty-check용).
    // 포트열기 토글은 아직 없고(Phase 6 예정), 있더라도 PRD 4.12는 dirty-check 대상에서 제외한다.
    private string _snapshotReader1Port = string.Empty;
    private string _snapshotReader2Port = string.Empty;
    private bool _snapshotReader1Multipad;
    private bool _snapshotReader2Multipad;

    public ReaderSetupWindow()
    {
        InitializeComponent();
        SourceInitialized += ReaderSetupWindow_SourceInitialized;
    }

    private void ReaderSetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // PRD 4.2: 초기 포커스는 확인(OK) 버튼.
        ConfirmButton.Focus();

        // 2026-08-14 추가(Phase 5 보완, 사용자 피드백): 창을 열 때 저장된 레지스트리 값을 읽어
        // 콤보/토글 초기 상태에 반영한다(지금까지는 저장만 하고 로드가 없어 매번 XAML 기본값으로
        // 뜨는 버그였음). 아래 SelectionChanged 구독/ApplyReaderCardEnabled/스냅샷 캡처보다
        // 반드시 먼저 실행되어야 "로드된 값 기준"으로 활성화 상태와 dirty-check 스냅샷이 잡힌다.
        LoadFromRegistry();

        // 2026-08-14 추가(사용자 피드백): 콤보(COM 포트) 선택값에 따라 해당 리더기 카드의
        // 액션 버튼 5개 + 멀티패드 토글을 활성/비활성 연동한다. 정식 AOP/레지스트리 연동은
        // 별도 단계(Phase 5 범위 조정으로 제외) — 여기서는 "미사용"이면 비활성, 아니면 활성이라는
        // 가벼운 코드비하인드 연동만 수행한다. "포트 열기" 토글은 아직 만들지 않았으므로(Hidden
        // 자리만 존재) 이 연동에서 제외한다.
        //
        // 핸들러를 XAML의 SelectionChanged 속성 대신 여기서(Loaded 이후) 붙이는 이유:
        // XAML에서 SelectedIndex를 선언하면 InitializeComponent가 트리를 순서대로 구성하는
        // 도중 그 시점에 곧바로 SelectionChanged가 발생하는데, 이때 아직 이 콤보보다 뒤에
        // 선언된 액션 버튼 패널/토글의 x:Name 필드가 연결되지 않아 NullReferenceException이
        // 날 위험이 있다. Loaded 시점에는 전체 트리가 이미 구성되어 있어 안전하다(2026-08-14
        // 추가 보완: 이제는 XAML에 SelectedIndex 자체가 없고 위 LoadFromRegistry가 최초 선택을
        // 담당하므로 이 우려가 더 명확해졌다 — LoadFromRegistry가 먼저 끝난 뒤에 구독한다).
        Reader1PortCombo.SelectionChanged += (_, _2) => ApplyReaderCardEnabled(Reader1PortCombo, Reader1ActionButtonsPanel, Reader1MultipadToggle);
        Reader2PortCombo.SelectionChanged += (_, _2) => ApplyReaderCardEnabled(Reader2PortCombo, Reader2ActionButtonsPanel, Reader2MultipadToggle);

        ApplyReaderCardEnabled(Reader1PortCombo, Reader1ActionButtonsPanel, Reader1MultipadToggle);
        ApplyReaderCardEnabled(Reader2PortCombo, Reader2ActionButtonsPanel, Reader2MultipadToggle);

        // Phase 5: 확인/취소 dirty-check용 초기 스냅샷(PRD 4.13/4.12) — LoadFromRegistry가 반영한
        // "로드된 값"을 기준으로 잡아야 하므로 반드시 이 시점(로드 이후)에 캡처한다.
        _snapshotReader1Port = GetComboText(Reader1PortCombo);
        _snapshotReader2Port = GetComboText(Reader2PortCombo);
        _snapshotReader1Multipad = Reader1MultipadToggle.IsChecked == true;
        _snapshotReader2Multipad = Reader2MultipadToggle.IsChecked == true;
    }

    /// <summary>
    /// 2026-08-14 추가(Phase 5 보완, 사용자 피드백 "레지스트리 값 로드 누락"): 창을 열 때
    /// HKCU\Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT의 COMPORT1_FIELD/COMPORT2_FIELD/
    /// MULTIPAD1_FIELD/MULTIPAD2_FIELD를 읽어 콤보/토글 초기 상태에 반영한다.
    /// - 콤보: 저장된 값과 일치하는 항목이 있으면 그 항목을 선택. 값이 없거나(키/값 자체가 없음)
    ///   콤보 항목에 없는 값이면 안전하게 "미사용"으로 폴백(SelectComboValue 참고).
    /// - 토글: MULTIPAD{N}_FIELD == "0" 일 때만 켜짐(반전 인코딩, PRD 5장). 값이 없거나 "1"이면
    ///   기본값(꺼짐).
    /// - 레지스트리 접근 자체가 실패하는 경우(권한 등)에도 예외를 창 밖으로 던지지 않고 기본값
    ///   (미사용/꺼짐)으로 조용히 폴백한다.
    /// </summary>
    private void LoadFromRegistry()
    {
        string? port1 = null;
        string? port2 = null;
        string? multipad1 = null;
        string? multipad2 = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key != null)
            {
                port1 = key.GetValue("COMPORT1_FIELD") as string;
                port2 = key.GetValue("COMPORT2_FIELD") as string;
                multipad1 = key.GetValue("MULTIPAD1_FIELD") as string;
                multipad2 = key.GetValue("MULTIPAD2_FIELD") as string;
            }
        }
        catch
        {
            // 레지스트리 접근 실패(권한 등) — 아래 SelectComboValue/토글 초기화가 그대로
            // 기본값(미사용/꺼짐)으로 폴백하므로 별도 처리 없이 조용히 무시한다.
        }

        SelectComboValue(Reader1PortCombo, port1);
        SelectComboValue(Reader2PortCombo, port2);
        Reader1MultipadToggle.IsChecked = multipad1 == "0";
        Reader2MultipadToggle.IsChecked = multipad2 == "0";
    }

    /// <summary>
    /// 저장된 값과 일치하는 ComboBoxItem을 선택한다. 값이 비어있거나(키/값 없음) 콤보 항목
    /// 중에 일치하는 것이 없으면(예: 이전에 존재했던 COM 포트가 사라진 경우) 안전하게 "미사용"
    /// 항목으로 폴백한다.
    /// </summary>
    private static void SelectComboValue(ComboBox combo, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            foreach (var obj in combo.Items)
            {
                if (obj is ComboBoxItem item && item.Content as string == value)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }

        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && item.Content as string == "미사용")
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static void ApplyReaderCardEnabled(ComboBox portCombo, Panel actionButtonsPanel, UIElement multipadToggle)
    {
        var isPortSelected = (portCombo.SelectedItem as ComboBoxItem)?.Content as string != "미사용";
        actionButtonsPanel.IsEnabled = isPortSelected;
        multipadToggle.IsEnabled = isPortSelected;
    }

    private static string GetComboText(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Content as string ?? string.Empty;

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

    // ===================== 액션 버튼(초기화/상태체크/키다운로드/무결성체크/업데이트) =====================
    // PRD 4.7: 클릭 시 로딩 문구로 전환 + 화면 전체 잠금(동시 작업 1개 제한) → 3초 후 자동 완료 →
    // 원복 + AOP 제약(이번 Phase 범위 아님) 재적용 대신 기존 ApplyReaderCardEnabled만 재적용.
    // 실제 리더기 통신 로직은 범위 밖(원본도 스텁) — 딜레이 후 항상 성공 처리.

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        var button = (Button)sender;
        var originalContent = button.Content;
        var loadingText = button.Tag as string ?? "처리중...";

        _isBusy = true;
        SetGlobalEnabled(false);
        button.Content = loadingText;
        // 2026-08-14 추가(Phase 5 보완, 사용자 피드백): 텍스트 전환뿐 아니라 회전 스피너도 함께
        // 표시(Themes/Buttons.xaml ReaderButtonStyle의 controls:ButtonLoadingHelper.IsLoading 트리거).
        ButtonLoadingHelper.SetIsLoading(button, true);

        await Task.Delay(3000);

        button.Content = originalContent;
        ButtonLoadingHelper.SetIsLoading(button, false);
        SetGlobalEnabled(true);
        ApplyReaderCardEnabled(Reader1PortCombo, Reader1ActionButtonsPanel, Reader1MultipadToggle);
        ApplyReaderCardEnabled(Reader2PortCombo, Reader2ActionButtonsPanel, Reader2MultipadToggle);
        _isBusy = false;
    }

    // ===================== 조회(무결성 체크 리스트) =====================
    // PRD 4.5/4.6: 조회기간별 더미 행 수(오늘 3 / 7일 5 / 30일·100일 10), 2초 로딩.

    private async void QueryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        _isBusy = true;
        SetGlobalEnabled(false);

        var originalContent = QueryButton.Content;
        QueryButton.Content = "조회중...";
        ButtonLoadingHelper.SetIsLoading(QueryButton, true);

        IntegrityListItemsControl.Visibility = Visibility.Collapsed;
        IntegrityEmptyText.Visibility = Visibility.Collapsed;
        IntegrityLoadingText.Visibility = Visibility.Visible;

        await Task.Delay(2000);

        var period = GetComboText(QueryPeriodCombo);
        var rows = BuildDummyRows(period);
        IntegrityListItemsControl.ItemsSource = rows;

        IntegrityLoadingText.Visibility = Visibility.Collapsed;
        if (rows.Count == 0)
        {
            IntegrityListItemsControl.Visibility = Visibility.Collapsed;
            IntegrityEmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            IntegrityListItemsControl.Visibility = Visibility.Visible;
            IntegrityEmptyText.Visibility = Visibility.Collapsed;
        }

        QueryButton.Content = originalContent;
        ButtonLoadingHelper.SetIsLoading(QueryButton, false);
        SetGlobalEnabled(true);
        ApplyReaderCardEnabled(Reader1PortCombo, Reader1ActionButtonsPanel, Reader1MultipadToggle);
        ApplyReaderCardEnabled(Reader2PortCombo, Reader2ActionButtonsPanel, Reader2MultipadToggle);
        _isBusy = false;
    }

    /// <summary>
    /// 더미 무결성 체크 데이터(PRD 4.6 하단 "데이터 소스 관련 확인 필요" — 원본도 하드코딩 더미
    /// 데이터를 사용해 동일하게 이식). 조회기간별 행 수: 오늘=3, 7일=5, 30일/100일=10.
    /// </summary>
    private static List<IntegrityCheckRow> BuildDummyRows(string period)
    {
        var count = period switch
        {
            "오늘" => 3,
            "7일" => 5,
            "30일" => 10,
            "100일" => 10,
            _ => 3,
        };

        var rows = new List<IntegrityCheckRow>(count);
        var baseTime = new DateTime(2026, 3, 8, 9, 12, 34);
        for (var i = 0; i < count; i++)
        {
            var checkTime = baseTime.AddMinutes(-i * 37).AddSeconds(-i * 11).ToString("yyyyMMddHHmmss");
            var port = i % 2 == 0 ? "COM 01" : "COM 02";
            var resultCode = i % 4 == 3 ? "01" : "00"; // "00" 정상, 그 외 오류(PRD 4.6)
            var moduleId = $"MD-{1000 + i:D4}";
            var readerId = $"RDR-{100000 + i:D6}";
            var posId = $"POS-{200000 + i:D6}";
            rows.Add(new IntegrityCheckRow(checkTime, port, resultCode, moduleId, readerId, posId));
        }

        return rows;
    }

    /// <summary>
    /// PRD 4.7 "동시에 하나의 작업만 진행 가능" — 액션 버튼/콤보/토글/조회/확인/취소 전체를
    /// 한 번에 잠그거나 푼다. 세부 카드별 "미사용" 비활성화는 완료 후 ApplyReaderCardEnabled가
    /// 다시 정리한다.
    /// </summary>
    private void SetGlobalEnabled(bool enabled)
    {
        Reader1PortCombo.IsEnabled = enabled;
        Reader2PortCombo.IsEnabled = enabled;
        Reader1ActionButtonsPanel.IsEnabled = enabled;
        Reader2ActionButtonsPanel.IsEnabled = enabled;
        Reader1MultipadToggle.IsEnabled = enabled;
        Reader2MultipadToggle.IsEnabled = enabled;
        QueryPeriodCombo.IsEnabled = enabled;
        QueryButton.IsEnabled = enabled;
        ConfirmButton.IsEnabled = enabled;
        CancelButton.IsEnabled = enabled;
    }

    // ===================== 정보 팝오버(멀티패드, PRD 4.10) =====================
    // 리더기1/2 멀티패드 info 버튼이 단일 Popup을 공유한다. 같은 버튼 재클릭 시 닫히고,
    // 다른 버튼 클릭 시 PlacementTarget만 바뀌어 자동으로 이전 팝오버가 닫히고 새로 뜬다.
    // "포트 열기" info 버튼은 자리가 Hidden이라 이번 Phase에서 배선하지 않는다.

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
    // 이번 Phase 범위에서 제외됨. 여기서는 콤보/멀티패드 값의 레지스트리 저장과 dirty-check만 다룬다.

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        SaveToRegistry();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        // PRD 4.12 취소 흐름 1: 열려있는 팝오버 먼저 닫기.
        if (MultipadInfoPopup.IsOpen)
            MultipadInfoPopup.IsOpen = false;

        // PRD 4.12 취소 흐름 2~4: 변경사항(콤포1/2, 멀티패드1/2) 추적 후 확인창.
        var isDirty =
            GetComboText(Reader1PortCombo) != _snapshotReader1Port ||
            GetComboText(Reader2PortCombo) != _snapshotReader2Port ||
            (Reader1MultipadToggle.IsChecked == true) != _snapshotReader1Multipad ||
            (Reader2MultipadToggle.IsChecked == true) != _snapshotReader2Multipad;

        if (isDirty)
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

    /// <summary>
    /// PRD 4.12/5장: COMPORT1_FIELD/COMPORT2_FIELD(콤보 텍스트 그대로), MULTIPAD1_FIELD/
    /// MULTIPAD2_FIELD(반전 인코딩: ON→"0", OFF→"1")를
    /// HKCU\Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT 에 저장한다.
    /// </summary>
    private void SaveToRegistry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
        if (key is null)
            return;

        key.SetValue("COMPORT1_FIELD", GetComboText(Reader1PortCombo), RegistryValueKind.String);
        key.SetValue("COMPORT2_FIELD", GetComboText(Reader2PortCombo), RegistryValueKind.String);
        key.SetValue("MULTIPAD1_FIELD", Reader1MultipadToggle.IsChecked == true ? "0" : "1", RegistryValueKind.String);
        key.SetValue("MULTIPAD2_FIELD", Reader2MultipadToggle.IsChecked == true ? "0" : "1", RegistryValueKind.String);
    }
}
