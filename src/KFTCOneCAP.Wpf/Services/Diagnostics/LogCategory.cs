namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-1, PRD.md §1.3-b) 구조화 로그의 카테고리 —
/// "통제 밖으로 나가거나 통제 안으로 들어오는" 경계 8종.
/// 렌더링 시 실제 텍스트(대문자, PRD §1.3-b 표)로 바꾸는 책임은 <see cref="LogCategoryText"/>가 진다.
/// </summary>
public enum LogCategory
{
    /// <summary>앱 기동/종료, DLL 로드, 로그 정리.</summary>
    App,

    /// <summary>소켓 서버, POS 전문 송수신.</summary>
    Pos,

    /// <summary><c>ReaderSerial.dll</c> 연동, 포트 열기/닫기, CALLBACK.</summary>
    Reader,

    /// <summary><c>KFTC_GIRO.dll</c>(<c>FNAISCRDVAN</c>) 호출.</summary>
    Van,

    /// <summary>결제 Flow 오케스트레이션, 큐, 알림창 상태 전이.</summary>
    Payment,

    /// <summary>키다운로드 5단계(PRD §3).</summary>
    Keydown,

    /// <summary>설정 화면 저장/반영(PRD §2).</summary>
    Settings,

    /// <summary>화면 열기/닫기, 키보드 후킹.</summary>
    Ui,
}

/// <summary>
/// <see cref="LogCategory"/> ↔ PRD.md §1.3-b가 정한 대문자 텍스트 변환. 렌더러(<see cref="LogLineRenderer"/>)
/// 외의 다른 곳에서 카테고리 이름을 하드코딩하지 않기 위해 이 한 지점으로 모은다.
/// </summary>
public static class LogCategoryText
{
    public static string ToText(this LogCategory category) => category switch
    {
        LogCategory.App => "APP",
        LogCategory.Pos => "POS",
        LogCategory.Reader => "READER",
        LogCategory.Van => "VAN",
        LogCategory.Payment => "PAYMENT",
        LogCategory.Keydown => "KEYDOWN",
        LogCategory.Settings => "SETTINGS",
        LogCategory.Ui => "UI",
        _ => throw new System.ArgumentOutOfRangeException(nameof(category), category, "매핑되지 않은 LogCategory"),
    };
}
