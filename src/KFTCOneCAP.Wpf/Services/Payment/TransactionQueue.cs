using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// PRD §3.2/§8.1 "동시에 두 거래가 리더기 또는 VAN 통신을 수행해서는 안 된다"를 보장하는 **유일한**
/// 직렬화 지점(docs/payment_relay/development_plan.md P14-3, Phase 17에서 전문 타입만 교체 — P17-5).
/// 이 앱 전체에서 "거래는 한 번에 하나만 처리한다"를 지키는 코드는 이 클래스 하나뿐이어야 한다.
///
/// - **전용 워커 스레드 1개**가 FIFO로 소비한다.
/// - 실제 처리 로직은 <see cref="_processor"/>로 **주입**받는다 — 지금은 <c>PaymentOrchestrator.
///   ProcessAsync</c>가 꽂힌다. 이 클래스 자체는 소켓/리더기/VAN/전문 형식 중 무엇도 알지 못한다
///   (Phase 17에서 전문 형식이 임시 `PosPaymentRequest`에서 실제 SPEC `PosRequestTelegram`으로
///   바뀌었어도 이 클래스는 타입 매개변수만 바뀌었을 뿐 로직은 그대로다).
/// - 워커 루프 최상위에 try/catch가 있다 — 처리 중 예외가 워커 스레드를 죽이면 그 뒤 모든 거래가
///   영원히 멈춘다.
/// - **처리 위임은 <c>Task</c>를 반환한다** — Flow가 쓰는 부품(리더기 명령, 무결성 체크, VAN 호출)이
///   전부 비동기이기 때문이다. 이 앱에서 그 <c>Task</c>를 동기적으로 기다리는 지점은
///   <see cref="WorkerLoop"/>의 <c>GetAwaiter().GetResult()</c> **한 줄뿐**이어야 한다.
/// </summary>
internal sealed class TransactionQueue
{
    private readonly BlockingCollection<TransactionWorkItem> _queue = new();
    private readonly Func<PosRequestTelegram, Task<PosResponseTelegram>> _processor;
    private readonly Thread _workerThread;

    /// <summary>지금 워커가 거래를 처리 중인가(Phase 16, P16-5) — <c>HomeWindow</c>가 "거래 진행 중
    /// 리더기 설정 화면 열기"를 막는 판정 기준으로 쓰는 **유일한** 읽기 전용 신호다.</summary>
    internal bool IsProcessing => _isProcessing;

    private volatile bool _isProcessing;

    internal TransactionQueue(Func<PosRequestTelegram, Task<PosResponseTelegram>> processor)
    {
        _processor = processor;
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "PaymentTransactionWorker" };
        _workerThread.Start();
    }

    /// <summary>
    /// 요청을 큐에 넣는다. 처리 결과(정상/예외 둘 다)는 워커 스레드에서 <paramref name="onCompleted"/>로
    /// 통지된다 — 그 요청이 들어온 소켓 연결로 회신하는 책임은 호출자(<c>PosSocketServer</c>)에 있다.
    /// </summary>
    internal void Enqueue(PosRequestTelegram request, Action<PosResponseTelegram> onCompleted)
    {
        _queue.Add(new TransactionWorkItem(request, onCompleted));
    }

    /// <summary>앱 종료 시 호출(App.xaml.cs OnExit). 새 항목 추가를 막고, 워커가 마저 처리 중이던 항목을
    /// 끝낼 시간을 <paramref name="timeout"/>만큼만 준 뒤 반환한다.</summary>
    internal void Stop(TimeSpan timeout)
    {
        _queue.CompleteAdding();
        _workerThread.Join(timeout);
    }

    private void WorkerLoop()
    {
        foreach (TransactionWorkItem item in _queue.GetConsumingEnumerable())
        {
            string txType = item.Request.TransactionTypeCode;
            _isProcessing = true;
            try
            {
                FileLogger.Info($"[TransactionQueue] 처리 시작 전문={txType}");
                // 이 앱에서 처리 Task를 동기적으로 기다리는 유일한 지점(P15-1) — 클래스 주석 참고.
                PosResponseTelegram response = _processor(item.Request).GetAwaiter().GetResult();
                FileLogger.Info($"[TransactionQueue] 처리 종료 전문={txType}");
                InvokeCompletedSafely(item, response);
            }
            catch (Exception ex)
            {
                FileLogger.Error($"[TransactionQueue] 처리 중 예외 전문={txType}: {ex}");
                // 결과코드 리터럴을 직접 쓰지 않고 PosResultCodeMapper를 거친다(P15-3/P17-4 — Flow/큐
                // 어디에도 전문 코드 문자열이 등장하지 않아야 한다).
                PosResponseTelegram fallback = PosResponseTelegram.Failure(
                    item.Request, PosResultCodeMapper.ToTelegramCode(PosPaymentResultCode.InternalError));
                InvokeCompletedSafely(item, fallback);
            }
            finally
            {
                // Phase 25 P25-6(PRD.md §4.2 #7, 요청 쪽) — item.Request.Telegram의 원본 _body를
                // 여기서 지운다. **PaymentOrchestrator.RunCardTransactionAsync의 finally에서 지우지
                // 않는다** — 위 catch 블록의 PosResponseTelegram.Failure(item.Request, ...)가 실패
                // 응답을 합성할 때 item.Request.Telegram을 Clone()해야 하므로, 그보다 먼저 지워버리면
                // #3/#6/#7/#8/#51 외의 필드(전문관리번호·금액 등)가 전부 깨진 실패 응답이 POS로 나간다
                // (구현 중 발견해 계획을 수정한 지점 — PRD.md §4.3.3 근거 문단 참고). InvokeCompletedSafely
                // 가 이미 SendResponse(동기, 프레임 송신까지 완료)를 호출했으므로, 이 지점(성공/예외
                // 두 분기 모두가 합류하는 finally)이 "요청을 더 이상 아무도 읽지 않는" 가장 이른
                // 시점이다.
                item.Request.Telegram.ClearBody();
                _isProcessing = false;
            }
        }
    }

    /// <summary>
    /// onCompleted(회신 콜백) 자체가 던지는 예외까지 워커 밖으로 새면 다음 큐 항목을 영영 못 받는다
    /// — 이 메서드가 그 마지막 안전판이다.
    /// </summary>
    private static void InvokeCompletedSafely(TransactionWorkItem item, PosResponseTelegram response)
    {
        try
        {
            item.OnCompleted(response);
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[TransactionQueue] 완료 콜백 처리 중 예외 전문={item.Request.TransactionTypeCode}: {ex}");
        }
    }

    private sealed class TransactionWorkItem
    {
        internal TransactionWorkItem(PosRequestTelegram request, Action<PosResponseTelegram> onCompleted)
        {
            Request = request;
            OnCompleted = onCompleted;
        }

        internal PosRequestTelegram Request { get; }

        internal Action<PosResponseTelegram> OnCompleted { get; }
    }
}
