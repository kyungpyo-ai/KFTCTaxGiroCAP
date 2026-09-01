using System;
using System.Diagnostics;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Interop;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// <see cref="IVanRelayService"/>의 실제 구현체 — <c>KFTC_GIRO.dll</c>의 <c>FNAISCRDVAN</c>을 호출한다
/// (docs/payment_relay/development_plan.md Phase 20, P20-2). <see cref="StubVanRelayService"/>와 같은
/// 자리에 꽂히며, <c>App.xaml.cs</c>는 이 Phase에서 아직 이 클래스를 쓰지 않는다(스텁 유지, 결정 1).
///
/// <b>VAN 서버가 아직 개발 중이라 접속 자체가 되지 않는다</b>(2026-08-26/2026-08-31 확인) — 이 클래스가
/// 검증하는 것은 "실거래가 성공하는가"가 아니라 "네이티브 호출 경계가 안전한가"(마샬링, 버퍼, 예외 내성)다.
///
/// <b>스레드 안전성 전제</b>: <c>FNAISCRDVAN</c>은 <c>TransactionQueue</c>(Phase 14)가 보장하는 단일
/// 워커 큐 안에서만 호출된다 — 동시에 두 번 호출되지 않는다. <c>KFTC_GIRO.dll</c> 자체의 스레드 안전성은
/// 알 수 없으므로 이 전제에 의존한다 — 나중에 큐 정책이 바뀌면 이 가정도 재검토해야 한다.
///
/// <b>relay 원칙</b>: <c>nRet == 0</c>이면 <c>outData</c>를 해석하지 않고 그대로
/// <see cref="VanRelayOutcome.Success"/>로 돌려준다 — 승인/거절 판정은 원캡의 일이 아니다(PRD §4.10).
/// </summary>
internal sealed class VanService : IVanRelayService
{
    /// <summary>SPEC <c>#9</c> 전문관리번호(<c>PosSocketServer.ManagementNumberFieldNumber</c>,
    /// <c>PaymentOrchestrator.LogTxId</c>와 동일한 필드) — P22-6 로깅용.</summary>
    private const int ManagementNumberFieldNumber = 9;

