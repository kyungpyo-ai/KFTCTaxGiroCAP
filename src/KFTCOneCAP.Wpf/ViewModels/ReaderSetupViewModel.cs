using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KFTCOneCAP.Wpf.Models;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Reader;
using KFTCOneCAP.Wpf.Services.Settings;
using KFTCOneCAP.Wpf.Services.Storage;

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
/// Phase 12(development_plan.md P12-1~P12-5)에서 실동작을 배선했다 — COM 포트 실제 열거(P12-2),
/// 초기화/상태체크(P12-3), 무결성체크 2단계(P12-4, 실제 시퀀스/DB 저장은
/// <see cref="Services.Reader.IntegrityCheckService"/>에 있다), 무결성 리스트 실제 조회(P12-5).
/// 포트 생명주기 자체는 이 ViewModel이 소유하지 않는다 — 앱 수명 소유자
/// <see cref="Services.Reader.ReaderConnectionManager"/>(P12-1, App.xaml.cs가 생성)를 생성자로
/// 전달받아 참조만 한다(화면이 열렸다 닫혔다 할 때마다 ViewModel이 새로 생기므로, 포트를 이
/// ViewModel이 소유하면 "항상 열어둔다"는 PRD §2.2.2를 만족할 수 없다).
///
/// 이 클래스는 WPF Window/Control 타입을 알지 못한다(ViewModels → Services → Protocol → Interop
/// 단방향 계층 규칙, docs/payment_relay/ROADMAP.md "계층 구조"). Window.Close()/DialogResult/
/// MessageBox/Popup 배치/DWM 타이틀바처럼 창·OS에 직접 묶인 동작은 여전히
/// Views/ReaderSetupWindow.xaml.cs에 남아 있다(그쪽 파일 상단 주석에 이유를 남겨 둠). P12-3이
/// 추가한 <see cref="ResultMessageReady"/>도 같은 이유로 이벤트로만 알리고 MessageBox를 직접
/// 호출하지 않는다.
/// </summary>
public sealed partial class ReaderSetupViewModel : ObservableObject
{
    /// <summary>명령 4종 공통 타임아웃(P12-3 — 값을 흩뿌리지 말고 한 곳에 상수로).
    /// Phase 9 파일럿이 쓴 5초를 그대로 따른다. Phase 16에서 결제 타임아웃(120초)과 함께 재검토.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly ReaderSettingsService _settingsService = new();
    private readonly ReaderConnectionManager _connectionManager;
    private readonly IntegrityCheckService _integrityCheckService = new();
    private readonly IntegrityCheckStore _integrityCheckStore = new();

    // PRD 4.13/4.12 dirty-check 스냅샷(취소 시 비교용) — Load() 직후 값을 기준으로 잡는다.
    private string _snapshotReader1Port = ComPortFormat.Unused;
    private string _snapshotReader2Port = ComPortFormat.Unused;
    private bool _snapshotReader1Multipad;
    private bool _snapshotReader2Multipad;

