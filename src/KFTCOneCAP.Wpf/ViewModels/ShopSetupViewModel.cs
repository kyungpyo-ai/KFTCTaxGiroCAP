using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.ViewModels;

/// <summary>
/// 가맹점 설정 화면(Views/ShopSetupWindow.xaml)의 ViewModel.
/// Phase 23(docs/operations/development_plan.md P23-3) — <see cref="ReaderSetupViewModel"/>과 같은
/// 역할 분담을 따른다: 레지스트리 로드/저장·검증은 이 클래스가 맡고, <c>Window.Close()</c>/
/// <c>DialogResult</c>/<c>MessageBox</c>는 <c>Views/ShopSetupWindow.xaml.cs</c>에 남는다(계층 규칙,
/// ViewModels → Services → Protocol → Interop 단방향, PRD.md §0.2). 이 클래스는 WPF 타입을 알지
/// 못한다 — 검증 실패/저장 실패를 <see cref="ResultMessageReady"/> 이벤트로만 알린다.
/// </summary>
public sealed partial class ShopSetupViewModel : ObservableObject
{
    // PRD.md §2.2 — 표시 문구만 원본 MFC에서 가져오고 저장값은 FNAISCRDVAN의 Mode 인자다.
    // "R" 같은 저장값 리터럴이 XAML에 등장하지 않도록 이 클래스 안에서만 표시 문구 ↔ 저장값을 매핑한다.
    private const string VanModeProductionDisplay = "운영 서버";
    private const string VanModeExternalTestDisplay = "테스트 서버";
    private const string VanModeInternalTestDisplay = "테스트 서버(내부용)";

    private const int MinimumConfigurableTimeoutSeconds = 30;

    private readonly ShopSettingsService _settingsService = new();

    /// <summary>ComboBox ItemsSource. 표시 문구만 담는다(저장값은 <see cref="ToStorageValue"/>가 매핑).</summary>
    public ObservableCollection<string> VanModeOptions { get; } = new()
    {
        VanModeProductionDisplay,
        VanModeExternalTestDisplay,
        VanModeInternalTestDisplay,
    };

    [ObservableProperty]
    private string vanModeSelection = VanModeProductionDisplay;

    [ObservableProperty]
    private string kioskId = string.Empty;

    // TextBox 바인딩이라 문자열로 들고 있는다 — 검증(숫자만·0 또는 30 이상)은 TryConfirm에서 수행한다.
    [ObservableProperty]
    private string cardReadTimeoutSecondsText = string.Empty;

    [ObservableProperty]
    private bool autoReboot = true;

    [ObservableProperty]
    private bool autoUpdate;

    [ObservableProperty]
    private bool keyinDim;

    /// <summary>P23-3 — 검증 실패(타임아웃 범위 위반) 또는 저장 실패(레지스트리 권한 등)를 View에
    /// 알린다. ReaderSetupViewModel.ResultMessageReady와 같은 이유로 이벤트로만 알리고 MessageBox를
    /// 직접 호출하지 않는다.</summary>
    public event EventHandler<string>? ResultMessageReady;

    // 2026-09-02 Opus 리뷰(CP1) 개선권장 9 — ReaderSetupViewModel의 dirty-check 스냅샷 패턴
    // (_snapshotReader1Port 등)과 동일하게, Load() 시점 값을 6개 필드 모두 따로 들고 있다가
    // IsDirty()에서 현재 값과 비교한다.
    private string _snapshotVanModeSelection = string.Empty;
    private string _snapshotKioskId = string.Empty;
    private string _snapshotCardReadTimeoutSecondsText = string.Empty;
    private bool _snapshotAutoReboot;
    private bool _snapshotAutoUpdate;
    private bool _snapshotKeyinDim;

    internal ShopSetupViewModel()
    {
        Load();
    }

    private void Load()
    {
        var settings = _settingsService.Load();

        VanModeSelection = ToDisplayValue(settings.VanMode);
        KioskId = settings.KioskId;
        CardReadTimeoutSecondsText = settings.CardReadTimeoutSeconds.ToString();
        AutoReboot = settings.AutoReboot;
        AutoUpdate = settings.AutoUpdate;
        KeyinDim = settings.KeyinDim;

        _snapshotVanModeSelection = VanModeSelection;
        _snapshotKioskId = KioskId;
        _snapshotCardReadTimeoutSecondsText = CardReadTimeoutSecondsText;
        _snapshotAutoReboot = AutoReboot;
        _snapshotAutoUpdate = AutoUpdate;
        _snapshotKeyinDim = KeyinDim;
    }

