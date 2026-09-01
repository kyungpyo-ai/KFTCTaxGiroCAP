using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Protocol.Reader;
using KFTCOneCAP.Wpf.Services.Payment;
using KFTCOneCAP.Wpf.Services.Reader;
using KFTCOneCAP.Wpf.Services.Settings;
using KFTCOneCAP.Wpf.Services.Storage;
using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 17(docs/payment_relay/development_plan.md P17-5/P17-6) 개발/회귀 검증용 테스트 하네스.
/// **최종 산출물이 아니다** — <c>App.xaml.cs</c>가 <c>--payment-flow-test</c> 인자로 실행될 때만
/// <see cref="RunAll"/>을 백그라운드에서 호출한다.
///
/// Phase 15/16이 만든 26개 시나리오(임시 전문 기준)를 대체한다 — 카드리딩/취소/Timeout/단일 유효
/// 응답 게이트 로직 자체는 <c>PaymentOrchestrator.RunCardReadingRoundsAsync</c>가 그대로 재사용하므로
/// (P17-5) 그쪽 경합 로직은 재검증하지 않고, **3전문 라우팅·필드 채움·relay 배선**에 집중한다. 전체
/// 경합 시나리오(취소/Timeout 9종 등)의 전면 재구성은 P17-7 몫으로 남아 있다(development_plan.md
/// Phase 17 남은 작업 참고) — 이 파일은 그 전 단계의 가벼운 스모크 검증이다.
/// </summary>
internal static class PaymentFlowTestScenarios
{
    private static int _passCount;
    private static int _failCount;

