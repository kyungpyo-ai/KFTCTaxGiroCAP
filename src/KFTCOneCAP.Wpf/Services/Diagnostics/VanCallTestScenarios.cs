using System;
using System.IO;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 20(docs/payment_relay/development_plan.md P20-3) 개발/회귀 검증용 테스트 하네스.
/// **최종 산출물이 아니다** — <c>App.xaml.cs</c>가 <c>--van-call-test</c> 인자로 실행될 때만
/// <see cref="RunAll"/>을 백그라운드에서 호출한다.
///
/// <b>왜 필요한가</b>: Phase 20의 결정 1(<c>App.xaml.cs</c>는 여전히 <see cref="StubVanRelayService"/>를
/// 쓴다)에 따라 <see cref="VanService"/>는 기본 실행 경로에서 한 번도 불리지 않는다. 이 하네스가 그것을
/// 직접 만들어 호출하는 유일한 지점이다 — 없으면 작성한 P/Invoke 코드가 한 번도 실행되지 않은 채 Phase가
/// 끝난다.
///
/// <b>VAN 서버가 아직 개발 중이라 접속이 되지 않는다</b>(2026-08-26/2026-08-31 확인). 그래서 이 하네스가
/// 검증하는 것은 "승인이 나는가"가 아니라 "호출이 성립하고 마샬링·버퍼가 안전하며 실패가 올바르게
/// 분류되는가"다. 모든 시나리오의 **기대 결과는 통신 실패(D01/D02)**이고, 그것이 예외 없이 일관되게
/// 나오는지를 확인한다.
/// </summary>
internal static class VanCallTestScenarios
{
    private static int _passCount;
    private static int _failCount;

    internal static async Task RunAll()
    {
        try
        {
            FileLogger.Info("[van-call-test] Phase 20 진단 검증 시작");

            await Scenario1_ThreeTelegramTypesCallWithoutCrash().ConfigureAwait(false);
            await Scenario2_RepeatedCallsStayConsistent().ConfigureAwait(false);

            FileLogger.Info($"[van-call-test] 완료 — 통과 {_passCount}건, 실패 {_failCount}건. " +
                "DLL 부재 시나리오(P20-3 완료조건 3)는 이 하네스가 자동으로 재현할 수 없다 — " +
                "KFTC_GIRO.dll을 출력 폴더에서 수동으로 치운 뒤 --van-call-test를 다시 실행해 확인할 것.");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[van-call-test] 하네스 자체 예외로 중단: {ex}");
        }
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            _passCount++;
            FileLogger.Info($"[van-call-test][OK] {name}");
        }
        else
        {
            _failCount++;
            FileLogger.Error($"[van-call-test][FAIL] {name}");
        }
    }

    /// <summary>3전문(501008/800000/902614) 각각의 정상 형식 요청을 실제로 <c>FNAISCRDVAN</c>에
    /// 넘긴다. 서버가 없으므로 기대 결과는 통신 실패(D01 또는 D02)이고, 확인하는 것은 "호출이 성립하고
    /// 크래시 없이 리턴하며 실패로 올바르게 분류되는가"다.</summary>
    private static async Task Scenario1_ThreeTelegramTypesCallWithoutCrash()
    {
        var van = new VanService();

        await CallOnce(van, "501008");
        await CallOnce(van, "800000");
        await CallOnce(van, "902614");
    }

    /// <summary>같은 호출을 10회 반복해 매번 같은 결과(통신 실패)가 나오고 프로세스가 살아 있는지
    /// 본다 — 버퍼 재사용/누적 결함이 있다면 여기서 반복 호출 중 결과가 흔들리거나 프로세스가
    /// 죽는 형태로 드러난다.</summary>
    private static async Task Scenario2_RepeatedCallsStayConsistent()
    {
        var van = new VanService();
        bool allConsistent = true;

        for (int i = 0; i < 10; i++)
        {
            VanRelayOutcome outcome = await CallOnce(van, "902614", logEachCall: false);
            if (outcome.Kind != VanRelayOutcomeKind.CommunicationFailure)
            {
                allConsistent = false;
                FileLogger.Warn($"[van-call-test] 902614 연속 호출 {i + 1}회차 결과가 기대(통신 실패)와 다름: {outcome.Kind}");
            }
        }

        Check("902614를 10회 연속 호출해도 매번 통신 실패로 일관되고 프로세스가 살아 있음", allConsistent);
    }

    private static async Task<VanRelayOutcome> CallOnce(VanService van, string transactionType, bool logEachCall = true)
    {
        PosRequestTelegram request = BuildRequest(transactionType);
        VanRelayOutcome outcome = await van.RelayAsync(request).ConfigureAwait(false);

        if (logEachCall)
        {
            FileLogger.Info($"[van-call-test] {transactionType} 호출 결과: Kind={outcome.Kind}, Detail={outcome.Detail}");
            // 서버가 없는 지금은 성공이 나오면 오히려 이상 상황이다(예기치 못한 로컬 응답 등) —
            // 그래도 하네스가 "크래시 없이 끝났다"는 사실 자체가 이 Phase가 확인해야 할 최소 기준이므로
            // Kind가 무엇이든 Check는 "호출이 성립했는가"만 본다.
            Check($"{transactionType} 호출이 크래시 없이 성립하고 실패가 분류됨 (Kind={outcome.Kind})",
                outcome.Kind == VanRelayOutcomeKind.CommunicationFailure);
        }

        return outcome;
    }

    private static int _managementSequence;

    /// <summary>SPEC 형식에 맞는 최소 요청 전문을 만든다. 카드 데이터 등 원캡 담당 필드는 채우지
    /// 않는다 — 이 하네스는 마샬링/호출 경계만 확인하고 필드 내용을 검증하지 않는다(relay 원칙과
    /// 같은 이유로, 이 하네스도 전문 내용에 대한 판단을 하지 않는다).</summary>
    private static PosRequestTelegram BuildRequest(string transactionType)
    {
        if (!PosSchemaRegistry.TryResolve(transactionType, out PosTelegramSchema? schema) || schema is null)
            throw new InvalidOperationException($"알 수 없는 거래구분: {transactionType}");

        PosTelegram telegram = PosTelegram.CreateEmpty(schema);
        telegram.Write(1, "IGN");
        telegram.Write(2, "095");
        telegram.Write(3, "0200");
        telegram.Write(4, transactionType);
        telegram.Write(6, "G");
        telegram.Write(9, "0EC0" + (++_managementSequence).ToString("D8"));

        PosRequestParseOutcome outcome = PosRequestTelegram.Parse(telegram.ToBody());
        if (!outcome.IsSuccess || outcome.Telegram is null)
            throw new InvalidOperationException($"테스트 요청 빌드 실패: {outcome.ErrorCode}");

        return outcome.Telegram;
    }
}
