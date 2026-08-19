using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KFTCOneCAP.Wpf.Models;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.ViewModels;

/// <summary>
/// 무결성 체크 리스트(PRD 4.6)의 표시 상태. 리스트/빈 상태/로딩 문구 세 Visibility를 코드비하인드가
/// 각각 따로 맞추면 상태가 어긋날 여지가 있어(development_plan.md P7-3), 하나의 열거값에서 세
/// 파생 bool(IsEmptyState/IsLoadingState/IsLoadedState)을 만들어 View는 그것만 바인딩한다.
/// </summary>
public enum IntegrityListState
{
    Empty,
    Loading,
    Loaded,
}

/// <summary>
/// 리더기 설정 화면(Views/ReaderSetupWindow.xaml)의 ViewModel.
/// Phase 7(MVVM 전환, docs/payment_relay/development_plan.md P7-2)에서 코드비하인드(416줄)의
/// 업무 로직 — 레지스트리 로드/저장, dirty-check, COM 콤보/멀티패드 상태, busy 상태(PRD 4.7
/// "동시 1작업"), "미사용" 카드 비활성 판정, 조회 결과 — 를 전부 이곳으로 이관했다.
///
/// 이 클래스는 WPF Window/Control 타입을 알지 못한다(ViewModels → Services → Protocol → Interop
/// 단방향 계층 규칙, docs/payment_relay/ROADMAP.md "계층 구조"). Window.Close()/DialogResult/
/// MessageBox/Popup 배치/DWM 타이틀바처럼 창·OS에 직접 묶인 동작은 여전히
/// Views/ReaderSetupWindow.xaml.cs에 남아 있다(그쪽 파일 상단 주석에 이유를 남겨 둠).
/// </summary>
public sealed partial class ReaderSetupViewModel : ObservableObject
{
    private readonly ReaderSettingsService _settingsService = new();

    // PRD 4.13/4.12 dirty-check 스냅샷(취소 시 비교용) — Load() 직후 값을 기준으로 잡는다.
    private string _snapshotReader1Port = "미사용";
    private string _snapshotReader2Port = "미사용";
    private bool _snapshotReader1Multipad;
    private bool _snapshotReader2Multipad;

    public ReaderSetupViewModel()
    {
        Reader1InitButton = new ReaderActionButtonViewModel(this, "초기화", "초기화중...");
        Reader1StatusCheckButton = new ReaderActionButtonViewModel(this, "상태체크", "확인중...");
        Reader1KeyDownloadButton = new ReaderActionButtonViewModel(this, "키다운로드", "다운로드중...");
        Reader1IntegrityCheckButton = new ReaderActionButtonViewModel(this, "무결성체크", "체크중...");
        Reader1UpdateButton = new ReaderActionButtonViewModel(this, "업데이트", "업데이트중...");

        Reader2InitButton = new ReaderActionButtonViewModel(this, "초기화", "초기화중...");
        Reader2StatusCheckButton = new ReaderActionButtonViewModel(this, "상태체크", "확인중...");
        Reader2KeyDownloadButton = new ReaderActionButtonViewModel(this, "키다운로드", "다운로드중...");
        Reader2IntegrityCheckButton = new ReaderActionButtonViewModel(this, "무결성체크", "체크중...");
        Reader2UpdateButton = new ReaderActionButtonViewModel(this, "업데이트", "업데이트중...");

        QueryCommand = new AsyncRelayCommand(ExecuteQueryAsync);

        Load();
    }

    // ===================== COM 포트 / 멀티패드 (PRD 4.13/4.10) =====================

    [ObservableProperty]
    private string reader1PortSelection = "미사용";

    [ObservableProperty]
    private string reader2PortSelection = "미사용";

    [ObservableProperty]
    private bool reader1Multipad;

    [ObservableProperty]
    private bool reader2Multipad;

    /// <summary>
    /// PRD 4.13 "미사용"이면 해당 리더기 카드의 액션버튼 5개 + 멀티패드 토글 비활성화.
    /// busy 상태도 함께 반영한다(기존 코드비하인드의 SetGlobalEnabled + ApplyReaderCardEnabled
    /// 재적용 조합과 동일한 결과 — 여기서는 두 조건을 하나의 값으로 합쳐 항상 최신 상태를 유지).
    /// </summary>
    public bool Reader1CardEnabled => !IsBusy && Reader1PortSelection != "미사용";

    public bool Reader2CardEnabled => !IsBusy && Reader2PortSelection != "미사용";

    partial void OnReader1PortSelectionChanged(string value) => OnPropertyChanged(nameof(Reader1CardEnabled));

    partial void OnReader2PortSelectionChanged(string value) => OnPropertyChanged(nameof(Reader2CardEnabled));

    // ===================== busy 상태 (PRD 4.7 "동시에 하나의 작업만") =====================

    [ObservableProperty]
    private bool isBusy;

