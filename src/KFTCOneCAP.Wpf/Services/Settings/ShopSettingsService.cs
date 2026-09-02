using System;
using Microsoft.Win32;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Services.Settings;

/// <summary>
/// 가맹점 설정(HKCU\Software\KFTC_VAN\KFTCTaxGiroCAP\TCP, ...\SERIALPORT) 접근 계층.
/// Phase 23(docs/operations/development_plan.md P23-1) — <see cref="ReaderSettingsService"/>와 같은
/// 패턴이다: 읽기 실패는 예외를 던지지 않고 기본값으로 폴백하고(PRD §0.3), 반전 인코딩은 이 클래스
/// 안에서만 다룬다.
///
/// <b>저장 위치가 두 하위 키에 걸쳐 있다</b>(2026-09-02 최종 확정) — <c>VAN_MODE</c>/<c>KIOSK_ID</c>는
/// <c>TCP</c>, <c>TIMEOUT</c>/<c>AUTO_REBOOT</c>/<c>AUTO_UPDATE</c>/<c>KEYIN_DIM</c>은
/// <c>SERIALPORT</c>다. <c>KIOSK_ID</c>만 원본 MFC에 없던 신규 항목이라 VAN Mode와 같은 `TCP`로
/// 모았다 — 코드 수정 시 이 매핑을 추측하지 말고 PRD.md §2.2~§2.5 표를 확인한다.
///
/// <b>이 클래스 밖으로 새어 나가면 안 되는 것 3가지</b>(PRD §2.2/§2.4/§2.5):
/// <list type="bullet">
/// <item>AUTO_REBOOT/AUTO_UPDATE/KEYIN_DIM의 반전 인코딩(ON→"0", OFF→"1").</item>
/// <item><b>카드입력 타임아웃의 "0=미설정" 규칙</b> — 레지스트리 값이 <c>0</c>이거나 아예 없으면
/// <see cref="Load"/>가 <c>120</c>을 돌려준다(2026-09-01 사용자 확정, PRD §2.4). 무제한이 아니다.
/// <c>Save</c>는 이 변환을 하지 않는다 — 사용자가 입력한 값을 그대로 쓴다(다시 열었을 때 방금 입력한
/// <c>0</c>이 그대로 보여야 한다).</item>
/// <item>레지스트리에 손으로 써넣은 이상값 처리 — <c>TIMEOUT</c>이 숫자가 아니거나 1~29(화면 검증이
/// 막는 범위), <c>VAN_MODE</c>가 R/OT/IT 외의 값, <c>KIOSK_ID</c>가 20자 초과인 경우 각각 안전한
/// 기본값으로 폴백하고 <c>WARN</c> 로그를 남긴다(이 계획서 P23-1 판단 — PRD에 명시 없음).</item>
/// </list>
///
/// WPF 타입에 의존하지 않는다(ViewModels → Services → Protocol → Interop 계층 규칙).
///
/// <para><b>반복 WARN 로그 억제(2026-09-02 CP2 — Opus 리뷰(CP1) 개선권장 8 해결)</b> — <c>PaymentOrchestrator</c>가
/// 거래마다(902614 한 건당 최대 3회) <see cref="Load"/>를 호출하게 되면서(P23-6/P23-7, 설정값 캐시
/// 금지 PRD §2.6) 이상값이 바뀌지 않았는데도 같은 WARN이 거래마다 반복 출력될 위험이 실제로 생겼다.
/// <see cref="PaymentOrchestrator"/>/<see cref="Van.VanService"/> 둘 다 생성자에서 <c>new
/// ShopSettingsService().Load</c>를 메서드 그룹으로 한 번만 바인딩해 앱 수명 동안 <b>같은
/// <see cref="ShopSettingsService"/> 인스턴스를 재사용</b>한다(그 두 클래스 자체가 앱 수명 싱글턴으로
/// 한 번만 생성됨, <c>App.xaml.cs</c>) — 따라서 인스턴스 필드에 "마지막으로 로깅한 이상값(raw)"을
/// 3개(VAN_MODE/KIOSK_ID/TIMEOUT 각각) 기억해뒀다가, 같은 이상값이면 WARN을 다시 찍지 않고 값이
/// 바뀔 때만(정상값으로 복구된 뒤 다시 같은 이상값이 나타나는 경우도 포함) 다시 찍는다.</para>
/// </summary>
public sealed class ShopSettingsService
{
    private const string TcpKeyPath = @"Software\KFTC_VAN\KFTCTaxGiroCAP\TCP";
    private const string SerialPortKeyPath = @"Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT";

    private const int DefaultCardReadTimeoutSeconds = 120;
    private const int MinimumConfigurableTimeoutSeconds = 30;

