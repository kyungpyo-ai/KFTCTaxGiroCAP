using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Payment;
using KFTCOneCAP.Wpf.Services.Pos;
using KFTCOneCAP.Wpf.Services.Reader;
using KFTCOneCAP.Wpf.Services.Settings;
using KFTCOneCAP.Wpf.ViewModels;
using KFTCOneCAP.Wpf.Views;

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

    /// <summary>
    /// Phase 12(docs/payment_relay/development_plan.md P12-1) — 리더기1/2 포트의 앱 수명 소유자.
    /// 이 정적 프로퍼티가 유일한 접근점이다(DI 컨테이너를 도입하지 않는 이유는
    /// <see cref="ReaderConnectionManager"/> 클래스 주석 참고). <c>Views/ReaderSetupWindow.xaml.cs</c>가
    /// <c>ReaderSetupViewModel</c> 생성 시 이 값을 전달만 하고, ViewModel은 이 매니저를 생성하지 않는다.
    /// </summary>
    internal static ReaderConnectionManager? ReaderConnections { get; private set; }

    /// <summary>
    /// Phase 14(docs/payment_relay/development_plan.md P14-3) — 결제 요청을 순차 처리하는 유일한
    /// 워커/큐. <see cref="ReaderConnections"/>와 같은 이유로 정적 접근점 하나만 둔다(DI 컨테이너
    /// 미사용). Phase 15가 이 큐의 처리 위임을 스텁에서 <c>PaymentOrchestrator</c>로 교체한다.
    /// </summary>
    internal static TransactionQueue? PaymentQueue { get; private set; }

    /// <summary>Phase 14(P14-2) — PRD §3.1 <c>localhost:8002</c> 소켓 서버의 앱 수명 소유자.</summary>
    internal static PosSocketServer? PosServer { get; private set; }

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

        // Phase 12(P12-1): 리더기 설정 화면이 열리기 전에도 레지스트리에 설정된 포트를 앱 기동 시
        // 미리 연다(PRD §2.2.2 "항상 열어둔다"). 열기 실패해도 여기서 모달을 띄우지 않는다 — 이
        // 앱은 트레이 상주로 자동 최소화 기동하므로(원본 동작), 기동 직후 모달은 사용자가 보지도
        // 못한 채 포커스만 뺏는다. 실패한 포트는 다음 명령 시 SendCommandSafe(P10-3)가 자동으로
        // 재오픈을 시도한다.
        ReaderConnections = new ReaderConnectionManager(new ReaderSettingsService());
        ReaderConnections.InitializeFromSettings();

        // Phase 13(P13-1): 결제 알림창 배경 3장을 미리 디코드해 캐시(표시 지연 방지).
        PaymentNoticeBackgroundSource.WarmUp();

        // Phase 14(P14-2/P14-3): 소켓 서버 + 단일 워커 Queue 기동. 처리 위임은 지금은 스텁이고
        // (Phase 15에서 PaymentOrchestrator로 교체), 8002 포트가 이미 사용 중이어도(PRD §9) 앱
        // 기동은 막지 않는다 — PosServer.Start()가 실패를 로그로만 남기고 넘어간다.
        PaymentQueue = new TransactionQueue(StubPaymentProcessor);
        PosServer = new PosSocketServer(PaymentQueue);
        PosServer.Start();

        if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--gallery")
        {
            StartupUri = new Uri("Views/StyleGalleryWindow.xaml", UriKind.Relative);
        }
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--home")
        {
            StartupUri = new Uri("Views/HomeWindow.xaml", UriKind.Relative);
        }
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--home-notice-test")
        {
            // 개발/회귀 검증용(docs/payment_relay/development_plan.md P13-4): 홈 화면을 먼저 띄운 뒤
            // 2초 후 알림창을 같은 프로세스에서 띄워, "알림창이 홈 화면을 전면에 끌어올리지 않는다"를
            // 실기로 재현할 수 있게 한다. StartupUri만으로는 창을 하나만 띄울 수 있어 별도 분기로 둔다.
            StartupUri = new Uri("Views/HomeWindow.xaml", UriKind.Relative);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                new PaymentNoticeWindow().Show();
            };
            timer.Start();
        }
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--notice-van-processing-test")
        {
            // 개발/회귀 검증용(P13-5 완료 조건 "VanProcessing 중 ESC를 눌러도 취소가 발생하지 않음"):
            // 3초 자동 순환 데모(PaymentNoticeWindow())는 상태가 계속 바뀌어 타이밍을 맞추기 어려우므로,
            // State를 VanProcessing으로 고정한 채(순환 없이) 띄워 ESC 게이팅을 정확히 재현/검증한다.
            var vm = new PaymentNoticeViewModel { State = PaymentNoticeState.VanProcessing };
            var window = new PaymentNoticeWindow(vm);
            MainWindow = window;
            window.Show();

            // VanProcessing 상태에서는 취소 버튼이 항상 disabled로 보이므로(정상 게이팅이든, ESC가
            // 게이트를 뚫고 실제로 취소해버린 버그든 겉보기엔 똑같다), 5초 뒤 IcCardRequest로 전환해
            // "여전히 취소 가능한지"(_canceled가 실제로는 false인지)를 눈으로 구분할 수 있게 한다.
            var revealTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            revealTimer.Tick += (_, _) =>
            {
                revealTimer.Stop();
                vm.State = PaymentNoticeState.IcCardRequest;
            };
            revealTimer.Start();
        }
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--presenter-test")
        {
            // 개발/회귀 검증용(P13-6 완료 조건): IPaymentNoticePresenter의 모든 메서드를 백그라운드
            // 스레드에서 호출해 예외 없이 동작하는지, 닫힌 뒤 ChangeState/Close가 조용히 무시되는지,
            // Canceled 이벤트가 취소 1회당 정확히 1번만 발생하는지 확인한다. 각 단계 사이에 사람이
            // 취소 버튼/ESC를 눌러볼 시간(대기 구간)을 둔다 — 로그(FileLogger)로 결과 확인.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var presenter = new PaymentNoticePresenter();
            int cancelCount = 0;
            presenter.Canceled += (_, _) =>
            {
                cancelCount++;
                FileLogger.Info($"[presenter-test] Canceled 이벤트 발생 (누적 {cancelCount}회)");
            };

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    presenter.Show(PaymentNoticeState.IcCardRequest);
                    FileLogger.Info("[presenter-test][BG] Show(IcCardRequest) 성공");
                    System.Threading.Thread.Sleep(1000);

                    presenter.ChangeState(PaymentNoticeState.FallbackCardRequest);
                    FileLogger.Info("[presenter-test][BG] ChangeState(FallbackCardRequest) 성공");
                    System.Threading.Thread.Sleep(1000);

                    presenter.ChangeState(PaymentNoticeState.VanProcessing);
                    FileLogger.Info("[presenter-test][BG] ChangeState(VanProcessing) 성공");
                    System.Threading.Thread.Sleep(1000);

                    presenter.ChangeState(PaymentNoticeState.IcCardRequest);
                    FileLogger.Info("[presenter-test][BG] ChangeState(IcCardRequest) 성공 — 이제 취소 가능 상태, 15초 대기(수동 취소 테스트 구간)");
                    System.Threading.Thread.Sleep(15000);

                    presenter.Close();
                    FileLogger.Info("[presenter-test][BG] Close() 성공");

                    presenter.Close();
                    FileLogger.Info("[presenter-test][BG] Close() 재호출(이미 닫힘) — 예외 없이 통과했으면 성공, 위 경고 로그 확인");

                    presenter.ChangeState(PaymentNoticeState.IcCardRequest);
                    FileLogger.Info("[presenter-test][BG] 닫힌 뒤 ChangeState() — 예외 없이 통과했으면 성공, 위 경고 로그 확인");

                    FileLogger.Info($"[presenter-test] 전체 완료 — 예외 없음, Canceled 누적 {cancelCount}회");
                }
                catch (Exception ex)
                {
                    FileLogger.Error($"[presenter-test][BG] 예외 발생: {ex}");
                }
            });
        }
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--esc-hook-stress-test")
        {
            // 개발/회귀 검증용(docs/payment_relay/development_plan.md P13-5 완료 조건 "10회 연속
            // 열고 닫은 뒤에도 훅이 남아 있지 않음"): 알림창을 같은 프로세스에서 10회 연속 열고 닫아
            // ESC 훅 설치/해제(PaymentNoticeEscapeHook.Install/Uninstall)가 누적 실패 없이 반복되는지
            // 확인한다. 훅 설치는 Loaded에서, 해제는 Closed에서 일어나며, Show()가 반환하기 전에
            // Loaded가 처리되므로(WPF가 Show() 안에서 그만큼 디스패처를 펌프함) 메시지 펌프 없이도
            // 안전하다. 예외 없이 끝까지 돌면 성공 — 끝나면 조용히 종료한다.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            for (int i = 0; i < 10; i++)
            {
                var window = new PaymentNoticeWindow();
                window.Show();
                window.Close();
            }
            FileLogger.Info("ESC 훅 스트레스 테스트(알림창 10회 연속 열고 닫기) 완료 — 예외 없음");
            Shutdown();
        }
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--pos-client-test")
        {
            // 개발/회귀 검증용(P14-3/P14-4/P14-5 완료 조건, 최종 산출물 아님): 소켓 서버가 이미
            // 기동돼 있으므로(위에서 Start() 완료) 백그라운드에서 이 프로세스 자신에게 클라이언트로
            // 접속해 시나리오를 재현한다. UI는 홈 화면을 그대로 띄워 앱이 평소처럼 동작함을 같이 보여준다.
            StartupUri = new Uri("Views/HomeWindow.xaml", UriKind.Relative);
            System.Threading.Tasks.Task.Run(PosClientTestScenarios.RunAll);
        }
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--notice-demo")
        {
            // 개발용 결제 알림창 실시간 애니메이션 데모(수동 실행 전용). 예전엔 인자 없는 기본
            // 실행이 곧장 이 데모로 갔는데(Opus 검증 리뷰 2026-08-24, H-2), 그러면 배포 빌드에서
            // exe를 그냥 실행했을 때도 알림창 데모가 뜬다 — 원본 앱은 트레이 상주로 홈 화면 기동이
            // 정상 동작이므로 어긋난다. 데모는 이 명시적 인자로만 접근하게 분리했다.
            StartupUri = new Uri("Views/PaymentNoticeWindow.xaml", UriKind.Relative);
        }
        else
        {
            // 기본 실행: 홈 화면(원본 앱 정상 동작과 일치 — 1차 범위 완료 문서 참고).
            StartupUri = new Uri("Views/HomeWindow.xaml", UriKind.Relative);
        }

        base.OnStartup(e);
    }

    /// <summary>Phase 12(P12-1)/Phase 14(P14-2/P14-3) — 앱 종료 시 리소스를 정리한다(PRD §9).</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        PosServer?.Stop();
        PaymentQueue?.Stop(TimeSpan.FromSeconds(5));
        ReaderConnections?.CloseAll();
        base.OnExit(e);
    }

    /// <summary>
    /// Phase 14 스텁 처리 위임(P14-3) — Phase 15에서 <c>PaymentOrchestrator</c>로 교체될 자리다.
    /// 지금은 "일정 시간 지연 후 고정 응답"만 돌려준다. 금액 필드에 <c>THROW</c>를 넣으면 일부러
    /// 예외를 던진다 — 워커가 예외에도 죽지 않는지 확인하는 개발용 테스트 경로(P14-3 완료 조건).
    /// </summary>
    private static PosPaymentResponse StubPaymentProcessor(PosPaymentRequest request)
    {
        FileLogger.Info($"[stub] 결제 처리 시작 txId={request.TransactionId} amount={request.Amount}");

        if (request.Amount == "THROW")
        {
            throw new InvalidOperationException("[stub] 의도적으로 던진 테스트 예외(amount=THROW)");
        }

        Thread.Sleep(1500);

        FileLogger.Info($"[stub] 결제 처리 종료 txId={request.TransactionId}");

        // 개발용 테스트 경로(H-1 재검증, --pos-client-test): 응답 본문을 최대치 근처까지 부풀려,
        // 응답을 안 읽는 클라이언트를 상대로 실제 소켓 쓰기 블로킹을 유도할 수 있게 한다. 운영
        // 경로에서는 amount에 이 값이 올 수 없다(POS가 보내는 임시 전문 필드일 뿐).
        if (request.Amount == "BIGRESPONSE")
        {
            return new Protocol.Pos.PosPaymentResponse("00", request.TransactionId, new string('A', 9900));
        }

        return new Protocol.Pos.PosPaymentResponse("00", request.TransactionId, "OK(STUB)");
    }
}