    public async Task<VanRelayOutcome> RelayAsync(PosRequestTelegram populatedRequest)
    {
        string transactionTypeCode = populatedRequest.TransactionTypeCode;
        int bodyLength = populatedRequest.Schema.TotalLength;
        // #9(전문관리번호) — PaymentOrchestrator.LogTxId와 같은 필드(값이 비어 있지 않은 정상 케이스라면
        // 동일한 값)라 POS/READER 경계 로그와 같은 거래ID로 남는다(P22-6 완료 조건).
        string txId = populatedRequest.Read(ManagementNumberFieldNumber);

        try
        {
            byte[] body = populatedRequest.Telegram.ToBody();

            // 사용자 요청(2026-09-01) — 전문 원문(위치기반 마스킹, TelegramLogRedactor 클래스 요약
            // 참고)을 로그에 남긴다. 아래 "전문 본문(카드번호/PIN 등)은 절대 로그에 남기지 않는다"는
            // 예전 방침(길이/종별만 남김)을 대체한다 — 이제는 902614 #46(암호화된 카드정보)만 마스킹
            // 하고 나머지 필드는 원문 그대로 남긴다.
            string redactedRequestBody = TelegramLogRedactor.Redact(transactionTypeCode, body);

            // NUL 종단 — char*는 C 문자열이므로 DLL이 strlen으로 길이를 잴 가능성이 있다. 본문은
            // 공백 패딩된 고정 길이이고 내부에 0x00이 없으므로, "본문 길이 + 1" 크기로 배열을 잡고
            // 마지막 바이트를 0으로 남겨 두면 고정 길이/NUL 종단 두 해석 모두에서 안전하다.
            byte[] inData = new byte[body.Length + 1];
            Buffer.BlockCopy(body, 0, inData, 0, body.Length);
            // inData[body.Length]는 배열 기본값 0으로 이미 NUL.

            byte[] mode = BuildNulTerminatedAscii(KftcGiroNative.ModeExternalTest);

            // 매 호출마다 새로 할당한다 — 재사용하면 이전 거래의 잔여 바이트가 다음 응답에 섞일 수
            // 있다. 카드 데이터가 흐르는 경로이므로 특히 중요하다.
            byte[] outData = new byte[KftcGiroNative.OutDataBufferSize];
            byte[] outRetCode = new byte[KftcGiroNative.RetCodeBufferSize];

            var stopwatch = Stopwatch.StartNew();

            // P22-6(PRD.md §1.5 경계 표 "VAN") — FNAISCRDVAN 호출 직전.
            FileLogger.Info(LogCategory.Van, $"[VanService] 거래구분={transactionTypeCode} FNAISCRDVAN 호출 원문={redactedRequestBody}", code: null, txId);

            // FNAISCRDVAN은 블로킹 호출이다(타임아웃 인자를 받는 것 자체가 근거) — RelayAsync가
            // async인데 그냥 호출하면 호출 스레드를 최대 타임아웃 시간만큼 붙잡는다. Task.Run으로
            // 감싸 그 블로킹을 별도 스레드로 밀어낸다.
            int nRet = await Task.Run(() => KftcGiroNative.FNAISCRDVAN(
                mode, inData, outData, outRetCode, KftcGiroNative.DefaultTimeoutSeconds)).ConfigureAwait(false);

            stopwatch.Stop();

            string retCodeText = DecodeNulTerminated(outRetCode);

            // Phase 18 H-2(PIN이 스텁 응답에 실려 로그/화면에 노출된 전례)를 반영해, 응답 본문 자체는
            // 여기서 남기지 않고 nRet/길이/소요시간만 남긴다 — 응답 전문 원문(마스킹 적용)은 아래
            // responseBody 확보 시점에 별도로 남긴다(TelegramLogRedactor). P22-6 — FNAISCRDVAN 반환.
            FileLogger.Info(
                LogCategory.Van,
                $"[VanService] 거래구분={transactionTypeCode} nRet={nRet} out_szRetCode='{retCodeText}' " +
                $"본문길이={bodyLength} 소요={stopwatch.ElapsedMilliseconds}ms",
                code: null, txId);

            if (nRet == 0)
            {
                if (bodyLength > outData.Length)
                {
                    // L-1: bodyLength(스키마 총 길이, 최대 1500)는 항상 OutDataBufferSize(4096)보다
                    // 작아야 하지만, 어긋나면 Buffer.BlockCopy가 던지는 예외가 아래 generic catch에
                    // 삼켜져 원인 불명의 D02로만 남는다. 여기서 먼저 걸러 진단 가능한 사유를 남긴다.
                    FileLogger.Error(
                        LogCategory.Van,
                        $"[VanService] 거래구분={transactionTypeCode} bodyLength({bodyLength})가 outData 버퍼" +
                        $"({outData.Length})보다 큼 — 응답을 자를 수 없음",
                        code: null, txId);
                    return VanRelayOutcome.CommunicationFailure(
                        VanFailureKind.CommunicationFailure,
                        $"응답 버퍼 부족(bodyLength={bodyLength}, bufferSize={outData.Length})");
                }

                byte[] responseBody = new byte[bodyLength];
                Buffer.BlockCopy(outData, 0, responseBody, 0, bodyLength);

                if (ContainsNulByte(responseBody))
                {
                    // H-1: 유효한 전문 본문은 space(0x20) 또는 '0'(0x30)로만 패딩되므로(PosTelegram.
                    // CreateEmpty, PosField.Pad) 0x00은 절대 나올 수 없다 — 응답 구간에 0x00이 하나라도
                    // 있으면 DLL이 그 자리를 채우지 않은 것이다. 이전 버전은 outData 전체(4096바이트)가
                    // 전부 0일 때만 걸렀는데, DLL이 nRet=0을 주면서 응답을 "부분만" 쓴 경우(예: 앞부분
                    // 몇 바이트만 채우고 나머지는 0x00)는 걸러지지 않아 NUL이 섞인 응답이 그대로 POS에
                    // relay됐다. relay 원칙(필드 내용 미해석)은 지키면서 구조적 무결성만 검사한다.
                    FileLogger.Warn(
                        LogCategory.Van,
                        $"[VanService] 거래구분={transactionTypeCode} nRet=0인데 응답 본문에 0x00 바이트 포함 — " +
                        "통신 실패로 처리",
                        code: null, txId);
                    return VanRelayOutcome.CommunicationFailure(
                        VanFailureKind.CommunicationFailure, "nRet=0이지만 응답 본문이 불완전함(0x00 포함, 방어적 처리)");
                }

                // 사용자 요청(2026-09-01) — 응답 전문 원문(위치기반 마스킹, TelegramLogRedactor 클래스
                // 요약 참고).
                string redactedResponseBody = TelegramLogRedactor.Redact(transactionTypeCode, responseBody);
                FileLogger.Info(LogCategory.Van, $"[VanService] 거래구분={transactionTypeCode} 응답 원문={redactedResponseBody}", code: null, txId);

                return VanRelayOutcome.Success(responseBody);
            }

            if (nRet == -1)
            {
                return VanRelayOutcome.CommunicationFailure(
                    VanFailureKind.CommunicationFailure, $"FNAISCRDVAN 통신 실패(nRet=-1), out_szRetCode='{retCodeText}'");
            }

            // PRD에 정의되지 않은 반환값 — 성공으로 취급하지 않는다. 실제 값을 남겨 발주처에 물어볼
            // 근거를 만든다.
            FileLogger.Warn(
                LogCategory.Van,
                $"[VanService] 거래구분={transactionTypeCode} FNAISCRDVAN이 PRD에 정의되지 않은 값을 반환: nRet={nRet}",
                code: null, txId);
            return VanRelayOutcome.CommunicationFailure(
                VanFailureKind.CommunicationFailure, $"FNAISCRDVAN이 알 수 없는 값을 반환(nRet={nRet})");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            FileLogger.Error(LogCategory.Van, $"[VanService] 거래구분={transactionTypeCode} DLL 로드 실패: {ex.GetType().Name}: {ex.Message}", code: null, txId);
            return VanRelayOutcome.CommunicationFailure(VanFailureKind.DllLoadFailure, $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            // DLL 호출 실패로 앱이 죽으면 안 된다(PRD §9) — 어떤 예외도 밖으로 던지지 않는다.
            FileLogger.Error(LogCategory.Van, $"[VanService] 거래구분={transactionTypeCode} 예상치 못한 예외: {ex.GetType().Name}: {ex.Message}", code: null, txId);
            return VanRelayOutcome.CommunicationFailure(VanFailureKind.CommunicationFailure, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static byte[] BuildNulTerminatedAscii(string value)
    {
        byte[] ascii = System.Text.Encoding.ASCII.GetBytes(value);
        byte[] result = new byte[ascii.Length + 1];
        Buffer.BlockCopy(ascii, 0, result, 0, ascii.Length);
        return result;
    }

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

    private static string DecodeNulTerminated(byte[] buffer)
    {
        int nulIndex = Array.IndexOf(buffer, (byte)0);
        int length = nulIndex >= 0 ? nulIndex : buffer.Length;
        return length == 0 ? string.Empty : PosMessageEncoding.Value.GetString(buffer, 0, length);
    }
}
