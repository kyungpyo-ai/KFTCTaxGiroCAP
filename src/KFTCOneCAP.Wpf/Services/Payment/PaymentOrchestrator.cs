using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Reader;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Reader;
using KFTCOneCAP.Wpf.Services.Settings;
using KFTCOneCAP.Wpf.Services.Storage;
using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-6~P15-9) — PRD §4.1의 결제 요청 처리 순서를
/// 조립하는 자리. Phase 10(리더기)·11(DB)·13(알림창)·14(소켓/Queue)이 만든 부품을 **엮기만** 한다 —
/// 이 클래스 자체는 전문 바이트를 다루지 않고(계층 규칙), 새 리더기/DB/VAN 로직을 만들지 않는다.
///
/// Phase 16(P16-1~P16-3)에서 취소/Timeout 경합을 <see cref="TransactionOutcomeGate"/> 하나로 확정하는
/// 구조로 갈아끼웠다 — Phase 15가 남긴 세 갈래 판정(플래그/TCS/방어적 재확인)을 이 게이트가 대체한다.
///
/// <see cref="TransactionQueue"/>가 이 클래스의 <see cref="ProcessAsync"/>를 처리 위임으로 받는다
/// (P15-1이 위임을 <c>Task</c> 반환으로 바꿔 둔 자리). 큐가 워커 스레드 하나로 거래를 직렬화하므로,
/// **이 클래스는 거래 사이에 어떤 가변 상태도 인스턴스 필드로 들고 있지 않다**(2026-08-25, Phase 16
/// 체크포인트 리뷰 H-1 수정). 거래 1건 동안만 살아 있어야 하는 것(결과 확정 게이트, 데드라인, 취소 시
/// 정리할 리더기 목록)은 전부 <see cref="ProcessAsync"/>의 지역 변수 + <see cref="TransactionScope"/>에
/// 담아 클로저로 넘긴다. 이유는 <see cref="TransactionScope"/> 문서 참고 — 인스턴스 필드로 두면 앞
/// 거래의 뒤늦은 콜백이 **다음 거래의** 상태를 읽어 엉뚱한 리더기를 초기화하는 사고가 가능했다.
///
/// **생성자 인자 중 <see cref="_readerEndpoints"/>는 순서가 의미를 가진다** — 인덱스 0은 리더기1/
/// <c>ReaderSettings.Port1</c>, 인덱스 1은 리더기2/<c>ReaderSettings.Port2</c>에 대응한다(App.xaml.cs가
/// <c>ReaderConnectionManager.Reader1</c>/<c>Reader2</c> 순서로 감싸 넘긴다). 이 순서가 어긋나면
/// 참여 후보 판정(2단계)이 엉뚱한 포트를 켜고 끄게 된다.
///
/// **정적 접근(<c>App.XXX</c>)을 이 클래스 안에서 하지 않는다** — 배선은 <c>App.xaml.cs</c>가 하고,
/// 검증 하네스(P15-10)가 전부 가짜로 갈아 끼울 수 있어야 한다.
/// </summary>
internal sealed class PaymentOrchestrator
{
    // PRD §4.9(2026-08-25 갱신) — 카드 입력 대기의 시작 데드라인 기본값. 거래 단위로 딱 하나만
    // 존재하고(Services/Payment/PaymentDeadline), 라운드마다 새로 주지 않는다(development_plan.md
    // Phase 16 착수 전 전제 — 라운드마다 리셋하면 FALLBACK·재요청이 겹칠 때 최악 360초까지
    // 늘어난다). 검증 하네스(P16-6)가 실제 120초를 기다리지 않고 데드라인 만료 경로를 검증할 수
    // 있도록 생성자에서 <see cref="_initialCardReadDeadline"/>으로 주입 가능하다(운영 코드는 항상
    // 기본값을 쓴다 — _loadSettings와 같은 이유의 최소 테스트 접점).
    private static readonly TimeSpan DefaultInitialCardReadDeadline = TimeSpan.FromSeconds(120);

    // PRD §4.9(2026-08-25 갱신) — 새 사용자 입력 단계가 시작될 때마다 데드라인을 이만큼 연장한다.
    // 지금은 FALLBACK(07)/재요청(12) 두 경우에서만 쓰이지만, 이름과 위치를 "카드 재요청 전용"이
    // 아니라 "사용자 입력 단계 일반"으로 잡아 뒀다 — 추후 서명·PIN 입력 단계가 생기면 그 진입점에서
    // 같은 상수를 그대로 재사용한다(development_plan.md P16-2).
    private static readonly TimeSpan UserInputStepExtension = TimeSpan.FromSeconds(30);

    // 리더기 명령 타임아웃에 남은 데드라인을 그대로 넘기되, 0에 가까운 값을 주지 않기 위한 하한이다
    // (development_plan.md P16-2 "하한 클램프를 둔다"). 실제 만료 판정은 PaymentDeadline이 독립적으로
    // 내리므로(MonitorDeadlineAsync), 이 하한은 리더기 계층 호출 자체가 무의미한 0초로 나가지 않게
    // 막는 안전장치일 뿐이다.
    private static readonly TimeSpan MinimumCommandTimeout = TimeSpan.FromSeconds(1);

