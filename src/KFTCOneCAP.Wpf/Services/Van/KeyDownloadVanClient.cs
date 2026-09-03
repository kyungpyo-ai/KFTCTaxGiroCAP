using System;
using System.Text;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Interop;
using KFTCOneCAP.Wpf.Protocol.KeyDownload;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// 리더기 키다운로드 서버 구간 호출 결과 종류. <c>VanRelayOutcomeKind</c>(결제)와 별도 계열이다 —
/// 결제와 키다운로드는 응답코드 의미가 다르다(키다운로드는 <c>"00"</c>만 성공, 나머지는 값을 그대로
/// 노출한다, `PRD.md` §3.5).
/// </summary>
internal enum KeyDownloadVanCallKind
{
    /// <summary>구조적으로 정상 파싱됐고 응답코드(P-39)가 <c>"00"</c>.</summary>
    Success,

    /// <summary><c>KFTC_GIRO.dll</c> 로드 자체가 실패(<see cref="DllNotFoundException"/> 등).</summary>
    DllLoadFailure,

    /// <summary><c>FNAISCRDVAN</c> 호출 예외(DLL 로드 실패 제외) 또는 <c>nRet != 0</c>.</summary>
    CommunicationFailure,

    /// <summary>응답 길이/"ISO" 개시문자/전문 TYPE이 SPEC과 다름 — <see cref="IsoKeyDownloadResponseResult.ParseFailed"/>.</summary>
    ResponseParseFailure,

    /// <summary>구조적으로는 정상 파싱됐지만 응답코드(P-39)가 <c>"00"</c>이 아님. <see cref="ResponseCode"/>에
    /// 실제 값이 담긴다(예: <c>"395"</c> = 단말기 교체 요망).</summary>
    NonSuccessResponseCode,
}

/// <summary>
/// 리더기 키다운로드 서버 구간 호출(②/④) 1건의 결과. <c>KeyDownloadService</c>(P24-5)가 단계 이름과
/// 함께 실패 문구를 만드는 데 쓴다(`PRD.md` §3.6).
/// </summary>
internal readonly struct KeyDownloadVanCallOutcome
{
    internal KeyDownloadVanCallKind Kind { get; }

    /// <summary><see cref="Kind"/>가 <see cref="KeyDownloadVanCallKind.Success"/> 또는
    /// <see cref="KeyDownloadVanCallKind.NonSuccessResponseCode"/>일 때만 값이 있다.
    /// 0110이면 P-28(AN 610), 0130이면 P-29(AN 146).</summary>
    internal string Payload { get; }

    /// <summary><see cref="Kind"/>가 <see cref="KeyDownloadVanCallKind.Success"/> 또는
    /// <see cref="KeyDownloadVanCallKind.NonSuccessResponseCode"/>일 때만 값이 있다(P-39, AN 2).</summary>
    internal string ResponseCode { get; }

    /// <summary>사람이 읽는 실패 사유. <see cref="Kind"/>가 <see cref="KeyDownloadVanCallKind.Success"/>가
    /// 아닐 때만 값이 있다.</summary>
    internal string Detail { get; }

    internal bool IsSuccess => Kind == KeyDownloadVanCallKind.Success;

    private KeyDownloadVanCallOutcome(KeyDownloadVanCallKind kind, string payload, string responseCode, string detail)
    {
        Kind = kind;
        Payload = payload;
        ResponseCode = responseCode;
        Detail = detail;
    }

    internal static KeyDownloadVanCallOutcome Success(string payload, string responseCode) =>
        new(KeyDownloadVanCallKind.Success, payload, responseCode, string.Empty);

    internal static KeyDownloadVanCallOutcome NonSuccessResponseCode(string payload, string responseCode) =>
        new(KeyDownloadVanCallKind.NonSuccessResponseCode, payload, responseCode, $"응답코드={responseCode}");

    internal static KeyDownloadVanCallOutcome DllLoadFailure(string detail) =>
        new(KeyDownloadVanCallKind.DllLoadFailure, string.Empty, string.Empty, detail);

    internal static KeyDownloadVanCallOutcome CommunicationFailure(string detail) =>
        new(KeyDownloadVanCallKind.CommunicationFailure, string.Empty, string.Empty, detail);

    internal static KeyDownloadVanCallOutcome ResponseParseFailure(string detail) =>
        new(KeyDownloadVanCallKind.ResponseParseFailure, string.Empty, string.Empty, detail);
}

