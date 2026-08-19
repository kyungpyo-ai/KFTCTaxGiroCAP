using System;
using System.IO;
using System.Reflection;
using System.Windows;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 컴팩트 모드 판정 기준(96dpi 환산 px) — PRD 2.2/3.2 "화면 높이 ≤800px".
    /// docs/home_reader_setup/ROADMAP.md Phase 6 참고.
    /// </summary>
    private const double CompactHeightThreshold = 800.0;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Phase 8(docs/payment_relay/development_plan.md P8-4): 앱 기동 시 두 네이티브 DLL의 로드
        // 가능 여부를 미리 확인해 로그로 남긴다. 실제 함수 호출은 Phase 9/17 몫이며, 여기서는 로드
        // 실패해도 앱 기동을 막지 않는다(PRD §9).
        FileLogger.Info("애플리케이션 기동 시작");
        string baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        NativeDllLoadSmokeTest.RunAll(baseDirectory);

        // 컴팩트 모드 판정(임의 판단, 2026-08-14 Phase 6): SystemParameters.WorkArea.Height(작업
        // 표시줄 등을 제외한 가용 영역) 대신 SystemParameters.PrimaryScreenHeight(모니터 자체의
        // 해상도 높이)를 사용한다. PRD 원문이 "화면 높이(screen height) ≤800px"라고 명시하고
        // 있어 "이 화면(모니터) 자체가 저해상도인가"를 묻는 것에 가깝다고 판단했다 — WorkArea는
        // 작업표시줄 위치/크기에 따라 매번 달라져(예: 작업표시줄을 위/아래로 옮기거나 두껍게
        // 하면) 같은 모니터에서도 판정이 오락가락할 수 있는 반면, PrimaryScreenHeight는 모니터
        // 해상도 자체라 원본 MFC 앱의 의도(저해상도 화면 대응)에 더 안정적으로 부합한다. 둘 다
        // WPF에서는 96dpi 논리 픽셀 단위로 반환되므로 별도 DPI 스케일링은 필요 없다.
        bool isCompact = SystemParameters.PrimaryScreenHeight <= CompactHeightThreshold;

        var typographySource = isCompact
            ? new Uri("Themes/Typography.Compact.xaml", UriKind.Relative)
            : new Uri("Themes/Typography.xaml", UriKind.Relative);
        var layoutSource = isCompact
            ? new Uri("Themes/Layout.Compact.xaml", UriKind.Relative)
            : new Uri("Themes/Layout.xaml", UriKind.Relative);

        // 순서 중요: Buttons.xaml이 Layout.xaml 키를, Typography/Buttons/ComboBox 등이 Colors.xaml
        // 브러시를 자기 파싱 시점에 StaticResource로 즉시 참조하므로 반드시 이 순서로 병합해야
        // 한다(App.xaml 상단 주석 참고).
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Colors.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = typographySource });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = layoutSource });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Buttons.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/ComboBox.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/ToggleSwitch.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/TextBox.xaml", UriKind.Relative) });

        base.OnStartup(e);
    }
}
