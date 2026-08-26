using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Reader;
using KFTCOneCAP.Wpf.Services.Payment;
using KFTCOneCAP.Wpf.Services.Reader;
using KFTCOneCAP.Wpf.Services.Settings;
using KFTCOneCAP.Wpf.Services.Storage;
using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-10) 개발/회귀 검증용 테스트 하네스.
/// **최종 산출물이 아니다** — <c>App.xaml.cs</c>가 <c>--payment-flow-test</c> 인자로 실행될 때만
/// <see cref="RunAll"/>을 백그라운드에서 호출한다.
///
/// <c>PaymentOrchestrator</c>(P15-6~P15-9)의 15개 시나리오를 실장비 없이 재현한다 —
/// <see cref="FakeReaderEndpoint"/>/<see cref="FakePaymentNoticePresenter"/>/
/// <see cref="FakeReaderSetupGate"/>를 꽂은 별도 <c>PaymentOrchestrator</c> 인스턴스를 시나리오마다
/// 새로 만들어 서로 격리한다(<c>App.Orchestrator</c>는 건드리지 않는다 — 그건 실제 하드웨어에
/// 연결돼 있다). 결과는 <see cref="FileLogger"/>에 OK/FAIL로 남고, 사람이 로그를 대조한다(다른
/// 개발용 하네스와 같은 방식).
/// </summary>
internal static class PaymentFlowTestScenarios
{
    internal static void RunAll()
    {
        FileLogger.Info("[payment-flow-test] 시작");
        RunAllAsync().GetAwaiter().GetResult();
        FileLogger.Info("[payment-flow-test] 전체 완료 — 로그에서 각 시나리오의 OK/FAIL을 대조할 것");
    }

    private static async Task RunAllAsync()
    {
        await RunScenario("1", Scenario1_NormalIcTwoReaders).ConfigureAwait(false);
        await RunScenario("2", Scenario2_Fallback).ConfigureAwait(false);
        await RunScenario("3", Scenario3_RetryCode12).ConfigureAwait(false);
        await RunScenario("4", Scenario4_OtherResponseCode).ConfigureAwait(false);
        await RunScenario("5", Scenario5_DllFailure).ConfigureAwait(false);
        await RunScenario("6", Scenario6_IntegrityOneSideFails).ConfigureAwait(false);
        await RunScenario("7", Scenario7_IntegrityBothFail).ConfigureAwait(false);
        await RunScenario("8", Scenario8_NoReaderConfigured).ConfigureAwait(false);
        await RunScenario("9", Scenario9_ReaderSetupOpen).ConfigureAwait(false);
        await RunScenario("10", Scenario10_UserCancel).ConfigureAwait(false);
        await RunScenario("11", Scenario11_Timeout).ConfigureAwait(false);
        await RunScenario("12", Scenario12_VanDeclinedAndCommFailure).ConfigureAwait(false);
        await RunScenario("13", Scenario13_FallbackRetryLimit).ConfigureAwait(false);
        await RunScenario("14", Scenario14_ConsecutiveTransactionsDoNotLeak).ConfigureAwait(false);
        await RunScenario("15", Scenario15_QueueSerializesOrchestratorCalls).ConfigureAwait(false);
        await RunScenario("16", Scenario16_CancelRightAfterShowIsNotLost).ConfigureAwait(false);
        await RunScenario("17", Scenario17_CancelDuringCardWait_InterruptWins).ConfigureAwait(false);
        await RunScenario("18", Scenario18_LateCancelAfterFlowClaimed_FlowWins).ConfigureAwait(false);
        await RunScenario("19", Scenario19_DeadlineExpiresBeforeCardArrives).ConfigureAwait(false);
        await RunScenario("20", Scenario20_CancelAndTimeoutRace_OnlyOneWins).ConfigureAwait(false);
        await RunScenario("21", Scenario21_DuplicateCancelSignals_OnlyOneOutcome).ConfigureAwait(false);
        await RunScenario("22", Scenario22_DeadlineExtendsOnFallbackAndRetry).ConfigureAwait(false);
        await RunScenario("23", Scenario23_ExtensionAmountIsExactlyThirtySeconds).ConfigureAwait(false);
        await RunScenario("24", Scenario24_CancelDuringVanIsRejected).ConfigureAwait(false);
        await RunScenario("25", Scenario25_RepeatedTransactionsDoNotLeak).ConfigureAwait(false);
        await RunScenario("26", Scenario26_AbnormalExitCleansUpOwnReadersOnly).ConfigureAwait(false);
    }

    private static async Task RunScenario(string number, Func<Task> scenario)
    {
        try
        {
            await scenario().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[payment-flow-test][{number}] 예외로 중단: {ex}");
        }
    }

    // ===================== 시나리오 1~5: 카드 리딩 라운드(P15-7) =====================

