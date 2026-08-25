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
        // (2026-08-25, Opus 검증 리뷰 L-1 수정) PRD §4.9의 120초 카드 입력 대기 상한이
        // PaymentOrchestrator.CardReadTimeout에서 실제로 SendCardReadCommandAsync까지 전달되는지
        // 확인한다 — 이 값이 잘못되면(예: 상수를 실수로 12초로 바꿔도) 다른 14개 시나리오는
        // 전부 그대로 통과하므로 이 확인이 없으면 조용한 회귀가 생길 수 있었다.
        Check(label, "카드 리딩 timeout 인자가 PRD §4.9의 120초로 전달됨", readerA.LastCardReadTimeout == TimeSpan.FromSeconds(120));
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

        internal TestContext(IReadOnlyList<IReaderEndpoint> endpoints, ReaderSettings settings)
        {
            IntegrityStore = new IntegrityCheckStore(TempDbPath());
            Presenter = new FakePaymentNoticePresenter();
            Gate = new FakeReaderSetupGate();
            VanService = new StubVanService();
            Orchestrator = new PaymentOrchestrator(endpoints, IntegrityStore, Presenter, Gate, VanService, () => settings);
        }

        private static string TempDbPath() =>
            Path.Combine(Path.GetTempPath(), $"kftc_payment_flow_test_{Guid.NewGuid():N}.db");
    }
}
