namespace KFTCOneCAP.Wpf.Services.Settings;

/// <summary>
/// 리더기 설정 화면(및 Phase 9 이후 결제 Flow)이 공유하는 COM 포트/멀티패드 설정값.
/// 레지스트리의 반전 인코딩(MULTIPAD{N}_FIELD: ON→"0", OFF→"1")은
/// <see cref="ReaderSettingsService"/> 안에서만 다루고, 이 모델은 평범한 bool로만 노출한다
/// (docs/payment_relay/development_plan.md P7-1).
/// </summary>
public sealed class ReaderSettings
{
    /// <summary>리더기1 COM 포트 콤보 선택값(콤보 Content 그대로, 예: "COM 01", "미사용").</summary>
    public string Port1 { get; set; } = "미사용";

    /// <summary>리더기2 COM 포트 콤보 선택값.</summary>
    public string Port2 { get; set; } = "미사용";

    /// <summary>리더기1 멀티패드 토글(ON=true).</summary>
    public bool Multipad1 { get; set; }

    /// <summary>리더기2 멀티패드 토글(ON=true).</summary>
    public bool Multipad2 { get; set; }
}