/// <summary>
/// 리더기 키다운로드 서버 구간(②0100→0110 Key Download / ④0120→0130 Key Bundling)을 담당한다
/// (development_plan.md P24-3). <see cref="FnaisCrdVanInvoker"/>(P/Invoke 호출)와
/// <see cref="IsoKeyDownloadRequestBuilder"/>/<see cref="IsoKeyDownloadResponseParser"/>(P24-1, 전문
/// 조립/파싱)를 조합하는 오케스트레이션 계층일 뿐 — P/Invoke를 직접 부르지 않는다
/// (<see cref="FnaisCrdVanInvoker"/>가 유일한 호출 지점).
///
/// <b>Mode 캐시 금지</b>(`PRD.md` §2.6, <see cref="VanService"/>와 동일 원칙) — 매 호출마다
/// <see cref="ShopSettings.VanMode"/>를 새로 읽는다. 필드에 담아두지 않는다.
///
/// <b>메모리 클리어(2026-09-02 사용자 확정)</b> — 조립한 0100/0120 요청 바이트는 invoker 호출 직후,
/// 0110/0130 응답 바이트는 필요한 필드를 파서로 복사해낸 직후 각각 <see cref="Array.Clear(Array,int,int)"/>
/// 로 지운다. best-effort다(GC가 이동시킨 옛 복사본까지는 못 지운다, net48엔
/// <c>CryptographicOperations.ZeroMemory</c>도 없다) — 결제 경로(<see cref="VanService"/>)는 이 클리어
/// 대상이 아니다(범위 확대는 Phase 25로 미룸).
/// </summary>
internal sealed class KeyDownloadVanClient : IKeyDownloadVanClient
{
    private readonly Func<ShopSettings> _loadSettings;

    internal KeyDownloadVanClient() : this(new ShopSettingsService().Load)
    {
    }

    internal KeyDownloadVanClient(Func<ShopSettings> loadSettings)
    {
        _loadSettings = loadSettings;
    }

    /// <summary>② Key Download 요청(0100) → 응답(0110). <paramref name="p28"/>은 정확히 12문자
    /// (AN 12 — 키버전 2 + 모듈ID 10, 리더기 <c>[73]</c> 응답에서 그대로 잘라온 값).</summary>
    internal async Task<KeyDownloadVanCallOutcome> SendKeyDownloadRequestAsync(string p28)
    {
        byte[] request = IsoKeyDownloadRequestBuilder.BuildRequest0100(DateTime.Now, p28);
        return await InvokeAndParseAsync(
            "0100",
            request,
            IsoKeyDownloadResponseParser.Response0110Length,
            IsoKeyDownloadResponseParser.ParseResponse0110).ConfigureAwait(false);
    }

    /// <summary>④ Key Bundling 요청(0120) → 응답(0130). <paramref name="p29"/>은 정확히 524문자
    /// (AN 524 — 키버전 2 + 모듈ID 10 + 암호화데이터 512, 리더기 <c>[74]</c> 응답에서 그대로
    /// 잘라온 값).</summary>
    internal async Task<KeyDownloadVanCallOutcome> SendKeyBundlingRequestAsync(string p29)
    {
        byte[] request = IsoKeyDownloadRequestBuilder.BuildRequest0120(DateTime.Now, p29);
        return await InvokeAndParseAsync(
            "0120",
            request,
            IsoKeyDownloadResponseParser.Response0130Length,
            IsoKeyDownloadResponseParser.ParseResponse0130).ConfigureAwait(false);
    }

    // ===================== IKeyDownloadVanClient 명시적 구현(P24-4) =====================
    //
    // 위 두 메서드는 internal이라 암시적 인터페이스 구현이 안 된다 — 접근자를 public으로 넓히지
    // 않고 명시적 인터페이스 구현으로 얇은 위임만 추가한다(development_plan.md P24-4 지시,
    // Services/Reader/ReaderService.cs의 IKeyDownloadReaderEndpoint 구현과 동일 패턴).
    Task<KeyDownloadVanCallOutcome> IKeyDownloadVanClient.SendKeyDownloadRequestAsync(string p28) =>
        SendKeyDownloadRequestAsync(p28);