    // 개선권장 2(CP2 Opus 리뷰) — 직전에 WARN으로 남긴 이상값(raw) 3종. null이면 "아직 이 이상값으로
    // 경고한 적 없음"을 뜻한다. 정상값으로 돌아오면 각 Resolve*가 이 필드를 다시 null로 되돌려, 같은
    // 이상값이 나중에 재발해도 다시 한번 WARN이 찍히게 한다(완전 무음이 되지 않도록).
    private string? _lastWarnedVanModeRaw;
    private string? _lastWarnedKioskIdRaw;
    private string? _lastWarnedTimeoutRaw;

    public ShopSettings Load()
    {
        string? vanMode = null;
        string? kioskId = null;
        string? timeoutText = null;
        string? autoReboot = null;
        string? autoUpdate = null;
        string? keyinDim = null;

        try
        {
            using var tcpKey = Registry.CurrentUser.OpenSubKey(TcpKeyPath);
            if (tcpKey != null)
            {
                vanMode = tcpKey.GetValue("VAN_MODE") as string;
                kioskId = tcpKey.GetValue("KIOSK_ID") as string;
            }

            using var serialKey = Registry.CurrentUser.OpenSubKey(SerialPortKeyPath);
            if (serialKey != null)
            {
                timeoutText = serialKey.GetValue("TIMEOUT") as string;
                autoReboot = serialKey.GetValue("AUTO_REBOOT") as string;
                autoUpdate = serialKey.GetValue("AUTO_UPDATE") as string;
                keyinDim = serialKey.GetValue("KEYIN_DIM") as string;
            }
        }
        catch
        {
            // 레지스트리 접근 실패(권한 등) — 아래 기본값 폴백으로 조용히 무시한다(ReaderSettingsService와 동일 원칙).
        }

        return new ShopSettings
        {
            VanMode = ResolveVanMode(vanMode),
            KioskId = ResolveKioskId(kioskId),
            CardReadTimeoutSeconds = ResolveCardReadTimeoutSeconds(timeoutText),
            AutoReboot = autoReboot != "1",
            AutoUpdate = autoUpdate == "0",
            KeyinDim = keyinDim == "0",
        };
    }

    /// <summary>
    /// 2026-09-02 Opus 리뷰(CP1) 개선권장 6 — <c>CreateSubKey</c>가 <c>null</c>을 반환하는 극단적
    /// 권한 문제 상황에서 예전엔 조용히 <c>return</c>했다. <c>TCP</c> 키는 성공하고
    /// <c>SERIALPORT</c> 키만 실패하면 절반만 저장된 채 호출부(<c>ShopSetupViewModel.TryConfirm</c>)가
    /// 성공으로 착각해 창을 닫을 위험이 있었다(PRD.md §2.6 "저장 실패는 조용히 넘기지 않는다" 위반).
    /// <see cref="ReaderSettingsService.Save"/>와 계약을 맞춰 예외를 던진다 — 호출부는 이미
    /// <c>try/catch</c>로 저장 실패를 사용자에게 알리고 있다(<see cref="ShopSetupViewModel.TryConfirm"/>).
    /// </summary>
    public void Save(ShopSettings settings)
    {
        using var tcpKey = Registry.CurrentUser.CreateSubKey(TcpKeyPath);
        if (tcpKey is null)
            throw new InvalidOperationException($@"레지스트리 키를 생성하지 못했습니다: HKCU\{TcpKeyPath}");

        tcpKey.SetValue("VAN_MODE", settings.VanMode, RegistryValueKind.String);
        tcpKey.SetValue("KIOSK_ID", settings.KioskId, RegistryValueKind.String);

        using var serialKey = Registry.CurrentUser.CreateSubKey(SerialPortKeyPath);
        if (serialKey is null)
            throw new InvalidOperationException($@"레지스트리 키를 생성하지 못했습니다: HKCU\{SerialPortKeyPath}");

        // 입력값을 그대로 쓴다 — 0은 0으로 저장한다(Load()에서만 120으로 해석한다).
        serialKey.SetValue("TIMEOUT", settings.CardReadTimeoutSeconds.ToString(), RegistryValueKind.String);
        serialKey.SetValue("AUTO_REBOOT", settings.AutoReboot ? "0" : "1", RegistryValueKind.String);
        serialKey.SetValue("AUTO_UPDATE", settings.AutoUpdate ? "0" : "1", RegistryValueKind.String);
        serialKey.SetValue("KEYIN_DIM", settings.KeyinDim ? "0" : "1", RegistryValueKind.String);
    }