    /// <summary>
    /// <paramref name="connectionManager"/>는 App.xaml.cs(OnStartup)가 앱 수명 동안 하나만 만든
    /// <see cref="ReaderConnectionManager"/>다(P12-1) — 이 생성자는 그것을 참조만 하고 소유하지
    /// 않는다(포트를 열고 닫는 책임은 그 클래스에만 있다).
    /// </summary>
    internal ReaderSetupViewModel(ReaderConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;

        Reader1InitButton = new ReaderActionButtonViewModel(this, "초기화", "초기화중...",
            () => ExecuteInitAsync(_connectionManager.Reader1, "리더기1", () => Reader1PortSelection));
        Reader1StatusCheckButton = new ReaderActionButtonViewModel(this, "상태체크", "확인중...",
            () => ExecuteStatusAsync(_connectionManager.Reader1, "리더기1", () => Reader1PortSelection));
        Reader1KeyDownloadButton = new ReaderActionButtonViewModel(this, "키다운로드", "다운로드중...");
        Reader1IntegrityCheckButton = new ReaderActionButtonViewModel(this, "무결성체크", "체크중...",
            () => ExecuteIntegrityAsync(_connectionManager.Reader1, "리더기1", () => Reader1PortSelection));
        Reader1UpdateButton = new ReaderActionButtonViewModel(this, "업데이트", "업데이트중...");

        Reader2InitButton = new ReaderActionButtonViewModel(this, "초기화", "초기화중...",
            () => ExecuteInitAsync(_connectionManager.Reader2, "리더기2", () => Reader2PortSelection));
        Reader2StatusCheckButton = new ReaderActionButtonViewModel(this, "상태체크", "확인중...",
            () => ExecuteStatusAsync(_connectionManager.Reader2, "리더기2", () => Reader2PortSelection));
        Reader2KeyDownloadButton = new ReaderActionButtonViewModel(this, "키다운로드", "다운로드중...");
        Reader2IntegrityCheckButton = new ReaderActionButtonViewModel(this, "무결성체크", "체크중...",
            () => ExecuteIntegrityAsync(_connectionManager.Reader2, "리더기2", () => Reader2PortSelection));
        Reader2UpdateButton = new ReaderActionButtonViewModel(this, "업데이트", "업데이트중...");

        QueryCommand = new AsyncRelayCommand(ExecuteQueryAsync);

        Load();

        // 2026-08-20 사용자 확정 — 화면을 처음 열었을 때부터 "오늘"(QueryPeriodSelection 기본값)
        // 기준 무결성 체크 정보가 바로 보여야 한다(조회 버튼을 눌러야만 나오던 것을 개선). 생성자는
        // 동기라 await할 수 없으므로 fire-and-forget으로 실행한다 — ExecuteQueryAsync 내부 경로
        // (IntegrityCheckStore.GetHistory)는 예외를 던지지 않으므로(P11-4) 관찰되지 않는 예외 위험이
        // 없다.
        _ = ExecuteQueryAsync();
    }

    // ===================== COM 포트 / 멀티패드 (PRD 4.13/4.10) =====================

    /// <summary>P12-2 — 실제 열거된 COM 포트 목록("미사용" + "COM %02d" 오름차순). 두 콤보
    /// (리더기1/2)가 이 컬렉션 하나를 공유한다. XAML은 이 컬렉션에 ItemsSource로 바인딩하고,
    /// 코드비하인드는 더 이상 ComboBoxItem을 하드코딩하지 않는다(P7-3 "ItemsSource 대입 금지"
    /// 규칙 유지 — 대입 대상이 아니라 바인딩 대상일 뿐이다).</summary>
    public ObservableCollection<string> AvailablePorts { get; } = new();

    [ObservableProperty]
    private string reader1PortSelection = ComPortFormat.Unused;

    [ObservableProperty]
    private string reader2PortSelection = ComPortFormat.Unused;

    [ObservableProperty]
    private bool reader1Multipad;

    [ObservableProperty]
    private bool reader2Multipad;

    /// <summary>
    /// PRD 4.13 "미사용"이면 해당 리더기 카드의 액션버튼 5개 + 멀티패드 토글 비활성화.
    /// busy 상태도 함께 반영한다(기존 코드비하인드의 SetGlobalEnabled + ApplyReaderCardEnabled
    /// 재적용 조합과 동일한 결과 — 여기서는 두 조건을 하나의 값으로 합쳐 항상 최신 상태를 유지).
    /// </summary>
    public bool Reader1CardEnabled => !IsBusy && Reader1PortSelection != ComPortFormat.Unused;

    public bool Reader2CardEnabled => !IsBusy && Reader2PortSelection != ComPortFormat.Unused;

    /// <summary>
    /// <see cref="Load"/>가 레지스트리 값을 콤보에 반영하는 동안에는 true다. 그 대입도
    /// <c>Reader1PortSelection</c> 세터를 거쳐 <see cref="OnReader1PortSelectionChanged"/>를
    /// 부르는데, 그건 "사용자가 콤보를 바꾼 것"이 아니라 "이미 열려 있는(또는 앞으로 열릴) 포트를
    /// 그대로 반영하는 것"이므로 이 플래그로 구분해 포트를 닫지 않는다.
    /// </summary>
    private bool _isLoadingPortSelection;