    Task<KeyDownloadVanCallOutcome> IKeyDownloadVanClient.SendKeyBundlingRequestAsync(string p29) =>
        SendKeyBundlingRequestAsync(p29);

    private async Task<KeyDownloadVanCallOutcome> InvokeAndParseAsync(
        string telegramName, byte[] request, int expectedResponseLength, Func<byte[], IsoKeyDownloadResponseResult> parse)
    {
        // 개선권장 #4(Phase 24 2차 Opus 리뷰) — VanService.RelayAsync는 전체를 try/catch(Exception)로
        // 감싸 어떤 예외도 밖으로 던지지 않는데, 이 메서드는 invoker 호출 구간(아래 안쪽 try/finally)
        // 밖(설정 조회/로깅/파싱)에 그런 안전망이 없었다. 지금 실제로 예외를 던지는 경로는 없는 것으로
        // 확인됐지만(방어적 차원), VanService와 동일하게 전체를 감싼다 — 예외가 나도
        // CommunicationFailure로 안전하게 떨어지도록 한다.
        try
        {
            return await InvokeAndParseAsyncCore(telegramName, request, expectedResponseLength, parse).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLogger.Error(LogCategory.Keydown,
                $"[KeyDownloadVanClient] 전문={telegramName} 예상치 못한 예외로 중단: {ex.GetType().Name}: {ex.Message}");
            return KeyDownloadVanCallOutcome.CommunicationFailure($"예상치 못한 예외: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<KeyDownloadVanCallOutcome> InvokeAndParseAsyncCore(
        string telegramName, byte[] request, int expectedResponseLength, Func<byte[], IsoKeyDownloadResponseResult> parse)
    {
        // C-2(CP1 리뷰) — 결제 경로(VanService.RelayAsync)와 같은 스타일로 호출 직전/직후를 남긴다.
        //
        // Phase 24 후속(2026-09-02 사용자 명시적 확정, 위험 고지 후 재확인, PRD.md §3.6) — VAN 서버
        // 구간(0100/0110/0120/0130)에 한해 결제 경로(VanService)와 동일하게 전문 원문을 마스킹 없이
        // 그대로 로그에 남긴다. §3.6의 "SIGN/HASH/RND/암호화데이터는 길이만 기록" 원칙은 리더기
        // 구간([64]/[65]/[74], KEYDOWN 카테고리 중 ReaderService/KeyDownloadService 쪽)에만 계속
        // 적용되고, 이 서버 구간은 대상이 아니다 — SIGN(512)/HASH(64)/RND(32)/암호화데이터가 전문
        // 대부분을 차지하므로 이 결정은 사실상 키 자재가 로그 파일에 평문으로 남는다는 뜻이지만,
        // 사용자가 그 위험을 인지한 뒤 그대로 진행하기로 했다. 지금은 위치 기반 마스킹 없이 전체를
        // 남기되, 나중에 특정 필드를 마스킹할 필요가 생기면 TelegramLogRedactor(POS/VAN 결제 경계)
        // 처럼 위치 기반 마스킹을 추가하는 방식으로 간다 — 지금 이 클래스가 그 마스킹을 미리 구현할
        // 필요는 없다.
        string mode = _loadSettings().VanMode;
        string requestText = DecodeAscii(request);
        FileLogger.Info(LogCategory.Keydown,
            $"[KeyDownloadVanClient] 전문={telegramName} mode={mode} 요청 원문={requestText} FNAISCRDVAN 호출");

        FnaisCrdVanInvokeResult invokeResult;
        try
        {
            invokeResult = await FnaisCrdVanInvoker.InvokeAsync(
                mode, request, KftcGiroNative.DefaultTimeoutSeconds).ConfigureAwait(false);
        }
        finally
        {
            // 조립한 요청 바이트(SIGN/HASH/RND/암호화 데이터 포함, P-28/P-29)는 invoker 호출 직후
            // 더 필요 없다 — best-effort 클리어(2026-09-02 사용자 확정, 위 클래스 주석 참고).
            Array.Clear(request, 0, request.Length);
        }

        if (invokeResult.Threw)
        {
            FileLogger.Error(LogCategory.Keydown,
                $"[KeyDownloadVanClient] 전문={telegramName} " +
                $"{(invokeResult.IsDllLoadFailure ? "DLL 로드 실패" : "예상치 못한 예외")}: " +
                $"{invokeResult.Exception!.GetType().Name}: {invokeResult.Exception.Message}");
            return invokeResult.IsDllLoadFailure
                ? KeyDownloadVanCallOutcome.DllLoadFailure(
                    $"{invokeResult.Exception!.GetType().Name}: {invokeResult.Exception.Message}")
                : KeyDownloadVanCallOutcome.CommunicationFailure(
                    $"{invokeResult.Exception!.GetType().Name}: {invokeResult.Exception.Message}");
        }

        FileLogger.Info(LogCategory.Keydown,
            $"[KeyDownloadVanClient] 전문={telegramName} nRet={invokeResult.ReturnCode} " +
            $"out_szRetCode='{DecodeNulTerminated(invokeResult.OutRetCode)}' 소요={invokeResult.ElapsedMilliseconds}ms");

        if (invokeResult.ReturnCode != 0)
        {
            // R-3(Phase 24 전체 Opus 리뷰) — 통신 실패 조기 반환 경로도 정상 경로와 동일하게 4096바이트
            // OutData를 클리어한다. Threw 경로는 OutData가 항상 빈 배열이라(FnaisCrdVanInvoker) 별도
            // 처리가 필요 없다 — Array.Clear에 빈 배열을 넘겨도 무해하므로 조건 분기 없이 공통화한다.
            Array.Clear(invokeResult.OutData, 0, invokeResult.OutData.Length);
            return KeyDownloadVanCallOutcome.CommunicationFailure($"FNAISCRDVAN 통신 실패(nRet={invokeResult.ReturnCode})");
        }

        // invoker의 OutData는 4096바이트 원본 버퍼 — 전문별 고정 길이(660/196)만큼 잘라 파서에 넘긴다.
        // 결제(VanService)와 달리 요청 스키마 길이가 아니라 ISO 전문의 고정 응답 길이를 쓴다
        // (요청과 응답의 길이가 다르므로 — development_plan.md P24-2 "착수 전 전제").
        byte[] response = new byte[expectedResponseLength];
        int copyLength = Math.Min(expectedResponseLength, invokeResult.OutData.Length);
        Buffer.BlockCopy(invokeResult.OutData, 0, response, 0, copyLength);
        // invoker가 돌려준 원본 4096바이트 버퍼도 같은 응답 데이터를 담고 있다 — 위 response로
        // 필요한 부분을 옮겼으니 더 갖고 있을 필요가 없다.
        Array.Clear(invokeResult.OutData, 0, invokeResult.OutData.Length);

        // R-1(Phase 24 전체 Opus 리뷰) — VanService.ContainsNulByte(H-1)와 동일한 방어를 여기도 추가한다.
        // DLL이 nRet=0을 주면서 응답을 앞부분만 채우면 나머지가 0x00으로 남는데, 아래
        // IsoKeyDownloadResponseParser의 ContainsNonAscii(I-1)는 0x00을 통과시킨다(0x00 < 0x80이라
        // "비-ASCII"가 아님) — 그 결과 P-39가 NUL 바이트인 채로 파싱이 "성공"해버려 알림창에 안 보이는
        // 문자가 뜨고 로그에도 NUL이 섞인 원문이 남는다. 원문 로깅(R-5, 손대지 않음)보다 먼저 걸러서
        // NUL 섞인 응답이 로그에 찍히지 않게 한다 — 파싱을 아예 시도하지 않고 통신 실패로 떨어뜨린다
        // (이 클래스의 실패 분류상 데이터를 신뢰할 수 없는 경우이므로 ResponseParseFailure보다
        // CommunicationFailure가 더 맞다 — DLL이 nRet=0을 줬지만 실제로는 응답을 못 채운 통신 이상
        // 상황이라는 뜻이라 VanService의 동일 상황 분류와도 맞춘다).
        if (ContainsNulByte(response))
        {
            FileLogger.Warn(LogCategory.Keydown,
                $"[KeyDownloadVanClient] 전문={telegramName} nRet=0인데 응답 본문에 0x00 바이트 포함 — 통신 실패로 처리");
            Array.Clear(response, 0, response.Length);
            return KeyDownloadVanCallOutcome.CommunicationFailure("nRet=0이지만 응답 본문이 불완전함(0x00 포함, 방어적 처리)");
        }

        IsoKeyDownloadResponseResult parsed = parse(response);
        string payload = parsed.Payload;
        string responseCode = parsed.ResponseCode;

        // Phase 24 후속(2026-09-02 사용자 명시적 확정) — 응답 전문 원문(0110/0130)도 요청과 동일하게
        // 마스킹 없이 로그에 남긴다. 파싱 성공(구조/ASCII 검증 통과, 위 IsoKeyDownloadResponseParser.
        // Parse의 ContainsNonAscii 방어) 시에만 남긴다 — 파싱 실패면 데이터를 신뢰할 수 없어(비-ASCII
        // 등) 문자열로 남기는 의미가 없다.
        if (!parsed.ParseFailed)
        {
            FileLogger.Info(LogCategory.Keydown,
                $"[KeyDownloadVanClient] 전문={telegramName} 응답 원문={DecodeAscii(response)}");
        }

        // 필요한 필드(payload/responseCode)를 위 지역 변수로 복사해냈으니 원본 응답 바이트는 더
        // 필요 없다 — best-effort 클리어.
        Array.Clear(response, 0, response.Length);

        if (parsed.ParseFailed)
        {
            // R-7(Phase 24 전체 Opus 리뷰) — I-1(비-ASCII 감지)도 ParseFailed 사유가 될 수 있으므로
            // 문구에 반영한다.
            return KeyDownloadVanCallOutcome.ResponseParseFailure(
                "응답 형식 불일치(길이/ISO 개시문자/전문 TYPE/PRIMARY BITMAP/비-ASCII 데이터 포함 중 하나 이상 " +
                $"SPEC과 다름, 응답길이={expectedResponseLength})");
        }

        return parsed.IsSuccess
            ? KeyDownloadVanCallOutcome.Success(payload, responseCode)
            : KeyDownloadVanCallOutcome.NonSuccessResponseCode(payload, responseCode);
    }

    /// <summary>Phase 24 후속(2026-09-02) — 요청/응답 전문 원문 로깅 전용. 전문은 §3.5 지시대로
    /// 전 필드 ASCII(한글 없음)라 <c>Encoding.ASCII</c>로 그대로 디코딩한다(IsoKeyDownloadRequestBuilder
    /// 와 동일 인코딩). NUL 종단 처리를 하지 않는다 — 이 전문들은 <see cref="DecodeNulTerminated"/>가
    /// 다루는 out_szRetCode(가변 길이, NUL 종단)와 달리 항상 고정 길이라 전체를 그대로 남긴다.</summary>
    private static string DecodeAscii(byte[] buffer) => Encoding.ASCII.GetString(buffer);

    /// <summary>VanService.DecodeNulTerminated와 동일한 로직(out_szRetCode NUL 종단 디코딩) —
    /// 로깅 전용이라 별도 공용 유틸로 승격하지 않고 그대로 복제한다.</summary>
    private static string DecodeNulTerminated(byte[] buffer)
    {
        int nulIndex = Array.IndexOf(buffer, (byte)0);
        int length = nulIndex >= 0 ? nulIndex : buffer.Length;
        return length == 0 ? string.Empty : PosMessageEncoding.Value.GetString(buffer, 0, length);
    }

    /// <summary>R-1(Phase 24 전체 Opus 리뷰) — VanService.ContainsNulByte(H-1)와 동일한 로직을
    /// 그대로 복제한다(로깅/파싱 방어 전용이라 별도 공용 유틸로 승격하지 않는다).</summary>
    private static bool ContainsNulByte(byte[] buffer)
    {
        foreach (byte b in buffer)
        {
            if (b == 0)
            {
                return true;
            }
        }

        return false;
    }
}
