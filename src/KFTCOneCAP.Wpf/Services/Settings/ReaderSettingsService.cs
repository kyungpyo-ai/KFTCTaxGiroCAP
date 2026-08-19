using Microsoft.Win32;

namespace KFTCOneCAP.Wpf.Services.Settings;

/// <summary>
/// 리더기 설정 레지스트리(HKCU\Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT) 접근 계층.
/// Phase 7(docs/payment_relay/development_plan.md P7-1)에서 Views/ReaderSetupWindow.xaml.cs의
/// LoadFromRegistry/SaveToRegistry를 옮겨왔다 — Phase 9 이후 결제 Flow도 같은 COM 포트 값을
/// 읽어야 하므로(PRD §2.2.1) 화면 코드에 묶여 있으면 재사용이 불가능하다.
///
/// - MULTIPAD1_FIELD/MULTIPAD2_FIELD는 반전 인코딩(ON→"0", OFF→"1")이며, 이 인코딩 규칙이 화면
///   쪽으로 새어 나가지 않도록 이 클래스 안에서만 bool로 변환한다.
/// - 레지스트리 접근 실패(권한 등)를 예외로 던지지 않는다 — 기본값(미사용/꺼짐)으로 조용히
///   폴백한다(기존 코드비하인드와 동일 동작).
/// - WPF 타입에 의존하지 않는다(ViewModels → Services → Protocol → Interop 계층 규칙,
///   docs/payment_relay/ROADMAP.md "계층 구조").
/// </summary>
public sealed class ReaderSettingsService
{
    private const string RegistryKeyPath = @"Software\KFTC_VAN\KFTCTaxGiroCAP\SERIALPORT";

    public ReaderSettings Load()
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
            // 레지스트리 접근 실패(권한 등) — 아래 기본값(미사용/꺼짐) 폴백으로 조용히 무시한다.
        }

        return new ReaderSettings
        {
            Port1 = string.IsNullOrEmpty(port1) ? "미사용" : port1!,
            Port2 = string.IsNullOrEmpty(port2) ? "미사용" : port2!,
            Multipad1 = multipad1 == "0",
            Multipad2 = multipad2 == "0",
        };
    }

    public void Save(ReaderSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
        if (key is null)
            return;

        key.SetValue("COMPORT1_FIELD", settings.Port1, RegistryValueKind.String);
        key.SetValue("COMPORT2_FIELD", settings.Port2, RegistryValueKind.String);
        key.SetValue("MULTIPAD1_FIELD", settings.Multipad1 ? "0" : "1", RegistryValueKind.String);
        key.SetValue("MULTIPAD2_FIELD", settings.Multipad2 ? "0" : "1", RegistryValueKind.String);
    }
}