    /// <summary>
    /// 2026-08-20 사용자 확정: 레지스트리 저장(확인 버튼 클릭)과 콤보 변경 시 포트 닫기는 서로 다른
    /// 시점이다 — 확인/취소는 "레지스트리에 반영할지"만 정하고, 창 안에서 콤보를 바꾸는 즉시(확인을
    /// 누르기 전이라도) 기존 포트를 닫아 포트 변경을 준비해야 한다. 새 포트를 여는 것은 여전히
    /// 확인 시점(<see cref="Save"/> → <see cref="ReaderConnectionManager.Reopen"/>)에만 한다.
    /// </summary>
    partial void OnReader1PortSelectionChanged(string value)
    {
        OnPropertyChanged(nameof(Reader1CardEnabled));
        if (!_isLoadingPortSelection)
            _connectionManager.ClosePortForPendingChange(_connectionManager.Reader1, "리더기1");
    }

    partial void OnReader2PortSelectionChanged(string value)
    {
        OnPropertyChanged(nameof(Reader2CardEnabled));
        if (!_isLoadingPortSelection)
            _connectionManager.ClosePortForPendingChange(_connectionManager.Reader2, "리더기2");
    }

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

    // ===================== 액션 버튼 5종 × 2 (PRD 4.7) =====================
    // 초기화/상태체크/무결성체크는 P12-3/P12-4에서 실동작으로 배선됐다. 키다운로드/업데이트는
    // "이 Phase에서 손대지 않는 것"(development_plan.md Phase 12 상단)이라 기존 3초 스텁 그대로다.

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

    /// <summary>
    /// P12-3 — PRD §6.1/§6.2/§6.4가 요구하는 여러 줄 결과 문구는 화면 어디에도 인라인으로 놓을
    /// 자리가 없어 모달 알림이 전제다. 다만 ViewModel이 MessageBox를 직접 호출하지 않는다(P7-2
    /// 규칙 — Window 타입에 묶인 동작은 View 책임) — 이 이벤트로 "보여줄 문구가 준비됐다"만
    /// 알리고, Views/ReaderSetupWindow.xaml.cs가 구독해 MessageBox.Show를 호출한다.
    /// </summary>
    public event EventHandler<string>? ResultMessageReady;

    private void RaiseResultMessage(string message) => ResultMessageReady?.Invoke(this, message);

    /// <summary>0x60→0x70 초기화(PRD §6.1). Phase 9(P9-3) 파일럿을 정식 배선으로 교체했다 — 그때는
    /// 리더기1 하나만, 로그로만 남겼다. 지금은 리더기1/2 모두, 결과를 PRD 문구로 화면에도 보여준다.
    /// ViewModel 쪽 await에는 ConfigureAwait(false)를 붙이지 않는다(P12-3 스레드 주의 — 붙이면
    /// 아래 이어지는 프로퍼티 갱신/이벤트 발행이 콜백 스레드에서 일어나 바인딩이 깨진다).
    /// <paramref name="portAccessor"/>는 2026-08-20 수정 — 확인(저장)을 누르기 전이라도 "화면에
    /// 선택된 콤보 값 = 실제 연결 대상"을 지키기 위해, 명령을 보내기 전 항상
    /// <see cref="ReaderConnectionManager.EnsureOpenForSelection"/>으로 현재 선택값에 맞춰 둔다
    /// (콤보를 안 바꿨다면 이미 그 포트에 연결돼 있어 아무 일도 일어나지 않는다).</summary>
    private async Task ExecuteInitAsync(ReaderService reader, string readerLabel, Func<string> portAccessor)
    {
        _connectionManager.EnsureOpenForSelection(reader, readerLabel, ComPortFormat.StripUnavailableSuffix(portAccessor()));

        var outcome = await reader.SendInitCommandAsync(CommandTimeout);
        LogOutcome(readerLabel, "초기화", outcome.Kind, outcome.ResponseCode, outcome.DllResultName, outcome.DllResult, outcome.Detail);
        RaiseResultMessage(BuildMessage("초기화", outcome.Kind, outcome.ResponseCode, outcome.DllResultName, outcome.DllResult, outcome.Detail));
    }