    // ReaderSetupViewModel의 명령 타임아웃(CommandTimeout)과 동일한 값을 쓴다 — 같은 0x61/0x62
    // 시퀀스를 화면과 결제 Flow가 공유하므로(IntegrityCheckService), 타임아웃 감각도 통일한다.
    private static readonly TimeSpan IntegrityCommandTimeout = TimeSpan.FromSeconds(5);

    // PRD 미규정 — 07/12 응답이 계속 반복되면 무한 루프가 된다. 최대 3라운드(최초 1 + 재요청 2)로
    // 제한하고 초과 시 실패 처리한다(development_plan.md P15-7 계획). 실제 운용 값은 SPEC 확정 시
    // 재검토.
    private const int MaxCardReadRounds = 3;

    // 0x2B 요청의 AID 인덱스/PIN 블록 입력 여부는 PRD/샘플 모두 특정 값 요구가 없어 기본값을 쓴다
    // (Protocol/Reader/TransactionInfoRequest 필드 주석과 동일한 근거).
    private const string AidIndexDefault = "0";
    private const string PinBlockInputRequiredDefault = "0";

    // 리더기 화면 표시 문구 — PRD가 아직 문구 내용을 정하지 않아
    // vendor/ReaderSerial/CSharpSample/CommandFieldSpecs.cs의 TRANSACTION_INFO_REQUEST 예시를 그대로
    // 쓴다(PRD §4.3 "나머지 요청 필드는 리더기 샘플 소스를 참고한다"). FALLBACK(MS) 라운드에서도 같은
    // 문구를 재사용한다 — PRD에 MS 전용 문구 요구사항이 없다(TODO: SPEC 확정 시 재검토).
    private const string Message1 = "1-----승인------";
    private const string Message2 = "2 카드를        ";
    private const string Message3 = "3    넣어주세요.";
    private const string Message4 = "4  IC  INSERT   ";

    private readonly IReadOnlyList<IReaderEndpoint> _readerEndpoints;
    private readonly Func<ReaderSettings> _loadSettings;
    private readonly IntegrityCheckStore _integrityStore;
    private readonly IPaymentNoticePresenter _presenter;
    private readonly IReaderSetupGate _readerSetupGate;
    private readonly IVanService _vanService;
    private readonly TimeSpan _initialCardReadDeadline;

    internal PaymentOrchestrator(
        IReadOnlyList<IReaderEndpoint> readerEndpoints,
        IntegrityCheckStore integrityStore,
        IPaymentNoticePresenter presenter,
        IReaderSetupGate readerSetupGate,
        IVanService vanService,
        Func<ReaderSettings>? loadSettings = null,
        TimeSpan? initialCardReadDeadline = null)
    {
        _readerEndpoints = readerEndpoints;
        // 기본값은 실제 레지스트리(ReaderSettingsService.Load)를 읽는다. 검증 하네스(P15-10)가
        // 참여 후보 필터링(2단계)을 실제 레지스트리 값과 무관하게 스크립트하려면 이 값을
        // 주입해야 한다 — ReaderSettingsService는 레지스트리를 직접 읽는 sealed 클래스라
        // 인터페이스 없이는 가짜로 바꿔치기할 수 없어, 필요한 최소 접근("설정값을 어떻게
        // 얻는가")만 함수로 뽑아냈다.
        _loadSettings = loadSettings ?? new ReaderSettingsService().Load;
        _integrityStore = integrityStore;
        _presenter = presenter;
        _readerSetupGate = readerSetupGate;
        _vanService = vanService;
        // 운영 코드는 항상 기본값(120초)을 쓴다 — 검증 하네스(P16-6)만 이 값을 짧게 주입해 데드라인
        // 만료 경로를 실제 120초를 기다리지 않고 검증한다.
        _initialCardReadDeadline = initialCardReadDeadline ?? DefaultInitialCardReadDeadline;
    }

