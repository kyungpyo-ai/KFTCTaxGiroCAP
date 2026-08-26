using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
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
/// - 실제 처리 로직은 <see cref="_processor"/>로 **주입**받는다 — Phase 14는 스텁(App.xaml.cs)을 넣었고,
///   Phase 15(docs/payment_relay/development_plan.md P15-1)부터 이 자리에 <c>PaymentOrchestrator</c>를
///   꽂는다. 이 클래스 자체는 소켓/리더기/VAN 중 무엇도 알지 못한다.
/// - 워커 루프 최상위에 try/catch가 있다 — 처리 중 예외가 워커 스레드를 죽이면 그 뒤 모든 거래가
///   영원히 멈춘다("앱은 살아 있는데 결제만 안 됨"이라는, 원인 파악이 오래 걸리는 사고).
/// - **처리 위임은 <c>Task</c>를 반환한다**(P15-1) — Flow가 쓰는 부품(리더기 명령, 무결성 체크, VAN
///   호출)이 전부 비동기이기 때문이다. 이 앱에서 그 <c>Task</c>를 동기적으로 기다리는 지점은
///   <see cref="WorkerLoop"/>의 <c>GetAwaiter().GetResult()</c> **한 줄뿐**이어야 한다 — 여기저기서
///   각자 기다리면 "무엇이 언제 완료되는가"를 추적할 수 없는 구조가 된다.
///   데드락 걱정이 없는 이유: 이 워커 스레드는 <see cref="SynchronizationContext"/>가 없는 전용
///   <see cref="Thread"/>이고(스레드풀도, UI 스레드도 아니다), <c>Services/</c> 내부는 공통 규칙 5에
///   따라 항상 <c>ConfigureAwait(false)</c>를 쓴다 — 그래서 await된 continuation이 "원래 스레드로
///   돌아가려고" 이 스레드를 다시 필요로 하는 일이 없다(sync-over-async 데드락의 전형적 조건이
///   애초에 성립하지 않는다). <c>.Result</c>가 아니라 <c>GetAwaiter().GetResult()</c>를 쓰는 이유는
///   예외가 <see cref="AggregateException"/>으로 감싸이지 않고 원래 타입 그대로 올라오게 하기 위함
///   — 아래 catch의 <c>ex.GetType().Name</c> 로그나 향후 특정 예외 타입 분기가 어긋나지 않는다.
/// </summary>
internal sealed class TransactionQueue
{
    private readonly BlockingCollection<TransactionWorkItem> _queue = new();
    private readonly Func<PosPaymentRequest, Task<PosPaymentResponse>> _processor;
    private readonly Thread _workerThread;

    /// <summary>지금 워커가 거래를 처리 중인가(Phase 16, P16-5) — <c>HomeWindow</c>가 "거래 진행 중
    /// 리더기 설정 화면 열기"를 막는 판정 기준으로 쓰는 **유일한** 읽기 전용 신호다. 새 잠금 장치를
    /// 만들지 않는다(P14-3 "직렬화 지점은 이 클래스 하나뿐" 규칙 — 이 필드는 그 직렬화 상태를
    /// 노출만 할 뿐 별도로 무언가를 잠그지 않는다).</summary>
    internal bool IsProcessing => _isProcessing;

    private volatile bool _isProcessing;

    internal TransactionQueue(Func<PosPaymentRequest, Task<PosPaymentResponse>> processor)
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
            _isProcessing = true;
            try
            {
                FileLogger.Info($"[TransactionQueue] 처리 시작 txId={txId}");
                // 이 앱에서 처리 Task를 동기적으로 기다리는 유일한 지점(P15-1) — 클래스 주석 참고.
                PosPaymentResponse response = _processor(item.Request).GetAwaiter().GetResult();
                FileLogger.Info($"[TransactionQueue] 처리 종료 txId={txId}");
                InvokeCompletedSafely(item, response);
            }
            catch (Exception ex)
            {
                FileLogger.Error($"[TransactionQueue] 처리 중 예외 txId={txId}: {ex}");
                // 메시지는 반드시 PosMessageEncoding(ASCII)로 안전하게 표현 가능한 문자만 써야 한다 —
                // 한글을 넣으면 ASCII 인코딩 시 '?'로 깨진다(2026-08-24 --pos-client-test로 실측 발견).
                // 결과코드 리터럴을 직접 쓰지 않고 PosPaymentResponse.Create를 거친다(P15-3 — Flow/큐
                // 어디에도 전문 코드 문자열이 등장하지 않아야 한다).
                InvokeCompletedSafely(item, PosPaymentResponse.Create(PosPaymentResultCode.InternalError, txId, "INTERNAL_ERROR"));
            }
            finally
            {
                _isProcessing = false;
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