    /// <summary>
    /// 2026-09-02 Opus 리뷰(CP1) 개선권장 9(사용자 확정) — 취소/창 닫기(X, Alt+F4) 흐름에서
    /// <c>ReaderSetupWindow</c>와 동일하게 "변경사항을 버리시겠습니까" 확인창을 띄우기 위해 필요하다.
    /// 6개 필드 중 하나라도 <see cref="Load"/> 시점 스냅샷과 다르면 dirty.
    /// </summary>
    public bool IsDirty() =>
        VanModeSelection != _snapshotVanModeSelection ||
        KioskId != _snapshotKioskId ||
        CardReadTimeoutSecondsText != _snapshotCardReadTimeoutSecondsText ||
        AutoReboot != _snapshotAutoReboot ||
        AutoUpdate != _snapshotAutoUpdate ||
        KeyinDim != _snapshotKeyinDim;

    private static string ToDisplayValue(string vanMode) => vanMode switch
    {
        "OT" => VanModeExternalTestDisplay,
        "IT" => VanModeInternalTestDisplay,
        _ => VanModeProductionDisplay,
    };

    private static string ToStorageValue(string display) => display switch
    {
        VanModeExternalTestDisplay => "OT",
        VanModeInternalTestDisplay => "IT",
        _ => "R",
    };

    /// <summary>
    /// PRD.md §2.4 — 카드입력 타임아웃은 숫자만, 0 또는 30 이상만 허용한다. 음수나 비숫자 입력도
    /// 이 조건으로 함께 걸러진다(파싱 실패 시 통과 조건을 만족할 수 없음).
    /// </summary>
    private static bool IsValidTimeout(string text, out int seconds)
    {
        if (!int.TryParse(text, out seconds))
            return false;

        return seconds == 0 || seconds >= MinimumConfigurableTimeoutSeconds;
    }

    /// <summary>
    /// 확인 버튼(PRD.md §2.6) — 검증 후 저장한다. 검증 실패 시 저장하지 않고 false를 반환한다(View는
    /// 이 경우 창을 닫지 않는다). 저장은 <c>확인</c>을 눌렀을 때만 일어난다(취소는 아무것도 저장하지
    /// 않으므로 이 메서드를 호출하지 않는다).
    /// </summary>
    internal bool TryConfirm()
    {
        if (!IsValidTimeout(CardReadTimeoutSecondsText, out int timeoutSeconds))
        {
            ResultMessageReady?.Invoke(this, "30초 이상 입력");
            return false;
        }

        var settings = new ShopSettings
        {
            VanMode = ToStorageValue(VanModeSelection),
            KioskId = KioskId,
            CardReadTimeoutSeconds = timeoutSeconds,
            AutoReboot = AutoReboot,
            AutoUpdate = AutoUpdate,
            KeyinDim = KeyinDim,
        };

        try
        {
            _settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            // PRD.md §2.6 — 저장 실패는 조용히 넘기지 않는다(사용자가 방금 한 조작이 반영되지
            // 않았음을 알아야 한다). View는 이 메시지를 받으면 창을 닫지 않는다.
            ResultMessageReady?.Invoke(this, $"설정을 저장하지 못했습니다.\n{ex.Message}");
            return false;
        }

        // 2026-09-02 Opus 리뷰(CP1) 개선권장 4 — PRD.md §1.5가 "설정 저장"을 로그 경계로 명시한다.
        // 여기 적는 값들(VAN Mode/타임아웃/키오스크ID/토글 3종)은 카드·PIN이 아니라 이미 §1.6.1이
        // 장애 봉투에 싣기로 확정한 값들이라(VAN Mode, KIOSK_ID) 그대로 로그에 남겨도 된다. 거래ID는
        // 이 맥락에 없으므로 null.
        FileLogger.Info(
            LogCategory.Settings,
            $"가맹점 설정 저장 — VAN_MODE={settings.VanMode}, KIOSK_ID='{settings.KioskId}', " +
            $"TIMEOUT={settings.CardReadTimeoutSeconds}, AUTO_REBOOT={settings.AutoReboot}, " +
            $"AUTO_UPDATE={settings.AutoUpdate}, KEYIN_DIM={settings.KeyinDim}",
            code: null,
            transactionId: null);

        return true;
    }
}
