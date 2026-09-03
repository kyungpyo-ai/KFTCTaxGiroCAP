using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Interop;
using KFTCOneCAP.Wpf.Protocol.KeyDownload;
using KFTCOneCAP.Wpf.Protocol.Reader;
using KFTCOneCAP.Wpf.Services.Reader;
using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 24(docs/operations/development_plan.md P24-5) 개발/회귀 검증용 테스트 하네스.
/// **최종 산출물이 아니다** — <c>App.xaml.cs</c>가 <c>--keydown-test</c> 인자로 실행될 때만
/// <see cref="RunAll"/>을 백그라운드에서 호출한다.
///
/// <b>왜 필요한가</b>: <see cref="KeyDownloadService"/>(P24-4)는 실장비·VAN 서버 없이는 한 번도
/// 실행된 적이 없다 — 이 하네스가 <see cref="FakeKeyDownloadReaderEndpoint"/>/
/// <see cref="FakeKeyDownloadVanClient"/>로 감싸 5단계 시퀀스 전체(성공 경로 + 실패 시나리오 7종)를
/// IPEK 소모 없이 돌린다. 특히 <c>PRD.md</c> §3.3의 바이트 slicing(②/③/④/⑤ 4건)을 **내용까지**
/// 대조하는 것이 이 하네스의 핵심 목적이다(development_plan.md P24-5 완료 조건).
/// </summary>
internal static class KeyDownloadTestScenarios
{
    private static int _passCount;
    private static int _failCount;

