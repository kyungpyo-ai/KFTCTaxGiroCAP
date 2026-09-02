using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Payment;
using KFTCOneCAP.Wpf.Services.Pos;
using KFTCOneCAP.Wpf.Services.Reader;
using KFTCOneCAP.Wpf.Services.Settings;
using KFTCOneCAP.Wpf.Services.Storage;
using KFTCOneCAP.Wpf.Services.Van;
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
    /// Phase 15(docs/payment_relay/development_plan.md P15-4) — <b>리더기 설정 화면과 가맹점 설정
    /// 화면(Phase 23, docs/operations/development_plan.md P23-2) 둘 다</b> 열려 있는지 결제 Flow가
    /// 판정하는 유일한 접근점(카운터 공유, PRD.md §2.7). <see cref="ReaderConnections"/>와 달리 다른
    /// 서비스에 의존하지 않아 <c>OnStartup</c>을 기다릴 필요가 없으므로 필드 초기화로 즉시 만든다 —
    /// <c>ReaderSetupWindow</c>/<c>ShopSetupWindow</c>가 <c>OnStartup</c> 이전(테스트 하네스 등)에
    /// 생성돼도 안전하다.
    /// </summary>
    internal static Views.SetupScreenGate SetupScreenGate { get; } = new();

    /// <summary>
    /// Phase 14(docs/payment_relay/development_plan.md P14-3) — 결제 요청을 순차 처리하는 유일한
    /// 워커/큐. <see cref="ReaderConnections"/>와 같은 이유로 정적 접근점 하나만 둔다(DI 컨테이너
    /// 미사용). Phase 15부터 이 큐의 처리 위임은 <see cref="Orchestrator"/>의 <c>ProcessAsync</c>다.
    /// </summary>
    internal static TransactionQueue? PaymentQueue { get; private set; }

    /// <summary>Phase 14(P14-2) — PRD §3.1 <c>localhost:8002</c> 소켓 서버의 앱 수명 소유자.</summary>
    internal static PosSocketServer? PosServer { get; private set; }

    /// <summary>
    /// Phase 15(docs/payment_relay/development_plan.md P15-6) — PRD §4.1 결제 처리 순서를 조립하는
    /// 자리. <see cref="ReaderConnections"/>와 같은 이유로 정적 접근점 하나만 둔다. 이 프로퍼티 자체는
    /// 진단/디버깅 외 다른 코드가 참조할 필요가 없다(<see cref="PaymentQueue"/>가 이미
    /// <c>Orchestrator.ProcessAsync</c>를 위임으로 들고 있다) — 다른 정적 프로퍼티들과의 일관성,
    /// 그리고 필요 시 진단 하네스가 인스턴스에 접근할 수 있도록 노출해 둔다.
    /// </summary>
    internal static PaymentOrchestrator? Orchestrator { get; private set; }

    /// <summary>
    /// 단일 인스턴스 가드용 Mutex(docs/operations/development_plan.md P22-0 보강, 2026-09-01) — 관리자
    /// 권한 승격(UAC) 과정에서 exe가 중복 기동되는 현상이 실물 검증 중 발견됐다. 소유권을 얻은
    /// (새 인스턴스로 판정된) 프로세스만 이 필드를 보관하며, 앱 종료 시 <see cref="OnExit"/>에서
    /// 명시적으로 해제한다. .NET Mutex는 프로세스 종료 시 OS가 자동 회수하므로 이 명시적 해제가
    /// 없어도 안전하지만, 정상 종료 경로에서 즉시 해제해 두는 편이 재실행 지연을 줄인다.
    /// </summary>
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 단일 인스턴스 가드(docs/operations/development_plan.md P22-0 보강, 2026-09-01): 로그 싱크
        // 구성보다도 먼저 체크한다 — 이미 살아있는 인스턴스가 있으면 이 프로세스는 로그 한 줄 남기지
        // 않고 조용히 즉시 종료한다(살아있는 인스턴스가 있다면 그쪽 로그에 흔적을 남기는 편이 더
        // 유용하지만, 그건 이 인스턴스가 할 일이 아니다). 개발용 진단 하네스 인자(--payment-flow-test
        // 등, 아래 분기 참고)가 있을 때는 가드를 건너뛴다 — 프로덕션 기동은 인자 없이 이뤄지므로,
        // 인자가 있으면 개발자가 의도적으로 여러 인스턴스/여러 하네스를 동시에 띄우는 상황으로
        // 간주한다.
        if (e.Args.Length == 0 && !TryAcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        // Phase 22(docs/operations/development_plan.md P22-3/P22-4, PRD.md §1.3-a/§1.3-d): 로그 싱크
        // 목록을 앱 기동 시 한 번 구성한다. 다른 모든 FileLogger 호출보다 먼저 실행돼야 한다.
        // FileLogSink는 파일에 렌더링해 남기고, RingBufferSink는 최근 500건을 메모리에 유지한다
        // (장래 장애 보고 기능이 LogRingBuffer 정적 메서드로 직접 조회). 장래 원격 싱크는 여기에
        // 인자를 추가하는 것만으로 병렬 연결된다.
        FileLogger.ConfigureSinks(new FileLogSink(), new RingBufferSink());

        // Phase 22(docs/operations/development_plan.md P22-5, PRD.md §1.2): 90일 보관 정리를 앱 기동
        // 시 1회 백그라운드로 수행한다(기동 경로를 블로킹하지 않음). 날짜가 바뀌어 새 로그 파일을
        // 처음 만들 때의 재실행은 FileLogSink가 자체적으로 트리거한다.
        LogRetentionCleaner.RunAtStartup();

        // Phase 8(docs/payment_relay/development_plan.md P8-4): 앱 기동 시 두 네이티브 DLL의 로드
        // 가능 여부를 미리 확인해 로그로 남긴다. 실제 함수 호출은 Phase 9/17 몫이며, 여기서는 로드
        // 실패해도 앱 기동을 막지 않는다(PRD §9).
        FileLogger.Info(LogCategory.App, "애플리케이션 기동 시작");
        string baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        NativeDllLoadSmokeTest.RunAll(baseDirectory);

        // Phase 17(P17-2/P17-3, 체크포인트 1 검증 M-2 수정): POS 전문 스키마 3종을 기동 시점에 강제로
        // 초기화한다. 스키마 생성자가 POSITION 연속성·총 길이를 자체 검증하므로, SPEC 표를 옮겨 적다
        // 틀렸다면 첫 결제 요청 때가 아니라 지금 즉시 드러난다 — DLL 로드 스모크(위)와 같은 취지다.
        // DLL 로드와 달리 이건 우리 코드의 자체 모순이라 조용히 넘기지 않고 그대로 던진다.
        PosSchemaRegistry.ValidateAtStartup();
        FileLogger.Info("POS 전문 스키마 3종 검증 완료(POSITION 연속성·총 길이·라우팅 상수 일치)");

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

        // Phase 15(P15-6~P15-9), Phase 17(P17-5)에서 3전문 구조로 재구성: 결제 Flow 조립. 리더기1/2를
        // IReaderEndpoint로 감싸고(P15-2 어댑터), 하나의 IntegrityCheckStore/IntegrityCheckService를
        // 공유시킨다(App.ReaderConnections의 Reader1/Reader2 순서를 그대로 따름 — PaymentOrchestrator
        // 클래스 주석의 "인덱스 0=리더기1" 전제). VAN은 아직 스텁(실제 FNAISCRDVAN은 Phase 20).
        var integrityStore = new IntegrityCheckStore();
        var observedIdentityStore = new ObservedIdentityStore();
        var integrityCheckService = new IntegrityCheckService(integrityStore);
        var readerEndpoints = new IReaderEndpoint[]
        {
            new ReaderEndpoint(ReaderConnections.Reader1, integrityCheckService),
            new ReaderEndpoint(ReaderConnections.Reader2, integrityCheckService),
        };
        var paymentPresenter = new PaymentNoticePresenter();
        var vanRelay = new StubVanRelayService();
        // (2026-08-25, Opus 검증 리뷰 M-2 — Phase 17에서 StubVanRelayService로 교체돼도 같은 취지로
        // 유지) 이 빌드를 실단말에서 그대로 돌리면 모든 거래가 실제 VAN 통신 없이 조용히 승인된다 —
        // 로그만 보는 사람이 실거래 승인으로 오해하지 않도록 기동 시점에 명시적으로 남긴다. Phase 20이
        // 이 스텁을 실제 FNAISCRDVAN 구현으로 교체하면 이 로그도 함께 제거한다.
        FileLogger.Warn("[PaymentOrchestrator] VAN 서비스가 스텁(StubVanRelayService)입니다 — 실제 승인이 아닙니다(Phase 20에서 FNAISCRDVAN으로 교체 예정)");
        Orchestrator = new PaymentOrchestrator(readerEndpoints, integrityStore, observedIdentityStore, paymentPresenter, SetupScreenGate, vanRelay);

        // Phase 14(P14-2/P14-3): 소켓 서버 + 단일 워커 Queue 기동. 8002 포트가 이미 사용 중이어도
        // (PRD §9) 앱 기동은 막지 않는다 — PosServer.Start()가 실패를 로그로만 남기고 넘어간다.
        PaymentQueue = new TransactionQueue(Orchestrator.ProcessAsync);
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
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--shop-setup")
        {
            // 개발/회귀 검증용(P23-3 완료 조건, 최종 산출물 아님) — app.manifest가
            // requireAdministrator라 UIPI 때문에 자동화 클릭으로 홈 화면 카드를 눌러 이 창을 여는
            // 경로를 재현할 수 없어(HomeWindow 자동화 시도, 2026-09-02), --home/--gallery와 같은
            // 패턴으로 이 창을 직접 띄워 스크린샷 대조가 가능하게 한다.
            StartupUri = new Uri("Views/ShopSetupWindow.xaml", UriKind.Relative);
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
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--notice-pin-test")
        {
            // 개발/회귀 검증용(P18-2 완료 조건 "PIN 상태로 띄워 스크린샷 대조"): State를 PinEntry로
            // 고정한 채(순환 없이) 띄워 레이아웃을 확인한다. --notice-van-processing-test와 같은 패턴.
            var vm = new PaymentNoticeViewModel { State = PaymentNoticeState.PinEntry };
            var window = new PaymentNoticeWindow(vm);
            MainWindow = window;
            window.Show();
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
            // ESC 훅 설치/해제(PaymentNoticeKeyboardHook.Install/Uninstall)가 누적 실패 없이 반복되는지
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
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--repeat-transactions-test")
        {
            // 개발/회귀 검증용(docs/payment_relay/development_plan.md P21-4 완료 조건, 최종 산출물
            // 아님): 501008(카드리딩 없음)을 50회 반복 처리하며 이 프로세스의 핸들/WorkingSet 추이를
            // 5회마다 로그에 남긴다 — PRD §9 "장시간 실행 시 메모리 누수 없음"을 확인하는 용도.
            StartupUri = new Uri("Views/HomeWindow.xaml", UriKind.Relative);
            System.Threading.Tasks.Task.Run(RepeatedTransactionResourceTest.Run);
        }
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--payment-flow-test")
        {
            // 개발/회귀 검증용(P15-10 완료 조건, 최종 산출물 아님): PaymentOrchestrator(P15-6~P15-9)를
            // FakeReaderEndpoint 등 가짜 부품으로 감싼 별도 인스턴스를 만들어 실장비 없이 15개
            // 시나리오를 재현한다 — App.Orchestrator(실제 하드웨어에 연결됨)는 건드리지 않는다.
            // UI는 홈 화면을 그대로 띄워 알림창(PaymentNoticeWindow)이 정상 동작함을 같이 보여준다.
            StartupUri = new Uri("Views/HomeWindow.xaml", UriKind.Relative);
            System.Threading.Tasks.Task.Run(PaymentFlowTestScenarios.RunAll);
        }
        else if (e.Args.Length > 0 && e.Args[0].ToLowerInvariant() == "--van-call-test")
        {
            // 개발/회귀 검증용(docs/payment_relay/development_plan.md P20-3 완료 조건, 최종 산출물
            // 아님): VanService(Phase 20)를 직접 만들어 FNAISCRDVAN을 실제로 호출한다. App.Orchestrator
            // 는 여전히 StubVanRelayService를 쓰므로(결정 1) 이 테스트와 무관하다 — VAN 배선은
            // 건드리지 않는다. VAN 서버가 아직 없어 기대 결과는 통신 실패(D01/D02)이고, 확인하는 것은
            // 호출이 크래시 없이 성립하는가다.
            StartupUri = new Uri("Views/HomeWindow.xaml", UriKind.Relative);
            System.Threading.Tasks.Task.Run(VanCallTestScenarios.RunAll);
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
        // P22-6(PRD.md §1.5 경계 표 "앱 수명" — 종료).
        FileLogger.Info(LogCategory.App, "애플리케이션 종료 시작");
        PosServer?.Stop();
        PaymentQueue?.Stop(TimeSpan.FromSeconds(5));
        ReaderConnections?.CloseAll();
        FileLogger.Info(LogCategory.App, "애플리케이션 종료 완료");

        // 단일 인스턴스 가드 해제(P22-0 보강, 2026-09-01): 이 프로세스가 실제로 Mutex 소유권을
        // 획득했던 경우(=정상 실행된 인스턴스)에만 해제한다. 중복 인스턴스로 판정돼 Shutdown()된
        // 경로는 Mutex를 획득한 적이 없으므로(TryAcquireSingleInstance에서 실패 시 즉시 Dispose하고
        // null로 둔다) 여기서 할 일이 없다.
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 이미 소유하지 않은 Mutex에 ReleaseMutex를 호출한 경우(방어적 처리) — 무시한다.
        }
        finally
        {
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    /// <summary>
    /// 단일 인스턴스 가드(P22-0 보강, 2026-09-01)를 시도한다. <c>Global\</c> 네임스페이스로 먼저
    /// 시도해 터미널 서비스/세션 분리 환경에서도 전역으로 단일 인스턴스를 보장한다. 이 앱은 항상
    /// 관리자 권한으로 기동하므로(app.manifest <c>requireAdministrator</c>) 모든 인스턴스가 같은
    /// 무결성 수준에서 Mutex를 만들고 열게 되어 <see cref="UnauthorizedAccessException"/>이 실제로
    /// 발생할 가능성은 낮지만, 방어적으로 <c>Local\</c> 네임스페이스로 한 번 더 시도한다(그래도
    /// 실패하면 가드를 포기하고 기동을 계속한다 — 가드 실패가 결제 기능 자체를 막아서는 안 된다).
    /// </summary>
    /// <returns>이 프로세스가 유일한 인스턴스로 인정되어 계속 기동해야 하면 true.</returns>
    private bool TryAcquireSingleInstance()
    {
        const string GlobalMutexName = @"Global\KFTCOneCAP_Wpf_SingleInstance";
        const string LocalMutexName = @"Local\KFTCOneCAP_Wpf_SingleInstance";

        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name: GlobalMutexName, createdNew: out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // Global\ Mutex가 다른 무결성 수준(예: 비관리자 프로세스)에서 먼저 만들어져 있으면 DACL
            // 차이로 접근이 거부될 수 있다 — Local\로 우회한다.
            try
            {
                mutex = new Mutex(initiallyOwned: true, name: LocalMutexName, createdNew: out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                // 그래도 실패하면 가드를 포기한다(기동은 계속 진행) — 단일 인스턴스 보장보다 앱이
                // 아예 기동하지 못하는 쪽이 더 나쁘다.
                return true;
            }
        }

        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _singleInstanceMutex = mutex;
        return true;
    }
}
