using System;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Interop;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Security;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Payment;
using KFTCOneCAP.Wpf.Services.Settings;

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

    /// <summary>Phase 23(docs/operations/development_plan.md P23-5) — VAN Mode를 매 호출마다 다시
    /// 읽는다(PRD.md §2.6 "설정값을 캐시하지 않는다"). 생성자에서 한 번 읽어 필드에 고정하면 화면에서
    /// 설정을 바꿔도 앱을 재시작하기 전까지 반영되지 않는 캐시 버그가 된다.</summary>
    private readonly Func<ShopSettings> _loadSettings;

    public VanService() : this(new ShopSettingsService().Load)
    {
    }

    internal VanService(Func<ShopSettings> loadSettings)
    {
        _loadSettings = loadSettings;
    }

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
            try
            {
                // 사용자 요청(2026-09-01) — 전문 원문(위치기반 마스킹, TelegramLogRedactor 클래스 요약
                // 참고)을 로그에 남긴다. 아래 "전문 본문(카드번호/PIN 등)은 절대 로그에 남기지 않는다"는
                // 예전 방침(길이/종별만 남김)을 대체한다 — 이제는 902614 #46(암호화된 카드정보)만 마스킹
                // 하고 나머지 필드는 원문 그대로 남긴다.
                string redactedRequestBody = TelegramLogRedactor.Redact(transactionTypeCode, body);

                // 매 호출마다 새로 읽는다(캐시 금지, PRD.md §2.6) — 화면에서 서버를 바꾸면 다음 호출부터
                // 바로 반영돼야 한다.
                string vanMode = _loadSettings().VanMode;

                // P22-6(PRD.md §1.5 경계 표 "VAN") — FNAISCRDVAN 호출 직전. 개선권장 1(CP2 Opus 리뷰) —
                // 실제로 나가는 mode(R/OT/IT)를 한 토큰 남긴다. 민감정보가 아니므로 마스킹하지 않는다.
                // P23-8 "OT/R이 FNAISCRDVAN 첫 인자로 실제로 나가는 것을 로그로 확인"의 선행 조건.
                FileLogger.Info(LogCategory.Van, $"[VanService] 거래구분={transactionTypeCode} mode={vanMode} FNAISCRDVAN 호출 원문={redactedRequestBody}", code: null, txId);

                // P24-3(docs/operations/development_plan.md) — P/Invoke 호출·NUL 종단·버퍼 할당·예외
                // 차단은 FnaisCrdVanInvoker로 옮겨졌다. 이 메서드는 그 결과를 해석만 한다(응답 절단,
                // H-1/L-1 방어, 마스킹 로깅은 여기 그대로 남는다 — invoker마다 규칙이 다르기 때문).
                FnaisCrdVanInvokeResult invokeResult = await FnaisCrdVanInvoker.InvokeAsync(
                    vanMode, body, KftcGiroNative.DefaultTimeoutSeconds).ConfigureAwait(false);

                if (invokeResult.Threw)
                {
                    if (invokeResult.IsDllLoadFailure)
                    {
                        FileLogger.Error(LogCategory.Van, $"[VanService] 거래구분={transactionTypeCode} DLL 로드 실패: {invokeResult.Exception!.GetType().Name}: {invokeResult.Exception.Message}", code: null, txId);
                        return VanRelayOutcome.CommunicationFailure(VanFailureKind.DllLoadFailure, $"{invokeResult.Exception.GetType().Name}: {invokeResult.Exception.Message}");
                    }

                    // DLL 호출 실패로 앱이 죽으면 안 된다(PRD §9) — 어떤 예외도 밖으로 던지지 않는다.
                    FileLogger.Error(LogCategory.Van, $"[VanService] 거래구분={transactionTypeCode} 예상치 못한 예외: {invokeResult.Exception!.GetType().Name}: {invokeResult.Exception.Message}", code: null, txId);
                    return VanRelayOutcome.CommunicationFailure(VanFailureKind.CommunicationFailure, $"{invokeResult.Exception.GetType().Name}: {invokeResult.Exception.Message}");
                }

                int nRet = invokeResult.ReturnCode;
                byte[] outData = invokeResult.OutData;
                byte[] outRetCode = invokeResult.OutRetCode;
                try
                {
                    string retCodeText = DecodeNulTerminated(outRetCode);

                    // Phase 18 H-2(PIN이 스텁 응답에 실려 로그/화면에 노출된 전례)를 반영해, 응답 본문 자체는
                    // 여기서 남기지 않고 nRet/길이/소요시간만 남긴다 — 응답 전문 원문(마스킹 적용)은 아래
                    // responseBody 확보 시점에 별도로 남긴다(TelegramLogRedactor). P22-6 — FNAISCRDVAN 반환.
                    FileLogger.Info(
                        LogCategory.Van,
                        $"[VanService] 거래구분={transactionTypeCode} nRet={nRet} out_szRetCode='{retCodeText}' " +
                        $"본문길이={bodyLength} 소요={invokeResult.ElapsedMilliseconds}ms",
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

                        // Phase 25 P25-6(PRD.md §4.2 #7/#12) — responseBody는 여기서 만든 임시 버퍼가
                        // 아니라 VanRelayOutcome.Success로 반환돼 PosResponseTelegram.Relay(FromBytes,
                        // 복사 없음)를 거쳐 최종 POS 응답 본문 그 자체가 된다. 그래서 outData/outRetCode
                        // 와 달리 이 메서드 안에서 지우지 않는다 — 지우면 승인 응답이 그대로 깨진다.
                        //
                        // 클리어 시점(CP2 Opus 리뷰 개선권장 1, 2026-09-03 — 이전 버전 주석이 틀렸다):
                        // **PaymentOrchestrator.RunCardTransactionAsync의 finally가 아니다** — 그 finally는
                        // 응답을 실제로 보내기 *전에* 실행된다(RelayToVanAsync 반환 → 이 메서드 리턴
                        // → HandleCardApprovalAsync/HandleCardInfoInquiryAsync → RunCardTransactionAsync의
                        // finally 실행 → 그 뒤 TransactionQueue.WorkerLoop가 InvokeCompletedSafely로
                        // SendResponse를 호출). 이 배열은 **PosSocketServer.SendResponse가 WriteFrame으로
                        // 실제 송신을 끝낸 뒤 response.Telegram.ClearBody()로 지운다**(Services/Pos/
                        // PosSocketServer.cs 참고). 여기 다시 손대지 말 것 — 다음 사람이 이 주석만 보고
                        // RunCardTransactionAsync에 클리어를 넣으면 승인 응답이 0으로 덮인 채 나간다.
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
                            // 여기서도 responseBody는 즉시 클리어하지 않는다 — 실패 응답 자체는 이
                            // 배열을 담지 않으므로(CommunicationFailure는 responseBody를 넘기지 않는다)
                            // 값이 어디로도 새지 않는다. GC가 회수할 뿐이라 P25-5의 "즉시 클리어" 대상이
                            // 아니다(이 배열을 여기서 지운다고 심사 대응이 더 좋아지지 않는다 — 참조가
                            // 그대로 사라지므로).
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
                finally
                {
                    // Phase 25 P25-5(PRD.md §4.2 #11) — outData(4096B 원본)·outRetCode는 이 메서드
                    // 밖으로 절대 나가지 않는다(성공 시 그 슬라이스인 responseBody만 나간다). 모든
                    // 분기 공통으로 여기서 지운다.
                    SecureClear.Clear(outData);
                    SecureClear.Clear(outRetCode);
                }
            }
            finally
            {
                // Phase 25 P25-5(PRD.md §4.2 #9) — populatedRequest.Telegram.ToBody()가 만든 요청
                // 본문 복사본. FnaisCrdVanInvoker.InvokeAsync에 넘겨졌지만 그 내부에서 다시 복사해
                // NUL 종단하므로(그 복사본은 FnaisCrdVanInvoker가 자체적으로 지운다), 이 로컬 변수
                // 자체는 이 메서드 밖으로 나가지 않는다.
                SecureClear.Clear(body);
            }
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