    /// <summary>PRD §4.1의 처리 순서를 그대로 따른다. 각 단계 실패 시 즉시 해당 결과코드로 종료한다.
    /// <see cref="TransactionQueue"/>의 워커 스레드에서 호출된다 — 이 메서드 자체는 어느 스레드에서
    /// 불려도 안전하지만(취소 이벤트만 별도 스레드), 동시에 두 번 호출되는 것은 전제하지 않는다
    /// (큐가 직렬화를 보장).</summary>
    internal async Task<PosPaymentResponse> ProcessAsync(PosPaymentRequest request)
    {
        string txId = request.TransactionId;

        // ===== PRD §4.1 1단계 — 설정 화면 게이트(2026-08-25 확정, P15-4) =====
        if (_readerSetupGate.IsReaderSetupOpen)
        {
            FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 리더기 설정 화면이 열려 있어 결제 거부");
            return PosPaymentResponse.Create(PosPaymentResultCode.ReaderSetupInProgress, txId, "READER_SETUP_OPEN");
        }

        // ===== PRD §4.1 1단계 계속 — 참여 후보 결정(§2.2.3) =====
        var settings = _loadSettings();
        string[] configuredPorts = { settings.Port1, settings.Port2 };
        var candidates = new List<IReaderEndpoint>();
        for (int i = 0; i < _readerEndpoints.Count && i < configuredPorts.Length; i++)
        {
            if (ComPortFormat.ToPortNumber(configuredPorts[i]) > 0)
                candidates.Add(_readerEndpoints[i]);
        }

        if (candidates.Count == 0)
        {
            FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 설정된 리더기가 없음(양쪽 미사용) — 카드 리딩 시도 안 함");
            return PosPaymentResponse.Create(PosPaymentResultCode.NoReaderConfigured, txId, "NO_READER");
        }

        // ===== PRD §4.1 1~2단계 — 무결성 선행 판정(§4.2) =====
        var participants = new List<IReaderEndpoint>();
        foreach (IReaderEndpoint candidate in candidates)
        {
            // ComPortDisplay는 DB 조회/저장 키다(P12-2 형식) — 후보는 이미 설정된 포트만 걸러졌으므로
            // ReaderEndpoint.ComPortDisplay가 예외를 던지지 않는다(L-1 수정 전제, P15-6이 그 전제를
            // 지키는 유일한 호출자).
            string comPortDisplay = candidate.ComPortDisplay;

            if (_integrityStore.HasSuccessToday(comPortDisplay))
            {
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} {comPortDisplay} 금일 무결성 성공 이력 있음 — 재검사 생략, 참여");
                participants.Add(candidate);
                continue;
            }

            IntegrityCheckSequenceOutcome outcome = await candidate
                .RunIntegrityCheckAsync(IntegrityCommandTimeout, IntegrityCommandTimeout)
                .ConfigureAwait(false);

            if (outcome.IsSuccess)
            {
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} {comPortDisplay} 무결성 체크 성공 — 참여");
                participants.Add(candidate);
            }
            else
            {
                FileLogger.Warn($"[PaymentOrchestrator] txId={txId} {comPortDisplay} 무결성 체크 실패(Kind={outcome.Kind}) — 카드 리딩에서 제외");
            }
        }

        if (participants.Count == 0)
        {
            FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 참여 가능한 리더기 없음(무결성 전원 실패)");
            return PosPaymentResponse.Create(PosPaymentResultCode.IntegrityCheckFailure, txId, "INTEGRITY_FAILED");
        }

        // ===== PRD §4.1 3단계 이후 — 알림창 표시 + 카드 리딩(§4.3~§4.7) + VAN(§4.10) =====
        // 이 거래 동안만 살아 있는 상태를 전부 여기 모은다 — 인스턴스 필드를 쓰지 않는 이유는
        // TransactionScope 문서 참고(H-1 수정).
        var scope = new TransactionScope(txId);
        using var deadline = new PaymentDeadline(_initialCardReadDeadline);

        // 감시는 백그라운드에서 — 만료되면 게이트를 Timeout으로 확정 시도할 뿐 예외를 던지지 않으므로
        // 결과를 기다리지 않아도 안전하다(관찰되지 않는 예외가 없음). 거래가 정상 종료돼 위 using이
        // deadline을 Dispose하면 이 Task는 확정을 시도하지 않고 조용히 끝난다
        // (PaymentDeadline.WaitForExpiryAsync가 "실제 만료"와 "Dispose"를 구분해 돌려준다).
        _ = MonitorDeadlineAsync(deadline, scope);

        // 같은 거래는 카드 리딩(0x2B)과 VAN 요청 양쪽에 같은 거래 일시를 써야 한다(2026-08-25, Opus
        // 검증 리뷰 M-1 수정) — 예전엔 VAN 단계에서 DateTime.Now를 다시 계산해, 고객이 카드를 늦게
        // 넣을수록 두 값이 벌어졌다(라운드 재시도까지 겹치면 최악 120초+). PRD §4.1이 하나의 거래로
        // 취급하는 흐름이므로 여기서 한 번만 계산해 양쪽에 그대로 넘긴다(P15-7 계획의 "라운드마다
        // 새로 만들지 않는다"는 원칙을 VAN까지 확장).
        string transactionDateTime = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        EventHandler onCanceled = (_, _) => OnCanceled(scope);

        try
        {
            // 구독을 Show()보다 먼저 건다(Opus 검증 리뷰 H-1) — Show()는 Dispatcher.Invoke로 동기
            // 마샬링되므로 반환 시점엔 이미 창이 떠 있고 취소 버튼이 활성 상태다. 구독이 그 뒤에
            // 있으면, Show() 반환 직후 그 짧은 창 사이에 취소가 들어올 때 취소 이벤트가 구독자
            // 0명에게 통지돼 사라진다.
            _presenter.Canceled += onCanceled;
            _presenter.Show(PaymentNoticeState.IcCardRequest);

            CardReadRoundResult roundResult = await RunCardReadingRoundsAsync(participants, request, transactionDateTime, deadline, scope).ConfigureAwait(false);
            if (roundResult.EarlyResponse != null)
                return roundResult.EarlyResponse;

            // 카드 리딩 성공 — VAN 진입 직전에 이 거래를 FlowResult로 확정한다(P16-1). 여기서
            // 실패하면(취소/Timeout이 카드 리딩 성공과 근소한 차이로 먼저 확정된 것) VAN에 들어가지
            // 않고 그 사유로 응답한다(PRD §4.8 — VAN 요청이 나간 뒤 취소를 받으면 승인/취소 응답이
            // 실제 승인 여부와 불일치할 수 있다). 이것이 "선착순" 규칙이 VAN 경계에서도 정확히
            // 지켜지는 지점이다(2026-08-25 사용자 확정).
            if (!scope.Gate.TryClaim(TransactionOutcomeReason.FlowResult))
            {
                TransactionOutcomeReason reason = scope.Gate.ClaimedReason!.Value;
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 성공했으나 VAN 진입 전 이미 확정됨({reason}) — VAN 미진입");
                roundResult.Winner?.SendInvalidationInit();
                return BuildInterruptResponse(reason, txId);
            }

            // VAN 구간부터는 취소가 결과를 바꾸지 않는다 — 위에서 이미 FlowResult로 확정했으므로
            // onCanceled가 이후에 불려도 TryClaim이 실패해 조용히 무시된다(방어 심층화). 여기서는
            // 구독만 끊는다.
            _presenter.Canceled -= onCanceled;

            return await RunVanApprovalAsync(roundResult, request, transactionDateTime, txId).ConfigureAwait(false);
        }
        finally
        {
            // 정상/조기 반환 어느 경로든 안전하게 정리한다 — 이미 위에서 구독 해제했어도 -=는
            // 멱등이라 무해하다(P13 Opus 리뷰 M-1과 같은 종류의 비대칭을 여기서 만들지 않는다).
            _presenter.Canceled -= onCanceled;
            _presenter.Close();

            // (2026-08-25, Phase 16 체크포인트 리뷰 H-1) 게이트를 봉인한다. 정상 경로에서는 이미
            // 누군가 확정했으므로 이 TryClaim은 반드시 실패하고 아무 일도 일어나지 않는다. 성공하는
            // 경우는 단 하나 — **아무도 결과를 확정하지 못한 채 예외로 빠져나가는 경로**다(알림창
            // Show 실패, VAN 스텁/실구현의 예기치 못한 예외 등. 이때 POS 응답은 TransactionQueue의
            // catch가 InternalError로 만든다). 그 경우엔 대기 중이던 리더기가 카드를 계속 기다리는
            // 상태로 남으므로 여기서 정리해 준다.
            //
            // 봉인 자체가 중요한 이유: 이게 없으면 이 거래의 데드라인 감시/취소 통지가 **거래가 끝난
            // 뒤에** 게이트를 확정하는 데 성공할 수 있고, 그 시점엔 이미 다음 거래가 진행 중일 수
            // 있다. 정리 대상을 TransactionScope에 담아 거래별로 격리한 것과 이 봉인이 짝을 이뤄,
            // 앞 거래의 뒤늦은 콜백이 다음 거래에 영향을 주는 경로를 구조적으로 없앤다.
            if (scope.Gate.TryClaim(TransactionOutcomeReason.FlowResult))
            {
                FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 결과가 확정되지 않은 채 거래가 종료됨(예외 경로로 추정) — 대기 중이던 리더기를 정리한다");
                FireInterruptCleanup(TransactionOutcomeReason.FlowResult, scope);
            }

            // deadline은 위 using이 이 블록이 끝나는 시점에 Dispose한다(P16-4 리소스 해제 목록) —
            // MonitorDeadlineAsync의 재확인 루프가 그 즉시 끝난다.
        }
    }

    /// <summary>
    /// PRD §4.3~§4.7 카드 리딩 라운드. 참여자 전체 → (07/12면) 채택된 리더기 1대만으로 좁혀가며
    /// 반복한다. 매 라운드 경계와 브로드캐스트 대기 중에 <see cref="TransactionOutcomeGate.Interrupted"/>
    /// (취소 또는 Timeout이 확정되면 완료됨)를 리더기 응답 대기와 경쟁시켜, 먼저 확정된 쪽이 즉시
    /// 이긴다(선착순, 2026-08-25 사용자 확정 — Phase 15의 "취소 플래그가 응답을 이긴다"는 임시 규칙을
    /// 대체). 게이트가 취소/Timeout으로 확정되는 순간 그 확정 경로(<see cref="OnCanceled"/> 또는 <see
    /// cref="MonitorDeadlineAsync"/>)가 이미 대기 중인 참여 리더기 전부에 0x60을 쏘므로, 이 메서드는
    /// 리더기 응답을 더 기다리지 않고 즉시 반환하기만 하면 된다.
    /// </summary>
    private async Task<CardReadRoundResult> RunCardReadingRoundsAsync(
        IReadOnlyList<IReaderEndpoint> participants, PosPaymentRequest request, string transactionDateTime,
        PaymentDeadline deadline, TransactionScope scope)
    {
        string txId = scope.TransactionId;
        TransactionOutcomeGate gate = scope.Gate;
        IReadOnlyList<IReaderEndpoint> roundParticipants = participants;
        string transactionTypeCode = TransactionInfoRequestBuilder.TransactionTypeIc;

        for (int round = 1; round <= MaxCardReadRounds; round++)
        {
            if (gate.ClaimedReason is { } claimedBeforeRound)
            {
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} 시작 전 이미 확정됨({claimedBeforeRound}) — 중단");
                return CardReadRoundResult.Early(BuildInterruptResponse(claimedBeforeRound, txId));
            }

            TransactionInfoRequest infoRequest = transactionTypeCode == TransactionInfoRequestBuilder.TransactionTypeIc
                ? TransactionInfoRequestBuilder.CreateIcRequest(transactionDateTime, request.Amount, AidIndexDefault, Message1, Message2, Message3, Message4, PinBlockInputRequiredDefault)
                : TransactionInfoRequestBuilder.CreateFallbackRequest(transactionDateTime, request.Amount, AidIndexDefault, Message1, Message2, Message3, Message4, PinBlockInputRequiredDefault);

            scope.PendingParticipants = roundParticipants;

            // 리더기 명령 타임아웃 = 남은 거래 데드라인(하한 클램프, P16-2). 거래 Timeout의 정본은
            // PaymentDeadline/MonitorDeadlineAsync이지 이 타임아웃이 아니다 — 이 값은 리더기 계층이
            // 하드웨어 무응답에서 스스로를 회수하기 위한 값일 뿐이다(클래스 상단 주석).
            TimeSpan roundTimeout = ClampCommandTimeout(deadline.Remaining);
            FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round}/{MaxCardReadRounds} 시작 — 참여 {roundParticipants.Count}대, 거래구분={transactionTypeCode}, 남은데드라인={roundTimeout.TotalSeconds:F1}s");

            Task<CardReadBroadcastResult> broadcastTask = CardReadBroadcaster.SendAsync(roundParticipants, infoRequest, roundTimeout);
            Task interruptTask = gate.Interrupted;
            Task firstCompleted = await Task.WhenAny(broadcastTask, interruptTask).ConfigureAwait(false);

            if (firstCompleted == interruptTask)
            {
                // 리더기 응답을 더 기다리지 않고 즉시 반환한다 — broadcastTask는 백그라운드에서
                // 계속 진행되지만(리더기가 실제로 응답하거나 로컬 타임아웃이 날 때까지) 아무도 그
                // 결과를 기다리지 않는다. 정리(0x60)는 게이트를 확정시킨 그 경로가 이미 수행했다.
                TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} 대기 중 확정됨({reason}) — 리더기 응답을 기다리지 않고 즉시 처리");
                return CardReadRoundResult.Early(BuildInterruptResponse(reason, txId));
            }

            CardReadBroadcastResult broadcast = await broadcastTask.ConfigureAwait(false);

            if (!broadcast.HasWinner)
            {
                if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                {
                    TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} 완료 시점에 이미 확정됨({reason}) — 우선 처리");
                    return CardReadRoundResult.Early(BuildInterruptResponse(reason, txId));
                }

                FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} — 참여 리더기 전원 송신 실패(또는 참여자 없음)");
                return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.ReaderDllFailure, txId, "READER_SEND_FAIL"));
            }

            IReaderEndpoint winner = broadcast.Winner!;
            CardReadCommandOutcome outcome = broadcast.WinnerOutcome!;

            switch (outcome.Kind)
            {
                case ReaderCommandOutcomeKind.Success:
                    if (outcome.CardData == null)
                    {
                        // 이론상 응답코드 00이면 CardData도 채워진다(CardReadResponseParser 계약) —
                        // 방어적으로만 대비한다. 카드 데이터 없이 VAN에 진입해서는 안 된다.
                        if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                        {
                            TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                            winner.SendInvalidationInit();
                            return CardReadRoundResult.Early(BuildInterruptResponse(reason, txId));
                        }

                        FileLogger.Error($"[PaymentOrchestrator] txId={txId} 응답코드 00인데 CardData가 없음 — 방어적으로 실패 처리");
                        winner.SendInvalidationInit();
                        return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.ReaderDllFailure, txId, "READER_NO_CARD_DATA"));
                    }

                    // 아직 거래를 확정하지 않는다 — VAN 진입 여부는 ProcessAsync가 VAN 진입 직전에
                    // 다시 gate.TryClaim(FlowResult)를 시도해 결정한다(카드 리딩 성공과 VAN 진입
                    // 사이에도 취소/Timeout이 끼어들 수 있기 때문 — development_plan.md P16-6
                    // 시나리오 17/18).
                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 성공(라운드 {round}) — VAN 단계로");
                    return CardReadRoundResult.Success(winner, outcome.CardData);

                case ReaderCommandOutcomeKind.BusinessFailure when outcome.IsFallback:
                    deadline.Extend(UserInputStepExtension);
                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} FALLBACK(07) — MS 재요청(채택된 그 리더기에만, 거래구분 F), 데드라인 {UserInputStepExtension.TotalSeconds:F0}초 연장");
                    _presenter.ChangeState(PaymentNoticeState.FallbackCardRequest);
                    roundParticipants = new[] { winner };
                    transactionTypeCode = TransactionInfoRequestBuilder.TransactionTypeFallback;
                    continue;

                case ReaderCommandOutcomeKind.BusinessFailure when outcome.IsRetryCode12:
                    deadline.Extend(UserInputStepExtension);
                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} 응답코드 12 — 재요청(채택된 그 리더기에만, 거래구분 유지), 데드라인 {UserInputStepExtension.TotalSeconds:F0}초 연장");
                    roundParticipants = new[] { winner };
                    continue;

                case ReaderCommandOutcomeKind.BusinessFailure:
                    if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                    {
                        TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                        winner.SendInvalidationInit();
                        return CardReadRoundResult.Early(BuildInterruptResponse(reason, txId));
                    }

                    FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 실패 — 응답코드={outcome.ResponseCode}");
                    winner.SendInvalidationInit();
                    return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.ReaderResponseFailure, txId, $"READER_RESP_{outcome.ResponseCode}"));

                case ReaderCommandOutcomeKind.Timeout:
                    // 리더기 계층의 로컬 명령 타임아웃 — roundTimeout이 deadline.Remaining에서 그대로
                    // 파생되므로, 실제로는 이것과 MonitorDeadlineAsync의 거래 Timeout 확정이 거의
                    // 동시에 일어나는 경우가 대부분이다(클래스 상단 주석 "리더기 명령 타임아웃 =
                    // 안전장치" 참고). 어느 쪽이 근소하게 먼저 TryClaim에 성공하든 결과 코드는
                    // 동일(Timeout)하므로 사용자에게 보이는 차이는 없다 — 게이트가 이중 확정만
                    // 막아 주면 된다.
                    if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                    {
                        TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                        winner.SendInvalidationInit();
                        return CardReadRoundResult.Early(BuildInterruptResponse(reason, txId));
                    }

                    FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 리더기 명령 타임아웃(라운드 {round})");
                    winner.SendInvalidationInit();
                    return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.Timeout, txId, "CARD_INPUT_TIMEOUT"));

                default: // DllCallFailure, CommunicationError
                    if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
                    {
                        TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
                        winner.SendInvalidationInit();
                        return CardReadRoundResult.Early(BuildInterruptResponse(reason, txId));
                    }

                    FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 DLL 연동 실패(Kind={outcome.Kind}): {outcome.Detail}");
                    winner.SendInvalidationInit();
                    return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.ReaderDllFailure, txId, "READER_DLL_FAIL"));
            }
        }

        if (!gate.TryClaim(TransactionOutcomeReason.FlowResult))
        {
            TransactionOutcomeReason reason = gate.ClaimedReason!.Value;
            foreach (IReaderEndpoint p in roundParticipants)
                p.SendInvalidationInit();
            return CardReadRoundResult.Early(BuildInterruptResponse(reason, txId));
        }

        FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 재요청 상한({MaxCardReadRounds}) 초과");
        foreach (IReaderEndpoint p in roundParticipants)
            p.SendInvalidationInit();
        return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.ReaderResponseFailure, txId, "RETRY_LIMIT"));
    }

    /// <summary>PRD §4.10 — VAN 승인 요청. 이 메서드가 불릴 때는 이미 거래가 <see
    /// cref="TransactionOutcomeReason.FlowResult"/>로 확정되고 취소 구독이 해제된 뒤다(<see
    /// cref="ProcessAsync"/> 참고) — VAN 요청이 나간 뒤 취소를 받아들이면 "VAN은 승인했는데 POS에는
    /// 취소로 응답"하는 불일치가 생기기 때문이다.</summary>
    private async Task<PosPaymentResponse> RunVanApprovalAsync(CardReadRoundResult roundResult, PosPaymentRequest request, string transactionDateTime, string txId)
    {
        _presenter.ChangeState(PaymentNoticeState.VanProcessing);

        var vanRequest = new VanApprovalRequest(roundResult.CardData!, request.Amount, transactionDateTime);

        VanApprovalOutcome vanOutcome = await _vanService.RequestApprovalAsync(vanRequest).ConfigureAwait(false);

        switch (vanOutcome.Kind)
        {
            case VanApprovalOutcomeKind.Approved:
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} VAN 승인");
                return PosPaymentResponse.Create(PosPaymentResultCode.Approved, txId, "OK");

            case VanApprovalOutcomeKind.Declined:
                FileLogger.Warn($"[PaymentOrchestrator] txId={txId} VAN 거절: {vanOutcome.Detail}");
                roundResult.Winner?.SendInvalidationInit();
                string declinedReason = string.IsNullOrEmpty(vanOutcome.ResponseCode) ? "VAN_DECLINED" : $"VAN_DECLINED_{vanOutcome.ResponseCode}";
                return PosPaymentResponse.Create(PosPaymentResultCode.VanDeclined, txId, declinedReason);

            default: // CommunicationFailure
                FileLogger.Warn($"[PaymentOrchestrator] txId={txId} VAN DLL 통신 실패: {vanOutcome.Detail}");
                roundResult.Winner?.SendInvalidationInit();
                return PosPaymentResponse.Create(PosPaymentResultCode.VanCommunicationFailure, txId, "VAN_COMM_FAIL");
        }
    }

    /// <summary>취소 알림 — <see cref="IPaymentNoticePresenter.Canceled"/>는 UI 스레드에서 발생한다
    /// (P13-6, 취소 버튼은 <c>RelayCommand</c>·ESC는 <c>Dispatcher.BeginInvoke</c> 경유). 게이트
    /// 확정만 이 자리에서 동기로 하고, 실제 0x60 발사는 <see cref="FireInterruptCleanup"/>이
    /// <c>Task.Run</c>으로 넘긴다(Opus 검증 리뷰 H-2 — 리더기 I/O를 UI 스레드에서 돌리면 고객이
    /// Topmost 알림창에서 취소를 누른 순간 창이 얼어붙는다).
    ///
    /// <paramref name="scope"/>는 <see cref="ProcessAsync"/>가 지역 변수로 캡처해 넘긴다(2026-08-25,
    /// H-1 수정) — 예전엔 인스턴스 필드로 읽었는데, 이 메서드가 실행되는 시점엔 이미 다음 거래가
    /// 시작해 그 필드를 덮어썼을 수 있어 **엉뚱한 거래의 리더기를 초기화**할 위험이 있었다.</summary>
    private void OnCanceled(TransactionScope scope)
    {
        if (scope.Gate.TryClaim(TransactionOutcomeReason.UserCanceled))
        {
            FileLogger.Info($"[PaymentOrchestrator] txId={scope.TransactionId} 사용자 취소 통지 수신 — 거래 확정(UserCanceled)");
            FireInterruptCleanup(TransactionOutcomeReason.UserCanceled, scope);
        }
        else
        {
            // 취소 연타/ESC+버튼 동시 입력이 이 핸들러를 두 번 부르거나, 취소보다 먼저 Timeout/정상
            // 흐름이 확정된 경우다 — 둘 다 정상 상황이며 조용히 무시한다(선착순 규칙).
            FileLogger.Info($"[PaymentOrchestrator] txId={scope.TransactionId} 사용자 취소 통지 수신 — 이미 다른 사유({scope.Gate.ClaimedReason})로 확정되어 무시");
        }
    }

    /// <summary>거래 데드라인(PRD §4.9)을 감시한다. **실제로 만료됐을 때만** 게이트를 <see
    /// cref="TransactionOutcomeReason.Timeout"/>으로 확정 시도한다 — 거래가 정상 종료돼 <see
    /// cref="PaymentDeadline"/>이 <c>Dispose</c>된 경우엔 확정을 시도조차 하지 않는다(2026-08-25,
    /// Phase 16 체크포인트 리뷰 M-2: 예전엔 두 경우가 같은 신호로 합쳐져 있어 "정상 종료했는데
    /// Timeout 확정을 시도한다"는, 의도와 다른 코드가 됐다. 게이트가 이미 확정돼 있어 결과적으로는
    /// 무해했지만, P16-1이 "확정을 시도하는 지점을 전부 나열한다"고 정한 이상 정상 경로가 그 목록에
    /// 섞여서는 안 된다).</summary>
    private async Task MonitorDeadlineAsync(PaymentDeadline deadline, TransactionScope scope)
    {
        bool actuallyExpired = await deadline.WaitForExpiryAsync().ConfigureAwait(false);
        if (!actuallyExpired)
        {
            return;
        }

        if (scope.Gate.TryClaim(TransactionOutcomeReason.Timeout))
        {
            FileLogger.Warn($"[PaymentOrchestrator] txId={scope.TransactionId} 거래 데드라인 만료 — 거래 확정(Timeout)");
            FireInterruptCleanup(TransactionOutcomeReason.Timeout, scope);
        }
    }

    /// <summary>취소/Timeout 정리 경로를 하나로 통일한다(development_plan.md P16-3 — "코드도 하나여야
    /// 한다"). 대기 중인 참여 리더기 전부에 0x60을 백그라운드에서 발사한다. UI 스레드를 막지 않기
    /// 위해 <c>Task.Run</c>으로 넘긴다(Opus 검증 리뷰 H-2와 같은 이유 — Timeout 확정은 원래도 UI
    /// 스레드가 아니지만, 경로를 하나로 합쳐 둔 덕분에 자연히 같은 처리를 받는다).
    ///
    /// 정리 대상을 <paramref name="scope"/>에서 읽는 것이 핵심이다 — 인스턴스 필드에서 읽으면 앞
    /// 거래의 뒤늦은 확정이 다음 거래의 리더기를 초기화할 수 있다(H-1).</summary>
    private static void FireInterruptCleanup(TransactionOutcomeReason reason, TransactionScope scope)
    {
        IReadOnlyList<IReaderEndpoint> pending = scope.PendingParticipants;
        FileLogger.Info($"[PaymentOrchestrator] txId={scope.TransactionId} {reason} 확정 — 대기 중인 참여 리더기 {pending.Count}대에 초기화(0x60) 전송 예약(백그라운드)");

        Task.Run(() =>
        {
            foreach (IReaderEndpoint endpoint in pending)
            {
                try
                {
                    endpoint.SendInvalidationInit();
                }
                catch (Exception ex)
                {
                    FileLogger.Warn($"[PaymentOrchestrator] {reason} 처리 중 리더기 초기화 실패(무시하고 계속): {ex.Message}");
                }
            }
        });
    }

    /// <summary>취소/Timeout으로 확정된 거래의 POS 응답을 만든다. <see
    /// cref="TransactionOutcomeReason.FlowResult"/>는 이 메서드가 다루지 않는다 — 정상 흐름은 이미
    /// 자기 자신의 결과 코드를 알고 있으므로 이 매핑이 필요 없다.</summary>
    private static PosPaymentResponse BuildInterruptResponse(TransactionOutcomeReason reason, string txId) => reason switch
    {
        TransactionOutcomeReason.UserCanceled => PosPaymentResponse.Create(PosPaymentResultCode.UserCanceled, txId, "USER_CANCELED"),
        TransactionOutcomeReason.Timeout => PosPaymentResponse.Create(PosPaymentResultCode.Timeout, txId, "CARD_INPUT_TIMEOUT"),
        _ => throw new InvalidOperationException($"FlowResult은 인터럽트 응답을 만들지 않는다: {reason}"),
    };

    private static TimeSpan ClampCommandTimeout(TimeSpan remaining) =>
        remaining < MinimumCommandTimeout ? MinimumCommandTimeout : remaining;

    /// <summary>
    /// 거래 1건 동안만 존재하는 상태 묶음(2026-08-25, Phase 16 체크포인트 리뷰 H-1). 결과 확정
    /// 게이트와 "취소/Timeout 시 초기화할 리더기 목록"을 함께 들고 다닌다.
    ///
    /// **왜 인스턴스 필드가 아니라 이 객체인가**: 취소 통지(<see cref="OnCanceled"/>)와 데드라인 감시
    /// (<see cref="MonitorDeadlineAsync"/>)는 결제 워커가 아닌 다른 스레드에서, 그것도 <see
    /// cref="ProcessAsync"/>가 이미 반환한 **뒤에** 실행될 수 있다. 이 상태를 인스턴스 필드에 두면 그
    /// 뒤늦은 콜백이 **다음 거래의** 목록을 읽어, 지금 카드를 기다리는 리더기에 0x60을 쏴 멀쩡한
    /// 거래를 깨뜨릴 수 있었다(큐가 거래를 직렬화해도 이 경로는 막히지 않는다 — 직렬화되는 것은
    /// <see cref="ProcessAsync"/> 본문이지 그것이 남긴 콜백이 아니기 때문이다). 거래마다 새 객체를
    /// 만들어 클로저로 넘기면 앞 거래의 콜백은 앞 거래의 목록만 볼 수 있다.
    /// </summary>
    private sealed class TransactionScope
    {
        private volatile IReadOnlyList<IReaderEndpoint> _pendingParticipants = Array.Empty<IReaderEndpoint>();

        internal TransactionScope(string transactionId)
        {
            TransactionId = transactionId;
        }

        internal string TransactionId { get; }

        internal TransactionOutcomeGate Gate { get; } = new();

        /// <summary>지금 이 라운드가 응답을 기다리고 있는 참여 리더기 — 라운드마다 갱신한다
        /// (PRD §4.8/§4.9 "아직 응답 대기 중인 모든 리더기에 초기화 요청"). 결제 워커가 쓰고
        /// 취소/데드라인 스레드가 읽으므로 <c>volatile</c>.</summary>
        internal IReadOnlyList<IReaderEndpoint> PendingParticipants
        {
            get => _pendingParticipants;
            set => _pendingParticipants = value;
        }
    }

    /// <summary>카드 리딩 라운드의 결과 — <see cref="EarlyResponse"/>가 있으면 즉시 반환할 최종 응답
    /// (실패/타임아웃/취소), 없으면 <see cref="Winner"/>/<see cref="CardData"/>로 VAN 단계를 진행한다.
    /// 이 코드베이스의 다른 Outcome 타입들과 같은 모양(private 생성자 + 정적 팩터리)이다 — 잘못된
    /// 조합(성공인데 EarlyResponse도 있음 등)이 만들어지지 않는다.</summary>
    private sealed class CardReadRoundResult
    {
        private CardReadRoundResult(PosPaymentResponse? earlyResponse, IReaderEndpoint? winner, CardReadData? cardData)
        {
            EarlyResponse = earlyResponse;
            Winner = winner;
            CardData = cardData;
        }

        internal PosPaymentResponse? EarlyResponse { get; }

        internal IReaderEndpoint? Winner { get; }

        internal CardReadData? CardData { get; }

        internal static CardReadRoundResult Early(PosPaymentResponse response) => new(response, null, null);

        internal static CardReadRoundResult Success(IReaderEndpoint winner, CardReadData cardData) => new(null, winner, cardData);
    }
}