    /// <summary>콤보/조회/확인/취소 등 카드 상태와 무관하게 busy 여부에만 좌우되는 전역 활성 상태.</summary>
    public bool GlobalEnabled => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(Reader1CardEnabled));
        OnPropertyChanged(nameof(Reader2CardEnabled));
        OnPropertyChanged(nameof(GlobalEnabled));
    }

    // ===================== 액션 버튼 5종 × 2 (PRD 4.7, 스텁 3초) =====================

    public ReaderActionButtonViewModel Reader1InitButton { get; }
    public ReaderActionButtonViewModel Reader1StatusCheckButton { get; }
    public ReaderActionButtonViewModel Reader1KeyDownloadButton { get; }
    public ReaderActionButtonViewModel Reader1IntegrityCheckButton { get; }
    public ReaderActionButtonViewModel Reader1UpdateButton { get; }

    public ReaderActionButtonViewModel Reader2InitButton { get; }
    public ReaderActionButtonViewModel Reader2StatusCheckButton { get; }
    public ReaderActionButtonViewModel Reader2KeyDownloadButton { get; }
    public ReaderActionButtonViewModel Reader2IntegrityCheckButton { get; }
    public ReaderActionButtonViewModel Reader2UpdateButton { get; }

    // ===================== 조회(무결성 체크 리스트, PRD 4.5/4.6, 스텁 2초) =====================

    [ObservableProperty]
    private string queryPeriodSelection = "오늘";

    [ObservableProperty]
    private string queryButtonContent = "조회";

    [ObservableProperty]
    private bool queryButtonIsLoading;

    [ObservableProperty]
    private IntegrityListState integrityState = IntegrityListState.Empty;

    public bool IsEmptyState => IntegrityState == IntegrityListState.Empty;

    public bool IsLoadingState => IntegrityState == IntegrityListState.Loading;

    public bool IsLoadedState => IntegrityState == IntegrityListState.Loaded;

    partial void OnIntegrityStateChanged(IntegrityListState value)
    {
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(IsLoadingState));
        OnPropertyChanged(nameof(IsLoadedState));
    }

    public ObservableCollection<IntegrityCheckRow> IntegrityRows { get; } = new();

    public IAsyncRelayCommand QueryCommand { get; }

    /// <summary>
    /// 조회 결과가 갱신되어 목록 스크롤을 맨 위로 되돌려야 할 때 발생한다. 원본 코드비하인드의
    /// <c>IntegrityScrollViewer.ScrollToTop()</c>은 순수 UI 동작(스크롤 위치)이라 ViewModel이 직접
    /// 다루지 않고, View(Views/ReaderSetupWindow.xaml.cs)가 이 이벤트를 구독해 처리한다.
    /// </summary>
    public event EventHandler? ResultsUpdated;

    private async Task ExecuteQueryAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        QueryButtonIsLoading = true;
        QueryButtonContent = "조회중...";
        IntegrityState = IntegrityListState.Loading;

        await Task.Delay(2000);

        var rows = BuildDummyRows(QueryPeriodSelection);
        IntegrityRows.Clear();
        foreach (var row in rows)
            IntegrityRows.Add(row);

        IntegrityState = rows.Count == 0 ? IntegrityListState.Empty : IntegrityListState.Loaded;
        ResultsUpdated?.Invoke(this, EventArgs.Empty);

        QueryButtonContent = "조회";
        QueryButtonIsLoading = false;
        IsBusy = false;
    }

    /// <summary>
    /// 더미 무결성 체크 데이터(PRD 4.6 "데이터 소스 관련 확인 필요" — 원본도 하드코딩 더미 데이터를
    /// 사용해 동일하게 이식). 조회기간별 행 수: 오늘=3, 7일=5, 30일/100일=10. 실통신 교체는
    /// Phase 12 몫이며 이번 Phase에서는 스텁을 그대로 옮기기만 한다(development_plan.md P7-4).
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

    // ===================== 로드 / 저장 / dirty-check (PRD 4.12/4.13, 5장) =====================

    /// <summary>
    /// 생성 시점에 레지스트리 값을 읽어 콤보/토글에 반영하고, dirty-check 기준 스냅샷을 캡처한다.
    /// 콤보 항목은 현재 "COM 01"/"미사용" 두 가지뿐이므로(실제 포트 열거는 Phase 12,
    /// docs/home_reader_setup/PRD_WPF.md 4.13), 저장된 값이 그 외 값이면 안전하게 "미사용"으로
    /// 정규화한다(기존 코드비하인드 SelectComboValue와 동일 규칙).
    /// </summary>
    private void Load()
    {
        var settings = _settingsService.Load();

        Reader1PortSelection = NormalizePortSelection(settings.Port1);
        Reader2PortSelection = NormalizePortSelection(settings.Port2);
        Reader1Multipad = settings.Multipad1;
        Reader2Multipad = settings.Multipad2;

        _snapshotReader1Port = Reader1PortSelection;
        _snapshotReader2Port = Reader2PortSelection;
        _snapshotReader1Multipad = Reader1Multipad;
        _snapshotReader2Multipad = Reader2Multipad;
    }

    private static string NormalizePortSelection(string value) => value == "COM 01" ? "COM 01" : "미사용";

    /// <summary>PRD 4.12 취소 흐름: 콤보1/2, 멀티패드1/2 중 하나라도 스냅샷과 다르면 dirty.</summary>
    public bool IsDirty() =>
        Reader1PortSelection != _snapshotReader1Port ||
        Reader2PortSelection != _snapshotReader2Port ||
        Reader1Multipad != _snapshotReader1Multipad ||
        Reader2Multipad != _snapshotReader2Multipad;

    /// <summary>PRD 4.12/5장: 콤보/토글 현재 값을 레지스트리에 저장한다.</summary>
    public void Save()
    {
        _settingsService.Save(new ReaderSettings
        {
            Port1 = Reader1PortSelection,
            Port2 = Reader2PortSelection,
            Multipad1 = Reader1Multipad,
            Multipad2 = Reader2Multipad,
        });
    }
}