    /// <summary>0x61→0x71 상태체크(PRD §6.2). 응답코드 "00" 또는 "08"이면 성공
    /// (StatusResponseParser.IsSuccess가 이미 그 판정을 담당). 성공 시 리더기 인증 식별번호/모듈
    /// ID를 함께 보여준다. <paramref name="portAccessor"/>는 <see cref="ExecuteInitAsync"/>와
    /// 동일한 이유(2026-08-20).</summary>
    private async Task ExecuteStatusAsync(ReaderService reader, string readerLabel, Func<string> portAccessor)
    {
        _connectionManager.EnsureOpenForSelection(reader, readerLabel, ComPortFormat.StripUnavailableSuffix(portAccessor()));

        var outcome = await reader.SendStatusCommandAsync(CommandTimeout);
        LogOutcome(readerLabel, "상태체크", outcome.Kind, outcome.ResponseCode, outcome.DllResultName, outcome.DllResult, outcome.Detail);

        string? successExtra = outcome.Kind == ReaderCommandOutcomeKind.Success
            ? $"리더기 인증 식별번호 : {outcome.ReaderAuthId}\n모듈 ID : {outcome.ModuleId}"
            : null;
        RaiseResultMessage(BuildMessage("상태체크", outcome.Kind, outcome.ResponseCode, outcome.DllResultName, outcome.DllResult, outcome.Detail, successExtra));
    }

    /// <summary>
    /// 무결성체크(PRD §6.4) — 2단계(0x61→0x71→0x62→0x72) 시퀀스와 DB 저장은 화면 없이도 결제
    /// Flow(Phase 15)가 재사용할 수 있도록 <see cref="Services.Reader.IntegrityCheckService"/>(공용
    /// 서비스 계층)에 있다(P12-4). 이 메서드는 그 결과를 문구로 바꿔 보여주기만 한다.
    /// <paramref name="comPortAccessor"/>는 현재 콤보 선택값을 읽어 P12-2 형식("COM 05")으로
    /// 정규화한다 — "(사용불가)" 접미가 DB에 그대로 흘러가지 않도록 여기서 StripUnavailableSuffix를
    /// 거친다. 2026-08-20 수정 — <see cref="ExecuteInitAsync"/>와 동일한 이유로 명령 전송 전에
    /// 항상 이 값으로 연결을 맞춘다.
    /// </summary>
    private async Task ExecuteIntegrityAsync(ReaderService reader, string readerLabel, Func<string> comPortAccessor)
    {
        string comPort = ComPortFormat.StripUnavailableSuffix(comPortAccessor());
        _connectionManager.EnsureOpenForSelection(reader, readerLabel, comPort);

        var outcome = await _integrityCheckService.RunAsync(reader, comPort, CommandTimeout, CommandTimeout);
        LogOutcome(readerLabel, "무결성체크", outcome.Kind, outcome.ResponseCode, outcome.DllResultName, outcome.DllResult, outcome.Detail);

        string? successExtra = outcome.IsSuccess
            ? $"리더기 인증 식별번호 : {outcome.ReaderAuthId}\n모듈 ID : {outcome.ModuleId}"
            : null;
        RaiseResultMessage(BuildMessage("무결성 체크", outcome.Kind, outcome.ResponseCode, outcome.DllResultName, outcome.DllResult, outcome.Detail, successExtra));

        // 사용자 요청(2026-08-20): 무결성체크가 끝나면 "조회" 버튼을 누르지 않아도 아래 리스트에
        // 바로 반영돼야 한다. P12-4가 성공/실패 양쪽 다 DB에 저장하므로(IntegrityCheckService),
        // 결과와 무관하게 항상 새로고침한다 — 실패 건도 리스트에 나타나야 한다.
        await RefreshIntegrityRowsAsync();
    }