    internal static async Task RunAll()
    {
        try
        {
            FileLogger.Info("[payment-flow-test] Phase 17 스모크 검증 시작");

            await Scenario1_NoticeInquiryRelaysWithoutReader().ConfigureAwait(false);
            await Scenario2_CardInfoInquiryFillsBin().ConfigureAwait(false);
            await Scenario3_CardApprovalFillsSevenFields().ConfigureAwait(false);
            await Scenario4_SetupGateBlocksAllThreeTelegrams().ConfigureAwait(false);
            await Scenario5_UnknownWccSurfacesAsInternalError().ConfigureAwait(false);
            await Scenario6_UserCancelDuringCardApproval().ConfigureAwait(false);
            await Scenario7_VanCommunicationFailureResetsReader().ConfigureAwait(false);
            await Scenario8_CardApprovalCollectsPinAndOrdersHistory().ConfigureAwait(false);
            await Scenario9_CardInfoInquirySkipsPinStep().ConfigureAwait(false);
            await Scenario10_CancelDuringPinEntryYieldsE01().ConfigureAwait(false);
            await Scenario11_TimeoutDuringPinEntryYieldsE02().ConfigureAwait(false);
            await Scenario12_PinEnteredBeforeSubscriptionIsNotLost().ConfigureAwait(false);
            await Scenario13_ConsecutiveTransactionsDoNotLeakCardOrPinData().ConfigureAwait(false);
            Scenario14_MalformedTelegramFallsBackToGenericMasking();

            FileLogger.Info($"[payment-flow-test] 완료 — 통과 {_passCount}건, 실패 {_failCount}건");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[payment-flow-test] 하네스 자체 예외로 중단: {ex}");
        }
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            _passCount++;
            FileLogger.Info($"[payment-flow-test][OK] {name}");
        }
        else
        {
            _failCount++;
            FileLogger.Error($"[payment-flow-test][FAIL] {name}");
        }
    }

    // ===== 공통 빌드 헬퍼 =====

    private static PaymentOrchestrator BuildOrchestrator(
        out FakeReaderEndpoint reader1, out FakeReaderEndpoint reader2,
        out FakePaymentNoticePresenter presenter, out FakeReaderSetupGate gate,
        out CapturingVanRelayService vanRelay, string port1 = "COM 05", string port2 = "미사용")
    {
        reader1 = new FakeReaderEndpoint("COM 05");
        reader2 = new FakeReaderEndpoint("COM 03");
        presenter = new FakePaymentNoticePresenter();
        gate = new FakeReaderSetupGate();
        vanRelay = new CapturingVanRelayService();
        string dbPath = Path.Combine(Path.GetTempPath(), $"p17-test-{Guid.NewGuid():N}.db");
        var integrityStore = new IntegrityCheckStore(dbPath);
        // P22-7 — 같은 파일을 가리키게 한다(프로덕션과 동일한 전제, App.xaml.cs 참고).
        var observedIdentityStore = new ObservedIdentityStore(dbPath);

        return new PaymentOrchestrator(
            new IReaderEndpoint[] { reader1, reader2 },
            integrityStore,
            observedIdentityStore,
            presenter,
            gate,
            vanRelay,
            () => new ReaderSettings { Port1 = port1, Port2 = port2 },
            TimeSpan.FromSeconds(5)); // 검증용 짧은 데드라인
    }

    private static int _managementSequence;

    private static PosRequestTelegram BuildRequest(string transactionType, IReadOnlyDictionary<int, string> fields)
    {
        if (!PosSchemaRegistry.TryResolve(transactionType, out PosTelegramSchema? schema) || schema is null)
            throw new InvalidOperationException($"알 수 없는 거래구분: {transactionType}");

        var telegram = PosTelegram.CreateEmpty(schema);
        telegram.Write(1, "IGN");
        telegram.Write(2, "095");
        telegram.Write(3, "0200");
        telegram.Write(4, transactionType);
        telegram.Write(6, "G");
        // #9 전문 관리 번호(AN12) — 실제 POS가 반드시 채우는 상관관계 키이며 Orchestrator가 로그
        // txId로 쓴다(H-1/M-1). 하네스도 채워야 로그 경로가 실제와 같아진다. SPEC 번호체계는
        // 구분코드(3, "0EC") + "0"(Reserved) + 일련번호(8).
        telegram.Write(9, "0EC0" + (++_managementSequence).ToString("D8"));

        foreach (var kv in fields)
            telegram.Write(kv.Key, kv.Value);

        var outcome = PosRequestTelegram.Parse(telegram.ToBody());
        if (!outcome.IsSuccess)
            throw new InvalidOperationException($"테스트 요청 빌드 실패: {outcome.ErrorCode}");

        return outcome.Telegram!;
    }

    private static CardReadCommandOutcome SuccessOutcome(string cardNumber = "9412345678901234", string wcc = "I")
    {
        // #46 검증용(2026-09-01, PaymentOrchestrator.FillCardApprovalFields 참고) — 실제 파서는 리더기가
        // 보낸 길이필드를 읽은 payload 길이로 재구성한다. 하네스도 같은 전제를 지키도록 payload 길이로부터
        // 3자리 zero-padded 길이 텍스트를 계산한다(하드코딩하면 실제 파싱 경로와 어긋날 수 있다).
        const string encryptedData = "ENCRYPTEDDATA0001";
        string encryptedDataLengthText = encryptedData.Length.ToString("D3");

        return CardReadCommandOutcome.Success("00", new CardReadData(
            transactionType: "A", keyVersion: "01", tc: "TC0001", moduleId: "MODULE0001",
            fallbackCode: "0", amount: "000000000001000", cardNumber: cardNumber,
            encryptionMarker: "ENC", wcc: wcc, encryptedData: encryptedData,
            encryptedDataLengthText: encryptedDataLengthText,
            emvEncodingMethod: "B", emvEncodedData: "EMV0001", readerAuthId: "READERAUTH000001",
            readerSerialEncryptionMarker: "NOE", readerSerial: "SERIAL0001",
            readerEncryptionInfo: "READERENCRYPTINFO001", tc3: "TC30001", payOnCertifyCode: "PAYONCERT00000000000000000001"));
    }

    // ===== 시나리오 =====

    /// <summary>501008 — 카드리딩 없이 즉시 relay. 리더기가 하나도 설정 안 된 상태에서도 성공해야
    /// 한다(P17-5 완료 조건).</summary>
    private static async Task Scenario1_NoticeInquiryRelaysWithoutReader()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay,
            port1: "미사용", port2: "미사용");

        var request = BuildRequest("501008", new Dictionary<int, string>());
        PosResponseTelegram response = await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("501008: 리더기 미설정에도 성공(카드리딩 없음)", response.Telegram.Read(7) == "000");
        Check("501008: 카드리딩 호출 0회(리더기를 전혀 안 씀)", r1.CardReadCallCount == 0 && r2.CardReadCallCount == 0);
        Check("501008: VAN까지 relay 도달", vanRelay.LastRequest != null);
    }

    /// <summary>800000 — 카드리딩 성공 후 BIN(카드번호 앞 8자리)만 채워지는지.</summary>
    private static async Task Scenario2_CardInfoInquiryFillsBin()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        r1.EnqueueCardReadOutcome(SuccessOutcome(cardNumber: "9412345678901234"));

        var request = BuildRequest("800000", new Dictionary<int, string> { [15] = "1000" });
        PosResponseTelegram response = await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("800000: 응답 성공(#7=000)", response.Telegram.Read(7) == "000");
        Check("800000: VAN 요청에 실린 BIN이 카드번호 앞 8자리", vanRelay.LastRequest?.Read(14) == "94123456");
    }

    /// <summary>902614 — 원캡 담당 8필드(#43~#46,#48,#50,#51,#53)가 정확히 채워지는지. Phase 18(P18-4)부터 902614는
    /// 카드리딩 성공 후 PIN 입력 단계를 거치므로(<see cref="PaymentOrchestrator.CollectPinAsync"/>),
    /// PIN을 주지 않으면 이 시나리오가 실제 Timeout(35초)까지 블로킹된다 — 즉시발화 플래그로 PIN
    /// 단계를 빠르게 통과시킨다(이 시나리오의 관심사는 필드 채움이지 PIN 자체가 아니므로).</summary>
    private static async Task Scenario3_CardApprovalFillsSevenFields()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        r1.EnqueueCardReadOutcome(SuccessOutcome(wcc: "I"));
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "1234";

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram response = await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("902614: 응답 성공(#7=000)", response.Telegram.Read(7) == "000");
        string sentTelegram43 = vanRelay.LastRequest!.Read(43);
        Check("902614: #43 = 리더기인증(16)+프로그램식별자(16)", sentTelegram43 == "READERAUTH000001" + PaymentOrchestrator.ProgramIdentifier);
        // H-1 수정(체크포인트 1) 이후 N 필드 Read()는 패딩을 보존한다 — "0" -> "00"이 정답이다.
        Check("902614: #44 FALLBACK CODE 좌측0패딩(0 -> 00)", vanRelay.LastRequest.Read(44) == "00");
        Check("902614: #45 = KeyVersion+Tc+ModuleId(18바이트)", vanRelay.LastRequest.Read(45) == "01TC0001MODULE0001");
        // 2026-09-01 사용자 확정(PaymentOrchestrator.FillCardApprovalFields #46 주석 참고) — "0"+3자리
        // 길이값(리더기 원문, 재구성값)+페이로드. SuccessOutcome()의 encryptedData="ENCRYPTEDDATA0001"(17자)
        // 이므로 길이값은 "017".
        Check("902614: #46 = \"0\"+3자리길이(017)+EncryptedData", vanRelay.LastRequest.Read(46) == "0017ENCRYPTEDDATA0001");
        // 위 Read()는 AN 타입 우측 공백 패딩을 TrimEnd로 제거해서 돌려주므로, 실제 전문 바이트가
        // 정확히 196바이트(POSITION 407)를 채우고 나머지가 진짜 ' '(0x20)로 패딩됐는지는 별도로
        // 원문 바이트를 직접 읽어 확인한다(2026-09-01 사용자 확정 검증 — "바이트 단위로 확인").
        byte[] rawBody = vanRelay.LastRequest.Telegram.ToBody();
        string raw46 = System.Text.Encoding.ASCII.GetString(rawBody, 407, 196);
        Check("902614: #46 원문 바이트 길이 196, 헤더 \"0017\"로 시작", raw46.Length == 196 && raw46.StartsWith("0017ENCRYPTEDDATA0001", StringComparison.Ordinal));
        Check("902614: #46 나머지 175바이트(196-21)는 공백 패딩", raw46.Substring("0017ENCRYPTEDDATA0001".Length).TrimEnd(' ').Length == 0);
        Check("902614: #48 WCC 'I' -> '5'(IC)", vanRelay.LastRequest.Read(48) == "5");
        Check("902614: #50 고정값 '2'", vanRelay.LastRequest.Read(50) == "2");
        Check("902614: #53 EMV DATA = 0600(고정 길이 서브필드) + EmvEncodedData", vanRelay.LastRequest.Read(53) == "0600EMV0001");
        // P18-5 — #51(암호화된 비밀번호 정보)은 PIN 그대로(Read()는 ANS 타입 우측 space 패딩을
        // 제거하고 돌려준다 — #44의 좌측 0패딩과 반대로 이쪽은 trim이 정상 동작이다).
        // Check 이름에 PIN 리터럴을 직접 적지 않는다(P18-5 완료 조건 "#51 값이 어떤 로그에도 나타나지
        // 않는다"는 이 테스트 자신의 로그에도 그대로 적용한다 — presenter.PinToFireSynchronously의
        // 값을 그대로 참조해 이름을 짓는다).
        Check("902614: #51 = 화면에서 입력한 PIN 그대로(패딩 제거 후)", vanRelay.LastRequest.Read(51) == presenter.PinToFireSynchronously);

        // PRD §4.10 — VAN 통신 중에는 PROCESSING 화면이 실제로 떠 있어야 한다. 실제 Presenter는 창이
        // 닫힌 뒤의 ChangeState를 "무시 + Warn 로그"로 처리하므로(Views/PaymentNoticePresenter), 호출
        // 순서가 Close 뒤로 밀리면 사용자에게 통신중 화면이 전혀 보이지 않는다 — 가짜 Presenter는
        // 순서와 무관하게 History에 기록만 하기 때문에 이 조건을 명시적으로 검사해야 잡힌다.
        FileLogger.Info($"[payment-flow-test] 902614 알림창 호출 이력: {string.Join(" -> ", presenter.History)}");
        int closeIndex = presenter.History.IndexOf("Close");
        int processingIndex = presenter.History.IndexOf($"ChangeState:{PaymentNoticeState.VanProcessing}");
        Check("902614: PROCESSING 전환이 알림창이 닫히기 **전에** 일어남(PRD §4.10)",
            processingIndex >= 0 && (closeIndex < 0 || processingIndex < closeIndex));
    }

    /// <summary>설정 화면 게이트 — 3전문 모두 거부되는지(P17-5 확정 사항).</summary>
    private static async Task Scenario4_SetupGateBlocksAllThreeTelegrams()
    {
        foreach (string txType in new[] { "501008", "800000", "902614" })
        {
            var orchestrator = BuildOrchestrator(out _, out _, out _, out var gate, out _);
            gate.IsOpen = true;

            var fields = txType == "800000" ? new Dictionary<int, string> { [15] = "1000" }
                : txType == "902614" ? new Dictionary<int, string> { [29] = "1000" }
                : new Dictionary<int, string>();
            var request = BuildRequest(txType, fields);
            PosResponseTelegram response = await orchestrator.ProcessAsync(request).ConfigureAwait(false);

            Check($"{txType}: 설정화면 열림 중 E03 거부", response.Telegram.Read(7) == "E03");
        }
    }

    /// <summary>예상 밖 WCC 값 — 예외가 나서 TransactionQueue 최상위 catch로 이어지는지는 큐가 없는 이
    /// 하네스에서는 직접 검증하지 못하므로, Orchestrator가 예외를 던지는지까지만 확인한다(큐의
    /// InternalError 폴백은 P17-4 하네스에서 이미 검증됨).</summary>
    private static async Task Scenario5_UnknownWccSurfacesAsInternalError()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        r1.EnqueueCardReadOutcome(SuccessOutcome(wcc: "R")); // RF — 이 Flow가 다루지 않는 값
        // Phase 18(P18-4)부터 902614는 필드 채움 전에 PIN 단계를 먼저 거친다 — 이 시나리오의 관심사는
        // WCC 예외이지 PIN이 아니므로 즉시발화로 빠르게 통과시킨다.
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });

        bool threw = false;
        try
        {
            await orchestrator.ProcessAsync(request).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Check("902614: 알 수 없는 WCC('R')는 예외로 드러남(조용히 다른 값으로 대체되지 않음)", threw);
    }

    /// <summary>취소 — 카드 대기 중 취소가 오면 E01로 종료되는지(RunCardReadingRoundsAsync 재사용
    /// 확인용 최소 회귀).</summary>
    private static async Task Scenario6_UserCancelDuringCardApproval()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        // 응답을 일부러 늦춰(500ms) 그사이 취소가 라운드 "진행 중"(PendingParticipants가 채워진 뒤)에
        // 도착하게 한다 — Show() 시점에 곧바로 취소하면 아직 카드리딩 라운드가 시작 전이라 무효화할
        // 리더기 자체가 없다(정상 동작, 이 시나리오가 검증하려는 "대기 중 취소"가 아니다).
        r1.EnqueueCardReadOutcome(SuccessOutcome(), delay: TimeSpan.FromMilliseconds(500));

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        Task<PosResponseTelegram> processTask = orchestrator.ProcessAsync(request);

        // 라운드가 실제로 리더기에 요청을 보낸 뒤(CardReadCallCount>0) 취소한다.
        for (int i = 0; i < 40 && r1.CardReadCallCount == 0; i++)
            await Task.Delay(25).ConfigureAwait(false);
        Check("902614: 취소 전 카드리딩 라운드가 실제로 시작됨(전제 조건)", r1.CardReadCallCount > 0);
        presenter.FireCanceled();

        PosResponseTelegram response = await processTask.ConfigureAwait(false);

        Check("902614: 취소 시 E01", response.Telegram.Read(7) == "E01");

        // FireInterruptCleanup은 Task.Run으로 백그라운드 발사한다(UI 스레드 안 막기 위함, Phase 16
        // Opus 리뷰 H-2) — ProcessAsync가 반환한 시점에 아직 그 Task가 끝나지 않았을 수 있어 짧게
        // 폴링한다(실제 배선을 바꾸지 않고 테스트만 기다려 준다).
        for (int i = 0; i < 20 && r1.InvalidationCount < 1; i++)
            await Task.Delay(50).ConfigureAwait(false);

        Check("902614: 취소 시 대기 중이던 리더기에 0x60 전송", r1.InvalidationCount >= 1);
    }

    /// <summary>
    /// PRD §4.10 "실패 시 Reader 초기화" 회귀 방지(2026-08-27, Phase 17 최종 검증 H-3) — Phase 15의
    /// <c>RunVanApprovalAsync</c>가 VAN 실패 경로에서 채택 리더기를 초기화하고 있었는데 Phase 17
    /// 재구성에서 winner 참조와 함께 통째로 빠졌었다.
    /// </summary>
    private static async Task Scenario7_VanCommunicationFailureResetsReader()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        r1.EnqueueCardReadOutcome(SuccessOutcome());
        vanRelay.SetNextOutcome(VanRelayOutcome.CommunicationFailure(VanFailureKind.CommunicationFailure, "테스트용 통신 실패"));
        // Phase 18(P18-4)부터 902614는 VAN 진입 전에 PIN 단계를 먼저 거친다 — 이 시나리오의 관심사는
        // VAN 실패 시 리더기 초기화이지 PIN이 아니므로 즉시발화로 빠르게 통과시킨다.
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;

        int invalidationsBefore = r1.InvalidationCount;
        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram response = await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("902614: VAN 통신 실패 시 D02", response.Telegram.Read(7) == "D02");
        Check("902614: VAN 통신 실패 시 채택 리더기 초기화(PRD §4.10, H-3 회귀 방지)",
            r1.InvalidationCount > invalidationsBefore);

        // (2026-08-27 Phase 18 최종 검증 H-1 회귀 방지) 실패 응답은 요청을 clone해 만들므로, PIN을
        // 채운 뒤 실패하면 #51이 그대로 POS로 되돌아간다. #51은 kiosk가 원래 갖지 못하는 유일한
        // 필드이자(그래서 원캡이 입력받는다) 현재 평문이므로, 실패 응답에서는 반드시 비워야 한다.
        Check("902614: VAN 실패 응답에 PIN(#51)이 실려나가지 않음(H-1 회귀 방지)",
            response.Telegram.Read(51) == "");
    }

    // ===== Phase 18(P18-4) 임시 검증 시나리오 — 커밋 전 회귀 확인용, 정식 추가는 P18-6 몫 =====

    /// <summary>902614 정상 흐름: IC -> PIN -> 통신중 순서, 거래 종료 후 구독 누수 없음.</summary>
    private static async Task Scenario8_CardApprovalCollectsPinAndOrdersHistory()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        r1.EnqueueCardReadOutcome(SuccessOutcome());
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "1234";

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram response = await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        FileLogger.Info($"[payment-flow-test] 902614+PIN 알림창 호출 이력: {string.Join(" -> ", presenter.History)}");
        Check("902614+PIN: 응답 성공(#7=000)", response.Telegram.Read(7) == "000");

        int icIndex = presenter.History.IndexOf($"Show:{PaymentNoticeState.IcCardRequest}");
        int pinChangeIndex = presenter.History.IndexOf($"ChangeState:{PaymentNoticeState.PinEntry}");
        int processingIndex = presenter.History.IndexOf($"ChangeState:{PaymentNoticeState.VanProcessing}");
        Check("902614+PIN: IC -> PIN -> 통신중 순서", icIndex >= 0 && pinChangeIndex > icIndex && processingIndex > pinChangeIndex);
        Check("902614+PIN: 거래 종료 후 Canceled 구독 누수 없음", presenter.CanceledSubscriberCount == 0);
        Check("902614+PIN: 거래 종료 후 PinEntered 구독 누수 없음", presenter.PinEnteredSubscriberCount == 0);

        // P18-5 — #51(암호화된 비밀번호 정보, ANS 100)에 화면에서 입력한 PIN이 정확히 들어갔는지,
        // 인접 필드(#50/#53)가 밀리지 않았는지 요청 전문(request.Telegram, ProcessAsync가 제자리에서
        // 채운다)으로 확인한다. Read()는 ANS 타입의 우측 space 패딩을 제거해 돌려주므로
        // (PosField.Trim), trim된 값으로 단언하고, POSITION 612~711의 원본 바이트(PIN 4자리 + space
        // 96)는 raw ToBody()로 별도 확인한다. Check 이름에 PIN 리터럴을 직접 적지 않는다(P18-5 완료
        // 조건 "#51 값이 어떤 로그에도 나타나지 않는다"는 이 테스트 자신의 로그에도 그대로 적용한다).
        Check("902614+PIN: #51 값 = 화면에서 입력한 PIN 그대로(패딩 제거 후)", request.Telegram.Read(51) == presenter.PinToFireSynchronously);
        Check("902614+PIN: #50(신용카드 승인 인증방식) 밀리지 않음(고정값 \"2\")", request.Telegram.Read(50) == "2");
        Check("902614+PIN: #53(EMV DATA) 밀리지 않음(길이 서브필드 \"0600\"으로 시작)", request.Telegram.Read(53).StartsWith("0600"));

        byte[] rawBody = request.Telegram.ToBody();
        string raw51 = System.Text.Encoding.ASCII.GetString(rawBody, 612, 100); // PIN은 ASCII 숫자라 CP949/ASCII 동일
        Check("902614+PIN: raw POSITION 612~711 = 화면에서 입력한 PIN + space 96(hex 덤프 대응)",
            raw51 == presenter.PinToFireSynchronously + new string(' ', 96));
        string raw50 = System.Text.Encoding.ASCII.GetString(rawBody, 611, 1);
        string raw52to53Start = System.Text.Encoding.ASCII.GetString(rawBody, 712, 12); // #52(712,12)
        Check("902614+PIN: raw #50(POSITION 611) 밀리지 않음", raw50 == "2");
        Check("902614+PIN: raw #52(POSITION 712) 영역이 #51 침범으로 깨지지 않음(공백 12칸, #52는 원캡 미담당)",
            raw52to53Start == new string(' ', 12));
    }

    /// <summary>800000에는 PIN 단계가 끼어들지 않는다(전문 종별 구분 회귀 방지). PIN 즉시발화 플래그를
    /// 켜 둬도(사용자 실수를 가정) History에 PinEntry가 등장하지 않아야 한다.</summary>
    private static async Task Scenario9_CardInfoInquirySkipsPinStep()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        r1.EnqueueCardReadOutcome(SuccessOutcome());
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;

        var request = BuildRequest("800000", new Dictionary<int, string> { [15] = "1000" });
        PosResponseTelegram response = await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("800000: 응답 성공(#7=000)", response.Telegram.Read(7) == "000");
        Check("800000: History에 PinEntry 없음(PIN 단계 미진입)",
            !presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"));
    }

    /// <summary>PIN 대기 중 취소 -> E01 정확히 1건 확정 + 리더기 초기화 호출 확인.</summary>
    private static async Task Scenario10_CancelDuringPinEntryYieldsE01()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        r1.EnqueueCardReadOutcome(SuccessOutcome());

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        Task<PosResponseTelegram> processTask = orchestrator.ProcessAsync(request);

        for (int i = 0; i < 40 && !presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"); i++)
            await Task.Delay(25).ConfigureAwait(false);
        Check("902614: PIN 화면 진입 확인(전제조건)", presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"));

        presenter.FireCanceled();

        PosResponseTelegram response = await processTask.ConfigureAwait(false);
        Check("902614: PIN 대기 중 취소 시 E01(정확히 1건 확정)", response.Telegram.Read(7) == "E01");

        for (int i = 0; i < 20 && r1.InvalidationCount < 1; i++)
            await Task.Delay(50).ConfigureAwait(false);
        Check("902614: PIN 대기 중 취소 시 채택 리더기 초기화(0x60)", r1.InvalidationCount >= 1);
        Check("902614: 취소 후 PinEntered 구독 누수 없음", presenter.PinEnteredSubscriberCount == 0);
    }

    /// <summary>PIN 대기 중 Timeout -> E02 정확히 1건 확정. UserInputStepExtension(30초)이 실제로
    /// 적용되는 것을 그대로 겪어야 하므로(짧게 우회할 훅이 없다 — 상수 1곳 원칙) 약 30초 이상 걸린다.
    /// </summary>
    private static async Task Scenario11_TimeoutDuringPinEntryYieldsE02()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        r1.EnqueueCardReadOutcome(SuccessOutcome());

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        Task<PosResponseTelegram> processTask = orchestrator.ProcessAsync(request);

        for (int i = 0; i < 40 && !presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"); i++)
            await Task.Delay(25).ConfigureAwait(false);
        Check("902614(Timeout): PIN 화면 진입 확인(전제조건)", presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"));

        // PIN을 끝까지 입력하지 않고 데드라인(원래 5초 + PIN 진입 시 +30초 연장) 만료를 기다린다.
        PosResponseTelegram response = await processTask.ConfigureAwait(false);
        Check("902614(Timeout): PIN 대기 중 Timeout 시 E02(정확히 1건 확정)", response.Telegram.Read(7) == "E02");

        for (int i = 0; i < 20 && r1.InvalidationCount < 1; i++)
            await Task.Delay(50).ConfigureAwait(false);
        Check("902614(Timeout): Timeout 시 채택 리더기 초기화(0x60)", r1.InvalidationCount >= 1);
        Check("902614(Timeout): Timeout 후 PinEntered 구독 누수 없음", presenter.PinEnteredSubscriberCount == 0);
    }

    /// <summary>PIN 즉시발화 플래그로 "구독 -> ChangeState" 순서를 증명한다(Phase 15 Opus 리뷰 H-1과
    /// 같은 종류의 회귀 방지, development_plan.md P18-4 "반드시 지킬 것"). 순서가 반대라면 이 PIN
    /// 완료는 구독자 없이 유실되고 거래는 데드라인까지 멈춰야 한다 — 여기서는 짧은 시간 안에 정상
    /// 완료되는 것으로 순서가 올바름을 확인한다.</summary>
    private static async Task Scenario12_PinEnteredBeforeSubscriptionIsNotLost()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);
        r1.EnqueueCardReadOutcome(SuccessOutcome());
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "5678";

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        Task<PosResponseTelegram> processTask = orchestrator.ProcessAsync(request);
        Task completed = await Task.WhenAny(processTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

        Check("902614: PIN 즉시발화가 유실되지 않고 2초 안에 정상 완료(구독이 ChangeState보다 먼저 걸림)",
            completed == processTask);

        if (completed == processTask)
        {
            PosResponseTelegram response = await processTask.ConfigureAwait(false);
            Check("902614: 즉시발화 순서 검증 — 응답 성공(#7=000)", response.Telegram.Read(7) == "000");
        }
    }

    /// <summary>Phase 21 P21-2 — PRD §8.4 "이전 거래 데이터가 다음 거래에 영향을 주어서는 안 된다"를
    /// 연속 실행으로 실증한다. **같은 <see cref="PaymentOrchestrator"/> 인스턴스**로 서로 다른 PIN을
    /// 쓰는 두 902614 거래를 연달아 처리해(실제 운영과 동일하게 인스턴스를 재사용), 두 번째 거래가
    /// VAN에 보내는 전문 원문(raw bytes) 전체에 **첫 번째 거래의 PIN이 어디에도 남아 있지 않은지**
    /// 확인한다. <c>.Read(51)</c>처럼 필드 위치만 보는 검사로는 "엉뚱한 자리에 남는 잔존"을
    /// 놓칠 수 있어 raw 바이트 전체를 훑는다(P18-5 raw 검사 패턴 계승).</summary>
    private static async Task Scenario13_ConsecutiveTransactionsDoNotLeakCardOrPinData()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay);

        // 거래 A — 이 값들이 거래 B로 새면 안 된다.
        const string pinA = "1357";
        r1.EnqueueCardReadOutcome(SuccessOutcome(cardNumber: "1111222233334444"));
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = pinA;
        var requestA = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram responseA = await orchestrator.ProcessAsync(requestA).ConfigureAwait(false);
        Check("연속거래 A: 응답 성공(#7=000)", responseA.Telegram.Read(7) == "000");
        byte[] rawBodyA = vanRelay.LastRequest!.Telegram.ToBody();

        // 거래 B — 서로 다른 카드/PIN으로, 같은 orchestrator·리더기 인스턴스를 그대로 재사용한다
        // (실제 운영에서 앱을 껐다 켜지 않고 여러 거래를 처리하는 상황과 동일).
        const string pinB = "2468";
        r1.EnqueueCardReadOutcome(SuccessOutcome(cardNumber: "9999888877776666"));
        presenter.PinToFireSynchronously = pinB;
        var requestB = BuildRequest("902614", new Dictionary<int, string> { [29] = "2000" });
        PosResponseTelegram responseB = await orchestrator.ProcessAsync(requestB).ConfigureAwait(false);
        Check("연속거래 B: 응답 성공(#7=000)", responseB.Telegram.Read(7) == "000");
        byte[] rawBodyB = vanRelay.LastRequest!.Telegram.ToBody();

        string rawTextB = System.Text.Encoding.ASCII.GetString(rawBodyB);
        Check("연속거래: 거래 B의 VAN 요청 원문에 거래 A의 PIN이 어디에도 없음(raw 바이트 전수 검사)",
            !rawTextB.Contains(pinA));
        Check("연속거래: 거래 B의 #51은 거래 B 자신의 PIN(정확한 위치)", requestB.Read(51) == pinB);

        string rawTextA = System.Text.Encoding.ASCII.GetString(rawBodyA);
        Check("연속거래: 거래 A의 VAN 요청 원문에 거래 B의 PIN이 없음(순서 반대 방향도 확인 — sanity)",
            !rawTextA.Contains(pinB));

        Check("연속거래: 두 거래의 알림창 History가 각자 독립적으로 IcCardRequest로 시작함(이전 거래 상태 잔존 없음)",
            presenter.History.Count(h => h == "Show:IcCardRequest") == 2);
    }

    /// <summary>
    /// <see cref="TelegramLogRedactor"/>의 "기형 전문(길이 불일치) 폴백" 경로를 실제로 실행해 검증한다
    /// (2026-09-01, TelegramLogRedactor 클래스 요약 "정상/기형 분기" 절 — 지금까지 코드 리뷰로만
    /// 확인했고 실행 검증이 없었다). <c>PaymentOrchestrator</c>를 거치지 않고
    /// <see cref="TelegramLogRedactor.Redact"/>를 직접 호출한다 — 실제 POS 요청 경로(<see
    /// cref="PosRequestTelegram.Parse"/>)는 본문 길이가 스키마와 다르면 E40으로 요청 자체를 거부하므로
    /// (닿을 수 없는 malformed body를 만들 방법이 없다), 이 시나리오는 로그 유틸 자체를 순수하게
    /// 단위 검증하는 것이다 — 프로덕션 경로(Orchestrator/PosSocketServer/VanService)는 전혀 건드리지
    /// 않는다.
    ///
    /// 확인하는 것 2가지(development_plan.md "P22-6부속" 지시):
    /// <list type="number">
    /// <item>길이가 어긋나면 <c>Redact</c>가 위치 기반 마스킹(#46 부분 마스킹)을 시도하지 않고 원문을
    /// 그대로 돌려주는지 — #46 자리에 심어 둔 16자리 숫자열이 마스킹 없이 그대로 나오는지로 확인.</item>
    /// <item>그 원문이 파이프라인의 다음 단계인 <see cref="LogMessageMasker.Mask"/>(13~19자리 숫자
    /// 범용 마스킹)를 거치면, 카드번호처럼 보이는 그 숫자열이 최소한 그때는 마스킹되는지.</item>
    /// </list>
    /// 대조군으로 길이가 올바른 정상 본문도 같이 돌려, 정상 경로에서는 위치 기반 마스킹이 그대로
    /// 동작함을(회귀 없음) 같은 시나리오 안에서 확인한다.
    /// </summary>
    private static void Scenario14_MalformedTelegramFallsBackToGenericMasking()
    {
        if (!PosSchemaRegistry.TryResolve("902614", out PosTelegramSchema? schema) || schema is null)
        {
            Check("기형전문: 902614 스키마 해석(전제조건)", false);
            return;
        }

        // #46(암호화된 카드정보, AN 196)에 카드번호처럼 보이는 16자리 숫자열을 심는다 — 실제로는
        // 암호화된 데이터가 들어갈 자리지만, 이 시나리오는 "일반 마스킹 패턴에 걸리는 숫자열"이
        // 어떻게 되는지가 관심사라 의도적으로 숫자열을 쓴다.
        const string decoyDigits = "9412345678901234"; // 16자리 — 범용 카드번호 패턴(13~19자리)에 해당.
        var telegram = PosTelegram.CreateEmpty(schema);
        telegram.Write(46, decoyDigits);
        byte[] wellFormedBody = telegram.ToBody();
        Check("기형전문: 대조군 본문 길이가 스키마 TotalLength와 일치(전제조건)", wellFormedBody.Length == schema.TotalLength);

        // --- 대조군: 정상 길이 — 위치 기반 마스킹이 그대로 동작해야 한다(회귀 확인). ---
        string wellFormedRedacted = TelegramLogRedactor.Redact("902614", wellFormedBody);
        Check("기형전문(대조군): 정상 길이는 위치 기반 마스킹이 적용되어 #46 숫자열이 그대로 노출되지 않음",
            !wellFormedRedacted.Contains(decoyDigits));
        Check("기형전문(대조군): 정상 길이는 #46 앞 6바이트만 노출(부분 마스킹)",
            wellFormedRedacted.Contains(decoyDigits.Substring(0, 6) + new string('*', decoyDigits.Length - 6)));

        // --- 본 시나리오: 본문 끝에 1바이트를 덧붙여 길이를 스키마와 어긋나게 만든다(기형 전문). ---
        byte[] malformedBody = new byte[wellFormedBody.Length + 1];
        Array.Copy(wellFormedBody, malformedBody, wellFormedBody.Length);
        malformedBody[wellFormedBody.Length] = (byte)'X';
        Check("기형전문: 조작한 본문 길이가 스키마 TotalLength와 다름(전제조건)", malformedBody.Length != schema.TotalLength);

        string malformedRedacted = TelegramLogRedactor.Redact("902614", malformedBody);

        // 확인 1 — 위치 기반 마스킹을 시도하지 않고 원문을 그대로 돌려줬는지: #46 자리의 원래 값(16자리
        // 숫자열)이 마스킹 없이 그대로 남아 있어야 한다(위치 기반 마스킹이 적용됐다면 정상 케이스처럼
        // 앞 6자리만 남고 나머지가 '*'로 바뀌었을 것).
        Check("기형전문: 길이 불일치 시 위치 기반 마스킹을 시도하지 않고 원문을 그대로 반환(#46 숫자열이 마스킹 없이 그대로 남음)",
            malformedRedacted.Contains(decoyDigits));

        // 확인 2 — 그 원문이 파이프라인의 다음 단계(LogMessageMasker.Mask, 실제 FileLogger 호출부가
        // 모든 메시지에 자동으로 거는 범용 마스킹)를 거치면, 최소한 카드번호로 보이는 숫자열은
        // 마스킹돼야 한다(클래스 요약이 말하는 "최소한의 방어").
        string genericMasked = LogMessageMasker.Mask(malformedRedacted);
        Check("기형전문: 범용 마스킹(LogMessageMasker)을 거치면 #46 숫자열이 마스킹됨(최소한의 방어 확인)",
            !genericMasked.Contains(decoyDigits));
        Check("기형전문: 범용 마스킹 결과가 카드번호 마스킹 형식(앞6+뒤4, 가운데 '*')을 따름",
            genericMasked.Contains(decoyDigits.Substring(0, 6) + new string('*', decoyDigits.Length - 10) + decoyDigits.Substring(decoyDigits.Length - 4)));
    }
}
