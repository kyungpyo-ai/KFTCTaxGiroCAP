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
/// <see cref="TransactionQueue"/>가 이 클래스의 <see cref="ProcessAsync"/>를 처리 위임으로 받는다
/// (P15-1이 위임을 <c>Task</c> 반환으로 바꿔 둔 자리). 큐가 워커 스레드 하나로 거래를 직렬화하므로,
/// 이 클래스의 인스턴스 필드(<see cref="_canceled"/>/<see cref="_pendingParticipantsForCancel"/>)는
/// 한 번에 한 거래에서만 쓰인다는 전제로 설계됐다 — 매 <see cref="ProcessAsync"/> 호출 시작/종료마다
/// 초기화·정리된다.
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
    // PRD §4.9는 카드 입력 대기 상한을 120초로 규정한다. Phase 15는 별도 자체 타이머를 두지 않고
    // 이 값을 SendCardReadCommandAsync의 timeout 인자로 그대로 준다 — 리더기 명령 타임아웃이 곧
    // 카드 입력 대기 상한이 되게 한다(development_plan.md P15-9, Phase 16이 이 설계를 재검토한다).
    private static readonly TimeSpan CardReadTimeout = TimeSpan.FromSeconds(120);

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

    /// <summary>현재 처리 중인 거래가 취소됐는지. UI 스레드(<see cref="OnCanceled"/>)와 결제 워커
    /// 스레드 양쪽에서 접근하므로 <c>volatile</c>로 가시성을 보장한다.</summary>
    private volatile bool _canceled;

    /// <summary>취소 통지가 오면 즉시 초기화(0x60)를 보낼 대상 — "지금 이 라운드가 응답을 기다리고
    /// 있는 참여 리더기 목록"을 라운드마다 갱신한다(PRD §4.8 "아직 응답 대기 중인 모든 리더기에
    /// 초기화 요청"). <see cref="OnCanceled"/>가 다른 스레드에서 읽으므로 <c>volatile</c>.</summary>
    private volatile IReadOnlyList<IReaderEndpoint> _pendingParticipantsForCancel = Array.Empty<IReaderEndpoint>();

    /// <summary>
    /// (2026-08-25, 실장비 테스트로 발견) 카드 리딩 대기(<see cref="CardReadBroadcaster.SendAsync"/>)와
    /// 취소를 <c>Task.WhenAny</c>로 경쟁시키는 신호. 이게 없으면 취소 버튼을 눌러도 <see
    /// cref="_canceled"/> 플래그만 세워질 뿐, 라운드 루프는 진행 중이던 <c>await</c>가 스스로
    /// 끝날 때까지(카드 태그 또는 120초 로컬 타임아웃) 그 사실을 알아채지 못한다 — 실측 결과
    /// 취소 버튼을 누른 뒤 실제로 응답이 나가기까지 약 120초가 걸렸다(0x60은 fire-and-forget이라
    /// 진행 중인 0x2B 대기 자체를 끊지 못하기 때문, 클래스 주석 참고). 취소가 이 신호를 즉시
    /// 완료시키면 라운드 루프가 리더기 응답을 더 기다리지 않고 그 자리에서 바로 반환할 수 있다
    /// (리더기 쪽 실제 응답/타임아웃은 백그라운드에서 계속 진행되지만 결과를 아무도 기다리지
    /// 않는다 — <see cref="CardReadBroadcaster"/>가 무효화까지 이미 책임지므로 안전).
    ///
    /// **Phase 16과의 경계**: 이건 "취소 버튼을 누르면 즉시 반응해야 한다"는 P15-9의 기본 요구사항이지,
    /// 취소와 카드 리딩 성공이 사실상 동시에 도착했을 때 어느 쪽을 채택할지 정하는 동시성 중재
    /// (Phase 16 몫)와는 다르다 — 2026-08-25 사용자 확정.
    /// </summary>
    private TaskCompletionSource<bool>? _cancelSignal;

    internal PaymentOrchestrator(
        IReadOnlyList<IReaderEndpoint> readerEndpoints,
        IntegrityCheckStore integrityStore,
        IPaymentNoticePresenter presenter,
        IReaderSetupGate readerSetupGate,
        IVanService vanService,
        Func<ReaderSettings>? loadSettings = null)
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
        _canceled = false;
        _pendingParticipantsForCancel = Array.Empty<IReaderEndpoint>();
        // RunContinuationsAsynchronously: OnCanceled(UI 스레드)가 TrySetResult를 호출할 때, 이 Task를
        // 기다리던 워커 스레드 쪽 continuation이 UI 스레드에서 그대로 인라인 실행되는 것을 막는다
        // (P15-10 검증 하네스에서 겪은 것과 같은 종류의 스레드 얽힘을 피한다).
        _cancelSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 같은 거래는 카드 리딩(0x2B)과 VAN 요청 양쪽에 같은 거래 일시를 써야 한다(2026-08-25, Opus
        // 검증 리뷰 M-1 수정) — 예전엔 VAN 단계에서 DateTime.Now를 다시 계산해, 고객이 카드를 늦게
        // 넣을수록 두 값이 벌어졌다(라운드 재시도까지 겹치면 최악 120초+). PRD §4.1이 하나의 거래로
        // 취급하는 흐름이므로 여기서 한 번만 계산해 양쪽에 그대로 넘긴다(P15-7 계획의 "라운드마다
        // 새로 만들지 않는다"는 원칙을 VAN까지 확장).
        string transactionDateTime = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        try
        {
            // (2026-08-25, Opus 검증 리뷰 H-1 수정) 구독을 Show()보다 먼저 건다 — Show()는
            // Dispatcher.Invoke로 동기 마샬링되므로 반환 시점엔 이미 창이 떠 있고 취소 버튼이
            // 활성 상태다. 구독이 그 뒤에 있으면, Show() 반환 직후 그 짧은 창 사이에 취소가
            // 들어올 때 PaymentNoticeViewModel의 sticky _canceled 플래그만 확정되고
            // Canceled 이벤트는 구독자 0명에게 통지돼 사라진다 — 취소 버튼은 이미 비활성인데
            // Orchestrator는 취소를 영원히 모른 채 카드 리딩을 계속 진행하는 결함이었다(Phase 13
            // H-3과 같은 종류의 무증상 실패).
            _presenter.Canceled += OnCanceled;
            _presenter.Show(PaymentNoticeState.IcCardRequest);

            CardReadRoundResult roundResult = await RunCardReadingRoundsAsync(participants, request, transactionDateTime, txId).ConfigureAwait(false);
            if (roundResult.EarlyResponse != null)
                return roundResult.EarlyResponse;

            // VAN 구간부터는 취소가 결과를 바꾸지 않는다(PRD §4.8 — VAN 요청이 나간 뒤 취소를 받으면
            // 승인/취소 응답이 실제 승인 여부와 불일치할 수 있다). 카드 리딩까지 취소되지 않았다는
            // 뜻이므로(취소됐다면 위에서 EarlyResponse로 이미 반환됨) 여기서 구독만 끊는다.
            _presenter.Canceled -= OnCanceled;

            return await RunVanApprovalAsync(roundResult, request, transactionDateTime, txId).ConfigureAwait(false);
        }
        finally
        {
            // 정상/조기 반환 어느 경로든 안전하게 정리한다 — 이미 위에서 구독 해제했어도 -=는
            // 멱등이라 무해하다(P13 Opus 리뷰 M-1과 같은 종류의 비대칭을 여기서 만들지 않는다).
            _presenter.Canceled -= OnCanceled;
            _presenter.Close();
            _canceled = false;
            _pendingParticipantsForCancel = Array.Empty<IReaderEndpoint>();
            _cancelSignal = null;
        }
    }

    /// <summary>
    /// PRD §4.3~§4.7 카드 리딩 라운드. 참여자 전체 → (07/12면) 채택된 리더기 1대만으로 좁혀가며
    /// 반복한다. 매 라운드 경계에서 <see cref="_canceled"/>를 확인하고, 브로드캐스트 대기 중에는
    /// <see cref="_cancelSignal"/>과 경쟁시켜 취소가 오면 리더기 응답/로컬 타임아웃을 기다리지 않고
    /// 즉시 <see cref="PosPaymentResultCode.UserCanceled"/>로 중단한다(development_plan.md P15-9
    /// "취소 플래그가 응답 종류를 이긴다" + 2026-08-25 실장비 테스트로 발견한 응답 지연 수정).
    /// 리더기 하드웨어 자체는 0x60을 받아도 실제로 스캔을 멈추는지 이 앱이 확인할 방법이 없다
    /// (<see cref="ReaderService.SendInvalidationInit"/> 문서 참고: 0x60은 fire-and-forget이라 응답을
    /// 기다리지 않는다) — 그래도 소프트웨어 쪽 대기는 <see cref="_cancelSignal"/> 덕분에 즉시
    /// 끝난다. "취소와 카드 리딩 성공이 근소한 차이로 동시에 도착했을 때 어느 쪽이 최종 결과인가"를
    /// 엄밀하게 중재하는 것은 여전히 Phase 16의 몫이다(2026-08-25 사용자 확정 — 이 수정은 그것과
    /// 다른, "취소를 누르면 즉시 반응해야 한다"는 기본 요구사항).
    /// </summary>
    private async Task<CardReadRoundResult> RunCardReadingRoundsAsync(
        IReadOnlyList<IReaderEndpoint> participants, PosPaymentRequest request, string transactionDateTime, string txId)
    {
        IReadOnlyList<IReaderEndpoint> roundParticipants = participants;
        string transactionTypeCode = TransactionInfoRequestBuilder.TransactionTypeIc;

        for (int round = 1; round <= MaxCardReadRounds; round++)
        {
            if (_canceled)
            {
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} 시작 전 취소 감지 — 중단");
                return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.UserCanceled, txId, "USER_CANCELED"));
            }

            TransactionInfoRequest infoRequest = transactionTypeCode == TransactionInfoRequestBuilder.TransactionTypeIc
                ? TransactionInfoRequestBuilder.CreateIcRequest(transactionDateTime, request.Amount, AidIndexDefault, Message1, Message2, Message3, Message4, PinBlockInputRequiredDefault)
                : TransactionInfoRequestBuilder.CreateFallbackRequest(transactionDateTime, request.Amount, AidIndexDefault, Message1, Message2, Message3, Message4, PinBlockInputRequiredDefault);

            _pendingParticipantsForCancel = roundParticipants;
            FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round}/{MaxCardReadRounds} 시작 — 참여 {roundParticipants.Count}대, 거래구분={transactionTypeCode}");

            // (2026-08-25, 실장비 테스트로 발견) 리더기 응답을 그냥 기다리기만 하면 취소가 즉시
            // 반영되지 않는다 — 0x60은 fire-and-forget이라 이미 시작된 0x2B 대기 자체를 끊지
            // 못하므로, 이 await만으로는 취소 버튼을 눌러도 리더기 응답/로컬 타임아웃(최대 120초)이
            // 실제로 끝날 때까지 알아채지 못한다(실측: 취소 후 응답까지 약 120초 소요). 취소 신호를
            // 함께 경쟁시켜 먼저 끝나는 쪽을 즉시 채택한다.
            Task<CardReadBroadcastResult> broadcastTask = CardReadBroadcaster.SendAsync(roundParticipants, infoRequest, CardReadTimeout);
            Task cancelTask = _cancelSignal!.Task;
            Task firstCompleted = await Task.WhenAny(broadcastTask, cancelTask).ConfigureAwait(false);

            if (firstCompleted == cancelTask)
            {
                // 리더기 응답을 더 기다리지 않고 즉시 반환한다 — broadcastTask는 백그라운드에서
                // 계속 진행되지만(리더기가 실제로 응답하거나 로컬 타임아웃이 날 때까지) 그 결과를
                // 아무도 기다리지 않는다. 이 라운드의 참여 리더기에는 OnCanceled가 이미 0x60을
                // 보냈으므로(_pendingParticipantsForCancel) 정리 자체는 그대로 이뤄진다.
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} 대기 중 취소 감지 — 리더기 응답을 기다리지 않고 즉시 처리");
                return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.UserCanceled, txId, "USER_CANCELED"));
            }

            CardReadBroadcastResult broadcast = await broadcastTask.ConfigureAwait(false);

            if (_canceled)
            {
                // 위 경쟁에서 broadcastTask가 근소하게 먼저 끝난 경우를 위한 방어적 재확인.
                FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 라운드 {round} 완료 시점에 취소 감지 — 우선 처리");
                broadcast.Winner?.SendInvalidationInit();
                return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.UserCanceled, txId, "USER_CANCELED"));
            }

            if (!broadcast.HasWinner)
            {
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
                        FileLogger.Error($"[PaymentOrchestrator] txId={txId} 응답코드 00인데 CardData가 없음 — 방어적으로 실패 처리");
                        winner.SendInvalidationInit();
                        return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.ReaderDllFailure, txId, "READER_NO_CARD_DATA"));
                    }

                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} 카드 리딩 성공(라운드 {round}) — VAN 단계로");
                    return CardReadRoundResult.Success(winner, outcome.CardData);

                case ReaderCommandOutcomeKind.BusinessFailure when outcome.IsFallback:
                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} FALLBACK(07) — MS 재요청(채택된 그 리더기에만, 거래구분 F)");
                    _presenter.ChangeState(PaymentNoticeState.FallbackCardRequest);
                    roundParticipants = new[] { winner };
                    transactionTypeCode = TransactionInfoRequestBuilder.TransactionTypeFallback;
                    continue;

                case ReaderCommandOutcomeKind.BusinessFailure when outcome.IsRetryCode12:
                    FileLogger.Info($"[PaymentOrchestrator] txId={txId} 응답코드 12 — 재요청(채택된 그 리더기에만, 거래구분 유지)");
                    roundParticipants = new[] { winner };
                    continue;

                case ReaderCommandOutcomeKind.BusinessFailure:
                    FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 실패 — 응답코드={outcome.ResponseCode}");
                    winner.SendInvalidationInit();
                    return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.ReaderResponseFailure, txId, $"READER_RESP_{outcome.ResponseCode}"));

                case ReaderCommandOutcomeKind.Timeout:
                    FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 입력 대기 시간 초과(120초)");
                    winner.SendInvalidationInit();
                    return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.Timeout, txId, "CARD_INPUT_TIMEOUT"));

                default: // DllCallFailure, CommunicationError
                    FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 DLL 연동 실패(Kind={outcome.Kind}): {outcome.Detail}");
                    winner.SendInvalidationInit();
                    return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.ReaderDllFailure, txId, "READER_DLL_FAIL"));
            }
        }

        FileLogger.Warn($"[PaymentOrchestrator] txId={txId} 카드 리딩 재요청 상한({MaxCardReadRounds}) 초과");
        foreach (IReaderEndpoint p in roundParticipants)
            p.SendInvalidationInit();
        return CardReadRoundResult.Early(PosPaymentResponse.Create(PosPaymentResultCode.ReaderResponseFailure, txId, "RETRY_LIMIT"));
    }

    /// <summary>PRD §4.10 — VAN 승인 요청. 이 메서드가 불릴 때는 이미 취소 구독이 해제된 뒤다
    /// (<see cref="ProcessAsync"/> 참고) — VAN 요청이 나간 뒤 취소를 받아들이면 "VAN은 승인했는데
    /// POS에는 취소로 응답"하는 불일치가 생기기 때문이다.</summary>
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
    /// (P13-6, 취소 버튼은 <c>RelayCommand</c>·ESC는 <c>Dispatcher.BeginInvoke</c> 경유). PRD §4.8
    /// "아직 응답 대기 중인 모든 리더기에 초기화 요청"을 수행하지만, <see cref="_canceled"/> 플래그
    /// 확정만 이 자리에서 동기로 하고 실제 0x60 발사는 <see cref="Task.Run(Action)"/>으로 넘긴다.
    ///
    /// (2026-08-25, Opus 검증 리뷰 H-2 수정) 예전엔 0x60 발사 루프까지 이 핸들러 안에서 동기로
    /// 돌았다 — <see cref="IReaderEndpoint.SendInvalidationInit"/>는 <c>ReaderService.SendCommandSafe</c>
    /// (P10-3 재연결 래퍼)를 타므로, 포트가 <c>PORT_NOT_OPEN</c>이면 <c>ClosePort</c>→<c>OpenPort</c>→
    /// 재전송까지 동기로 일어날 수 있다(`--pos-client-test` 실측: `[자동복구] COM3 ... 실패` 같은
    /// 재오픈 시도가 실제로 발생함). 이 전부가 UI 스레드에서 벌어지면, 고객이 Topmost 알림창에서
    /// 취소를 누른 바로 그 순간 창이 얼어붙는다(PRD §9 "앱이 멈추지 않아야" 위반). 플래그 확정만
    /// 여기서 동기로 끝내면 라운드 루프의 취소 우선순위 판정(<see cref="_canceled"/> 검사)은 그대로
    /// 정확하고, 리더기 I/O만 백그라운드로 옮겨 UI 스레드를 막지 않는다.
    /// </summary>
    private void OnCanceled(object? sender, EventArgs e)
    {
        _canceled = true;
        // RunCardReadingRoundsAsync가 이 신호를 CardReadBroadcaster 대기와 경쟁시킨다 — 이걸 완료시켜야
        // 취소가 리더기 응답/로컬 타임아웃을 기다리지 않고 즉시 반영된다(2026-08-25 실장비 테스트로
        // 발견: 이게 없으면 취소 후 실제 응답까지 최대 120초가 걸렸다). TrySetResult를 쓰는 이유는
        // ESC 연타 등으로 이 핸들러가 이론상 두 번 불려도(구독은 거래당 한 번뿐이라 실제로는 안
        // 일어나지만) 이미 완료된 TCS에 다시 설정하면 예외가 나기 때문이다.
        _cancelSignal?.TrySetResult(true);

        IReadOnlyList<IReaderEndpoint> pending = _pendingParticipantsForCancel;
        FileLogger.Info($"[PaymentOrchestrator] 사용자 취소 통지 수신 — 대기 중인 참여 리더기 {pending.Count}대에 초기화(0x60) 전송 예약(백그라운드)");

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
                    FileLogger.Warn($"[PaymentOrchestrator] 취소 처리 중 리더기 초기화 실패(무시하고 계속): {ex.Message}");
                }
            }
        });
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
