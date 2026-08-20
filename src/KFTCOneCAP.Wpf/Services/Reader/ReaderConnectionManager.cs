using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 12(docs/payment_relay/development_plan.md P12-1) — 리더기1/2 포트의 앱 수명 소유자.
    ///
    /// <see cref="ReaderService"/> 인스턴스가 <c>ReaderSetupViewModel</c>의 필드였던 Phase 9~11
    /// 구조로는 PRD §2.2.2("포트는 항상 열어두고, 닫는 경우는 결제 대기 화면 종료 시뿐")를 만족할 수
    /// 없다 — 리더기 설정 화면은 열렸다 닫혔다 하고 그때마다 ViewModel이 새로 생성되며, Phase 15
    /// 결제 Flow는 화면 없이 같은 포트를 써야 한다. 그래서 앱과 같이 사는 단일 소유자를 둔다.
    ///
    /// - 소유/생성 지점은 <c>App.xaml.cs</c>(OnStartup)뿐이다. ViewModel은 이 클래스를 생성하지
    ///   않고 생성자로 전달받아 참조만 한다.
    /// - <see cref="ReaderService.ClosePort"/>를 호출하는 지점은 이 클래스 안
    ///   <see cref="ClosePortIfOpen"/> 하나뿐이다(P12-1 완료 조건) — <see cref="Reopen"/>(콤보 저장
    ///   시)과 <see cref="CloseAll"/>(앱 종료 시) 둘 다 이 메서드를 거친다. 다른 어떤 코드도
    ///   ClosePort를 직접 부르지 않는다.
    /// - DI 컨테이너를 쓰지 않는다. 이 앱에서 앱 수명 싱글턴이 필요한 대상은 현재 이것 하나뿐이고,
    ///   컨테이너를 넣으면 Phase 13~17에서 등록/해석 코드가 계속 늘어난다 — 대상이 3~4개로 늘면
    ///   그때 재검토한다(<c>App.ReaderConnections</c> 정적 프로퍼티로 충분).
    /// </summary>
    internal sealed class ReaderConnectionManager
    {
        /// <summary>PRD §2.2.1/§10 — baudRate는 항상 115200 고정.</summary>
        private const int BaudRate = 115200;

        private readonly ReaderSettingsService _settingsService;

        internal ReaderConnectionManager(ReaderSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        internal ReaderService Reader1 { get; } = new();

        internal ReaderService Reader2 { get; } = new();

        /// <summary>
        /// 앱 기동 시 1회 호출(App.xaml.cs OnStartup). 레지스트리에 설정된 포트를 연다. "미사용"이면
        /// 열지 않는다. 열기 실패해도 예외를 던지지 않는다 — 실패는 로그로만 남기고, 다음 명령
        /// 시점에 SendCommandSafe(P10-3)의 자동 재오픈 경로가 다시 시도한다(PRD §2.2.2/§9 — 기동
        /// 시점에 모달을 띄우지 않는다).
        /// </summary>
        internal void InitializeFromSettings()
        {
            var settings = _settingsService.Load();
            OpenIfConfigured(Reader1, "리더기1", settings.Port1);
            OpenIfConfigured(Reader2, "리더기2", settings.Port2);
        }

        /// <summary>
        /// 리더기 설정 화면의 COM 포트 콤보를 저장할 때만 호출된다(P12-1/P12-3) — 기존 포트가
        /// 열려 있으면 닫고, 새로 설정된 포트를 연다. "미사용"으로 바뀌면 닫히기만 하고 새로 열지
        /// 않는다(OpenIfConfigured가 portNumber&lt;=0이면 아무 것도 하지 않으므로 자연스럽게 그렇게
        /// 동작한다).
        /// </summary>
        internal void Reopen(ReaderService service, string label, string newPortDisplay)
        {
            ClosePortIfOpen(service, label);
            OpenIfConfigured(service, label, newPortDisplay);
        }

        /// <summary>
        /// 2026-08-20 사용자 확정: 레지스트리 저장(확인 버튼)과 별개로, **콤보에서 포트 선택을
        /// 바꾸는 즉시** 기존 포트를 닫아 포트 변경을 준비한다(확인/취소는 레지스트리 저장 시점을
        /// 정할 뿐, 창 안에서의 포트 점유 여부는 콤보 변경에 바로 반응해야 한다는 사용자 지시).
        /// 새 포트를 여는 것은 여전히 확인(<see cref="Reopen"/>) 시점에만 한다 — 선택 중인 값이
        /// 저장 전에 취소될 수도 있어, 열기까지 미리 하면 아직 확정되지 않은 포트를 점유하게 된다.
        /// 이 메서드도 <see cref="ClosePortIfOpen"/> 하나만 거치므로 "ClosePort 호출 지점 1곳" 규칙은
        /// 그대로 유지된다.
        /// </summary>
        internal void ClosePortForPendingChange(ReaderService service, string label) => ClosePortIfOpen(service, label);

        /// <summary>
        /// 실제 연결 상태를 <paramref name="portDisplay"/>가 가리키는 값에 맞춘다 — 이미 맞는
        /// 상태(원하는 포트에 연결돼 있음, 또는 "미사용"인데 연결도 안 돼 있음)면 **아무 것도 하지
        /// 않는다**(불필요한 닫기/재오픈을 피한다). 세 곳에서 이 하나의 판단 로직을 공유한다
        /// (2026-08-20 사용자 확정 — "이미 맞는 상태인데 확인을 누르면 다시 열어야 하나?" 질문에서
        /// 발견된 중복 재오픈을 없애기 위해 통일):
        /// - 액션 버튼(초기화/상태체크/무결성체크) — 저장 여부와 무관하게 **현재 화면에 선택된 콤보
        ///   값**으로 연결해서 실행해야 한다(명령 전송 직전에 호출).
        /// - <see cref="ReaderSetupViewModel.Save"/>(확인) — 콤보 변경으로 이미 닫혀 있던 포트를
        ///   새 값으로 연다. 액션 버튼으로 이미 새 포트에 연결해 본 상태라면 이 호출은 아무 일도
        ///   하지 않는다(전에는 스냅샷 비교만으로 무조건 재오픈해 불필요한 close/open이 있었다).
        /// - <see cref="ReaderSetupViewModel.DiscardPortChanges"/>(취소 확정) — 스냅샷(레지스트리)
        ///   값으로 되돌린다.
        /// </summary>
        internal void EnsureOpenForSelection(ReaderService service, string label, string portDisplay)
        {
            int desiredPortNumber = ComPortFormat.ToPortNumber(portDisplay);
            if (desiredPortNumber <= 0)
            {
                ClosePortIfOpen(service, label); // "미사용"이면 열려 있으면 안 된다(연결 안 돼 있으면 no-op).
                return;
            }

            if (service.IsConnected && service.PortNumber == desiredPortNumber)
                return; // 이미 원하는 포트에 연결돼 있음 — 그대로 둔다.

            Reopen(service, label, portDisplay);
        }

        /// <summary>앱 종료 시 리소스 정리(PRD §9). App.xaml.cs OnExit에서 호출한다.</summary>
        internal void CloseAll()
        {
            ClosePortIfOpen(Reader1, "리더기1");
            ClosePortIfOpen(Reader2, "리더기2");
        }

        private static void OpenIfConfigured(ReaderService service, string label, string portDisplay)
        {
            int portNumber = ComPortFormat.ToPortNumber(portDisplay);
            if (portNumber <= 0)
            {
                FileLogger.Info($"[{label}] 포트 미설정('{portDisplay}') — 열지 않음");
                return;
            }

            var result = service.OpenPort(portNumber, BaudRate);
            FileLogger.Info(result.Success
                ? $"[{label}] COM{portNumber} 열기 성공(readerId={result.ReaderId})"
                : $"[{label}] COM{portNumber} 열기 실패({result.DllResultName}({result.DllResult})) — 다음 명령 시 자동 재시도(SendCommandSafe)");
        }

        /// <summary>
        /// 이 클래스가 <see cref="ReaderService.ClosePort"/>를 호출하는 유일한 지점(P12-1 완료
        /// 조건 — grep으로 확인 가능하도록 호출을 이 메서드 하나로 모았다).
        /// </summary>
        private static void ClosePortIfOpen(ReaderService service, string label)
        {
            if (!service.IsConnected)
                return;

            var result = service.ClosePort();
            FileLogger.Info(result.Success
                ? $"[{label}] 포트 닫기 성공"
                : $"[{label}] 포트 닫기 실패({result.DllResultName}({result.DllResult}))");
        }
    }
}