    /// <summary>PRD §2.2 이상값 폴백 — 알 수 없는 Mode는 운영("R")로 폴백한다. 테스트 서버로 조용히
    /// 붙는 것보다, 설정이 깨졌을 때 운영으로 가는 편이 "결제가 되긴 한다"는 관점에서 안전하다.
    /// 개선권장 2(CP2) — 같은 이상값이면 WARN을 반복하지 않는다(클래스 요약 참고).</summary>
    private string ResolveVanMode(string? raw)
    {
        if (raw is "R" or "OT" or "IT")
        {
            _lastWarnedVanModeRaw = null; // 정상값으로 복구 — 다음 이상값은 새로 경고한다.
            return raw;
        }

        if (!string.IsNullOrEmpty(raw) && raw != _lastWarnedVanModeRaw)
        {
            FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] VAN_MODE 이상값 '{raw}' — 운영(R)으로 폴백", code: null, transactionId: null);
            _lastWarnedVanModeRaw = raw;
        }

        return "R";
    }

    /// <summary>
    /// 2026-09-02 Opus 리뷰(CP1) 개선권장 5 — PRD.md §2.3.2가 "설정값이 비어 있어도 거부(E06)"로
    /// 재확정되면서, 이 폴백의 실제 효과가 바뀌었다. 20자를 넘는 값을 그대로 써도 AN(20) SPEC과 맞을
    /// 수 없어 어차피 결제는 거부된다 — 그 점은 폴백 이전과 동일하다. 이 폴백이 막는 것은 더 이상
    /// "거부 자체"가 아니라 "거부 이유가 불명확해지는 것"이다: 폴백이 없으면 21자짜리 값이 그대로
    /// 저장되고, 매 거래마다 그 이상한 값과 정상적인 `#42` 수신값이 계속 불일치로 거부되는데 현장에서
    /// 그 원인이 "레지스트리에 잘못된 값이 들어있다"는 사실과 바로 연결되지 않는다. 빈 값으로
    /// 취급하면 §2.3.2의 "빈 값은 거부" 규칙 하나로 통일되고, 관리자가 20자 넘는 값을 실수로
    /// 넣었다는 사실 자체는 아래 WARN 로그로 남으므로 현장에서 원인 추적은 여전히 가능하다.
    /// (동작 자체는 그대로다 — 이 주석만 최신 정책에 맞춰 정정했다.)
    /// </summary>
    private string ResolveKioskId(string? raw)
    {
        // net48 BCL의 string.IsNullOrEmpty에는 NotNullWhen 어노테이션이 없어 null 가능성 분석이
        // 이어지지 않는다 — is null 패턴으로 직접 좁혀 CS8602 경고를 없앤다(LogLineRenderer와 동일 패턴).
        if (raw is null || raw.Length == 0)
        {
            _lastWarnedKioskIdRaw = null; // 정상값(빈 값)으로 복구.
            return string.Empty;
        }

        if (raw.Length > 20)
        {
            if (raw != _lastWarnedKioskIdRaw)
            {
                FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] KIOSK_ID 길이 초과({raw.Length}자) — 빈 값으로 취급(검증 미수행)", code: null, transactionId: null);
                _lastWarnedKioskIdRaw = raw;
            }

            return string.Empty;
        }

        _lastWarnedKioskIdRaw = null; // 정상값으로 복구.
        return raw;
    }

    /// <summary>PRD §2.4 — "0" 또는 값 없음은 120초로 해석한다(무제한 아님, 2026-09-01 확정).
    /// 숫자가 아니거나 화면 검증이 막는 1~29 범위의 이상값도 120으로 폴백한다. 개선권장 2(CP2) —
    /// 두 이상값 분기(비숫자/음수, 1~29 범위)가 같은 억제 필드(<see cref="_lastWarnedTimeoutRaw"/>)를
    /// 공유한다 — raw 텍스트가 같으면 같은 이상값으로 취급한다.</summary>
    private int ResolveCardReadTimeoutSeconds(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            _lastWarnedTimeoutRaw = null; // 정상값(미설정)으로 복구.
            return DefaultCardReadTimeoutSeconds;
        }

        if (!int.TryParse(raw, out int seconds) || seconds < 0)
        {
            if (raw != _lastWarnedTimeoutRaw)
            {
                FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] TIMEOUT 이상값 '{raw}' — {DefaultCardReadTimeoutSeconds}초로 폴백", code: null, transactionId: null);
                _lastWarnedTimeoutRaw = raw;
            }

            return DefaultCardReadTimeoutSeconds;
        }

        if (seconds == 0)
        {
            _lastWarnedTimeoutRaw = null; // 정상값(0=미설정)으로 복구.
            return DefaultCardReadTimeoutSeconds;
        }

        if (seconds < MinimumConfigurableTimeoutSeconds)
        {
            if (raw != _lastWarnedTimeoutRaw)
            {
                FileLogger.Warn(LogCategory.Settings, $"[ShopSettingsService] TIMEOUT 이상값 {seconds}초(30 미만) — {DefaultCardReadTimeoutSeconds}초로 폴백", code: null, transactionId: null);
                _lastWarnedTimeoutRaw = raw;
            }

            return DefaultCardReadTimeoutSeconds;
        }

        _lastWarnedTimeoutRaw = null; // 정상값으로 복구.
        return seconds;
    }
}