    /// <summary>P12-3 "실패 원인 구분"(PRD §6.6) — 명령 4종이 공유하는 결과 문구 매핑을 이 한 곳에
    /// 모은다(버튼 핸들러마다 switch를 복사하지 않는다).</summary>
    private static string BuildMessage(string commandLabel, ReaderCommandOutcomeKind kind, string? responseCode,
        string dllResultName, int dllResult, string detail, string? successExtra = null)
    {
        if (kind == ReaderCommandOutcomeKind.Success)
        {
            return successExtra == null
                ? $"리더기 {commandLabel} 성공"
                : $"리더기 {commandLabel} 성공\n{successExtra}";
        }

        string reason = kind switch
        {
            ReaderCommandOutcomeKind.BusinessFailure => $"응답코드: {responseCode}",
            ReaderCommandOutcomeKind.DllCallFailure => string.IsNullOrEmpty(detail)
                ? $"DLL 연동 오류: {dllResultName}({dllResult})"
                : $"DLL 연동 오류: {dllResultName}({dllResult}) - {detail}",
            ReaderCommandOutcomeKind.Timeout => "응답 시간 초과",
            ReaderCommandOutcomeKind.CommunicationError => $"통신 오류: {detail}",
            _ => "알 수 없는 오류",
        };

        return $"리더기 {commandLabel} 실패\n{reason}";
    }

    private static void LogOutcome(string readerLabel, string commandLabel, ReaderCommandOutcomeKind kind,
        string? responseCode, string dllResultName, int dllResult, string detail)
    {
        switch (kind)
        {
            case ReaderCommandOutcomeKind.Success:
                FileLogger.Info($"[{readerLabel} {commandLabel}] 성공, 응답코드={responseCode}");
                break;
            case ReaderCommandOutcomeKind.BusinessFailure:
                FileLogger.Warn($"[{readerLabel} {commandLabel}] 업무 응답코드 실패={responseCode}");
                break;
            case ReaderCommandOutcomeKind.DllCallFailure:
                FileLogger.Warn($"[{readerLabel} {commandLabel}] DLL 연동 실패: {dllResultName}({dllResult}) - {detail}");
                break;
            case ReaderCommandOutcomeKind.Timeout:
                FileLogger.Warn($"[{readerLabel} {commandLabel}] 응답 타임아웃");
                break;
            case ReaderCommandOutcomeKind.CommunicationError:
                FileLogger.Warn($"[{readerLabel} {commandLabel}] 통신 오류: {detail}");
                break;
        }
    }

    // ===================== 조회(무결성 체크 리스트, PRD 4.5/4.6) =====================

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

    /// <summary>
    /// P12-5 — <c>BuildDummyRows</c>(1차 범위 더미)를 <see cref="IntegrityCheckStore.GetHistory"/>
    /// 실제 조회로 교체했다. 2초 Task.Delay 스텁은 제거한다(실제 조회는 즉시 끝난다 — 인위적
    /// 지연을 남겨두지 않는다, development_plan.md P12-5). DB 조회 자체는 동기 API라
    /// Task.Run으로 스레드 풀에 넘겨 UI 스레드를 막지 않는다.
    /// </summary>
    private async Task ExecuteQueryAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        QueryButtonIsLoading = true;
        QueryButtonContent = "조회중...";
        IntegrityState = IntegrityListState.Loading;

        await RefreshIntegrityRowsAsync();

