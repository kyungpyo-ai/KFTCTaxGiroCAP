using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
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
            await Scenario15_KioskIdMatchAllowsCardApproval().ConfigureAwait(false);
            await Scenario16_KioskIdMismatchRejectsBeforeCardReading().ConfigureAwait(false);
            await Scenario17_KioskIdEmptyConfiguredRejects().ConfigureAwait(false);
            await Scenario18_KioskIdReceivedAllSpacesRejectedEvenIfConfiguredValid().ConfigureAwait(false);
            await Scenario19_CardApprovalDisposesCardDataAfterTransaction().ConfigureAwait(false);
            await Scenario20_ResponseCardReadingFieldsAreCleared().ConfigureAwait(false);
            Scenario21_LastTransactionResponseStoreRoundTrip();
            await Scenario22_OrchestratorPersistsResponseToLastTransactionStore().ConfigureAwait(false);
            await Scenario23_StatusInquiryMatchReturnsStoredResponseVerbatim().ConfigureAwait(false);
            await Scenario24_StatusInquiryNoMatchYieldsE07().ConfigureAwait(false);
            Scenario25_NoE07LiteralOutsidePosResultCodeMapper();

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

    /// <summary>개선권장 4/5(CP2 Opus 리뷰) — E06 비교가 "둘 중 하나라도 빈 값이면 무조건 거부"로
    /// 바뀌면서, 빈 문자열끼리 일치시켜 통과하던 옛 loophole이 사라졌다. 그래서 902614 시나리오들의
    /// 기본값을 더 이상 빈 문자열에 의존하지 않고, <b>설정값과 실제로 같은 값을 #42에 채우는 "정상
    /// 운영 상태"</b>로 바꿨다 — <see cref="BuildOrchestrator"/>의 <c>kioskId</c> 기본값과 <see
    /// cref="BuildRequest"/>의 902614 자동 채움이 둘 다 이 상수를 쓴다(둘 다 P23-7 신규 시나리오
    /// 15/16 전용이던 것을 기본값으로 승격).</summary>
    private const string ConfiguredKioskId = "TESTKIOSK001";

    private static PaymentOrchestrator BuildOrchestrator(
        out FakeReaderEndpoint reader1, out FakeReaderEndpoint reader2,
        out FakePaymentNoticePresenter presenter, out FakeSetupScreenGate gate,
        out CapturingVanRelayService vanRelay,
        // P26-2 — IntegrityCheckStore/ObservedIdentityStore와 같은 임시 dbPath를 공유하는
        // LastTransactionResponseStore도 함께 내보낸다. 새 시나리오가 TryLoad()로 저장 결과를 직접
        // 확인할 수 있어야 하기 때문이다(체크포인트 1 F1 — "검증 불가능" 지적 해소).
        out LastTransactionResponseStore lastTransactionResponseStore,
        string port1 = "COM 05", string port2 = "미사용",
        // 개선권장 5(CP2) — 기본값을 설정값과 일치하는 "정상 운영 상태"로 바꿨다(위 주석 참고). 이
        // 검증을 신경 쓰지 않는 기존 시나리오(3, 5~13)는 BuildRequest의 자동 채움과 짝을 이뤄 그대로
        // 통과한다.
        string kioskId = ConfiguredKioskId)
    {
        reader1 = new FakeReaderEndpoint("COM 05");
        reader2 = new FakeReaderEndpoint("COM 03");
        presenter = new FakePaymentNoticePresenter();
        gate = new FakeSetupScreenGate();
        vanRelay = new CapturingVanRelayService();
        string dbPath = Path.Combine(Path.GetTempPath(), $"p17-test-{Guid.NewGuid():N}.db");
        var integrityStore = new IntegrityCheckStore(dbPath);
        // P22-7 — 같은 파일을 가리키게 한다(프로덕션과 동일한 전제, App.xaml.cs 참고).
        var observedIdentityStore = new ObservedIdentityStore(dbPath);
        lastTransactionResponseStore = new LastTransactionResponseStore(dbPath);

        return new PaymentOrchestrator(
            new IReaderEndpoint[] { reader1, reader2 },
            integrityStore,
            observedIdentityStore,
            lastTransactionResponseStore,
            presenter,
            gate,
            vanRelay,
            () => new ReaderSettings { Port1 = port1, Port2 = port2 },
            // 검증용 짧은 데드라인(P23-6). 개선권장 3(CP2) — Func<ShopSettings, TimeSpan>로 바뀌었지만
            // 하네스는 여전히 설정과 무관하게 5초 고정을 쓴다(입력값은 무시).
            _ => TimeSpan.FromSeconds(5),
            () => new ShopSettings { KioskId = kioskId }); // P23-7
    }

    private static int _managementSequence;

    /// <summary>개선권장 5(CP2 Opus 리뷰) — <paramref name="autoFillKioskId"/>가 true(기본값)이고
    /// <paramref name="transactionType"/>이 902614이며 <paramref name="fields"/>가 #42를 명시적으로
    /// 채우지 않았으면, "설정값과 일치하는 정상 운영 상태"를 흉내내도록 <see cref="ConfiguredKioskId"/>를
    /// 자동으로 채운다. 개선권장 4로 E06 검사가 "둘 중 하나라도 빈 값이면 거부"로 바뀌면서, #42를
    /// 채우지 않던 기존 시나리오(3, 5~13)가 전부 깨지지 않게 하기 위함이다. #42를 의도적으로 비워
    /// 두려는 시나리오(신규 Scenario18)는 <paramref name="autoFillKioskId"/>를 false로 준다.</summary>
    private static PosRequestTelegram BuildRequest(
        string transactionType, IReadOnlyDictionary<int, string> fields, bool autoFillKioskId = true)
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

        if (autoFillKioskId && transactionType == "902614" && !fields.ContainsKey(42))
            telegram.Write(42, ConfiguredKioskId);

        foreach (var kv in fields)
            telegram.Write(kv.Key, kv.Value);

        var outcome = PosRequestTelegram.Parse(telegram.ToBody());
        if (!outcome.IsSuccess)
            throw new InvalidOperationException($"테스트 요청 빌드 실패: {outcome.ErrorCode}");

        return outcome.Telegram!;
    }

    /// <summary>P26-3/P26-4 — 거래 상태 조회 요청(70바이트, 개별부 없음)을 만든다. <paramref
    /// name="managementNumberToReuse"/>는 §3.4.4의 예외(원거래 <c>#9</c> 재사용)를 그대로 흉내낸다 —
    /// <see cref="BuildRequest"/>가 자동으로 채우는 새 일련번호를 이 값으로 덮어쓴다. 902614 전용
    /// 키오스크 고유번호 자동 채움 로직은 이 전문과 무관하므로 <c>autoFillKioskId</c>는 그대로
    /// 기본값을 써도 무해하다(거래구분이 "999900"이라 그 조건에 걸리지 않는다).</summary>
    private static PosRequestTelegram BuildInquiryRequest(string managementNumberToReuse) =>
        BuildRequest(TransactionStatusInquiryTransactionType, new Dictionary<int, string> { [9] = managementNumberToReuse });

    /// <summary>P26-3(PRD.md §3.4.8) — <c>TransactionStatusInquirySchema.FixedTransactionType</c>은
    /// <c>internal</c>이라 이 <c>Services/Diagnostics/</c> 하네스에서도 접근 가능하지만, 값 자체가
    /// "미채번" 임시값이라는 사실을 이 파일에서도 드러내기 위해 별도 상수로 한 번 더 참조한다.</summary>
    private const string TransactionStatusInquiryTransactionType = TransactionStatusInquirySchema.FixedTransactionType;

    private static CardReadCommandOutcome SuccessOutcome(string cardNumber = "9412345678901234", string wcc = "I")
    {
        // #46 검증용(2026-09-01, PaymentOrchestrator.FillCardApprovalFields 참고) — 실제 파서는 리더기가
        // 보낸 길이필드를 읽은 payload 길이로 재구성한다. 하네스도 같은 전제를 지키도록 payload 길이로부터
        // 3자리 zero-padded 길이 텍스트를 계산한다(하드코딩하면 실제 파싱 경로와 어긋날 수 있다).
        const string encryptedData = "ENCRYPTEDDATA0001";
        string encryptedDataLengthText = encryptedData.Length.ToString("D3");

        // Phase 25 P25-3 — CardReadData 생성자가 char[]를 받는다. 이 하네스는 가짜 고정값을 만들 뿐이라
        // ToCharArray()로 변환해서 넘긴다(실제 파싱 경로는 Protocol/Reader/CardReadResponseParser가
        // string을 거치지 않고 바이트에서 직접 char[]를 만든다 — 이 하네스만의 편의).
        return CardReadCommandOutcome.Success("00", new CardReadData(
            transactionType: "A".ToCharArray(), keyVersion: "01".ToCharArray(), tc: "TC0001".ToCharArray(),
            moduleId: "MODULE0001".ToCharArray(),
            fallbackCode: "0".ToCharArray(), amount: "000000000001000".ToCharArray(), cardNumber: cardNumber.ToCharArray(),
            encryptionMarker: "ENC".ToCharArray(), wcc: wcc.ToCharArray(), encryptedData: encryptedData.ToCharArray(),
            encryptedDataLengthText: encryptedDataLengthText.ToCharArray(),
            emvEncodingMethod: "B".ToCharArray(), emvEncodedData: "EMV0001".ToCharArray(), readerAuthId: "READERAUTH000001".ToCharArray(),
            readerSerialEncryptionMarker: "NOE".ToCharArray(), readerSerial: "SERIAL0001".ToCharArray(),
            readerEncryptionInfo: "READERENCRYPTINFO001".ToCharArray(), tc3: "TC30001".ToCharArray(),
            payOnCertifyCode: "PAYONCERT00000000000000000001".ToCharArray()));
    }

    // ===== 시나리오 =====

    /// <summary>501008 — 카드리딩 없이 즉시 relay. 리더기가 하나도 설정 안 된 상태에서도 성공해야
    /// 한다(P17-5 완료 조건).</summary>
    private static async Task Scenario1_NoticeInquiryRelaysWithoutReader()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore,
            port1: "미사용", port2: "미사용");

        var request = BuildRequest("501008", new Dictionary<int, string>());
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("501008: 리더기 미설정에도 성공(카드리딩 없음)", response.Telegram.Read(7) == "000");
        Check("501008: 카드리딩 호출 0회(리더기를 전혀 안 씀)", r1.CardReadCallCount == 0 && r2.CardReadCallCount == 0);
        Check("501008: VAN까지 relay 도달", vanRelay.LastRequest != null);
    }

    /// <summary>800000 — 카드리딩 성공 후 BIN(카드번호 앞 8자리)만 채워지는지.</summary>
    private static async Task Scenario2_CardInfoInquiryFillsBin()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        r1.EnqueueCardReadOutcome(SuccessOutcome(cardNumber: "9412345678901234"));

        var request = BuildRequest("800000", new Dictionary<int, string> { [15] = "1000" });
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("800000: 응답 성공(#7=000)", response.Telegram.Read(7) == "000");
        Check("800000: VAN 요청에 실린 BIN이 카드번호 앞 8자리", vanRelay.LastRequest?.Read(14) == "94123456");
    }

    /// <summary>902614 — 원캡 담당 8필드(#43~#46,#48,#50,#51,#53)가 정확히 채워지는지. Phase 18(P18-4)부터 902614는
    /// 카드리딩 성공 후 PIN 입력 단계를 거치므로(<see cref="PaymentOrchestrator.CollectPinAsync"/>),
    /// PIN을 주지 않으면 이 시나리오가 실제 Timeout(35초)까지 블로킹된다 — 즉시발화 플래그로 PIN
    /// 단계를 빠르게 통과시킨다(이 시나리오의 관심사는 필드 채움이지 PIN 자체가 아니므로).</summary>
    private static async Task Scenario3_CardApprovalFillsSevenFields()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        r1.EnqueueCardReadOutcome(SuccessOutcome(wcc: "I"));
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "1234".ToCharArray();

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

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
        Check("902614: #51 = 화면에서 입력한 PIN 그대로(패딩 제거 후)", vanRelay.LastRequest.Read(51) == new string(presenter.PinToFireSynchronously));

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
            var orchestrator = BuildOrchestrator(out _, out _, out _, out var gate, out _, out var lastTransactionResponseStore);
            gate.IsOpen = true;

            var fields = txType == "800000" ? new Dictionary<int, string> { [15] = "1000" }
                : txType == "902614" ? new Dictionary<int, string> { [29] = "1000" }
                : new Dictionary<int, string>();
            var request = BuildRequest(txType, fields);
            PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

            Check($"{txType}: 설정화면 열림 중 E03 거부", response.Telegram.Read(7) == "E03");
            // P26-2(체크포인트 1 F1) — 설정 화면 게이트로 거부된 요청은 "거래"가 성립하지 않으므로
            // §7 저장소에 아무 것도 남기지 않아야 한다.
            Check($"{txType}: 설정화면 거부 시 §7 저장소에 기록 없음", lastTransactionResponseStore.TryLoad() == null);
        }
    }

    /// <summary>예상 밖 WCC 값 — 예외가 나서 TransactionQueue 최상위 catch로 이어지는지는 큐가 없는 이
    /// 하네스에서는 직접 검증하지 못하므로, Orchestrator가 예외를 던지는지까지만 확인한다(큐의
    /// InternalError 폴백은 P17-4 하네스에서 이미 검증됨).</summary>
    private static async Task Scenario5_UnknownWccSurfacesAsInternalError()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
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
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        // 응답을 일부러 늦춰(500ms) 그사이 취소가 라운드 "진행 중"(PendingParticipants가 채워진 뒤)에
        // 도착하게 한다 — Show() 시점에 곧바로 취소하면 아직 카드리딩 라운드가 시작 전이라 무효화할
        // 리더기 자체가 없다(정상 동작, 이 시나리오가 검증하려는 "대기 중 취소"가 아니다).
        r1.EnqueueCardReadOutcome(SuccessOutcome(), delay: TimeSpan.FromMilliseconds(500));

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        Task<IPosOutboundResponse> processTask = orchestrator.ProcessAsync(request);

        // 라운드가 실제로 리더기에 요청을 보낸 뒤(CardReadCallCount>0) 취소한다.
        for (int i = 0; i < 40 && r1.CardReadCallCount == 0; i++)
            await Task.Delay(25).ConfigureAwait(false);
        Check("902614: 취소 전 카드리딩 라운드가 실제로 시작됨(전제 조건)", r1.CardReadCallCount > 0);
        presenter.FireCanceled();

        PosResponseTelegram response = (PosResponseTelegram)await processTask.ConfigureAwait(false);

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
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        r1.EnqueueCardReadOutcome(SuccessOutcome());
        vanRelay.SetNextOutcome(VanRelayOutcome.CommunicationFailure(VanFailureKind.CommunicationFailure, "테스트용 통신 실패"));
        // Phase 18(P18-4)부터 902614는 VAN 진입 전에 PIN 단계를 먼저 거친다 — 이 시나리오의 관심사는
        // VAN 실패 시 리더기 초기화이지 PIN이 아니므로 즉시발화로 빠르게 통과시킨다.
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;

        int invalidationsBefore = r1.InvalidationCount;
        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

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
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        r1.EnqueueCardReadOutcome(SuccessOutcome());
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "1234".ToCharArray();

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

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
        Check("902614+PIN: #51 값 = 화면에서 입력한 PIN 그대로(패딩 제거 후)", request.Telegram.Read(51) == new string(presenter.PinToFireSynchronously));
        Check("902614+PIN: #50(신용카드 승인 인증방식) 밀리지 않음(고정값 \"2\")", request.Telegram.Read(50) == "2");
        Check("902614+PIN: #53(EMV DATA) 밀리지 않음(길이 서브필드 \"0600\"으로 시작)", request.Telegram.Read(53).StartsWith("0600"));

        byte[] rawBody = request.Telegram.ToBody();
        string raw51 = System.Text.Encoding.ASCII.GetString(rawBody, 612, 100); // PIN은 ASCII 숫자라 CP949/ASCII 동일
        Check("902614+PIN: raw POSITION 612~711 = 화면에서 입력한 PIN + space 96(hex 덤프 대응)",
            raw51 == new string(presenter.PinToFireSynchronously) + new string(' ', 96));
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
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        r1.EnqueueCardReadOutcome(SuccessOutcome());
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;

        var request = BuildRequest("800000", new Dictionary<int, string> { [15] = "1000" });
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("800000: 응답 성공(#7=000)", response.Telegram.Read(7) == "000");
        Check("800000: History에 PinEntry 없음(PIN 단계 미진입)",
            !presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"));
    }

    /// <summary>PIN 대기 중 취소 -> E01 정확히 1건 확정 + 리더기 초기화 호출 확인.</summary>
    private static async Task Scenario10_CancelDuringPinEntryYieldsE01()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        r1.EnqueueCardReadOutcome(SuccessOutcome());

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        Task<IPosOutboundResponse> processTask = orchestrator.ProcessAsync(request);

        for (int i = 0; i < 40 && !presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"); i++)
            await Task.Delay(25).ConfigureAwait(false);
        Check("902614: PIN 화면 진입 확인(전제조건)", presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"));

        presenter.FireCanceled();

        PosResponseTelegram response = (PosResponseTelegram)await processTask.ConfigureAwait(false);
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
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        r1.EnqueueCardReadOutcome(SuccessOutcome());

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        Task<IPosOutboundResponse> processTask = orchestrator.ProcessAsync(request);

        for (int i = 0; i < 40 && !presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"); i++)
            await Task.Delay(25).ConfigureAwait(false);
        Check("902614(Timeout): PIN 화면 진입 확인(전제조건)", presenter.History.Contains($"ChangeState:{PaymentNoticeState.PinEntry}"));

        // PIN을 끝까지 입력하지 않고 데드라인(원래 5초 + PIN 진입 시 +30초 연장) 만료를 기다린다.
        PosResponseTelegram response = (PosResponseTelegram)await processTask.ConfigureAwait(false);
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
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        r1.EnqueueCardReadOutcome(SuccessOutcome());
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "5678".ToCharArray();

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        Task<IPosOutboundResponse> processTask = orchestrator.ProcessAsync(request);
        Task completed = await Task.WhenAny(processTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

        Check("902614: PIN 즉시발화가 유실되지 않고 2초 안에 정상 완료(구독이 ChangeState보다 먼저 걸림)",
            completed == processTask);

        if (completed == processTask)
        {
            PosResponseTelegram response = (PosResponseTelegram)await processTask.ConfigureAwait(false);
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
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);

        // 거래 A — 이 값들이 거래 B로 새면 안 된다.
        char[] pinA = "1357".ToCharArray();
        r1.EnqueueCardReadOutcome(SuccessOutcome(cardNumber: "1111222233334444"));
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = pinA;
        var requestA = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram responseA = (PosResponseTelegram)await orchestrator.ProcessAsync(requestA).ConfigureAwait(false);
        Check("연속거래 A: 응답 성공(#7=000)", responseA.Telegram.Read(7) == "000");
        byte[] rawBodyA = vanRelay.LastRequest!.Telegram.ToBody();

        // 거래 B — 서로 다른 카드/PIN으로, 같은 orchestrator·리더기 인스턴스를 그대로 재사용한다
        // (실제 운영에서 앱을 껐다 켜지 않고 여러 거래를 처리하는 상황과 동일).
        char[] pinB = "2468".ToCharArray();
        r1.EnqueueCardReadOutcome(SuccessOutcome(cardNumber: "9999888877776666"));
        presenter.PinToFireSynchronously = pinB;
        var requestB = BuildRequest("902614", new Dictionary<int, string> { [29] = "2000" });
        PosResponseTelegram responseB = (PosResponseTelegram)await orchestrator.ProcessAsync(requestB).ConfigureAwait(false);
        Check("연속거래 B: 응답 성공(#7=000)", responseB.Telegram.Read(7) == "000");
        byte[] rawBodyB = vanRelay.LastRequest!.Telegram.ToBody();

        string rawTextB = System.Text.Encoding.ASCII.GetString(rawBodyB);
        Check("연속거래: 거래 B의 VAN 요청 원문에 거래 A의 PIN이 어디에도 없음(raw 바이트 전수 검사)",
            !rawTextB.Contains(new string(pinA)));
        Check("연속거래: 거래 B의 #51은 거래 B 자신의 PIN(정확한 위치)", requestB.Read(51) == new string(pinB));

        string rawTextA = System.Text.Encoding.ASCII.GetString(rawBodyA);
        Check("연속거래: 거래 A의 VAN 요청 원문에 거래 B의 PIN이 없음(순서 반대 방향도 확인 — sanity)",
            !rawTextA.Contains(new string(pinB)));

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

    /// <summary>P23-7(PRD.md §2.3.1) — 설정값과 요청 #42가 일치하면 정상 처리(카드리딩까지 진행)돼야
    /// 한다. 값이 있는 경우의 "정상 통과"를 확인한다.</summary>
    private static async Task Scenario15_KioskIdMatchAllowsCardApproval()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore,
            kioskId: ConfiguredKioskId);
        r1.EnqueueCardReadOutcome(SuccessOutcome());
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "1234".ToCharArray();

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000", [42] = ConfiguredKioskId });
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("902614(#42 일치): 응답 성공(#7=000)", response.Telegram.Read(7) == "000");
        Check("902614(#42 일치): 카드리딩이 정상적으로 시도됨", r1.CardReadCallCount > 0);
    }

    /// <summary>P23-7(PRD.md §2.3.1) — 설정값과 다른 #42는 카드 리딩을 시작하기도 전에 E06으로
    /// 거부돼야 한다(사용자가 카드를 대는 헛수고를 막는다).</summary>
    private static async Task Scenario16_KioskIdMismatchRejectsBeforeCardReading()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore,
            kioskId: ConfiguredKioskId);
        r1.EnqueueCardReadOutcome(SuccessOutcome()); // 도달하면 안 되므로 호출되면 큐만 남는다(검증은 CallCount로).

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000", [42] = "DIFFERENTKIOSK0001" });
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("902614(#42 불일치): E06 거부", response.Telegram.Read(7) == "E06");
        Check("902614(#42 불일치): 카드 리딩을 시도하지 않음", r1.CardReadCallCount == 0);
        Check("902614(#42 불일치): VAN까지 도달하지 않음", vanRelay.LastRequest == null);
    }

    /// <summary>P23-7(PRD.md §2.3.2, 2026-09-02 재확정) — 설정값이 빈 상태에서 실제 값이 담긴 #42가
    /// 오면(빈 문자열이 아닌 이상) 항상 불일치로 간주해 E06으로 거부한다. 별도의 "설정값이 비어
    /// 있으면" 특수 분기가 없다는 것 자체를 확인한다 — 이 시나리오가 실패한다면 코드에 그런 특수
    /// 분기가 잘못 들어간 것이다.</summary>
    private static async Task Scenario17_KioskIdEmptyConfiguredRejects()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore,
            kioskId: ""); // 설정값 미입력(§2.3 기본값)

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000", [42] = "SOMEKIOSKID00000001" });
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("902614(설정값 빈 값): E06 거부", response.Telegram.Read(7) == "E06");
        Check("902614(설정값 빈 값): 카드 리딩을 시도하지 않음", r1.CardReadCallCount == 0);
    }

    /// <summary>개선권장 4/5(CP2 Opus 리뷰) — 설정값은 정상인데 수신값(#42)이 전체 공백(정규화 후 빈
    /// 문자열, <see cref="PosField.Trim"/>)인 경우. 개선권장 4 수정 전에는 "빈 문자열끼리
    /// 일치"로 판정되어 통과했지만(loophole), 수정 후에는 "둘 중 하나라도 빈 값이면 무조건 거부"가
    /// 먼저 걸려 E06으로 거부되어야 한다.</summary>
    private static async Task Scenario18_KioskIdReceivedAllSpacesRejectedEvenIfConfiguredValid()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore,
            kioskId: ConfiguredKioskId);

        // autoFillKioskId: false — #42를 의도적으로 채우지 않는다(전체 space 패딩 -> Read가 빈
        // 문자열을 돌려줌).
        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" }, autoFillKioskId: false);
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("902614(설정값 정상 + 수신값 빈 값): E06 거부(개선권장 4 loophole 수정 확인)", response.Telegram.Read(7) == "E06");
        Check("902614(설정값 정상 + 수신값 빈 값): 카드 리딩을 시도하지 않음", r1.CardReadCallCount == 0);
    }

    /// <summary>Phase 25 P25-9 CP3 Opus 리뷰 개선권장 F2(2026-09-03) — memory-clear-test는 SecureClear
    /// 원시 함수 자체가 지운다는 것만 증명하고, PaymentOrchestrator가 그 클리어를 실제로 호출한다는
    /// 것은 증명하지 못한다는 사각지대가 지적됐다(재현: PaymentOrchestrator.RunCardTransactionAsync의
    /// `roundResult?.CardData?.Dispose();` 호출을 통째로 지워도 memory-clear-test와 이 하네스의
    /// 기존 시나리오 전부가 그대로 통과했다). 이 시나리오는 그 사각지대를 메운다 — SuccessOutcome()
    /// 헬퍼를 거치지 않고 CardReadData를 직접 만들어 참조를 들고 있다가, 거래가 끝난 뒤 **그 인스턴스
    /// 자체**(fake 경계 안에서 실제로 오케스트레이터에 전달된 것과 동일한 참조)의 19개 필드가 전부
    /// NUL인지 확인한다. fake 경계 안쪽만 검사 가능하다는 기존 한계는 여전하지만(fake는 참조를 그대로
    /// 돌려주므로 이 검사가 가능하다), 최소한 "오케스트레이터가 Dispose()를 실제로 호출하는가"는
    /// 이 시나리오가 담당한다.</summary>
    private static async Task Scenario19_CardApprovalDisposesCardDataAfterTransaction()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);

        var cardData = new CardReadData(
            transactionType: "A".ToCharArray(), keyVersion: "01".ToCharArray(), tc: "TC0001".ToCharArray(),
            moduleId: "MODULE0001".ToCharArray(), fallbackCode: "0".ToCharArray(),
            amount: "000000000001000".ToCharArray(), cardNumber: "9412345678901234".ToCharArray(),
            encryptionMarker: "ENC".ToCharArray(), wcc: "I".ToCharArray(), encryptedData: "ENCRYPTEDDATA0001".ToCharArray(),
            encryptedDataLengthText: "017".ToCharArray(), emvEncodingMethod: "B".ToCharArray(),
            emvEncodedData: "EMV0001".ToCharArray(), readerAuthId: "READERAUTH000001".ToCharArray(),
            readerSerialEncryptionMarker: "NOE".ToCharArray(), readerSerial: "SERIAL0001".ToCharArray(),
            readerEncryptionInfo: "READERENCRYPTINFO001".ToCharArray(), tc3: "TC30001".ToCharArray(),
            payOnCertifyCode: "PAYONCERT00000000000000000001".ToCharArray());

        char[][] allFields =
        {
            cardData.TransactionType, cardData.KeyVersion, cardData.Tc, cardData.ModuleId, cardData.FallbackCode,
            cardData.Amount, cardData.CardNumber, cardData.EncryptionMarker, cardData.Wcc, cardData.EncryptedData,
            cardData.EncryptedDataLengthText, cardData.EmvEncodingMethod, cardData.EmvEncodedData,
            cardData.ReaderAuthId, cardData.ReaderSerialEncryptionMarker, cardData.ReaderSerial,
            cardData.ReaderEncryptionInfo, cardData.Tc3, cardData.PayOnCertifyCode,
        };

        r1.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", cardData));
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "1234".ToCharArray();

        var request = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram response = (PosResponseTelegram)await orchestrator.ProcessAsync(request).ConfigureAwait(false);

        Check("Scenario19: 거래 자체는 정상 성공(#7=000, 전제 확인)", response.Telegram.Read(7) == "000");

        bool allCleared = true;
        foreach (char[] field in allFields)
        {
            foreach (char c in field)
            {
                if (c != '\0')
                {
                    allCleared = false;
                    break;
                }
            }
        }

        Check("Scenario19: 거래 종료 후 PaymentOrchestrator가 CardReadData.Dispose()를 실제로 호출함(19개 필드 전부 NUL)", allCleared);
    }

    /// <summary>
    /// Phase 26 P26-1(PRD.md §3.4/§4.13/§7.1) — 응답 전문에서 카드리딩·PIN 필드(#45/#46/#51/#53)가
    /// 지워지는지를 검증한다. <see cref="PosResponseTelegram.Relay"/>와 <see
    /// cref="PosResponseTelegram.BuildFailure"/> 양쪽 경로 모두 확인한다.
    ///
    /// ★ 착수 전 사전 조사에서 발견한 유출 — 이 시나리오의 "실패 경로" 부분은 <c>ClearCardReadingFields</c>
    /// 를 <c>BuildFailure</c>에 넣기 전에는 통과하지 못했을 것이다. 옛 코드는 <c>#51</c>만 지웠으므로
    /// <c>#45</c>(복호화 정보)/<c>#46</c>(암호화된 카드정보)/<c>#53</c>(EMV DATA)이 카드 리딩까지 마친
    /// 뒤 VAN 통신 실패로 응답을 합성하는 경로에서 그대로 POS로 되돌아갔다 — 이 시나리오가 그 유출의
    /// 재현·차단을 검증한다.
    /// </summary>
    private static async Task Scenario20_ResponseCardReadingFieldsAreCleared()
    {
        if (!PosSchemaRegistry.TryResolve("902614", out PosTelegramSchema? schema902614) || schema902614 is null)
        {
            Check("P26-1: 902614 스키마 해석(전제조건)", false);
            return;
        }

        int[] cardReadingFields = { 45, 46, 51, 53 };

        // --- 1) relay 성공 경로 ---
        var orchestratorSuccess = BuildOrchestrator(out var r1s, out var r2s, out var presenterS, out var gateS, out var vanRelayS, out var lastTransactionResponseStoreS);
        r1s.EnqueueCardReadOutcome(SuccessOutcome(wcc: "I"));
        presenterS.FirePinEnteredSynchronouslyOnChangeState = true;
        presenterS.PinToFireSynchronously = "9999".ToCharArray();

        var requestSuccess = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram responseSuccess = (PosResponseTelegram)await orchestratorSuccess.ProcessAsync(requestSuccess).ConfigureAwait(false);

        Check("P26-1(성공): 응답 성공(#7=000, 전제조건)", responseSuccess.Telegram.Read(7) == "000");
        foreach (int fieldNumber in cardReadingFields)
        {
            Check($"P26-1(성공): 응답 #{fieldNumber} 값이 빈 문자열", responseSuccess.Telegram.Read(fieldNumber) == "");
        }

        // 바이트 단위 확인 — 필드 구간이 전부 0x20(space)인지. POSITION/길이는 하드코딩하지 않고
        // 스키마에서 직접 읽는다.
        byte[] rawSuccessBody = responseSuccess.Telegram.ToBody();
        foreach (int fieldNumber in cardReadingFields)
        {
            PosField field = schema902614[fieldNumber];
            bool allSpace = true;
            bool hasNul = false;
            for (int i = field.Position; i < field.EndPosition; i++)
            {
                if (rawSuccessBody[i] != (byte)' ') allSpace = false;
                if (rawSuccessBody[i] == 0x00) hasNul = true;
            }
            Check($"P26-1(성공): 응답 #{fieldNumber} 원문 바이트({field.Length}바이트)가 전부 0x20", allSpace);
            Check($"P26-1(성공): 응답 #{fieldNumber} 원문 바이트에 0x00 없음", !hasNul);
        }

        // 다른 필드(#43/#44/#48/#50)는 그대로 남아 있어야 한다 — 요청 쪽에 실제로 채워진 값과 비교한다
        // (스텁이 요청을 clone하므로 값이 같아야 한다).
        Check("P26-1(성공): 응답 #43 불변(요청과 동일)", responseSuccess.Telegram.Read(43) == requestSuccess.Telegram.Read(43));
        Check("P26-1(성공): 응답 #44 불변(요청과 동일)", responseSuccess.Telegram.Read(44) == requestSuccess.Telegram.Read(44));
        Check("P26-1(성공): 응답 #48 불변(요청과 동일)", responseSuccess.Telegram.Read(48) == requestSuccess.Telegram.Read(48));
        Check("P26-1(성공): 응답 #50 불변(요청과 동일)", responseSuccess.Telegram.Read(50) == requestSuccess.Telegram.Read(50));

        // 길이/구조 불변. #0(전문 길이)은 본문 밖 헤더라 스키마에 없다(PosCommonHeader 참고) — 대신
        // 본문에 실제로 있는 #1(업무 구분, 고정값 "IGN")로 헤더 앞부분이 어긋나지 않았는지 확인한다.
        Check("P26-1(성공): 응답 전체 길이 1500 불변", rawSuccessBody.Length == schema902614.TotalLength);
        Check("P26-1(성공): 응답 #1 불변(요청과 동일, 고정값 \"IGN\")", responseSuccess.Telegram.Read(1) == requestSuccess.Telegram.Read(1));

        // --- 2) 실패 경로(VAN 통신 실패, Scenario7과 같은 방식) — 카드 리딩+PIN 입력까지 끝낸 뒤 실패.
        var orchestratorFailure = BuildOrchestrator(out var r1f, out var r2f, out var presenterF, out var gateF, out var vanRelayF, out var lastTransactionResponseStoreF);
        r1f.EnqueueCardReadOutcome(SuccessOutcome(wcc: "I"));
        presenterF.FirePinEnteredSynchronouslyOnChangeState = true;
        presenterF.PinToFireSynchronously = "8888".ToCharArray();
        vanRelayF.SetNextOutcome(VanRelayOutcome.CommunicationFailure(VanFailureKind.CommunicationFailure, "P26-1 테스트용 통신 실패"));

        var requestFailure = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram responseFailure = (PosResponseTelegram)await orchestratorFailure.ProcessAsync(requestFailure).ConfigureAwait(false);

        Check("P26-1(실패): VAN 통신 실패 시 D02(전제조건)", responseFailure.Telegram.Read(7) == "D02");
        // 카드리딩+PIN이 실제로 채워진 뒤 실패했다는 전제 — vanRelayF에 도달한 요청(clone 대상)에
        // #46/#51이 채워져 있었는지 먼저 확인한다.
        Check("P26-1(실패): 실패 전 요청에 #46(카드정보)이 실제로 채워져 있었음(전제조건)",
            requestFailure.Telegram.Read(46) != "");
        Check("P26-1(실패): 실패 전 요청에 #51(PIN)이 실제로 채워져 있었음(전제조건)",
            requestFailure.Telegram.Read(51) == new string(presenterF.PinToFireSynchronously));

        foreach (int fieldNumber in cardReadingFields)
        {
            Check($"P26-1(실패): 실패 응답 #{fieldNumber} 값이 빈 문자열(이 시나리오가 유출 재현·차단을 검증)",
                responseFailure.Telegram.Read(fieldNumber) == "");
        }

        // --- 3) 800000 — 카드리딩 필드 4개 중 어느 것도 스키마에 없음(P17 스키마 검증으로 보장, 500바이트
        // 전문이라 43번대 필드 자체가 없다). 여기는 지시문 가정대로다.
        if (!PosSchemaRegistry.TryResolve("800000", out PosTelegramSchema? schema800000) || schema800000 is null)
        {
            Check("P26-1: 800000 스키마 해석(전제조건)", false);
        }
        else
        {
            Check("P26-1: 800000에는 #45/#46/#51/#53 필드 자체가 없음(P17 스키마 검증으로 이미 보장)",
                cardReadingFields.All(n => !schema800000.Fields.Any(f => f.Number == n)));
        }

        // --- 4) 501008 — 회귀 방지(2026-09-04, Task A 검증 중 발견 즉시 수정됨). 지시문 원안의
        // "501008/800000에는 필드 자체가 없다"는 전제가 800000에는 맞지만 501008에는 틀렸다 — 501008
        // 스키마에도 #45/#46/#51/#53 번호가 존재한다(카드리딩과 전혀 무관한 DigitalBudget 소유 업무
        // 필드: #45 납부 금액 수정 허용 유무, #46 연대 납부 대상 유무, #51 기 납부 금액, #53 분야(기능)
        // 코드 — NoticeInquirySchema.cs 참고). 필드 "번호"만 보고 지우면 501008의 정상 응답 데이터를
        // 침범하므로, <see cref="PosResponseTelegram.ClearCardReadingFields"/>는 이제 거래구분이
        // 902614일 때만 동작하도록 가드가 추가됐다(<c>CardReadingFieldsTransactionTypeCode</c>) — 이
        // 검사는 그 가드가 실제로 501008의 값을 보존하는지 확인하는 회귀 방지 테스트다.
        // PaymentOrchestrator 경로(오케스트레이터가 아직 이 필드들을 채우지 않음)로는 재현되지 않아
        // <see cref="PosResponseTelegram.Relay"/>를 직접 호출해 "VAN이 이미 채워 보낸 501008 응답"을
        // 흉내낸다(Scenario14가 TelegramLogRedactor를 직접 호출하는 것과 같은 패턴).
        if (!PosSchemaRegistry.TryResolve("501008", out PosTelegramSchema? schema501008) || schema501008 is null)
        {
            Check("P26-1: 501008 스키마 해석(전제조건)", false);
        }
        else
        {
            var noticeTelegram = PosTelegram.CreateEmpty(schema501008);
            noticeTelegram.Write(1, "IGN");
            noticeTelegram.Write(3, "0210");
            noticeTelegram.Write(4, "501008");
            noticeTelegram.Write(6, "C");
            noticeTelegram.Write(7, "000");
            // 카드리딩과 무관한 501008 고유 업무 데이터로 채운다(VAN/DigitalBudget이 실제로 채우는 값).
            noticeTelegram.Write(45, "Y"); // 납부 금액 수정 허용 유무
            noticeTelegram.Write(46, "N"); // 연대 납부 대상 유무
            noticeTelegram.Write(51, "5000"); // 기 납부 금액(N 15, 우측정렬 0패딩)
            noticeTelegram.Write(53, "010"); // 분야(기능) 코드
            byte[] noticeBody = noticeTelegram.ToBody();

            PosResponseTelegram relayedNotice = PosResponseTelegram.Relay(schema501008, noticeBody);

            Check("P26-1(회귀 방지, 902614 전용 가드): 501008 응답 #45(납부금액 수정 허용 유무, 카드리딩과 무관)가 " +
                  "지워지지 않고 그대로 남음", relayedNotice.Telegram.Read(45) == "Y");
            Check("P26-1(회귀 방지, 902614 전용 가드): 501008 응답 #46(연대 납부 대상 유무, 카드리딩과 무관)이 " +
                  "지워지지 않고 그대로 남음", relayedNotice.Telegram.Read(46) == "N");
            // #51은 N 타입(15)이라 Read()가 좌측 '0' 패딩을 보존한다(H-1 원칙, Scenario3 참고) —
            // "5000"이 아니라 "000000000005000"이 정답이다.
            Check("P26-1(회귀 방지, 902614 전용 가드): 501008 응답 #51(기 납부 금액, 카드리딩과 무관)이 " +
                  "지워지지 않고 그대로 남음", relayedNotice.Telegram.Read(51) == "000000000005000");
            Check("P26-1(회귀 방지, 902614 전용 가드): 501008 응답 #53(분야(기능) 코드, 카드리딩과 무관)이 " +
                  "지워지지 않고 그대로 남음", relayedNotice.Telegram.Read(53) == "010");
        }
    }

    /// <summary>
    /// Phase 26 P26-2(development_plan.md "완료 조건") — <see cref="LastTransactionResponseStore"/>를
    /// 오케스트레이터 배선 없이 단독으로 검증한다(이 Task의 범위 — 호출 지점 배선은 다음 체크포인트
    /// 이후). 다음을 확인한다:
    /// <list type="number">
    /// <item>저장 후 조회하면 저장한 값과 정확히 일치한다(문자열 필드 + BLOB 바이트 단위 + 시각).</item>
    /// <item>같은(고정) 키로 두 번 저장하면 행이 1개만 남고 최신값으로 갱신된다.</item>
    /// <item>DB 파일을 읽기 전용으로 만든 상태에서 저장을 시도하면 예외 없이 <c>false</c>를
    /// 반환한다(직전에 저장된 값은 그대로 남아 있어야 한다 — 실패한 쓰기가 기존 값을 깨지 않음).</item>
    /// <item>새 <see cref="LastTransactionResponseStore"/> 인스턴스(별도 커넥션)로 다시 열어도 값이
    /// 남아 있다 — 메모리 캐시가 아니라 디스크에 실제로 저장됐음을 확인한다(development_plan.md
    /// "앱을 껐다 켜도 행이 남아 있다"를 프로세스 재시작 없이 새 인스턴스로 흉내낸다).</item>
    /// </list>
    /// <see cref="LastTransactionResponseStore.Save"/>가 저장용 복사본을 <c>SecureClear.Clear</c>로
    /// 지운다는 것은 이 하네스로는 직접 관측할 수 없다(호출자가 넘긴 원본 배열은 그대로 둬야 하므로
    /// 복사본 쪽을 지우는데, 그 복사본은 메서드 내부 지역변수라 테스트가 참조를 들고 있을 수 없다) —
    /// development_plan.md 완료 조건이 명시한 대로 코드 리뷰로 확인한다(LastTransactionResponseStore.cs
    /// 클래스 요약 및 <c>Save</c>의 <c>finally</c> 블록 참고).
    /// </summary>
    private static void Scenario21_LastTransactionResponseStoreRoundTrip()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"p26-2-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new LastTransactionResponseStore(dbPath);

            byte[] bodyA = System.Text.Encoding.ASCII.GetBytes(new string('A', 1500));
            DateTime respondedAtA = new DateTime(2026, 9, 4, 10, 0, 0);
            bool savedA = store.Save("0EC0P26T0001", "902614", bodyA, respondedAtA);
            Check("P26-2: 첫 저장이 성공(true)을 반환", savedA);

            LastTransactionResponseRecord? loadedA = store.TryLoad();
            Check("P26-2: 저장 직후 조회 결과가 null이 아님", loadedA != null);
            if (loadedA != null)
            {
                Check("P26-2: 조회된 #9(관리번호)가 저장한 값과 일치", loadedA.ManagementNumber == "0EC0P26T0001");
                Check("P26-2: 조회된 거래구분 코드가 저장한 값과 일치", loadedA.TransactionTypeCode == "902614");
                Check("P26-2: 조회된 응답 원문(BLOB)이 저장한 바이트와 정확히 일치", loadedA.ResponseBody.SequenceEqual(bodyA));
                Check("P26-2: 조회된 응답 시각이 저장한 값과 일치", loadedA.RespondedAt == respondedAtA);
            }

            // 같은(고정) 키로 두 번째 저장 — 행이 늘지 않고 최신값으로 덮어써져야 한다.
            byte[] bodyB = System.Text.Encoding.ASCII.GetBytes(new string('B', 1500));
            DateTime respondedAtB = new DateTime(2026, 9, 4, 10, 5, 0);
            bool savedB = store.Save("0EC0P26T0002", "902614", bodyB, respondedAtB);
            Check("P26-2: 두 번째 저장도 성공(true)을 반환", savedB);

            int rowCountAfterSecondSave = CountRows(dbPath);
            Check("P26-2: 두 번째 저장 후에도 행이 정확히 1개(이력을 쌓지 않음)", rowCountAfterSecondSave == 1);

            LastTransactionResponseRecord? loadedB = store.TryLoad();
            Check("P26-2: 두 번째 저장 후 조회 결과가 null이 아님", loadedB != null);
            if (loadedB != null)
            {
                Check("P26-2: 두 번째 저장 후 조회된 #9가 최신값(0EC0P26T0002)", loadedB.ManagementNumber == "0EC0P26T0002");
                Check("P26-2: 두 번째 저장 후 조회된 BLOB이 최신값(B로 채운 1500바이트)와 일치", loadedB.ResponseBody.SequenceEqual(bodyB));
                Check("P26-2: 두 번째 저장 후 조회된 응답 시각이 최신값과 일치", loadedB.RespondedAt == respondedAtB);
            }

            // 새 인스턴스(별도 커넥션)로 다시 열어도 값이 남아 있음 — 디스크 영속 확인(앱 재시작 흉내).
            var reopenedStore = new LastTransactionResponseStore(dbPath);
            LastTransactionResponseRecord? loadedAfterReopen = reopenedStore.TryLoad();
            Check("P26-2: 새 인스턴스로 다시 열어도 값이 남아 있음(디스크 영속, 앱 재시작 흉내)",
                loadedAfterReopen != null && loadedAfterReopen.ManagementNumber == "0EC0P26T0002"
                    && loadedAfterReopen.ResponseBody.SequenceEqual(bodyB));

            // DB 파일을 읽기 전용으로 만든 뒤 저장을 시도한다 — 예외 없이 false를 반환해야 하고, 직전에
            // 저장된 값(bodyB)이 그대로 남아 있어야 한다(실패한 쓰기가 기존 값을 깨지 않음).
            // Microsoft.Data.Sqlite는 기본적으로 커넥션 풀링을 쓴다 — 앞의 Save 호출들이 이미 같은
            // 연결 문자열로 쓰기 가능한 네이티브 핸들을 풀에 남겨 두므로, 파일 속성만 바꾸면 풀에
            // 캐시된 핸들이 그대로 재사용돼 읽기 전용 속성이 무시된다(실측으로 확인됨) — 반드시
            // ClearAllPools()로 캐시된 핸들을 먼저 버려야 다음 열기가 실제 디스크 상태(읽기 전용)를
            // 마주친다.
            SqliteConnection.ClearAllPools();
            var readOnlyFileInfo = new FileInfo(dbPath) { IsReadOnly = true };
            try
            {
                bool savedWhileReadOnly = store.Save("0EC0P26T0003", "902614", System.Text.Encoding.ASCII.GetBytes(new string('C', 1500)), DateTime.Now);
                Check("P26-2: DB 파일이 읽기 전용이면 저장이 예외 없이 false를 반환", !savedWhileReadOnly);
            }
            finally
            {
                readOnlyFileInfo.IsReadOnly = false; // 임시 파일 정리(finally에서 삭제)가 가능하도록 원복.
                SqliteConnection.ClearAllPools(); // 다음 조회가 새 파일 상태(쓰기 가능)를 보도록 캐시도 정리.
            }

            LastTransactionResponseRecord? loadedAfterFailedSave = store.TryLoad();
            Check("P26-2: 읽기 전용 상태에서 저장 실패 후에도 직전 값(0EC0P26T0002)이 그대로 남아 있음",
                loadedAfterFailedSave != null && loadedAfterFailedSave.ManagementNumber == "0EC0P26T0002"
                    && loadedAfterFailedSave.ResponseBody.SequenceEqual(bodyB));
        }
        catch (Exception ex)
        {
            Check($"P26-2: 시나리오 실행 중 예상치 못한 예외 없음({ex.GetType().Name}: {ex.Message})", false);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    /// <summary>
    /// Phase 26 체크포인트 1 결함 F1(Task A) — <see cref="LastTransactionResponseStore"/>가
    /// <see cref="PaymentOrchestrator"/>에 실제로 배선돼 거래 1건마다 기록되는지 확인한다(Scenario21은
    /// 저장소 단독 검증이라 오케스트레이터 호출 지점 자체는 검증하지 못했다 — 체크포인트 1이 "검증
    /// 불가능"이라고 지적한 지점).
    ///
    /// 3전문 모두(501008/800000/902614) 저장되는지 확인한다(PRD §3.4 — "조회 대상은 #9로 식별되므로
    /// 원거래 종류와 무관"). 각 하위 검증은 <b>같은 orchestrator/store 인스턴스</b>를 이어 쓴다 — 고정
    /// 키 upsert이므로 다음 거래가 이전 값을 덮어쓰는 것도 함께 확인된다.
    /// </summary>
    private static async Task Scenario22_OrchestratorPersistsResponseToLastTransactionStore()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);

        // --- 1) 501008 — 카드리딩 없는 순수 중계. ---
        var noticeRequest = BuildRequest("501008", new Dictionary<int, string>());
        PosResponseTelegram noticeResponse = (PosResponseTelegram)await orchestrator.ProcessAsync(noticeRequest).ConfigureAwait(false);
        Check("P26-2(배선): 501008 응답 성공(전제조건)", noticeResponse.Telegram.Read(7) == "000");

        LastTransactionResponseRecord? noticeRecord = lastTransactionResponseStore.TryLoad();
        Check("P26-2(배선): 501008 처리 후 §7 저장소에 기록됨", noticeRecord != null);
        if (noticeRecord != null)
        {
            Check("P26-2(배선): 501008 저장된 #9가 요청과 일치", noticeRecord.ManagementNumber == noticeRequest.Read(9));
            Check("P26-2(배선): 501008 저장된 거래구분이 \"501008\"", noticeRecord.TransactionTypeCode == "501008");
            Check("P26-2(배선): 501008 저장된 응답 원문이 실제 응답과 바이트 단위로 일치",
                noticeRecord.ResponseBody.SequenceEqual(noticeResponse.Telegram.ToBody()));
        }

        // --- 2) 800000 — 카드리딩(BIN만) 후 중계. 이전 501008 기록을 덮어써야 한다(고정 키 upsert). ---
        r1.EnqueueCardReadOutcome(SuccessOutcome(cardNumber: "9412345678901234"));
        var cardInfoRequest = BuildRequest("800000", new Dictionary<int, string> { [15] = "1000" });
        PosResponseTelegram cardInfoResponse = (PosResponseTelegram)await orchestrator.ProcessAsync(cardInfoRequest).ConfigureAwait(false);
        Check("P26-2(배선): 800000 응답 성공(전제조건)", cardInfoResponse.Telegram.Read(7) == "000");

        LastTransactionResponseRecord? cardInfoRecord = lastTransactionResponseStore.TryLoad();
        Check("P26-2(배선): 800000 처리 후 §7 저장소가 최신값으로 갱신됨", cardInfoRecord != null);
        if (cardInfoRecord != null)
        {
            Check("P26-2(배선): 800000 저장된 #9가 요청과 일치(이전 501008 기록을 덮어씀)",
                cardInfoRecord.ManagementNumber == cardInfoRequest.Read(9));
            Check("P26-2(배선): 800000 저장된 거래구분이 \"800000\"", cardInfoRecord.TransactionTypeCode == "800000");
            Check("P26-2(배선): 800000 저장된 응답 원문이 실제 응답과 바이트 단위로 일치",
                cardInfoRecord.ResponseBody.SequenceEqual(cardInfoResponse.Telegram.ToBody()));
        }

        // --- 3) 902614 — 카드리딩+PIN 후 중계(P26-1이 이미 카드필드를 지운 뒤의 바이트가 저장돼야 함). ---
        r1.EnqueueCardReadOutcome(SuccessOutcome(wcc: "I"));
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "1234".ToCharArray();
        var approvalRequest = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        PosResponseTelegram approvalResponse = (PosResponseTelegram)await orchestrator.ProcessAsync(approvalRequest).ConfigureAwait(false);
        Check("P26-2(배선): 902614 응답 성공(전제조건)", approvalResponse.Telegram.Read(7) == "000");

        LastTransactionResponseRecord? approvalRecord = lastTransactionResponseStore.TryLoad();
        Check("P26-2(배선): 902614 처리 후 §7 저장소가 최신값으로 갱신됨", approvalRecord != null);
        if (approvalRecord != null)
        {
            Check("P26-2(배선): 902614 저장된 #9가 요청과 일치", approvalRecord.ManagementNumber == approvalRequest.Read(9));
            Check("P26-2(배선): 902614 저장된 거래구분이 \"902614\"", approvalRecord.TransactionTypeCode == "902614");
            // P26-1이 이미 지운 뒤의 바이트(카드필드 공백)가 그대로 저장돼야 한다 — 응답 객체와 저장된
            // 값이 정확히 같은 바이트인지 비교하는 것으로 이 전제도 함께 확인된다.
            Check("P26-2(배선): 902614 저장된 응답 원문이 실제 응답(카드필드 클리어 후)과 바이트 단위로 일치",
                approvalRecord.ResponseBody.SequenceEqual(approvalResponse.Telegram.ToBody()));
        }
    }

    /// <summary>테스트 전용 — 저장소 공개 API를 우회해 실제 행 개수를 직접 확인한다(이력이 쌓이지
    /// 않는다는 것을 저장소 자신의 <c>TryLoad</c>만으로는 확인할 수 없다 — <c>TryLoad</c>는 항상 1건
    /// 형태로만 돌려주므로 "행이 여러 개인데 그중 하나만 보여주는 것"과 구분되지 않는다).</summary>
    private static int CountRows(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM last_transaction_response;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                new FileInfo(path).IsReadOnly = false;
                File.Delete(path);
            }
        }
        catch
        {
            // 테스트 정리 실패는 무시한다(임시 파일이라 다음 실행에 영향 없음).
        }
    }

    /// <summary>
    /// P26-4(PRD.md §3.4.5/§3.4.6/§3.4.7) — 정상 거래(902614) 처리 후 같은 <c>#9</c>로 조회하면 저장된
    /// 원문을 바이트 단위로 그대로 되돌려주는지, 그 과정에서 부작용(카드 리딩/VAN 호출/알림창)이 전혀
    /// 없는지, 반복 조회해도 결과와 저장소가 그대로인지 확인한다.
    /// </summary>
    private static async Task Scenario23_StatusInquiryMatchReturnsStoredResponseVerbatim()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);
        r1.EnqueueCardReadOutcome(SuccessOutcome(wcc: "I"));
        presenter.FirePinEnteredSynchronouslyOnChangeState = true;
        presenter.PinToFireSynchronously = "1357".ToCharArray();

        var approvalRequest = BuildRequest("902614", new Dictionary<int, string> { [29] = "1000" });
        var approvalResponse = (PosResponseTelegram)await orchestrator.ProcessAsync(approvalRequest).ConfigureAwait(false);
        Check("P26-4: 사전 902614 거래 성공(전제조건)", approvalResponse.Telegram.Read(7) == "000");

        string managementNumber = approvalRequest.Read(9);
        byte[] originalBody = approvalResponse.Telegram.ToBody();

        int cardReadCallsBefore = r1.CardReadCallCount;
        int vanCallsBefore = vanRelay.CallCount;
        int presenterShowsBefore = presenter.History.Count(h => h.StartsWith("Show:", StringComparison.Ordinal));

        var inquiryResponse = (PosInquiryResponseTelegram)await orchestrator.ProcessAsync(BuildInquiryRequest(managementNumber)).ConfigureAwait(false);

        Check("P26-4: 조회 응답 #7 = 원거래 #7(000) relay", inquiryResponse.Read(7) == "000");
        Check("P26-4: 조회 응답 #9 = 요청과 동일(echo)", inquiryResponse.Read(9) == managementNumber);
        Check("P26-4: 조회 응답 개별부 #14(원거래구분) = 902614", inquiryResponse.Read(14) == "902614");
        Check("P26-4: 조회 응답 개별부 #15(원거래 응답 길이) = 1500", inquiryResponse.Read(15) == "1500");
        Check("P26-4: 조회 응답 꼬리가 원거래 응답과 바이트 단위로 완전히 동일",
            inquiryResponse.Tail != null && inquiryResponse.Tail.SequenceEqual(originalBody));

        byte[] frame = inquiryResponse.ToFrame();
        string outerLengthHeader = System.Text.Encoding.ASCII.GetString(frame, 0, 4);
        Check("P26-4: 바깥 프레임 길이 헤더 = 1580(80+1500)", outerLengthHeader == "1580");
        Check("P26-4: 꼬리 길이(원거래 자신의 총 길이) 1500 — 바깥 길이(1580)와 서로 다름(§3.4.5 주의 문단)",
            inquiryResponse.Tail != null && inquiryResponse.Tail.Length == 1500 && inquiryResponse.Tail.Length != 1580);
        Check("P26-4: 프레임 전체 바이트 수 = 4(길이헤더)+1580(본문) = 1584", frame.Length == 1584);

        Check("P26-4: 조회 처리 중 카드 리딩 호출 0회(부작용 없음)", r1.CardReadCallCount == cardReadCallsBefore);
        Check("P26-4: 조회 처리 중 VAN 호출 0회(부작용 없음)", vanRelay.CallCount == vanCallsBefore);
        Check("P26-4: 조회 처리 중 알림창 Show 0회(부작용 없음)",
            presenter.History.Count(h => h.StartsWith("Show:", StringComparison.Ordinal)) == presenterShowsBefore);

        LastTransactionResponseRecord? recordAfter = lastTransactionResponseStore.TryLoad();
        Check("P26-4: 조회 후에도 저장소가 원거래(902614) 그대로 유지됨(조회가 덮어쓰지 않음)",
            recordAfter != null && recordAfter.TransactionTypeCode == "902614" && recordAfter.ManagementNumber == managementNumber);

        // 같은 조회를 반복해도(연속 3회 — 이번 1회 + 아래 2회) 매번 같은 결과가 나오고 저장소가
        // 바뀌지 않아야 한다(PRD.md §3.4.7 "조회는 반복 가능", P26-4 완료 조건).
        var inquiryResponse2 = (PosInquiryResponseTelegram)await orchestrator.ProcessAsync(BuildInquiryRequest(managementNumber)).ConfigureAwait(false);
        Check("P26-4: 같은 조회를 2번째 반복해도 동일한 꼬리 바이트",
            inquiryResponse2.Tail != null && inquiryResponse2.Tail.SequenceEqual(originalBody));

        var inquiryResponse3 = (PosInquiryResponseTelegram)await orchestrator.ProcessAsync(BuildInquiryRequest(managementNumber)).ConfigureAwait(false);
        Check("P26-4: 같은 조회를 3번째 반복해도 동일한 꼬리 바이트",
            inquiryResponse3.Tail != null && inquiryResponse3.Tail.SequenceEqual(originalBody));

        LastTransactionResponseRecord? recordAfterRepeats = lastTransactionResponseStore.TryLoad();
        Check("P26-4: 반복 조회 후에도 저장소 값이 그대로(관리번호 동일, 조회가 덮어쓰지 않음)",
            recordAfterRepeats != null && recordAfterRepeats.ManagementNumber == managementNumber);
        Check("P26-4: 반복 조회 중에도 카드 리딩/VAN 호출이 늘지 않음",
            r1.CardReadCallCount == cardReadCallsBefore && vanRelay.CallCount == vanCallsBefore);
    }

    /// <summary>
    /// P26-4(PRD.md §3.4.5/§3.4.6) — 기록이 아예 없을 때와 <c>#9</c>가 불일치할 때 둘 다 <c>E07</c>로
    /// 응답하고, 개별부가 <c>000000</c>/<c>0000</c>으로 채워지며 본문 어디에도 <c>0x00</c>이 없는지
    /// 확인한다. 불일치 케이스에서는 저장소가 그 조회 때문에 바뀌지 않는지도 함께 확인한다.
    /// </summary>
    private static async Task Scenario24_StatusInquiryNoMatchYieldsE07()
    {
        var orchestrator = BuildOrchestrator(out var r1, out var r2, out var presenter, out var gate, out var vanRelay, out var lastTransactionResponseStore);

        // --- Case A: 기록이 아예 없음(첫 실행 상태) ---
        var noRecordResponse = (PosInquiryResponseTelegram)await orchestrator.ProcessAsync(BuildInquiryRequest("0EC0NORECORD")).ConfigureAwait(false);
        Check("P26-4(기록 없음): #7 = E07", noRecordResponse.Read(7) == "E07");
        Check("P26-4(기록 없음): 개별부 #14 = 000000", noRecordResponse.Read(14) == "000000");
        Check("P26-4(기록 없음): 개별부 #15 = 0000", noRecordResponse.Read(15) == "0000");
        Check("P26-4(기록 없음): 꼬리 없음(0바이트, 아예 붙이지 않음)", noRecordResponse.Tail == null);

        byte[] noRecordBody = noRecordResponse.BodyForLog();
        Check("P26-4(기록 없음): 본문 정확히 80바이트", noRecordBody.Length == 80);
        Check("P26-4(기록 없음): 본문 어디에도 0x00 없음", Array.IndexOf(noRecordBody, (byte)0x00) < 0);

        byte[] noRecordFrame = noRecordResponse.ToFrame();
        Check("P26-4(기록 없음): 프레임(길이헤더 포함) 어디에도 0x00 없음", Array.IndexOf(noRecordFrame, (byte)0x00) < 0);
        Check("P26-4(기록 없음): 프레임 길이 = 4+80 = 84", noRecordFrame.Length == 84);

        // --- Case B: 기록은 있지만 #9가 불일치 ---
        r1.EnqueueCardReadOutcome(SuccessOutcome(cardNumber: "9412345678901234"));
        var cardInfoRequest = BuildRequest("800000", new Dictionary<int, string> { [15] = "1000" });
        var cardInfoResponse = (PosResponseTelegram)await orchestrator.ProcessAsync(cardInfoRequest).ConfigureAwait(false);
        Check("P26-4(불일치 전제조건): 800000 거래 성공", cardInfoResponse.Telegram.Read(7) == "000");

        var mismatchResponse = (PosInquiryResponseTelegram)await orchestrator.ProcessAsync(BuildInquiryRequest("0ECMISMATCH1")).ConfigureAwait(false);
        Check("P26-4(#9 불일치): #7 = E07", mismatchResponse.Read(7) == "E07");
        Check("P26-4(#9 불일치): 개별부 000000/0000", mismatchResponse.Read(14) == "000000" && mismatchResponse.Read(15) == "0000");
        Check("P26-4(#9 불일치): 꼬리 없음", mismatchResponse.Tail == null);
        Check("P26-4(#9 불일치): 본문 어디에도 0x00 없음", Array.IndexOf(mismatchResponse.BodyForLog(), (byte)0x00) < 0);

        LastTransactionResponseRecord? recordUnaffected = lastTransactionResponseStore.TryLoad();
        Check("P26-4(#9 불일치): 저장소는 800000 거래 그대로(조회가 바꾸지 않음)",
            recordUnaffected != null && recordUnaffected.TransactionTypeCode == "800000");
    }

    /// <summary>P26-4 완료 조건 — <c>Services/Payment/</c> 안에 "E07" 리터럴이
    /// <c>PosResultCodeMapper</c> 한 곳에만 있는지 코드 자체를 스캔해 확인한다(develpment_plan.md
    /// P15-3/P17-4가 이미 확립한 grep 검증을 시나리오로도 재현 — 사람이 매번 grep을 다시 돌리지
    /// 않아도 회귀를 잡는다).</summary>
    private static void Scenario25_NoE07LiteralOutsidePosResultCodeMapper()
    {
        try
        {
            string paymentServicesDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Services", "Payment");
            paymentServicesDir = Path.GetFullPath(paymentServicesDir);

            if (!Directory.Exists(paymentServicesDir))
            {
                // 배포/실행 환경에 따라 소스 트리가 없을 수 있다(빌드 산출물만 있는 실행 위치) — 이
                // 경우 검증 자체를 건너뛴다(실패로 치지 않는다, 소스가 없으니 판단 불가).
                FileLogger.Info("[payment-flow-test][SKIP] P26-4: \"E07\" 리터럴 검사 — 소스 디렉터리를 찾을 수 없어 건너뜀");
                return;
            }

            var offendingFiles = new List<string>();
            foreach (string file in Directory.GetFiles(paymentServicesDir, "*.cs", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(file) == "PosResultCodeMapper.cs")
                    continue;

                if (File.ReadAllText(file).IndexOf("\"E07\"", StringComparison.Ordinal) >= 0)
                    offendingFiles.Add(Path.GetFileName(file));
            }

            Check("P26-4: Services/Payment/의 PosResultCodeMapper.cs 외에는 \"E07\" 리터럴이 없음",
                offendingFiles.Count == 0);
        }
        catch (Exception ex)
        {
            Check($"P26-4: \"E07\" 리터럴 검사 실행 중 예상치 못한 예외 없음({ex.GetType().Name}: {ex.Message})", false);
        }
    }
}