    internal static async Task RunAll()
    {
        try
        {
            FileLogger.Info("[keydown-test] Phase 24 진단 검증 시작");

            await Scenario1_SuccessPathCallsFiveStagesInOrderWithByteAccurateSlicing().ConfigureAwait(false);
            await Scenario2_StartBusinessFailureStopsBeforeServerAuth().ConfigureAwait(false);
            await Scenario3_AuthBusinessFailureStopsBeforeServerKeyBundling().ConfigureAwait(false);
            await Scenario4_ServerAuthCommunicationFailureStopsBeforeReaderAuth().ConfigureAwait(false);
            await Scenario5_ServerAuthNonSuccessResponseCodeStopsBeforeReaderAuth().ConfigureAwait(false);
            await Scenario6_ServerAuthDeviceReplacementResponseCodeIsFlagged().ConfigureAwait(false);
            await Scenario7_ServerKeyBundlingParseFailureStopsBeforeUsingKey().ConfigureAwait(false);
            await Scenario8_ServerAuthParseFailureStopsBeforeReaderAuth().ConfigureAwait(false);

            // ===== I-6(CP1 Opus 리뷰) — 리더기 Timeout/DllCallFailure/CommunicationError, [75] 실패 =====
            await Scenario9_ReaderTimeoutAtStartStopsImmediately().ConfigureAwait(false);
            await Scenario10_ReaderCommunicationErrorAtStartStopsImmediately().ConfigureAwait(false);
            await Scenario11_ReaderDllCallFailureAtStartStopsImmediately().ConfigureAwait(false);
            await Scenario12_UsingKeyBusinessFailureStopsAtFinalStage().ConfigureAwait(false);

            // ===== I-6 추가 지시 — P24-1/P24-2 프로토콜 계층(빌더/파서) 직접 검증 =====
            Scenario13_IsoRequestBuilder0100AssemblesExactly60BytesWithCorrectContent();
            Scenario14_IsoRequestBuilder0120AssemblesExactly572BytesWithCorrectContent();
            Scenario15_IsoResponseParser0110ParsesSuccessResponseCorrectly();
            Scenario16_IsoResponseParser0130ParsesSuccessResponseCorrectly();
            Scenario17_IsoResponseParser0110ParsesNonSuccessResponseCodeAtFullLength();
            Scenario18_ReaderKeyDownloadRequestBuilderAuthAssembles608BytesWithCorrectContent();
            Scenario19_ReaderKeyDownloadRequestBuilderUsingKeyAssembles144BytesWithCorrectContent();
            Scenario20_ReaderStartResponseParserParsesSuccessResponse46Bytes();
            Scenario21_ReaderStartResponseParserParsesTwoByteErrorResponse();
            Scenario22_ReaderAuthResponseParserParsesSuccessResponse558Bytes();
            Scenario23_ReaderAuthResponseParserParsesTwoByteErrorResponse();
            Scenario24_ReaderUsingKeyResponseParserParsesSuccessResponse12Bytes();
            Scenario25_ReaderUsingKeyResponseParserParsesTwoByteErrorResponse();

            // ===== 개선권장 #1(Phase 24 2차 Opus 리뷰) — 하네스 사각지대 보강. R-8-1(PRIMARY
            // BITMAP 검증)과 R-6([73]/[75] 비-ASCII 방어)은 위 Scenario13~25가 전부 "정상 값"만
            // 넣어 검증했을 뿐, 값이 SPEC과 어긋났을 때 실제로 ParseFailed가 되는지는 한 번도
            // 확인하지 않았다. =====
            Scenario26_IsoResponseParser0110FailsOnWrongPrimaryBitmap();
            Scenario27_IsoResponseParser0130FailsOnWrongPrimaryBitmap();
            Scenario28_ReaderStartResponseParserFailsOnNonAsciiByte();
            Scenario29_ReaderUsingKeyResponseParserFailsOnNonAsciiByte();

            FileLogger.Info($"[keydown-test] 완료 — 통과 {_passCount}건, 실패 {_failCount}건");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[keydown-test] 하네스 자체 예외로 중단: {ex}");
        }
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            _passCount++;
            FileLogger.Info($"[keydown-test][OK] {name}");
        }
        else
        {
            _failCount++;
            FileLogger.Error($"[keydown-test][FAIL] {name}");
        }
    }

    private static (KeyDownloadService Service, FakeKeyDownloadReaderEndpoint Reader, FakeKeyDownloadVanClient Van, List<string> CallLog)
        BuildService()
    {
        var callLog = new List<string>();
        var reader = new FakeKeyDownloadReaderEndpoint(callLog);
        var van = new FakeKeyDownloadVanClient(callLog);
        // 검증용 짧은 타임아웃 — 실장비가 없으므로 실제로 대기하지 않는다(Fake는 즉시 반환).
        var service = new KeyDownloadService(reader, van, TimeSpan.FromSeconds(1));
        return (service, reader, van, callLog);
    }

    /// <summary>성공 경로 — 5단계가 정확한 순서로 1회씩 호출되는지, PRD.md §3.3 slicing 4건이
    /// 바이트(내용) 단위로 일치하는지를 함께 검사한다(P24-4의 미확정 완료 조건도 여기서 확정).</summary>
    private static Task Scenario1_SuccessPathCallsFiveStagesInOrderWithByteAccurateSlicing()
    {
        var (service, reader, van, callLog) = BuildService();
        return RunSuccessAndAssert(service, reader, van, callLog);
    }

    private static async Task RunSuccessAndAssert(
        KeyDownloadService service, FakeKeyDownloadReaderEndpoint reader, FakeKeyDownloadVanClient van, List<string> callLog)
    {
        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("성공 경로 — Outcome.IsSuccess", outcome.IsSuccess);
        Check("성공 경로 — Stage가 UsingKey(마지막 단계)", outcome.Stage == KeyDownloadStage.UsingKey);
        Check("성공 경로 — 모듈ID가 마지막 [75] 응답의 모듈ID", outcome.ModuleId == "MODULE0001");

        Check("성공 경로 — [63] 1회", reader.StartCallCount == 1);
        Check("성공 경로 — 0100 1회", van.KeyDownloadCallCount == 1);
        Check("성공 경로 — [64] 1회", reader.AuthCallCount == 1);
        Check("성공 경로 — 0120 1회", van.KeyBundlingCallCount == 1);
        Check("성공 경로 — [65] 1회", reader.UsingKeyCallCount == 1);
        Check("성공 경로 — 5단계 호출 순서가 ①②③④⑤",
            callLog.Count == 5 &&
            callLog[0] == "①[63]" && callLog[1] == "②0100" && callLog[2] == "③[64]" &&
            callLog[3] == "④0120" && callLog[4] == "⑤[65]");

        // ===== PRD.md §3.3 slicing 4건 — 내용 비교 =====

        // ② 요청 P-28 = [73]의 키버전(2) + 모듈ID(10) = 12byte, 순서 그대로.
        string expectedP28 = reader.StartOutcome.KeyVersion + reader.StartOutcome.ModuleId;
        Check("② P-28 길이가 12", van.LastP28?.Length == 12);
        Check("② P-28 내용이 [73] 키버전+모듈ID와 순서까지 정확히 일치", van.LastP28 == expectedP28);

        // ③ [64] data = 0110 P-28(610)에서 앞 2byte(키버전)를 뗀 608byte, 그대로.
        string expectedAuthData = FakeKeyDownloadVanClient.DefaultResponse0110Payload.Substring(2);
        string actualAuthData = (reader.LastAuthHash ?? string.Empty) + (reader.LastAuthRnd ?? string.Empty) + (reader.LastAuthSign ?? string.Empty);
        Check("③ [64] data 길이가 608(64+32+512)",
            reader.LastAuthHash?.Length == 64 && reader.LastAuthRnd?.Length == 32 && reader.LastAuthSign?.Length == 512);
        Check("③ [64] data 내용이 0110 P-28에서 키버전만 뗀 나머지와 정확히 일치", actualAuthData == expectedAuthData);

        // ④ 요청 P-29 = [74]의 키버전(2) + 모듈ID(10) + 암호화데이터(512) = 524byte, 순서 그대로.
        string expectedP29 = reader.AuthOutcome.KeyVersion + reader.AuthOutcome.ModuleId + reader.AuthOutcome.EncryptedData;
        Check("④ P-29 길이가 524", van.LastP29?.Length == 524);
        Check("④ P-29 내용이 [74] 키버전+모듈ID+암호화데이터와 순서까지 정확히 일치", van.LastP29 == expectedP29);

        // ⑤ [65] data = 0130 P-29(146)에서 앞 2byte(키버전)를 뗀 144byte, 그대로.
        string expectedUsingKeyData = FakeKeyDownloadVanClient.DefaultResponse0130Payload.Substring(2);
        string actualUsingKeyData = (reader.LastUsingKeyEncryptedData ?? string.Empty) + (reader.LastUsingKeyMac ?? string.Empty);
        Check("⑤ [65] data 길이가 144(128+16)",
            reader.LastUsingKeyEncryptedData?.Length == 128 && reader.LastUsingKeyMac?.Length == 16);
        Check("⑤ [65] data 내용이 0130 P-29에서 키버전만 뗀 나머지와 정확히 일치", actualUsingKeyData == expectedUsingKeyData);
    }

    /// <summary>실패 1 — [73] 응답코드 "13"(키 미주입) → ②~⑤ 호출 안 됨.</summary>
    private static async Task Scenario2_StartBusinessFailureStopsBeforeServerAuth()
    {
        var (service, reader, van, callLog) = BuildService();
        reader.StartOutcome = KeyDownloadStartCommandOutcome.BusinessFailure("13", "01", "R", "V", string.Empty);

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패1 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패1 — Stage == Start", outcome.Stage == KeyDownloadStage.Start);
        Check("실패1 — ResponseCode == 13", outcome.ResponseCode == "13");
        Check("실패1 — Kind == ReaderBusinessFailure", outcome.Kind == KeyDownloadOutcomeKind.ReaderBusinessFailure);
        Check("실패1 — [63] 1회, 뒤 4단계 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 0 && reader.AuthCallCount == 0 &&
            van.KeyBundlingCallCount == 0 && reader.UsingKeyCallCount == 0);
        Check("실패1 — 호출 로그가 ①뿐", callLog.Count == 1 && callLog[0] == "①[63]");
    }

    /// <summary>실패 2 — [74] 응답코드 "10"(상호인증오류) → ④/⑤ 호출 안 됨.</summary>
    private static async Task Scenario3_AuthBusinessFailureStopsBeforeServerKeyBundling()
    {
        var (service, reader, van, callLog) = BuildService();
        reader.AuthOutcome = KeyDownloadAuthCommandOutcome.BusinessFailure("10", "01", "R", "V", "MODULE0001", new string('E', 512));

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패2 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패2 — Stage == Auth", outcome.Stage == KeyDownloadStage.Auth);
        Check("실패2 — ResponseCode == 10", outcome.ResponseCode == "10");
        Check("실패2 — Kind == ReaderBusinessFailure", outcome.Kind == KeyDownloadOutcomeKind.ReaderBusinessFailure);
        Check("실패2 — ①②③까지만 호출, ④/⑤ 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 1 && reader.AuthCallCount == 1 &&
            van.KeyBundlingCallCount == 0 && reader.UsingKeyCallCount == 0);
        Check("실패2 — 호출 로그가 ①②③", callLog.Count == 3 &&
            callLog[0] == "①[63]" && callLog[1] == "②0100" && callLog[2] == "③[64]");
    }

    /// <summary>실패 3 — 서버(0100) 통신 실패(nRet != 0에 해당) → ③~⑤ 호출 안 됨.</summary>
    private static async Task Scenario4_ServerAuthCommunicationFailureStopsBeforeReaderAuth()
    {
        var (service, reader, van, callLog) = BuildService();
        van.KeyDownloadOutcome = KeyDownloadVanCallOutcome.CommunicationFailure("FNAISCRDVAN 통신 실패(nRet=-1)");

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패3 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패3 — Stage == ServerAuth", outcome.Stage == KeyDownloadStage.ServerAuth);
        Check("실패3 — Kind == ServerCommunicationFailure", outcome.Kind == KeyDownloadOutcomeKind.ServerCommunicationFailure);
        Check("실패3 — ①②까지만 호출, ③~⑤ 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 1 && reader.AuthCallCount == 0 &&
            van.KeyBundlingCallCount == 0 && reader.UsingKeyCallCount == 0);
        Check("실패3 — 호출 로그가 ①②", callLog.Count == 2 && callLog[0] == "①[63]" && callLog[1] == "②0100");
    }

    /// <summary>실패 4 — 0110 응답 P-39가 "00"이 아닌 임의 값 → ③~⑤ 호출 안 됨.</summary>
    private static async Task Scenario5_ServerAuthNonSuccessResponseCodeStopsBeforeReaderAuth()
    {
        var (service, reader, van, callLog) = BuildService();
        van.KeyDownloadOutcome = KeyDownloadVanCallOutcome.NonSuccessResponseCode(
            FakeKeyDownloadVanClient.DefaultResponse0110Payload, "99");

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패4 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패4 — Stage == ServerAuth", outcome.Stage == KeyDownloadStage.ServerAuth);
        Check("실패4 — ResponseCode == 99", outcome.ResponseCode == "99");
        Check("실패4 — Kind == ServerNonSuccessResponseCode", outcome.Kind == KeyDownloadOutcomeKind.ServerNonSuccessResponseCode);
        Check("실패4 — IsDeviceReplacementRequired == false(395 아님)", !outcome.IsDeviceReplacementRequired);
        Check("실패4 — ①②까지만 호출, ③~⑤ 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 1 && reader.AuthCallCount == 0 &&
            van.KeyBundlingCallCount == 0 && reader.UsingKeyCallCount == 0);
    }

    /// <summary>실패 5 — 0110 응답 P-39 == "395" → "단말기 교체 요망"으로 표시되고 ③~⑤ 호출 안 됨.</summary>
    private static async Task Scenario6_ServerAuthDeviceReplacementResponseCodeIsFlagged()
    {
        var (service, reader, van, callLog) = BuildService();
        van.KeyDownloadOutcome = KeyDownloadVanCallOutcome.NonSuccessResponseCode(
            FakeKeyDownloadVanClient.DefaultResponse0110Payload, "395");

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패5 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패5 — Stage == ServerAuth", outcome.Stage == KeyDownloadStage.ServerAuth);
        Check("실패5 — ResponseCode == 395", outcome.ResponseCode == "395");
        Check("실패5 — IsDeviceReplacementRequired == true(단말기 교체 요망)", outcome.IsDeviceReplacementRequired);
        Check("실패5 — ①②까지만 호출, ③~⑤ 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 1 && reader.AuthCallCount == 0 &&
            van.KeyBundlingCallCount == 0 && reader.UsingKeyCallCount == 0);
    }

    /// <summary>실패 6 — 0130 응답 길이 부족(파싱 실패) → ⑤ 호출 안 됨.</summary>
    private static async Task Scenario7_ServerKeyBundlingParseFailureStopsBeforeUsingKey()
    {
        var (service, reader, van, callLog) = BuildService();
        van.KeyBundlingOutcome = KeyDownloadVanCallOutcome.ResponseParseFailure(
            "응답 형식 불일치(길이/ISO 개시문자/전문 TYPE 중 하나 이상 SPEC과 다름, 응답길이=196)");

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패6 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패6 — Stage == ServerKeyBundling", outcome.Stage == KeyDownloadStage.ServerKeyBundling);
        Check("실패6 — Kind == ServerResponseParseFailure", outcome.Kind == KeyDownloadOutcomeKind.ServerResponseParseFailure);
        Check("실패6 — ①~④까지 호출, ⑤ 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 1 && reader.AuthCallCount == 1 &&
            van.KeyBundlingCallCount == 1 && reader.UsingKeyCallCount == 0);
        Check("실패6 — 호출 로그가 ①②③④", callLog.Count == 4 &&
            callLog[0] == "①[63]" && callLog[1] == "②0100" && callLog[2] == "③[64]" && callLog[3] == "④0120");
    }

    /// <summary>실패 7 — 0110 응답 TYPE이 "ISO"가 아니거나 전문 TYPE이 다름(P24-1 파서가 이미 막는
    /// 경로, ResponseParseFailure로 대리) → ③~⑤ 호출 안 됨.</summary>
    private static async Task Scenario8_ServerAuthParseFailureStopsBeforeReaderAuth()
    {
        var (service, reader, van, callLog) = BuildService();
        van.KeyDownloadOutcome = KeyDownloadVanCallOutcome.ResponseParseFailure(
            "응답 형식 불일치(길이/ISO 개시문자/전문 TYPE 중 하나 이상 SPEC과 다름, 응답길이=660)");

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패7 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패7 — Stage == ServerAuth", outcome.Stage == KeyDownloadStage.ServerAuth);
        Check("실패7 — Kind == ServerResponseParseFailure", outcome.Kind == KeyDownloadOutcomeKind.ServerResponseParseFailure);
        Check("실패7 — ①②까지만 호출, ③~⑤ 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 1 && reader.AuthCallCount == 0 &&
            van.KeyBundlingCallCount == 0 && reader.UsingKeyCallCount == 0);
    }

    // ===================== I-6(CP1 Opus 리뷰) — 리더기 Timeout/DllCallFailure/CommunicationError,
    // [75] 실패 시나리오 보강. 기존 8개 시나리오가 전부 BusinessFailure(업무 실패 응답코드)만
    // 다뤘던 것을 보완한다. 뒤 단계가 없으므로 "그 자리에서 멈춘다"만 확인한다. =====================

    /// <summary>실패 8 — [63] 요청이 응답 없이 시간 초과(READER_EVENT_TIMEOUT) → ②~⑤ 호출 안 됨.</summary>
    private static async Task Scenario9_ReaderTimeoutAtStartStopsImmediately()
    {
        var (service, reader, van, callLog) = BuildService();
        reader.StartOutcome = KeyDownloadStartCommandOutcome.Timeout();

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패8 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패8 — Stage == Start", outcome.Stage == KeyDownloadStage.Start);
        Check("실패8 — Kind == ReaderDllFailure", outcome.Kind == KeyDownloadOutcomeKind.ReaderDllFailure);
        Check("실패8 — [63] 1회, 뒤 4단계 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 0 && reader.AuthCallCount == 0 &&
            van.KeyBundlingCallCount == 0 && reader.UsingKeyCallCount == 0);
        Check("실패8 — 호출 로그가 ①뿐", callLog.Count == 1 && callLog[0] == "①[63]");
    }

    /// <summary>실패 9 — [63] 요청이 통신 오류(READER_EVENT_LRC_ERROR/RECEIVE_ERROR/FRAME_STALL 대리)
    /// → ②~⑤ 호출 안 됨.</summary>
    private static async Task Scenario10_ReaderCommunicationErrorAtStartStopsImmediately()
    {
        var (service, reader, van, callLog) = BuildService();
        reader.StartOutcome = KeyDownloadStartCommandOutcome.CommunicationError(
            ReaderNames.ReaderEventTypeToString((int)ReaderEventType.READER_EVENT_LRC_ERROR));

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패9 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패9 — Stage == Start", outcome.Stage == KeyDownloadStage.Start);
        Check("실패9 — Kind == ReaderDllFailure", outcome.Kind == KeyDownloadOutcomeKind.ReaderDllFailure);
        Check("실패9 — [63] 1회, 뒤 4단계 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 0 && reader.AuthCallCount == 0 &&
            van.KeyBundlingCallCount == 0 && reader.UsingKeyCallCount == 0);
    }

    /// <summary>실패 10 — [63] Reader_SendCommand 자체 실패(포트 미오픈/BUSY 등, DllCallFailure) →
    /// ②~⑤ 호출 안 됨.</summary>
    private static async Task Scenario11_ReaderDllCallFailureAtStartStopsImmediately()
    {
        var (service, reader, van, callLog) = BuildService();
        reader.StartOutcome = KeyDownloadStartCommandOutcome.DllCallFailure(-1, "READER_ERR_PORT_NOT_FOUND", "포트 미오픈");

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패10 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패10 — Stage == Start", outcome.Stage == KeyDownloadStage.Start);
        Check("실패10 — Kind == ReaderDllFailure", outcome.Kind == KeyDownloadOutcomeKind.ReaderDllFailure);
        Check("실패10 — [63] 1회, 뒤 4단계 호출 안 됨",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 0 && reader.AuthCallCount == 0 &&
            van.KeyBundlingCallCount == 0 && reader.UsingKeyCallCount == 0);
    }

    /// <summary>실패 11 — 마지막 단계 [75] 응답코드가 업무 실패("11") → 뒤 단계가 없으므로 여기서
    /// 멈춘 채 실패로 끝나는지만 확인한다(①~⑤까지 전부 호출된다는 점이 앞 시나리오들과의 차이).</summary>
    private static async Task Scenario12_UsingKeyBusinessFailureStopsAtFinalStage()
    {
        var (service, reader, van, callLog) = BuildService();
        reader.UsingKeyOutcome = KeyDownloadUsingKeyCommandOutcome.BusinessFailure("11", string.Empty);

        KeyDownloadOutcome outcome = await service.RunAsync().ConfigureAwait(false);

        Check("실패11 — Outcome.IsSuccess == false", !outcome.IsSuccess);
        Check("실패11 — Stage == UsingKey", outcome.Stage == KeyDownloadStage.UsingKey);
        Check("실패11 — ResponseCode == 11", outcome.ResponseCode == "11");
        Check("실패11 — Kind == ReaderBusinessFailure", outcome.Kind == KeyDownloadOutcomeKind.ReaderBusinessFailure);
        Check("실패11 — ①~⑤ 전부 1회씩 호출(마지막 단계에서만 실패)",
            reader.StartCallCount == 1 && van.KeyDownloadCallCount == 1 && reader.AuthCallCount == 1 &&
            van.KeyBundlingCallCount == 1 && reader.UsingKeyCallCount == 1);
        Check("실패11 — 호출 로그가 ①②③④⑤",
            callLog.Count == 5 &&
            callLog[0] == "①[63]" && callLog[1] == "②0100" && callLog[2] == "③[64]" &&
            callLog[3] == "④0120" && callLog[4] == "⑤[65]");
    }

    // ===================== P24-1/P24-2 프로토콜 계층(빌더/파서) 직접 검증 =====================
    //
    // 위 시나리오들은 전부 IKeyDownloadReaderEndpoint/IKeyDownloadVanClient 경계에 fake를 꽂는 구조라
    // P24-1(Protocol/KeyDownload/ 서버 구간 ISO 전문 조립/파싱)과 P24-2(Protocol/Reader/KeyDownload*
    // 리더기 구간 전문 조립/파싱)의 실제 구현체가 이 하네스에서 한 번도 호출되지 않는다(CP1 Opus
    // 리뷰 지적 — C-1을 별도 scratchpad 프로그램으로 검증한 전례를 이 저장소 안에 재실행 가능한
    // 형태로 남긴다). 아래는 IKeyDownloadReaderEndpoint/IKeyDownloadVanClient를 거치지 않고 P24-1/
    // P24-2 클래스를 그대로 new/static 호출하는 순수 단위 검증이다.

    /// <summary>0100 요청이 정확히 60byte(3+9+4+16+10+6+12)로 조립되고, TEXT 개시문자/HEADER/전문
    /// TYPE/BITMAP/P-28이 순서까지 정확히 일치하는지 검증한다.</summary>
    private static void Scenario13_IsoRequestBuilder0100AssemblesExactly60BytesWithCorrectContent()
    {
        var timestamp = new DateTime(2026, 9, 2, 13, 5, 7);
        string p28 = "01" + "MODULE0001"; // 키버전(2) + 모듈ID(10) = 12byte
        byte[] request = IsoKeyDownloadRequestBuilder.BuildRequest0100(timestamp, p28);

        Check("P24-1 — 0100 요청 길이 60", request.Length == IsoKeyDownloadRequestBuilder.Request0100Length);
        string text = Encoding.ASCII.GetString(request);
        string expected = "ISO" + "023400052" + "0100" + "0220001000000000" + "0902130507" + "130507" + p28;
        Check("P24-1 — 0100 요청 내용이 ISO/HEADER/TYPE/BITMAP/일시/추적번호/P-28 순서까지 정확히 일치",
            text == expected);
    }

    /// <summary>0120 요청이 정확히 572byte(3+9+4+16+10+6+524)로 조립되고 내용이 일치하는지 검증한다.</summary>
    private static void Scenario14_IsoRequestBuilder0120AssemblesExactly572BytesWithCorrectContent()
    {
        var timestamp = new DateTime(2026, 9, 2, 23, 59, 1);
        string p29 = "01" + "MODULE0001" + new string('E', 512); // 키버전(2)+모듈ID(10)+암호화데이터(512)=524byte
        byte[] request = IsoKeyDownloadRequestBuilder.BuildRequest0120(timestamp, p29);

        Check("P24-1 — 0120 요청 길이 572", request.Length == IsoKeyDownloadRequestBuilder.Request0120Length);
        string text = Encoding.ASCII.GetString(request);
        string expected = "ISO" + "023400052" + "0120" + "0220000800000000" + "0902235901" + "235901" + p29;
        Check("P24-1 — 0120 요청 내용이 ISO/HEADER/TYPE/BITMAP/일시/추적번호/P-29 순서까지 정확히 일치",
            text == expected);
    }

    /// <summary>0110 정상 응답(660byte, 응답코드 "00")이 P-28/P-39로 정확히 파싱되는지 검증한다.</summary>
    private static void Scenario15_IsoResponseParser0110ParsesSuccessResponseCorrectly()
    {
        string p28 = "01" + new string('H', 64) + new string('R', 32) + new string('S', 512); // 610byte
        string frame = "ISO" + "023400052" + "0110" + "0220001002000000" + "0902130507" + "130507" + p28 + "00";
        byte[] data = Encoding.ASCII.GetBytes(frame);

        Check("P24-1 — 0110 응답 프레임 길이 660",
            data.Length == IsoKeyDownloadResponseParser.Response0110Length);

        IsoKeyDownloadResponseResult result = IsoKeyDownloadResponseParser.ParseResponse0110(data);
        Check("P24-1 — 0110 성공 응답 ParseFailed == false", !result.ParseFailed);
        Check("P24-1 — 0110 성공 응답 IsSuccess == true", result.IsSuccess);
        Check("P24-1 — 0110 성공 응답 Payload가 P-28과 정확히 일치(길이 610)",
            result.Payload == p28 && result.Payload.Length == 610);
        Check("P24-1 — 0110 성공 응답 ResponseCode == 00", result.ResponseCode == "00");
    }

    /// <summary>0130 정상 응답(196byte, 응답코드 "00")이 P-29/P-39로 정확히 파싱되는지 검증한다.</summary>
    private static void Scenario16_IsoResponseParser0130ParsesSuccessResponseCorrectly()
    {
        string p29 = "01" + new string('X', 144); // 146byte
        string frame = "ISO" + "023400052" + "0130" + "0220000802000000" + "0902130507" + "130507" + p29 + "00";
        byte[] data = Encoding.ASCII.GetBytes(frame);

        Check("P24-1 — 0130 응답 프레임 길이 196",
            data.Length == IsoKeyDownloadResponseParser.Response0130Length);

        IsoKeyDownloadResponseResult result = IsoKeyDownloadResponseParser.ParseResponse0130(data);
        Check("P24-1 — 0130 성공 응답 ParseFailed == false", !result.ParseFailed);
        Check("P24-1 — 0130 성공 응답 IsSuccess == true", result.IsSuccess);
        Check("P24-1 — 0130 성공 응답 Payload가 P-29과 정확히 일치(길이 146)",
            result.Payload == p29 && result.Payload.Length == 146);
        Check("P24-1 — 0130 성공 응답 ResponseCode == 00", result.ResponseCode == "00");
    }

    /// <summary>0110 응답이 SPEC대로 항상 고정 길이(660byte)이며, P-39가 "00"이 아닌 업무 실패
    /// 응답코드일 때도(PRD.md §3.5는 리더기 §3.4와 달리 짧은 오류 응답을 규정하지 않는다 — 헤더부/
    /// P-28/P-39가 전부 고정 길이 필드라 응답코드와 무관하게 항상 660byte로 온다) 정상 파싱되는지
    /// 검증한다.</summary>
    private static void Scenario17_IsoResponseParser0110ParsesNonSuccessResponseCodeAtFullLength()
    {
        string p28 = new string('0', 610);
        string frame = "ISO" + "023400052" + "0110" + "0220001002000000" + "0902130507" + "130507" + p28 + "99";
        byte[] data = Encoding.ASCII.GetBytes(frame);

        IsoKeyDownloadResponseResult result = IsoKeyDownloadResponseParser.ParseResponse0110(data);
        Check("P24-1 — 0110 업무 실패 응답도 ParseFailed == false(고정 길이라 짧게 오지 않음)", !result.ParseFailed);
        Check("P24-1 — 0110 업무 실패 응답 IsSuccess == false", !result.IsSuccess);
        Check("P24-1 — 0110 업무 실패 응답 ResponseCode == 99", result.ResponseCode == "99");
    }

    /// <summary>[64] data가 HASH(64)+RND(32)+SIGN(512)=608byte로, 순서까지 정확히 조립되는지 검증한다.</summary>
    private static void Scenario18_ReaderKeyDownloadRequestBuilderAuthAssembles608BytesWithCorrectContent()
    {
        string hash = new string('H', 64);
        string rnd = new string('R', 32);
        string sign = new string('S', 512);
        byte[] data = KeyDownloadRequestBuilder.BuildAuthRequest(hash, rnd, sign);

        Check("P24-2 — [64] data 길이 608", data.Length == KeyDownloadRequestBuilder.AuthRequestLength);
        Check("P24-2 — [64] data 내용이 HASH+RND+SIGN 순서까지 정확히 일치",
            Encoding.ASCII.GetString(data) == hash + rnd + sign);
    }

    /// <summary>[65] data가 암호화데이터(128)+MAC(16)=144byte로, 순서까지 정확히 조립되는지 검증한다.</summary>
    private static void Scenario19_ReaderKeyDownloadRequestBuilderUsingKeyAssembles144BytesWithCorrectContent()
    {
        string encryptedData = new string('E', 128);
        string mac = new string('M', 16);
        byte[] data = KeyDownloadRequestBuilder.BuildUsingKeyRequest(encryptedData, mac);

        Check("P24-2 — [65] data 길이 144", data.Length == KeyDownloadRequestBuilder.UsingKeyRequestLength);
        Check("P24-2 — [65] data 내용이 암호화데이터+MAC 순서까지 정확히 일치",
            Encoding.ASCII.GetString(data) == encryptedData + mac);
    }

    /// <summary>[73] 정상 응답(46byte, 응답코드 "00")이 키버전/리더기이름/리더기버전/모듈ID로 정확히
    /// 파싱되는지 검증한다.</summary>
    private static void Scenario20_ReaderStartResponseParserParsesSuccessResponse46Bytes()
    {
        string frame = "00" + "01" + "FAKE-READER-NAME" .PadRight(16, ' ') + "FAKE-READER-VER".PadRight(16, ' ') + "MODULE0001";
        byte[] data = Encoding.ASCII.GetBytes(frame);

        Check("P24-2 — [73] 응답 프레임 길이 46", data.Length == KeyDownloadStartResponseParser.TotalLength);

        KeyDownloadStartResponseResult result = KeyDownloadStartResponseParser.Parse(data);
        Check("P24-2 — [73] 성공 응답 ParseFailed == false", !result.ParseFailed);
        Check("P24-2 — [73] 성공 응답 IsSuccess == true", result.IsSuccess);
        Check("P24-2 — [73] 성공 응답 KeyVersion == 01", result.KeyVersion == "01");
        Check("P24-2 — [73] 성공 응답 ModuleId == MODULE0001", result.ModuleId == "MODULE0001");
    }

    /// <summary>C-1 회귀 검증 — [73] 응답이 "13"(키 미주입) 2byte만 왔을 때 ParseFailed가 아니라
    /// (ParseFailed=false, IsSuccess=false, ResponseCode="13")로 정상적으로 업무 실패 분류를 타는지
    /// 확인한다. 46byte에 못 미쳐도 통신 오류(ParseFailed)로 잘못 분류되면 안 된다.</summary>
    private static void Scenario21_ReaderStartResponseParserParsesTwoByteErrorResponse()
    {
        byte[] data = Encoding.ASCII.GetBytes("13");

        KeyDownloadStartResponseResult result = KeyDownloadStartResponseParser.Parse(data);
        Check("C-1 회귀 — [73] 2byte 오류 응답 ParseFailed == false(통신 오류로 오분류되지 않음)", !result.ParseFailed);
        Check("C-1 회귀 — [73] 2byte 오류 응답 IsSuccess == false", !result.IsSuccess);
        Check("C-1 회귀 — [73] 2byte 오류 응답 ResponseCode == 13", result.ResponseCode == "13");

        // 대조군 — 응답코드가 "00"인데 46byte에 못 미치면 진짜 통신 오류(ParseFailed)여야 한다.
        byte[] shortSuccess = Encoding.ASCII.GetBytes("00" + "01");
        KeyDownloadStartResponseResult shortResult = KeyDownloadStartResponseParser.Parse(shortSuccess);
        Check("C-1 회귀 — [73] 응답코드 00인데 46byte 미만이면 ParseFailed == true", shortResult.ParseFailed);
    }

    /// <summary>[74] 정상 응답(558byte, 응답코드 "00")이 정확히 파싱되는지 검증한다.</summary>
    private static void Scenario22_ReaderAuthResponseParserParsesSuccessResponse558Bytes()
    {
        string frame = "00" + "01" + "FAKE-READER-NAME".PadRight(16, ' ') + "FAKE-READER-VER".PadRight(16, ' ')
            + "MODULE0001" + new string('E', 512);
        byte[] data = Encoding.ASCII.GetBytes(frame);

        Check("P24-2 — [74] 응답 프레임 길이 558", data.Length == KeyDownloadAuthResponseParser.TotalLength);

        KeyDownloadAuthResponseResult result = KeyDownloadAuthResponseParser.Parse(data);
        Check("P24-2 — [74] 성공 응답 ParseFailed == false", !result.ParseFailed);
        Check("P24-2 — [74] 성공 응답 IsSuccess == true", result.IsSuccess);
        Check("P24-2 — [74] 성공 응답 EncryptedData 길이 512 및 내용 일치",
            result.EncryptedData == new string('E', 512));
        Check("P24-2 — [74] 성공 응답 ModuleId == MODULE0001", result.ModuleId == "MODULE0001");
    }

    /// <summary>C-1 회귀 검증 — [74] 응답이 "10"(상호인증오류) 2byte만 왔을 때 업무 실패로 정상
    /// 분류되는지 확인한다.</summary>
    private static void Scenario23_ReaderAuthResponseParserParsesTwoByteErrorResponse()
    {
        byte[] data = Encoding.ASCII.GetBytes("10");

        KeyDownloadAuthResponseResult result = KeyDownloadAuthResponseParser.Parse(data);
        Check("C-1 회귀 — [74] 2byte 오류 응답 ParseFailed == false(통신 오류로 오분류되지 않음)", !result.ParseFailed);
        Check("C-1 회귀 — [74] 2byte 오류 응답 IsSuccess == false", !result.IsSuccess);
        Check("C-1 회귀 — [74] 2byte 오류 응답 ResponseCode == 10", result.ResponseCode == "10");

        byte[] shortSuccess = Encoding.ASCII.GetBytes("00" + "01");
        KeyDownloadAuthResponseResult shortResult = KeyDownloadAuthResponseParser.Parse(shortSuccess);
        Check("C-1 회귀 — [74] 응답코드 00인데 558byte 미만이면 ParseFailed == true", shortResult.ParseFailed);
    }

    /// <summary>[75] 정상 응답(12byte, 응답코드 "00")이 정확히 파싱되는지 검증한다.</summary>
    private static void Scenario24_ReaderUsingKeyResponseParserParsesSuccessResponse12Bytes()
    {
        string frame = "00" + "MODULE0001";
        byte[] data = Encoding.ASCII.GetBytes(frame);

        Check("P24-2 — [75] 응답 프레임 길이 12", data.Length == KeyDownloadUsingKeyResponseParser.TotalLength);

        KeyDownloadUsingKeyResponseResult result = KeyDownloadUsingKeyResponseParser.Parse(data);
        Check("P24-2 — [75] 성공 응답 ParseFailed == false", !result.ParseFailed);
        Check("P24-2 — [75] 성공 응답 IsSuccess == true", result.IsSuccess);
        Check("P24-2 — [75] 성공 응답 ModuleId == MODULE0001", result.ModuleId == "MODULE0001");
    }

    /// <summary>C-1 회귀 검증 — [75] 응답이 "22" 2byte만 왔을 때 업무 실패로 정상 분류되는지
    /// 확인한다.</summary>
    private static void Scenario25_ReaderUsingKeyResponseParserParsesTwoByteErrorResponse()
    {
        byte[] data = Encoding.ASCII.GetBytes("22");

        KeyDownloadUsingKeyResponseResult result = KeyDownloadUsingKeyResponseParser.Parse(data);
        Check("C-1 회귀 — [75] 2byte 오류 응답 ParseFailed == false(통신 오류로 오분류되지 않음)", !result.ParseFailed);
        Check("C-1 회귀 — [75] 2byte 오류 응답 IsSuccess == false", !result.IsSuccess);
        Check("C-1 회귀 — [75] 2byte 오류 응답 ResponseCode == 22", result.ResponseCode == "22");

        byte[] shortSuccess = Encoding.ASCII.GetBytes("00");
        KeyDownloadUsingKeyResponseResult shortResult = KeyDownloadUsingKeyResponseParser.Parse(shortSuccess);
        Check("C-1 회귀 — [75] 응답코드 00인데 12byte 미만이면 ParseFailed == true", shortResult.ParseFailed);
    }

    // ===================== 개선권장 #1(Phase 24 2차 Opus 리뷰) — 하네스 사각지대 보강 =====================

    /// <summary>R-8-1 음성 테스트 — 0110 응답의 PRIMARY BITMAP이 SPEC 상수와 다르면(나머지는 전부
    /// 정상) ParseFailed == true가 되는지 확인한다. 지금까지 이 필드는 "정상 값"만으로만 검증됐다.</summary>
    private static void Scenario26_IsoResponseParser0110FailsOnWrongPrimaryBitmap()
    {
        string p28 = "01" + new string('H', 64) + new string('R', 32) + new string('S', 512); // 610byte
        string wrongBitmap = "0000000000000000"; // Response0110Bitmap("0220001002000000")과 다름, 길이는 동일(16)
        string frame = "ISO" + "023400052" + "0110" + wrongBitmap + "0902130507" + "130507" + p28 + "00";
        byte[] data = Encoding.ASCII.GetBytes(frame);

        Check("개선#1 — 0110 PRIMARY BITMAP 오염 시나리오 프레임 길이가 정상(660)과 동일",
            data.Length == IsoKeyDownloadResponseParser.Response0110Length);

        IsoKeyDownloadResponseResult result = IsoKeyDownloadResponseParser.ParseResponse0110(data);
        Check("R-8-1 음성 — 0110 PRIMARY BITMAP이 틀리면 ParseFailed == true", result.ParseFailed);
    }

    /// <summary>R-8-1 음성 테스트 — 0130 응답도 동일하게 PRIMARY BITMAP이 틀리면 ParseFailed가
    /// 되는지 확인한다.</summary>
    private static void Scenario27_IsoResponseParser0130FailsOnWrongPrimaryBitmap()
    {
        string p29 = "01" + new string('X', 144); // 146byte
        string wrongBitmap = "0000000000000000"; // Response0130Bitmap("0220000802000000")과 다름, 길이는 동일(16)
        string frame = "ISO" + "023400052" + "0130" + wrongBitmap + "0902130507" + "130507" + p29 + "00";
        byte[] data = Encoding.ASCII.GetBytes(frame);

        Check("개선#1 — 0130 PRIMARY BITMAP 오염 시나리오 프레임 길이가 정상(196)과 동일",
            data.Length == IsoKeyDownloadResponseParser.Response0130Length);

        IsoKeyDownloadResponseResult result = IsoKeyDownloadResponseParser.ParseResponse0130(data);
        Check("R-8-1 음성 — 0130 PRIMARY BITMAP이 틀리면 ParseFailed == true", result.ParseFailed);
    }

    /// <summary>R-6 음성 테스트 — [73] 정상 길이(46byte) 응답인데 응답코드 이후 구간(키버전/리더기
    /// 이름/리더기버전/모듈ID)에 0x80 이상 바이트가 하나라도 섞이면 ParseFailed == true가 되는지
    /// 확인한다. 지금까지 이 파서는 "전부 ASCII인 정상 값"만으로만 검증됐다.</summary>
    private static void Scenario28_ReaderStartResponseParserFailsOnNonAsciiByte()
    {
        string frame = "00" + "01" + "FAKE-READER-NAME".PadRight(16, ' ') + "FAKE-READER-VER".PadRight(16, ' ') + "MODULE0001";
        byte[] data = Encoding.ASCII.GetBytes(frame);
        Check("개선#1 — [73] 비-ASCII 오염 시나리오 프레임 길이가 정상(46)과 동일",
            data.Length == KeyDownloadStartResponseParser.TotalLength);

        data[data.Length - 1] = 0x80; // 모듈ID 마지막 바이트를 비-ASCII로 오염

        KeyDownloadStartResponseResult result = KeyDownloadStartResponseParser.Parse(data);
        Check("R-6 음성 — [73] 응답코드 00인데 0x80 이상 바이트가 섞이면 ParseFailed == true", result.ParseFailed);
    }

    /// <summary>R-6 음성 테스트 — [75] 정상 길이(12byte) 응답인데 모듈ID 구간에 0x80 이상 바이트가
    /// 섞이면 ParseFailed == true가 되는지 확인한다.</summary>
    private static void Scenario29_ReaderUsingKeyResponseParserFailsOnNonAsciiByte()
    {
        string frame = "00" + "MODULE0001";
        byte[] data = Encoding.ASCII.GetBytes(frame);
        Check("개선#1 — [75] 비-ASCII 오염 시나리오 프레임 길이가 정상(12)과 동일",
            data.Length == KeyDownloadUsingKeyResponseParser.TotalLength);

        data[data.Length - 1] = 0xFF; // 모듈ID 마지막 바이트를 비-ASCII로 오염

        KeyDownloadUsingKeyResponseResult result = KeyDownloadUsingKeyResponseParser.Parse(data);
        Check("R-6 음성 — [75] 응답코드 00인데 0x80 이상 바이트가 섞이면 ParseFailed == true", result.ParseFailed);
    }
}
