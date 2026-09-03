using System;
using System.Diagnostics;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Interop;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// <c>KFTC_GIRO.dll</c>의 <c>FNAISCRDVAN</c> 저수준 호출 결과. Mode/nRet/out_szRetCode를 그대로
/// 돌려줄 뿐 해석하지 않는다 — 해석(응답 절단, 전문 파싱, 성공/실패 판정)은 호출자
/// (<see cref="VanService"/>, <c>KeyDownloadVanClient</c>)의 몫이다(development_plan.md P24-3).
/// </summary>
internal readonly struct FnaisCrdVanInvokeResult
{
    /// <summary><c>FNAISCRDVAN</c> 호출 자체(마샬링 포함)에서 예외가 발생해 <see cref="Threw"/>가
    /// true인 경우, 그 예외가 <see cref="DllNotFoundException"/>/<see cref="EntryPointNotFoundException"/>/
    /// <see cref="BadImageFormatException"/> 중 하나였는지(DLL 로드 실패)와 그 밖의 예외였는지를
    /// 구분한다.</summary>
    internal bool IsDllLoadFailure { get; }

    /// <summary>호출 중 예외가 발생했는지. true면 <see cref="ReturnCode"/>/<see cref="OutData"/>/
    /// <see cref="OutRetCode"/>는 의미가 없다.</summary>
    internal bool Threw { get; }

    /// <summary><see cref="Threw"/>가 true일 때만 값이 있다.</summary>
    internal Exception? Exception { get; }

    /// <summary><c>FNAISCRDVAN</c>의 반환값(<c>nRet</c>). <see cref="Threw"/>가 false일 때만 유효.</summary>
    internal int ReturnCode { get; }

    /// <summary>DLL이 채운 응답 버퍼 전체(<see cref="KftcGiroNative.OutDataBufferSize"/>바이트,
    /// 절단하지 않은 원본). <see cref="Threw"/>가 false일 때만 유효.</summary>
    internal byte[] OutData { get; }

    /// <summary>DLL이 채운 <c>out_szRetCode</c> 버퍼 전체. <see cref="Threw"/>가 false일 때만 유효.</summary>
    internal byte[] OutRetCode { get; }

    /// <summary><c>FNAISCRDVAN</c> 호출에 걸린 시간(ms). <see cref="Threw"/>가 false일 때만 유효.</summary>
    internal long ElapsedMilliseconds { get; }

    private FnaisCrdVanInvokeResult(
        bool threw, bool isDllLoadFailure, Exception? exception,
        int returnCode, byte[] outData, byte[] outRetCode, long elapsedMilliseconds)
    {
        Threw = threw;
        IsDllLoadFailure = isDllLoadFailure;
        Exception = exception;
        ReturnCode = returnCode;
        OutData = outData;
        OutRetCode = outRetCode;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    internal static FnaisCrdVanInvokeResult Success(int returnCode, byte[] outData, byte[] outRetCode, long elapsedMilliseconds) =>
        new(false, false, null, returnCode, outData, outRetCode, elapsedMilliseconds);

    internal static FnaisCrdVanInvokeResult DllLoadFailure(Exception exception) =>
        new(true, true, exception, 0, Array.Empty<byte>(), Array.Empty<byte>(), 0);

    internal static FnaisCrdVanInvokeResult GenericFailure(Exception exception) =>
        new(true, false, exception, 0, Array.Empty<byte>(), Array.Empty<byte>(), 0);
}

/// <summary>
/// <c>KFTC_GIRO.dll</c>의 <c>FNAISCRDVAN</c>을 호출하는 유일한 지점(development_plan.md P24-3 —
/// "저수준 invoker 공통 추출"). 원래 <see cref="VanService"/> 안에 있던 P/Invoke 호출 부분만 그대로
/// 옮긴 것이다 — 담당 범위는 Mode/본문의 NUL 종단 변환, 응답 버퍼 매 호출 새 할당, <see cref="Task.Run"/>
/// 으로 블로킹 호출 격리, 예외 전면 차단(밖으로 던지지 않고 <see cref="FnaisCrdVanInvokeResult"/>로
/// 돌려준다)뿐이다.
///
/// <b>응답 절단·전문 해석·마스킹 로깅·H-1(0x00 방어)/L-1(버퍼 부족 방어)는 여기 없다</b> — 호출자마다
/// 규칙이 다르다(결제는 요청 스키마 길이로 절단, 키다운로드는 전문별 고정 길이로 절단).
/// <see cref="VanService"/>(결제)와 키다운로드 클라이언트가 각자 이 결과를 해석한다.
/// </summary>
internal static class FnaisCrdVanInvoker
{
    /// <summary>
    /// <c>FNAISCRDVAN</c>을 호출한다. <paramref name="body"/>는 NUL 종단되지 않은 원본 본문 바이트
    /// (전문 본문 또는 ISO 키다운로드 전문 바이트) — 이 메서드가 "본문 길이 + 1" 크기의 배열을 새로
    /// 만들어 NUL 종단한다(기존 <c>VanService</c>의 <c>inData</c> 조립 로직 그대로).
    /// </summary>
    internal static async Task<FnaisCrdVanInvokeResult> InvokeAsync(string mode, byte[] body, int timeoutSeconds)
    {
        try
        {
            byte[] modeBytes = BuildNulTerminatedAscii(mode);
            byte[] inData = BuildNulTerminatedFromBytes(body);

            // 매 호출마다 새로 할당한다 — 재사용하면 이전 거래의 잔여 바이트가 다음 응답에 섞일 수
            // 있다. 카드 데이터가 흐르는 경로이므로 특히 중요하다.
            byte[] outData = new byte[KftcGiroNative.OutDataBufferSize];
            byte[] outRetCode = new byte[KftcGiroNative.RetCodeBufferSize];

            var stopwatch = Stopwatch.StartNew();
            int nRet;
            try
            {
                // FNAISCRDVAN은 블로킹 호출이다(타임아웃 인자를 받는 것 자체가 근거) — 호출 스레드를
                // 최대 타임아웃 시간만큼 붙잡지 않도록 Task.Run으로 감싼다.
                nRet = await Task.Run(() => KftcGiroNative.FNAISCRDVAN(
                    modeBytes, inData, outData, outRetCode, timeoutSeconds)).ConfigureAwait(false);
            }
            finally
            {
                // 개선권장 #2(Phase 24 2차 Opus 리뷰) — inData(요청 전문 + NUL 종단, SIGN/HASH/RND/
                // 암호화데이터 포함 가능)는 여기서 새로 만든 복사본이라 호출자의 Array.Clear(원본
                // request 배열)로는 지워지지 않는다 — DLL 호출이 끝난 직후(성공/실패 무관) 지운다.
                // 이 invoker는 결제 경로(VanService)와 키다운로드 경로(KeyDownloadVanClient)가
                // 공유하므로, 이 변경은 두 경로 모두에 자동으로 적용된다.
                Array.Clear(inData, 0, inData.Length);
            }

            stopwatch.Stop();

            return FnaisCrdVanInvokeResult.Success(nRet, outData, outRetCode, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return FnaisCrdVanInvokeResult.DllLoadFailure(ex);
        }
        catch (Exception ex)
        {
            // DLL 호출 실패로 앱이 죽으면 안 된다(PRD §9) — 어떤 예외도 밖으로 던지지 않는다.
            return FnaisCrdVanInvokeResult.GenericFailure(ex);
        }
    }

    private static byte[] BuildNulTerminatedAscii(string value)
    {
        byte[] ascii = System.Text.Encoding.ASCII.GetBytes(value);
        byte[] result = new byte[ascii.Length + 1];
        Buffer.BlockCopy(ascii, 0, result, 0, ascii.Length);
        return result;
    }

    private static byte[] BuildNulTerminatedFromBytes(byte[] body)
    {
        // NUL 종단 — char*는 C 문자열이므로 DLL이 strlen으로 길이를 잴 가능성이 있다. "본문 길이 + 1"
        // 크기로 배열을 잡고 마지막 바이트를 0으로 남겨 두면 고정 길이/NUL 종단 두 해석 모두에서
        // 안전하다(기존 VanService.RelayAsync의 inData 조립 로직 그대로).
        byte[] result = new byte[body.Length + 1];
        Buffer.BlockCopy(body, 0, result, 0, body.Length);
        // result[body.Length]는 배열 기본값 0으로 이미 NUL.
        return result;
    }
}
