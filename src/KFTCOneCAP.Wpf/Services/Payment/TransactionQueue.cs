using System;
using System.Collections.Concurrent;
using System.Threading;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// PRD §3.2/§8.1 "동시에 두 거래가 리더기 또는 VAN 통신을 수행해서는 안 된다"를 보장하는 **유일한**
/// 직렬화 지점(docs/payment_relay/development_plan.md P14-3). 이 앱 전체에서 "거래는 한 번에 하나만
/// 처리한다"를 지키는 코드는 이 클래스 하나뿐이어야 한다 — 다른 곳에서 또 잠그거나 큐를 만들면
/// Phase 16의 경합(취소/Timeout/중복 CALLBACK) 검증이 원인을 추적할 수 없는 구조가 된다.
///
/// - **전용 워커 스레드 1개**가 FIFO로 소비한다. 스레드풀(Task.Run)을 쓰지 않는 이유: 처리 중 블로킹
///   (리더기 응답 대기, VAN 호출)이 길어질 수 있어 스레드풀 기아를 만들 수 있고, "워커는 정확히 하나"라는
///   사실이 코드 구조에서 눈에 보여야 한다.
/// - 실제 처리 로직은 <see cref="_processor"/>로 **주입**받는다 — Phase 14는 스텁(App.xaml.cs)을 넣고,
///   Phase 15가 이 자리에 <c>PaymentOrchestrator</c>를 꽂는다. 이 클래스 자체는 소켓/리더기/VAN 중
///   무엇도 알지 못한다.
/// - 워커 루프 최상위에 try/catch가 있다 — 처리 중 예외가 워커 스레드를 죽이면 그 뒤 모든 거래가
///   영원히 멈춘다("앱은 살아 있는데 결제만 안 됨"이라는, 원인 파악이 오래 걸리는 사고).
/// </summary>
internal sealed class TransactionQueue
{
    private readonly BlockingCollection<TransactionWorkItem> _queue = new();
    private readonly Func<PosPaymentRequest, PosPaymentResponse> _processor;
    private readonly Thread _workerThread;

    internal TransactionQueue(Func<PosPaymentRequest, PosPaymentResponse> processor)
    {
        _processor = processor;
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "PaymentTransactionWorker" };
        _workerThread.Start();
    }

    /// <summary>
    /// 요청을 큐에 넣는다. 처리 결과(정상/예외 둘 다)는 워커 스레드에서 <paramref name="onCompleted"/>로
    /// 통지된다 — 그 요청이 들어온 소켓 연결로 회신하는 책임은 호출자(<c>PosSocketServer</c>)에 있다.
    /// 이 메서드는 어느 스레드에서 호출해도 안전하다(<see cref="BlockingCollection{T}.Add(T)"/> 자체가
    /// 스레드 안전).
    /// </summary>
    internal void Enqueue(PosPaymentRequest request, Action<PosPaymentResponse> onCompleted)
    {
        _queue.Add(new TransactionWorkItem(request, onCompleted));
    }

    /// <summary>
    /// 앱 종료 시 호출(App.xaml.cs OnExit). 새 항목 추가를 막고, 워커가 마저 처리 중이던 항목을 끝낼
    /// 시간을 <paramref name="timeout"/>만큼만 준 뒤 반환한다 — 무한 대기하지 않는다.
    /// </summary>
    internal void Stop(TimeSpan timeout)
    {
        _queue.CompleteAdding();
        _workerThread.Join(timeout);
    }

    private void WorkerLoop()
    {
        foreach (TransactionWorkItem item in _queue.GetConsumingEnumerable())
        {
            string txId = item.Request.TransactionId;
            try
            {
                FileLogger.Info($"[TransactionQueue] 처리 시작 txId={txId}");
                PosPaymentResponse response = _processor(item.Request);
                FileLogger.Info($"[TransactionQueue] 처리 종료 txId={txId}");
                InvokeCompletedSafely(item, response);
            }
            catch (Exception ex)
            {
                FileLogger.Error($"[TransactionQueue] 처리 중 예외 txId={txId}: {ex}");
                // 메시지는 반드시 PosMessageEncoding(ASCII)로 안전하게 표현 가능한 문자만 써야 한다 —
                // 한글을 넣으면 ASCII 인코딩 시 '?'로 깨진다(2026-08-24 --pos-client-test로 실측 발견).
                InvokeCompletedSafely(item, new PosPaymentResponse("99", txId, "INTERNAL_ERROR"));
            }
        }
    }

    /// <summary>
    /// onCompleted(회신 콜백) 자체가 던지는 예외까지 워커 밖으로 새면 다음 큐 항목을 영영 못 받는다
    /// — 이 메서드가 그 마지막 안전판이다.
    /// </summary>
    private static void InvokeCompletedSafely(TransactionWorkItem item, PosPaymentResponse response)
    {
        try
        {
            item.OnCompleted(response);
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[TransactionQueue] 완료 콜백 처리 중 예외 txId={item.Request.TransactionId}: {ex}");
        }
    }

    private sealed class TransactionWorkItem
    {
        internal TransactionWorkItem(PosPaymentRequest request, Action<PosPaymentResponse> onCompleted)
        {
            Request = request;
            OnCompleted = onCompleted;
        }

        internal PosPaymentRequest Request { get; }

        internal Action<PosPaymentResponse> OnCompleted { get; }
    }
}