    private static async Task Scenario1_NormalIcTwoReaders()
    {
        const string label = "[payment-flow-test][1]";
        FileLogger.Info($"{label} 시작 — 정상 IC(2대, A가 먼저 00) → Approved, B에 0x60");

        var readerA = new FakeReaderEndpoint("COM 01");
        var readerB = new FakeReaderEndpoint("COM 02");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(50));
        readerB.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(800));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA, readerB }, TwoReadersConfigured());

        // P15-6 완료 조건 "금일 성공 이력이 있는 포트는 0x61/0x62가 나가지 않음"을 여기서 함께
        // 확인한다 — A는 금일 성공 이력을 미리 심어 무결성 체크를 건너뛰어야 하고(호출 0회),
        // B는 이력이 없으니 실제로 체크를 시도해야 한다(호출 1회, IntegrityOutcome 기본값=성공).
        ctx.IntegrityStore.Save(new IntegrityCheckRecord(DateTime.Now, "COM 01", true, "00", "MODULE-X", "AUTH-X"));

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-1")).ConfigureAwait(false);

        Check(label, "Approved(00) 응답", response.ResultCode == "00");
        Check(label, "B가 무효화됨(0x60)", readerB.InvalidationCount >= 1);
        Check(label, "A는 무효화되지 않음(카드데이터 사용)", readerA.InvalidationCount == 0);
        Check(label, "알림창이 Close로 끝남", ctx.Presenter.History.Count > 0 && ctx.Presenter.History[ctx.Presenter.History.Count - 1] == "Close");
        Check(label, "금일 성공 이력이 있는 A는 무결성 체크를 건너뜀(호출 0회)", readerA.IntegrityCheckCallCount == 0);
        Check(label, "이력이 없는 B는 무결성 체크를 실제로 수행함(호출 1회)", readerB.IntegrityCheckCallCount == 1);
        // (2026-08-25, Opus 검증 리뷰 L-1 수정) PRD §4.9의 120초 카드 입력 대기 상한이 실제로
        // SendCardReadCommandAsync까지 전달되는지 확인한다 — 이 값이 잘못되면(예: 상수를 실수로
        // 12초로 바꿔도) 다른 시나리오는 전부 그대로 통과하므로 이 확인이 없으면 조용한 회귀가 생길
        // 수 있었다. (Phase 16, P16-2) 이제 이 값은 거래 데드라인(PaymentDeadline.Remaining)에서
        // 파생되므로 거래 시작~카드 리딩 라운드 시작 사이에 흐른 극소 시간만큼 120초보다 아주 조금
        // 작다 — 정확히 120초가 아니라 "120초에 근접"으로 검증한다(1초 여유).
        Check(label, "카드 리딩 timeout 인자가 PRD §4.9의 120초에 근접함(데드라인에서 파생)",
            readerA.LastCardReadTimeout <= TimeSpan.FromSeconds(120) &&
            readerA.LastCardReadTimeout > TimeSpan.FromSeconds(119));
        // (2026-08-25, Opus 검증 리뷰 M-1 수정) 카드 리딩(0x2B)과 VAN 요청이 같은 거래 일시를 쓰는지
        // 확인한다 — 예전엔 VAN 단계에서 DateTime.Now를 다시 계산해 두 값이 벌어질 수 있었다.
        Check(label, "카드 리딩과 VAN 요청의 거래일시가 동일함(재계산 안 됨)",
            readerA.LastCardReadRequest != null && ctx.VanService.LastRequest != null &&
            readerA.LastCardReadRequest.TransactionDateTime == ctx.VanService.LastRequest.TransactionDateTime);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}, A무효화={readerA.InvalidationCount}, B무효화={readerB.InvalidationCount}");
    }

    private static async Task Scenario2_Fallback()
    {
        const string label = "[payment-flow-test][2]";
        FileLogger.Info($"{label} 시작 — FALLBACK(07→00) → MS 전환, 채택된 리더기에만 재요청(F), Approved");

        var readerA = new FakeReaderEndpoint("COM 01");
        var readerB = new FakeReaderEndpoint("COM 02");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("07"), TimeSpan.FromMilliseconds(30));
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()));
        readerB.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(500));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA, readerB }, TwoReadersConfigured());
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-2")).ConfigureAwait(false);

        Check(label, "Approved(00) 응답", response.ResultCode == "00");
        Check(label, "A가 2라운드 진행(카드리딩 2회)", readerA.CardReadCallCount == 2);
        Check(label, "B는 1라운드에서만 참여(1회)", readerB.CardReadCallCount == 1);
        Check(label, "2라운드 요청의 거래구분이 F", readerA.LastCardReadRequest?.TransactionTypeCode == "F");
        Check(label, "알림창이 FallbackCardRequest로 전환됨", ctx.Presenter.History.Contains("ChangeState:FallbackCardRequest"));

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}, A호출={readerA.CardReadCallCount}, B호출={readerB.CardReadCallCount}, 2라운드거래구분={readerA.LastCardReadRequest?.TransactionTypeCode}");
    }

    private static async Task Scenario3_RetryCode12()
    {
        const string label = "[payment-flow-test][3]";
        FileLogger.Info($"{label} 시작 — 응답코드 12 재시도 → 채택된 리더기에만 ARQo 재요청, Approved");

        var readerA = new FakeReaderEndpoint("COM 01");
        var readerB = new FakeReaderEndpoint("COM 02");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("12"), TimeSpan.FromMilliseconds(30));
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()));
        readerB.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(500));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA, readerB }, TwoReadersConfigured());
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-3")).ConfigureAwait(false);

        Check(label, "Approved(00) 응답", response.ResultCode == "00");
        Check(label, "A가 2라운드 진행", readerA.CardReadCallCount == 2);
        Check(label, "B는 1라운드에서만 참여", readerB.CardReadCallCount == 1);
        Check(label, "2라운드도 거래구분 ARQo 유지", readerA.LastCardReadRequest?.TransactionTypeCode == "ARQo");

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}, A호출={readerA.CardReadCallCount}, 2라운드거래구분={readerA.LastCardReadRequest?.TransactionTypeCode}");
    }

    private static async Task Scenario4_OtherResponseCode()
    {
        const string label = "[payment-flow-test][4]";
        FileLogger.Info($"{label} 시작 — 기타 응답코드(05) → ReaderResponseFailure, 0x60");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("05"));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-4")).ConfigureAwait(false);

        Check(label, "ReaderResponseFailure(10) 응답", response.ResultCode == "10");
        Check(label, "사유에 응답코드(05)가 실림", response.Message.Contains("05"));
        Check(label, "리더기 초기화(0x60) 나감", readerA.InvalidationCount >= 1);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}/{response.Message}");
    }

    private static async Task Scenario5_DllFailure()
    {
        const string label = "[payment-flow-test][5]";
        FileLogger.Info($"{label} 시작 — DLL 연동 실패 → ReaderDllFailure(응답코드 실패와 다른 코드)");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.DllCallFailure(-1100, "READER_ERR_PORT_NOT_FOUND", "테스트용 DLL 실패"));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-5")).ConfigureAwait(false);

        Check(label, "ReaderDllFailure(11) 응답", response.ResultCode == "11");
        Check(label, "응답코드 실패(10)와 다른 코드", response.ResultCode != "10");
        Check(label, "리더기 초기화(0x60) 나감", readerA.InvalidationCount >= 1);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}");
    }

    // ===================== 시나리오 6~9: 참여 후보/무결성/설정 화면 게이트(P15-6, P15-4) =====================

    private static async Task Scenario6_IntegrityOneSideFails()
    {
        const string label = "[payment-flow-test][6]";
        FileLogger.Info($"{label} 시작 — 무결성 한쪽 실패 → 성공한 쪽만 참여(N=1), 거래 계속");

        var readerA = new FakeReaderEndpoint("COM 01");
        var readerB = new FakeReaderEndpoint("COM 02") { IntegrityOutcome = IntegrityCheckSequenceOutcome.FromStatusFailure(StatusCommandOutcome.Timeout()) };
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA, readerB }, TwoReadersConfigured());
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-6")).ConfigureAwait(false);

        Check(label, "A/B 둘 다 무결성 체크 시도됨(둘 다 이력 없음)", readerA.IntegrityCheckCallCount == 1 && readerB.IntegrityCheckCallCount == 1);
        Check(label, "B는 카드 리딩에 참여하지 않음", readerB.CardReadCallCount == 0);
        Check(label, "A만으로 거래가 계속되어 승인됨(N=1)", response.ResultCode == "00");

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}, A카드리딩={readerA.CardReadCallCount}, B카드리딩={readerB.CardReadCallCount}");
    }

    private static async Task Scenario7_IntegrityBothFail()
    {
        const string label = "[payment-flow-test][7]";
        FileLogger.Info($"{label} 시작 — 무결성 양쪽 실패 → IntegrityCheckFailure, 알림창 안 뜸");

        var readerA = new FakeReaderEndpoint("COM 01") { IntegrityOutcome = IntegrityCheckSequenceOutcome.FromStatusFailure(StatusCommandOutcome.Timeout()) };
        var readerB = new FakeReaderEndpoint("COM 02") { IntegrityOutcome = IntegrityCheckSequenceOutcome.FromStatusFailure(StatusCommandOutcome.Timeout()) };

        var ctx = new TestContext(new IReaderEndpoint[] { readerA, readerB }, TwoReadersConfigured());
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-7")).ConfigureAwait(false);

        Check(label, "IntegrityCheckFailure(12) 응답", response.ResultCode == "12");
        Check(label, "알림창이 뜨지 않음", ctx.Presenter.History.Count == 0);
        Check(label, "카드 리딩이 전혀 시도되지 않음", readerA.CardReadCallCount == 0 && readerB.CardReadCallCount == 0);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}, 알림창History건수={ctx.Presenter.History.Count}");
    }

    private static async Task Scenario8_NoReaderConfigured()
    {
        const string label = "[payment-flow-test][8]";
        FileLogger.Info($"{label} 시작 — 양쪽 \"미사용\" → NoReaderConfigured, 리더기 명령 0건");

        var readerA = new FakeReaderEndpoint("COM 01");
        var readerB = new FakeReaderEndpoint("COM 02");
        var ctx = new TestContext(new IReaderEndpoint[] { readerA, readerB }, new ReaderSettings());

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-8")).ConfigureAwait(false);

        Check(label, "NoReaderConfigured(13) 응답", response.ResultCode == "13");
        Check(label, "리더기 명령이 전혀 나가지 않음",
            readerA.IntegrityCheckCallCount == 0 && readerA.CardReadCallCount == 0 &&
            readerB.IntegrityCheckCallCount == 0 && readerB.CardReadCallCount == 0);
        Check(label, "알림창이 뜨지 않음", ctx.Presenter.History.Count == 0);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}");
    }

    private static async Task Scenario9_ReaderSetupOpen()
    {
        const string label = "[payment-flow-test][9]";
        FileLogger.Info($"{label} 시작 — 설정 화면 열림 → ReaderSetupInProgress, 리더기 명령 0건. 닫힌 뒤엔 정상 진행");

        var readerA = new FakeReaderEndpoint("COM 01");
        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        ctx.Gate.IsOpen = true;

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-9")).ConfigureAwait(false);

        Check(label, "ReaderSetupInProgress(14) 응답", response.ResultCode == "14");
        Check(label, "리더기 명령이 전혀 나가지 않음", readerA.IntegrityCheckCallCount == 0 && readerA.CardReadCallCount == 0);
        Check(label, "알림창이 뜨지 않음", ctx.Presenter.History.Count == 0);

        ctx.Gate.IsOpen = false;
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()));
        PosPaymentResponse afterClose = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-9B")).ConfigureAwait(false);
        Check(label, "설정 화면 닫힌 뒤 정상 진행(승인)", afterClose.ResultCode == "00");

        FileLogger.Info($"{label} 완료 — 열림중응답={response.ResultCode}, 닫힌뒤응답={afterClose.ResultCode}");
    }

    // ===================== 시나리오 10~14: 취소/Timeout/VAN/정리(P15-8, P15-9) =====================

    private static async Task Scenario10_UserCancel()
    {
        const string label = "[payment-flow-test][10]";
        FileLogger.Info($"{label} 시작 — 사용자 취소(카드 대기 중) → UserCanceled, 대기 리더기 0x60");

        var readerA = new FakeReaderEndpoint("COM 01");
        var readerB = new FakeReaderEndpoint("COM 02");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromSeconds(1));
        readerB.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromSeconds(1));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA, readerB }, TwoReadersConfigured());

        _ = Task.Run(async () =>
        {
            await Task.Delay(200).ConfigureAwait(false);
            FileLogger.Info($"{label} 취소 통지 발생(카드 대기 중)");
            ctx.Presenter.FireCanceled();
        });

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-10")).ConfigureAwait(false);

        Check(label, "UserCanceled(20) 응답", response.ResultCode == "20");
        Check(label, "취소 시점에 대기 중이던 A/B 모두 초기화 통지", readerA.InvalidationCount >= 1 && readerB.InvalidationCount >= 1);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}, A무효화={readerA.InvalidationCount}, B무효화={readerB.InvalidationCount}");
    }

    private static async Task Scenario11_Timeout()
    {
        const string label = "[payment-flow-test][11]";
        FileLogger.Info($"{label} 시작 — Timeout → Timeout 응답, 대기 리더기 0x60");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Timeout());

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-11")).ConfigureAwait(false);

        Check(label, "Timeout(21) 응답", response.ResultCode == "21");
        Check(label, "리더기 초기화(0x60) 나감", readerA.InvalidationCount >= 1);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}");
    }

    private static async Task Scenario12_VanDeclinedAndCommFailure()
    {
        const string label = "[payment-flow-test][12]";
        FileLogger.Info($"{label} 시작 — VAN 거절 / VAN 통신 실패(서로 다른 코드여야 함)");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()));
        var ctxDeclined = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        ctxDeclined.VanService.SetNextResult(VanApprovalOutcome.Declined("05", "잔액부족(테스트)"));
        PosPaymentResponse declinedResponse = await ctxDeclined.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-12A")).ConfigureAwait(false);

        var readerB = new FakeReaderEndpoint("COM 01");
        readerB.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()));
        var ctxCommFail = new TestContext(new IReaderEndpoint[] { readerB }, OneReaderConfigured());
        ctxCommFail.VanService.SetNextResult(VanApprovalOutcome.CommunicationFailure("DLL 통신 실패(테스트)"));
        PosPaymentResponse commFailResponse = await ctxCommFail.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-12B")).ConfigureAwait(false);

        Check(label, "VAN 거절 응답코드=30", declinedResponse.ResultCode == "30");
        Check(label, "VAN 통신실패 응답코드=31", commFailResponse.ResultCode == "31");
        Check(label, "두 결과가 서로 다름", declinedResponse.ResultCode != commFailResponse.ResultCode);
        Check(label, "거절 시 리더기 초기화(0x60) 나감", readerA.InvalidationCount >= 1);
        Check(label, "통신실패 시 리더기 초기화(0x60) 나감", readerB.InvalidationCount >= 1);

        FileLogger.Info($"{label} 완료 — 거절={declinedResponse.ResultCode}, 통신실패={commFailResponse.ResultCode}");
    }

    private static async Task Scenario13_FallbackRetryLimit()
    {
        const string label = "[payment-flow-test][13]";
        FileLogger.Info($"{label} 시작 — 07 무한 반복 → 3라운드에서 RETRY_LIMIT 종료");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("07"));
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("07"));
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("07"));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-13")).ConfigureAwait(false);

        Check(label, "ReaderResponseFailure(10) 응답", response.ResultCode == "10");
        Check(label, "사유가 RETRY_LIMIT", response.Message == "RETRY_LIMIT");
        Check(label, "정확히 3라운드만 시도됨", readerA.CardReadCallCount == 3);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}/{response.Message}, 호출횟수={readerA.CardReadCallCount}");
    }

    private static async Task Scenario14_ConsecutiveTransactionsDoNotLeak()
    {
        const string label = "[payment-flow-test][14]";
        FileLogger.Info($"{label} 시작 — 연속 2건, 앞 거래 데이터/콜백이 뒤 거래에 섞이지 않음");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()));
        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());

        PosPaymentResponse first = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-14A")).ConfigureAwait(false);
        Check(label, "Canceled 구독자 수가 1번째 거래 종료 후 0으로 복귀", ctx.Presenter.CanceledSubscriberCount == 0);

        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("05"));
        PosPaymentResponse second = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-14B")).ConfigureAwait(false);

        Check(label, "1번째 거래 승인, txId 일치", first.ResultCode == "00" && first.TransactionId == "FLOW-14A");
        Check(label, "2번째 거래는 독립적으로 실패 처리됨, txId 일치", second.ResultCode == "10" && second.TransactionId == "FLOW-14B");
        Check(label, "Canceled 구독자 수가 2번째 거래 종료 후에도 0", ctx.Presenter.CanceledSubscriberCount == 0);

        FileLogger.Info($"{label} 완료 — 1번째={first.ResultCode}/{first.TransactionId}, 2번째={second.ResultCode}/{second.TransactionId}");
    }

    private static async Task Scenario15_QueueSerializesOrchestratorCalls()
    {
        const string label = "[payment-flow-test][15]";
        FileLogger.Info($"{label} 시작 — 동시 3건 요청 → 순차 처리, 리더기 명령이 겹치지 않음");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(200));
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(200));
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(200));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        var queue = new TransactionQueue(ctx.Orchestrator.ProcessAsync);

        var results = new ConcurrentQueue<string>();
        var completions = new List<TaskCompletionSource<bool>>();

        void EnqueueOne(string txId)
        {
            // RunContinuationsAsynchronously가 없으면 SetResult를 호출하는 스레드(큐의 워커 스레드)
            // 위에서 이 Task를 기다리던 continuation(아래 Task.WhenAll 재개 → queue.Stop() 호출)이
            // 그대로 인라인 실행될 수 있다 — 그러면 queue.Stop()의 워커 스레드 Join이 "자기 자신을
            // Join"하는 셈이 되어 항상 타임아웃까지 채우고서야 반환된다(실제로 최초 버전에서
            // 재현됨). 이 옵션으로 continuation을 스레드풀에 넘겨 워커 스레드를 즉시 돌려준다.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Add(tcs);
            queue.Enqueue(CreateRequest("1000", txId), response =>
            {
                results.Enqueue(response.TransactionId);
                tcs.SetResult(true);
            });
        }

        EnqueueOne("FLOW-15-A");
        EnqueueOne("FLOW-15-B");
        EnqueueOne("FLOW-15-C");

        await Task.WhenAll(completions.Select(t => t.Task)).ConfigureAwait(false);
        queue.Stop(TimeSpan.FromSeconds(5));

        string[] order = results.ToArray();
        Check(label, "3건 모두 완료", order.Length == 3);
        Check(label, "정확히 3번의 카드 리딩 호출(겹치지 않고 순차)", readerA.CardReadCallCount == 3);
        Check(label, "처리 순서가 접수 순서와 일치(큐가 직렬화)", order.Length == 3 && order[0] == "FLOW-15-A" && order[1] == "FLOW-15-B" && order[2] == "FLOW-15-C");

        FileLogger.Info($"{label} 완료 — 처리 순서: {string.Join(",", order)}");
    }

    // ===================== 시나리오 16: H-1 회귀 방지(체크포인트2 Opus 리뷰 추가분) =====================

    private static async Task Scenario16_CancelRightAfterShowIsNotLost()
    {
        const string label = "[payment-flow-test][16]";
        FileLogger.Info($"{label} 시작 — Show() 직후(최악의 타이밍) 취소해도 유실되지 않아야 함(H-1 회귀 방지)");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(50));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        ctx.Presenter.FireCanceledSynchronouslyOnShow = true;

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-16")).ConfigureAwait(false);

        // Canceled 구독이 Show() 호출보다 먼저 걸려 있어야만 이 취소가 잡힌다 — 순서가 반대라면
        // 구독자 0명에게 통지되어 유실되고, readerA는 그대로 진행돼 Approved가 나온다(수정 전 버그
        // 그대로 재현되는 신호).
        Check(label, "UserCanceled(20) 응답 — 구독이 Show()보다 먼저 걸려 있었다는 뜻", response.ResultCode == "20");

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}");
    }

    // ===================== 시나리오 17~25: Phase 16 경합/데드라인(P16-1~P16-4, P16-6) =====================
    //
    // 가짜 하네스로는 하드웨어급 진짜 "동시"를 재현할 수 없다 — 대신 지연 값을 조작해 "어느 쪽이
    // 먼저 게이트를 확정하는가"를 결정론적으로 재현한다(development_plan.md Phase 16 착수 전 전제와
    // 같은 한계). 데드라인 만료를 실제 120초 기다리지 않고 검증하기 위해 PaymentOrchestrator 생성자의
    // initialCardReadDeadline(운영 코드는 쓰지 않는 테스트 전용 주입)을 사용한다.

    private static async Task Scenario17_CancelDuringCardWait_InterruptWins()
    {
        const string label = "[payment-flow-test][17]";
        FileLogger.Info($"{label} 시작 — 카드 대기 중 취소(취소가 먼저) → UserCanceled, VAN 미진입");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(500));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        _ = Task.Run(async () =>
        {
            await Task.Delay(30).ConfigureAwait(false);
            ctx.Presenter.FireCanceled();
        });

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-17")).ConfigureAwait(false);

        Check(label, "UserCanceled(20) 응답", response.ResultCode == "20");
        Check(label, "VAN에 진입하지 않음(요청이 전달되지 않음)", ctx.VanService.LastRequest == null);
        Check(label, "대기 중이던 리더기가 무효화됨", readerA.InvalidationCount >= 1);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}");
    }

    private static async Task Scenario18_LateCancelAfterFlowClaimed_FlowWins()
    {
        const string label = "[payment-flow-test][18]";
        FileLogger.Info($"{label} 시작 — 카드 리딩이 먼저 확정된 뒤 도착한 취소는 결과를 바꾸지 않음");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(10));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        // 카드 리딩(10ms)이 끝나자마자 이 거래는 FlowResult로 확정되고 VAN(StubVanService, 1초 지연)
        // 이 시작된다 — 그 도중(300ms)에 취소를 보내도 이미 구독이 해제된 뒤라 통지 자체가 가지
        // 않는다. "선착순으로 이미 이긴 결과는 뒤늦은 취소에 흔들리지 않는다"는 성질을 검증한다.
        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);
            ctx.Presenter.FireCanceled();
        });

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-18")).ConfigureAwait(false);

        Check(label, "Approved(00) 응답 — 뒤늦은 취소가 결과를 바꾸지 못함", response.ResultCode == "00");
        Check(label, "거래 종료 후 구독자 수 0(뒤늦은 취소도 안전하게 무시됨)", ctx.Presenter.CanceledSubscriberCount == 0);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}");
    }

    private static async Task Scenario19_DeadlineExpiresBeforeCardArrives()
    {
        const string label = "[payment-flow-test][19]";
        FileLogger.Info($"{label} 시작 — 거래 데드라인이 카드 응답보다 먼저 만료 → Timeout, 추가 라운드 없음");

        var readerA = new FakeReaderEndpoint("COM 01");
        // 데드라인(50ms)보다 훨씬 긴 지연 — 카드가 응답하기 전에 데드라인이 반드시 먼저 만료된다.
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromSeconds(5));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured(), TimeSpan.FromMilliseconds(50));
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-19")).ConfigureAwait(false);

        Check(label, "Timeout(21) 응답", response.ResultCode == "21");
        Check(label, "리더기 초기화(0x60) 나감", readerA.InvalidationCount >= 1);
        Check(label, "추가 라운드가 시작되지 않음(호출 정확히 1회)", readerA.CardReadCallCount == 1);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}, 호출횟수={readerA.CardReadCallCount}");
    }

    private static async Task Scenario20_CancelAndTimeoutRace_OnlyOneWins()
    {
        const string label = "[payment-flow-test][20]";
        FileLogger.Info($"{label} 시작 — 취소와 데드라인 만료가 근접 → 1건만 확정, 0x60 중복 발사 없음");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromSeconds(5));

        // (2026-08-25, Phase 16 체크포인트 리뷰 L-1 수정) 예전엔 데드라인 40ms / 취소 50ms로 두어
        // **항상 Timeout이 이기도록** 배치돼 있었다 — 그러면 "둘 중 하나만 이긴다"가 아니라 사실상
        // Timeout 단독 경로만 검증하는 셈이라, 게이트가 없어도 통과했을 시나리오였다. 두 신호를
        // 같은 목표 시각에 쏘아 실제로 경쟁시킨다. 어느 쪽이 이기는지는 매 실행 달라질 수 있고,
        // 그것이 정상이다 — 검증 대상은 승자가 아니라 **결과가 정확히 1건**이라는 사실이다.
        var deadlineAndCancelAt = TimeSpan.FromMilliseconds(80);
        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured(), deadlineAndCancelAt);
        _ = Task.Run(async () =>
        {
            await Task.Delay(deadlineAndCancelAt).ConfigureAwait(false);
            ctx.Presenter.FireCanceled();
        });

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-20")).ConfigureAwait(false);
        // 0x60 발사는 게이트 확정과 별도로 백그라운드 Task.Run에서 일어난다(H-2 수정 유지) —
        // ProcessAsync가 반환된 시점에 그 작업이 아직 끝나지 않았을 수 있으므로, 검증을 위해
        // 짧게 기다린다(운영 코드에는 이런 대기가 없다 — 순수 테스트 동기화용).
        await Task.Delay(200).ConfigureAwait(false);

        Check(label, "UserCanceled 또는 Timeout 중 정확히 1건만 응답", response.ResultCode == "20" || response.ResultCode == "21");
        Check(label, "리더기가 정확히 한 번만 무효화됨(중복 0x60 없음)", readerA.InvalidationCount == 1);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}(경합 승자는 실행마다 달라질 수 있음), 무효화횟수={readerA.InvalidationCount}");
    }

    private static async Task Scenario21_DuplicateCancelSignals_OnlyOneOutcome()
    {
        const string label = "[payment-flow-test][21]";
        FileLogger.Info($"{label} 시작 — 취소 연타(버튼+ESC 동시 재현) → 응답 1건, 예외 없음");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromSeconds(5));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        _ = Task.Run(async () =>
        {
            await Task.Delay(30).ConfigureAwait(false);
            // 두 이벤트가 근접해 도착하는 것을 재현 — OnCanceled가 두 번 불려도 두 번째는
            // TryClaim 실패로 조용히 무시돼야 한다(예외가 나면 이 시나리오 자체가 FAIL로 중단됨).
            ctx.Presenter.FireCanceled();
            ctx.Presenter.FireCanceled();
        });

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-21")).ConfigureAwait(false);
        // 0x60 발사는 백그라운드 Task.Run이므로 응답 반환과 순서가 보장되지 않는다 — 검증을 위해
        // 짧게 기다린다(시나리오 20과 같은 이유).
        await Task.Delay(100).ConfigureAwait(false);

        Check(label, "UserCanceled(20) 응답 정확히 1건", response.ResultCode == "20");
        Check(label, "리더기가 정확히 한 번만 무효화됨(중복 0x60 없음)", readerA.InvalidationCount == 1);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}, 무효화횟수={readerA.InvalidationCount}");
    }

    private static async Task Scenario22_DeadlineExtendsOnFallbackAndRetry()
    {
        const string label = "[payment-flow-test][22]";
        FileLogger.Info($"{label} 시작 — 07→12→성공, 매 사용자 입력 단계마다 데드라인 연장 확인");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("07"), TimeSpan.FromMilliseconds(10));
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("12"), TimeSpan.FromMilliseconds(10));
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(100));

        // 초기 데드라인을 50ms로 극단적으로 짧게 주되, 각 라운드 자체는 그보다 짧게(10ms/10ms) 끝나
        // 연장이 전혀 없다면 3라운드째(100ms)에서 반드시 데드라인에 걸린다 — Approved로 끝난다는
        // 것 자체가 07/12 각각에서 +30초(운영 값) 연장이 실제로 일어났다는 증거다.
        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured(), TimeSpan.FromMilliseconds(50));
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-22")).ConfigureAwait(false);

        Check(label, "Approved(00) 응답 — 연장이 없었다면 3라운드에서 Timeout이었을 것", response.ResultCode == "00");
        Check(label, "정확히 3라운드 진행", readerA.CardReadCallCount == 3);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}, 라운드={readerA.CardReadCallCount}");
    }

    private static async Task Scenario23_ExtensionAmountIsExactlyThirtySeconds()
    {
        const string label = "[payment-flow-test][23]";
        FileLogger.Info($"{label} 시작 — 데드라인 연장량이 정확히 PRD §4.9의 +30초인지 라운드별 명령 타임아웃으로 검증");

        // (2026-08-25, Phase 16 체크포인트 리뷰 L-2로 교체) 원래 이 시나리오는 "데드라인 만료가 라운드
        // 대기 중과 겹침"이었는데, 시나리오 19와 **같은 Task.WhenAny 경쟁 메커니즘**을 타이밍만 바꿔
        // 반복하는 것이라 새로 확인되는 것이 없었다(원래 주석에도 그렇게 적혀 있었다). 대신 아직
        // 아무도 검증하지 않던 것을 본다 — **연장이 정확히 몇 초인가**. 시나리오 22는 "연장이
        // 일어났다"까지만 증명하므로, 상수를 30초에서 5초로 잘못 바꿔도 그대로 통과한다.
        //
        // 원리: Phase 16부터 리더기 명령 타임아웃은 거래 데드라인의 남은 시간에서 파생된다
        // (ClampCommandTimeout(deadline.Remaining)). 따라서 라운드 2의 타임아웃에서 라운드 1의
        // 타임아웃을 빼면 "그 사이 흐른 시간(음수 기여)"과 "연장량(양수 기여)"의 합이 되고, 라운드
        // 1이 아주 짧게 끝나도록 만들면 그 차이가 곧 연장량에 수렴한다.
        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("07"), TimeSpan.FromMilliseconds(5));
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(5));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured(), TimeSpan.FromSeconds(60));
        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-23")).ConfigureAwait(false);

        Check(label, "Approved(00) 응답", response.ResultCode == "00");
        Check(label, "라운드가 2회 기록됨", readerA.CardReadTimeouts.Count == 2);

        if (readerA.CardReadTimeouts.Count == 2)
        {
            TimeSpan delta = readerA.CardReadTimeouts[1] - readerA.CardReadTimeouts[0];
            // 라운드 1이 5ms 만에 끝나므로 delta는 30초보다 그만큼만 작다 — 1초 여유로 확인한다.
            Check(label, $"라운드 2의 데드라인이 라운드 1보다 약 30초 길다(실측 {delta.TotalSeconds:F2}초)",
                delta > TimeSpan.FromSeconds(29) && delta <= TimeSpan.FromSeconds(30));
            FileLogger.Info($"{label} 라운드별 명령 타임아웃: {readerA.CardReadTimeouts[0].TotalSeconds:F2}초 → {readerA.CardReadTimeouts[1].TotalSeconds:F2}초 (차이 {delta.TotalSeconds:F2}초)");
        }

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}");
    }

    private static async Task Scenario24_CancelDuringVanIsRejected()
    {
        const string label = "[payment-flow-test][24]";
        FileLogger.Info($"{label} 시작 — VAN 진입 후 취소 시도 → 거부됨(게이트 claim 실패), VAN 결과 그대로");

        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(10));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        // StubVanService의 고정 지연(1초) 도중(300ms)에 취소를 시도한다 — 이 시점엔 이미
        // Canceled 구독이 해제돼 있어야 한다(PRD §4.8/§5.3 VAN 통신 중 취소 불가 경계).
        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);
            ctx.Presenter.FireCanceled();
        });

        PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-24")).ConfigureAwait(false);

        Check(label, "Approved(00) 응답 — VAN 진입 후 취소가 결과를 바꾸지 못함", response.ResultCode == "00");
        Check(label, "VAN 요청이 실제로 전달됨", ctx.VanService.LastRequest != null);

        FileLogger.Info($"{label} 완료 — 응답={response.ResultCode}");
    }

    private static async Task Scenario25_RepeatedTransactionsDoNotLeak()
    {
        const string label = "[payment-flow-test][25]";
        FileLogger.Info($"{label} 시작 — 연속 20건 반복 → 구독자 0, 상태 누적 없음(P16-4)");

        var readerA = new FakeReaderEndpoint("COM 01");
        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());

        for (int i = 0; i < 20; i++)
        {
            readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()));
            PosPaymentResponse response = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", $"FLOW-25-{i}")).ConfigureAwait(false);

            if (response.ResultCode != "00")
            {
                Check(label, $"{i}번째 거래 승인", false);
                return;
            }

            if (ctx.Presenter.CanceledSubscriberCount != 0)
            {
                Check(label, $"{i}번째 거래 종료 후 구독자 수 0", false);
                return;
            }
        }

        Check(label, "20건 전부 승인, 매 거래 후 구독자 수 0", true);
        FileLogger.Info($"{label} 완료 — 20건 반복, 최종 구독자수={ctx.Presenter.CanceledSubscriberCount}");
    }

    private static async Task Scenario26_AbnormalExitCleansUpOwnReadersOnly()
    {
        const string label = "[payment-flow-test][26]";
        FileLogger.Info($"{label} 시작 — 결과 미확정 예외 종료 → 자기 리더기만 정리, 다음 거래는 오염되지 않음(H-1 회귀 방지)");

        // (2026-08-25, Phase 16 체크포인트 리뷰 H-1 회귀 방지) 거래가 결과를 확정하지 못한 채 예외로
        // 빠져나가는 경로를 재현한다. 이 경로에서 예전 구현은 두 가지가 잘못됐다:
        //   (1) 대기 중이던 리더기가 정리되지 않고 카드를 계속 기다리거나,
        //   (2) 뒤늦게 깨어난 데드라인 감시가 게이트를 Timeout으로 확정하는 데 **성공**한 뒤
        //       인스턴스 필드에서 정리 대상을 읽어 — 그때는 이미 다음 거래가 그 필드를 덮어썼을 수
        //       있으므로 — **다음 고객이 카드를 기다리는 리더기에 0x60을 쏴** 멀쩡한 거래를 깨뜨렸다.
        // 지금은 정리 대상이 거래별 TransactionScope에 담기고, finally가 게이트를 봉인한다.
        var readerA = new FakeReaderEndpoint("COM 01");
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.BusinessFailure("07"), TimeSpan.FromMilliseconds(10));

        var ctx = new TestContext(new IReaderEndpoint[] { readerA }, OneReaderConfigured());
        // 07 분기가 ChangeState(FallbackCardRequest)를 부르는 시점에 예외를 던진다 — 이때는 이미
        // 참여 리더기가 대기 목록에 올라가 있어, "정리 대상이 있는 상태에서의 비정상 종료"가 된다.
        ctx.Presenter.ThrowOnChangeState = true;

        bool threw = false;
        try
        {
            await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-26A")).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // 운영에서는 TransactionQueue의 catch가 이 예외를 InternalError 응답으로 바꾼다(P14-3).
            threw = true;
        }

        await Task.Delay(150).ConfigureAwait(false); // 백그라운드 0x60 발사를 기다린다(테스트 동기화용)

        int invalidationsAfterAbnormalExit = readerA.InvalidationCount;
        Check(label, "예외가 호출자(큐)까지 전파됨", threw);
        Check(label, "비정상 종료해도 자기 리더기는 정리됨(0x60 1회)", invalidationsAfterAbnormalExit == 1);

        // 이어서 **같은 Orchestrator/같은 리더기로** 정상 거래를 돌린다 — 거래 간 오염을 보려면
        // 반드시 같은 인스턴스여야 한다(다른 인스턴스를 쓰면 예전 구현의 인스턴스 필드가 애초에
        // 공유되지 않아 아무것도 검증하지 못한다). 앞 거래가 남긴 뒤늦은 확정/정리가 이 거래의
        // 카드 대기 중에 끼어들면 0x60이 한 번 더 나가고, 실제 하드웨어였다면 이 고객의 카드
        // 리딩이 조용히 깨졌을 것이다.
        ctx.Presenter.ThrowOnChangeState = false;
        readerA.EnqueueCardReadOutcome(CardReadCommandOutcome.Success("00", FakeCardData()), TimeSpan.FromMilliseconds(300));

        PosPaymentResponse next = await ctx.Orchestrator.ProcessAsync(CreateRequest("1000", "FLOW-26B")).ConfigureAwait(false);
        await Task.Delay(150).ConfigureAwait(false);

        Check(label, "다음 거래는 정상 승인됨", next.ResultCode == "00");
        Check(label, "다음 거래 동안 추가 0x60이 나가지 않음(앞 거래의 뒤늦은 정리에 오염되지 않음)",
            readerA.InvalidationCount == invalidationsAfterAbnormalExit);

        FileLogger.Info($"{label} 완료 — 비정상종료후 무효화={invalidationsAfterAbnormalExit}, 다음거래 응답={next.ResultCode}, 최종 무효화={readerA.InvalidationCount}");
    }

    // ===================== 공용 헬퍼 =====================

    private static PosPaymentRequest CreateRequest(string amount, string txId) =>
        PosPaymentRequest.Parse(PosMessageEncoding.Value.GetBytes($"PAY|{amount}|{txId}"));

    private static CardReadData FakeCardData() => new(
        "C", "01", "000000", "MODULE0001", "0", "000000001000", "1234567890123456",
        "ENC", "0", "DEADBEEF", "B", "EMVDATA", "AUTHID0000000001", "NOE",
        "SERIAL0001", "ENCRYPTIONINFO00000", "000000", "PAYONCODE0000000000000000000000");

    private static ReaderSettings TwoReadersConfigured() => new() { Port1 = "COM 01", Port2 = "COM 02" };

    private static ReaderSettings OneReaderConfigured() => new() { Port1 = "COM 01", Port2 = "미사용" };

    private static void Check(string label, string description, bool condition)
    {
        if (condition)
            FileLogger.Info($"{label} OK: {description}");
        else
            FileLogger.Error($"{label} FAIL: {description}");
    }

    /// <summary>시나리오마다 완전히 격리된 Orchestrator + 가짜 부품 묶음. 무결성 DB는 시나리오별로
    /// 새 임시 SQLite 파일을 써서 이전 시나리오의 "금일 성공 이력"이 다음 시나리오로 새지 않게 한다.</summary>
    private sealed class TestContext
    {
        internal PaymentOrchestrator Orchestrator { get; }

        internal FakePaymentNoticePresenter Presenter { get; }

        internal FakeReaderSetupGate Gate { get; }

        internal StubVanService VanService { get; }

        internal IntegrityCheckStore IntegrityStore { get; }

        internal TestContext(IReadOnlyList<IReaderEndpoint> endpoints, ReaderSettings settings, TimeSpan? initialCardReadDeadline = null)
        {
            IntegrityStore = new IntegrityCheckStore(TempDbPath());
            Presenter = new FakePaymentNoticePresenter();
            Gate = new FakeReaderSetupGate();
            VanService = new StubVanService();
            Orchestrator = new PaymentOrchestrator(endpoints, IntegrityStore, Presenter, Gate, VanService, () => settings, initialCardReadDeadline);
        }

        private static string TempDbPath() =>
            Path.Combine(Path.GetTempPath(), $"kftc_payment_flow_test_{Guid.NewGuid():N}.db");
    }
}