        QueryButtonContent = "조회";
        QueryButtonIsLoading = false;
        IsBusy = false;
    }

    /// <summary>
    /// 실제 DB 조회 + 목록 갱신의 핵심 로직만 뽑아 둔 메서드(P12-5, 2026-08-20 수정) — busy/스피너
    /// 상태는 건드리지 않는다. <see cref="ExecuteQueryAsync"/>(조회 버튼)는 이걸 busy 가드로
    /// 감싸서 쓰고, <see cref="ExecuteIntegrityAsync"/>(무결성체크 완료 후 자동 새로고침)는 이미
    /// <see cref="IsBusy"/>가 true인 상태(액션 버튼이 걸어 둠)에서 호출되므로 busy 가드를 다시
    /// 거치면 안 된다 — 그래서 그 가드를 이 메서드 밖으로 뺐다.
    /// </summary>
    private async Task RefreshIntegrityRowsAsync()
    {
        var (from, to) = ResolveQueryRange(QueryPeriodSelection);
        var entries = await Task.Run(() => _integrityCheckStore.GetHistory(from, to));

        var rows = entries.Select(ToRow).ToList();
        IntegrityRows.Clear();
        foreach (var row in rows)
            IntegrityRows.Add(row);

        IntegrityState = rows.Count == 0 ? IntegrityListState.Empty : IntegrityListState.Loaded;
        ResultsUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>P12-5 — "오늘"=오늘 하루, "N일"=오늘 포함 최근 N일(예: "7일"이면 6일 전 00:00 ~
    /// 오늘). GetHistory가 이미 날짜 경계(from.Date ~ to.Date.AddDays(1) 미만)를 처리하므로 여기서
    /// 시각을 직접 만들지 않는다 — 날짜만 넘긴다.</summary>
    private static (DateTime From, DateTime To) ResolveQueryRange(string period)
    {
        DateTime today = DateTime.Now.Date;
        int days = period switch
        {
            "오늘" => 1,
            "7일" => 7,
            "30일" => 30,
            "100일" => 100,
            _ => 1,
        };

        return (today.AddDays(-(days - 1)), today);
    }

    /// <summary>
    /// P12-5 — <see cref="IntegrityCheckHistoryEntry"/>(순수 DTO) → <see cref="IntegrityCheckRow"/>
    /// (WPF 바인딩용 모델) 변환은 ViewModel 책임이다(Storage가 표시 모델을 반환하던 계층 위반을
    /// Phase 11 리뷰에서 고치면서 이 책임이 여기로 넘어왔다).
    ///
    /// ResultCode는 <c>entry.ResponseCode ?? "ERR"</c>로 채운다 — <see cref="IntegrityCheckRow.IsOk"/>가
    /// "00" 완전 일치로만 성공을 판정하므로, 저장된 <see cref="IntegrityCheckHistoryEntry.IsSuccess"/>
    /// (업무 최종 판정)와 항상 일치하게 된다: 성공 건은 항상 ResponseCode="00"이고, 실패 건은
    /// 응답코드가 있어도("00"이 아닌 값) 없어도(null → "ERR") 어차피 "00"과 다르므로 화면에서
    /// "오류"로 표시된다 — 응답코드 없이 실패한 건(DLL 연동 실패)도 빠짐없이 오류로 보인다.
    /// </summary>
    private static IntegrityCheckRow ToRow(IntegrityCheckHistoryEntry entry)
    {
        string checkTime = entry.CheckedAt.ToString("yyyyMMddHHmmss");
        string resultCode = entry.ResponseCode ?? "ERR";
        return new IntegrityCheckRow(checkTime, entry.ComPort, resultCode, entry.ModuleId ?? "-", entry.ReaderAuthId ?? "-", entry.PosId);
    }

    // ===================== 로드 / 저장 / dirty-check (PRD 4.12/4.13, 5장) =====================

    /// <summary>
    /// 생성 시점에 실제 COM 포트를 열거하고(P12-2) 레지스트리 값을 읽어 콤보/토글에 반영한 뒤,
    /// dirty-check 기준 스냅샷을 캡처한다. 저장된 값이 현재 열거되지 않으면(리더기가 잠깐 빠졌거나
    /// 다른 PC 설정을 그대로 들고 온 경우) 조용히 "미사용"으로 바꾸지 않고 "COM 05(사용불가)"
    /// 형태로 목록에 추가해 선택 상태를 유지한다(P12-2).
    /// </summary>
    private void Load()
    {
        RebuildAvailablePorts();

        var settings = _settingsService.Load();

        _isLoadingPortSelection = true;
        try
        {
            Reader1PortSelection = ResolveSelectablePort(settings.Port1);
            Reader2PortSelection = ResolveSelectablePort(settings.Port2);
        }
        finally
        {
            _isLoadingPortSelection = false;
        }

        Reader1Multipad = settings.Multipad1;
        Reader2Multipad = settings.Multipad2;

        _snapshotReader1Port = Reader1PortSelection;
        _snapshotReader2Port = Reader2PortSelection;
        _snapshotReader1Multipad = Reader1Multipad;
        _snapshotReader2Multipad = Reader2Multipad;
    }

    /// <summary>P12-2 — <c>SerialPort.GetPortNames()</c>로 실제 연결된 COM 포트를 열거해 "미사용" +
    /// "COM %02d"(번호 오름차순) 형식으로 채운다.</summary>
    private void RebuildAvailablePorts()
    {
        var ports = SerialPort.GetPortNames()
            .Select(ComPortFormat.ParseSystemPortName)
            .Where(number => number > 0)
            .Distinct()
            .OrderBy(number => number)
            .Select(ComPortFormat.ToDisplay)
            .ToList();

        AvailablePorts.Clear();
        AvailablePorts.Add(ComPortFormat.Unused);
        foreach (var port in ports)
            AvailablePorts.Add(port);
    }

    /// <summary>레지스트리에 저장된 값이 현재 열거 목록(AvailablePorts)에 없으면 "(사용불가)" 접미를
    /// 붙여 목록에 추가하고 그 값을 선택 상태로 유지한다(P12-2 — 조용히 "미사용"으로 바꾸지 않음).</summary>
    private string ResolveSelectablePort(string savedDisplay)
    {
        if (savedDisplay == ComPortFormat.Unused || AvailablePorts.Contains(savedDisplay))
            return savedDisplay;

        int portNumber = ComPortFormat.ToPortNumber(savedDisplay);
        if (portNumber <= 0)
        {
            // 알 수 없는 형식의 값(레지스트리가 수동으로 오염된 경우 등) — 안전하게 미사용으로.
            return ComPortFormat.Unused;
        }

        string unavailable = ComPortFormat.ToUnavailableDisplay(portNumber);
        if (!AvailablePorts.Contains(unavailable))
            AvailablePorts.Add(unavailable);
        return unavailable;
    }

    /// <summary>
    /// 2026-08-20 사용자 확정 — 취소로 변경사항을 버릴 때 호출한다. "화면에 선택된 콤보 값 = 실제
    /// 연결 대상" 원칙(<see cref="ExecuteInitAsync"/> 등) 때문에, 콤보를 바꾸고 액션 버튼으로
    /// 실제 연결까지 해봤을 수 있다 — 그 저장되지 않은 연결을 레지스트리(스냅샷) 값으로 되돌린다.
    /// <see cref="ReaderConnectionManager.EnsureOpenForSelection"/>을 쓰므로, 이미 스냅샷 값과
    /// 일치하는 상태(콤보만 바꾸고 버튼은 안 눌러 이미 닫혀 있거나, 애초에 안 바꿨거나)면 아무 것도
    /// 하지 않는다 — 단순 "닫기"가 아니라 "스냅샷 포트로 맞추기"인 이유: 닫기만 하면
    /// <see cref="ReaderService"/>가 기억하는 포트 번호가 테스트했던 값(예: COM3)으로 남아, 나중에
    /// 자동 재연결(P10-3)이 레지스트리(COM5)가 아닌 그 값으로 시도하는 불일치가 생긴다. 스냅샷
    /// 포트가 이미 죽어 있어도(케이블 분리 등) 예외 없이 조용히 로그만 남긴다(PRD §2.2.2와 동일
    /// 원칙) — 실패해도 기억되는 포트 번호는 정확히 스냅샷 값으로 남으므로 다음 자동 재연결이
    /// 올바른 포트를 향한다.
    /// </summary>
    public void DiscardPortChanges()
    {
        string cleanSnapshotPort1 = ComPortFormat.StripUnavailableSuffix(_snapshotReader1Port);
        _connectionManager.EnsureOpenForSelection(_connectionManager.Reader1, "리더기1", cleanSnapshotPort1);

        string cleanSnapshotPort2 = ComPortFormat.StripUnavailableSuffix(_snapshotReader2Port);
        _connectionManager.EnsureOpenForSelection(_connectionManager.Reader2, "리더기2", cleanSnapshotPort2);
    }

    /// <summary>
    /// 2026-08-20 사용자 확정(Opus 리뷰에서 발견) — 리더기1/2에 같은 COM 포트를 지정할 수 없다.
    /// 포트는 배타적으로 열리므로(Win32 시리얼 포트 특성) 같은 값이면 실제로는 항상 한쪽이
    /// `READER_ERR_PORT_ALREADY_OPEN`으로 실패한다(실장비로 재현 확인). PRD에 이 케이스에 대한
    /// 규정이 없어(스펙 공백) 저장 자체를 막기로 확정했다. "미사용"끼리 같은 것은 당연히 허용한다
    /// (둘 다 실제로 포트를 열지 않으므로 충돌하지 않는다). "(사용불가)" 접미가 붙은 값도
    /// <see cref="ComPortFormat.StripUnavailableSuffix"/>로 걷어낸 뒤 비교한다.
    /// </summary>
    public bool IsDuplicatePortSelected()
    {
        if (Reader1PortSelection == ComPortFormat.Unused || Reader2PortSelection == ComPortFormat.Unused)
            return false;

        string clean1 = ComPortFormat.StripUnavailableSuffix(Reader1PortSelection);
        string clean2 = ComPortFormat.StripUnavailableSuffix(Reader2PortSelection);
        return clean1 == clean2;
    }

    /// <summary>PRD 4.12 취소 흐름: 콤보1/2, 멀티패드1/2 중 하나라도 스냅샷과 다르면 dirty.</summary>
    public bool IsDirty() =>
        Reader1PortSelection != _snapshotReader1Port ||
        Reader2PortSelection != _snapshotReader2Port ||
        Reader1Multipad != _snapshotReader1Multipad ||
        Reader2Multipad != _snapshotReader2Multipad;

    /// <summary>
    /// PRD 4.12/5장: 콤보/토글 현재 값을 레지스트리에 저장한다.
    /// <see cref="ReaderConnectionManager.EnsureOpenForSelection"/>으로 실제 연결을 이 값에
    /// 맞춘다(2026-08-20 수정) — 액션 버튼으로 이미 이 포트에 연결해 본 상태라면 아무 것도 하지
    /// 않고, 아니면(콤보만 바꾸고 확인을 바로 눌러 아직 닫혀 있는 상태 등) 새로 연다. "포트를
    /// 닫는 지점은 이 값이 바뀔 때뿐"이라는 규칙(P12-1)은 여전히 지켜진다 — 값이 같으면
    /// `EnsureOpenForSelection`이 아무 것도 하지 않는다. 레지스트리/DB에는 "(사용불가)" 접미를
    /// 걷어낸 깨끗한 형식만 남긴다(P12-2).
    /// </summary>
    public void Save()
    {
        string cleanPort1 = ComPortFormat.StripUnavailableSuffix(Reader1PortSelection);
        string cleanPort2 = ComPortFormat.StripUnavailableSuffix(Reader2PortSelection);

        _settingsService.Save(new ReaderSettings
        {
            Port1 = cleanPort1,
            Port2 = cleanPort2,
            Multipad1 = Reader1Multipad,
            Multipad2 = Reader2Multipad,
        });

        _connectionManager.EnsureOpenForSelection(_connectionManager.Reader1, "리더기1", cleanPort1);
        _connectionManager.EnsureOpenForSelection(_connectionManager.Reader2, "리더기2", cleanPort2);
    }
}
