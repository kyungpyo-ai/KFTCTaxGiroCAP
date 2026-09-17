using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Security;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.Services.Pos;

/// <summary>
/// PRD §3.1 <c>localhost:8002</c> 소켓 서버(docs/payment_relay/development_plan.md P14-2). 앱 수명과
/// 함께 <c>Start</c>/<c>Stop</c>되며(App.xaml.cs), 연결마다 별도 스레드로 수신 루프를 돌린다.
///
/// - **루프백 전용**(<see cref="IPAddress.Loopback"/>) — 결제 요청을 받는 서버를 LAN에 노출할 이유가
///   없다.
/// - **지속 연결 + 다중 클라이언트**(PRD §10.1 2026-08-24 확정). POS 쪽 원칙은 "전문 한 번 주고받고
///   연결 종료"지만, 이 서버는 그 원칙에 기대지 않는다 — 연결마다 붙는 수신 루프가 그 연결이 끊길
///   때까지 계속 프레임을 뽑아 큐로 넘기므로, POS가 한 번만 보내고 끊어도, 실수로 안 끊고 계속
///   보내도 **서버 쪽 코드가 둘을 구분할 필요가 없다**.
/// - 동시 연결 상한 16(원본 MFC 앱의 <c>CLIENT_MAX</c> 상수를 근거로 삼음). 초과 연결은 즉시 닫는다.
/// - **응답 후 유휴 연결 자동 종료**(2026-08-24 사용자 확정) — POS가 응답을 받고도 연결을 안 닫는
///   개발 실수에 대비해, 응답 전송 후 <see cref="IdleAfterResponseTimeoutMilliseconds"/> 안에 다음
///   요청이 없으면 서버가 그 연결을 먼저 닫는다. 지속 연결 자체는 유지된다 — <c>NetworkStream</c>의
///   **네이티브** <c>ReadTimeout</c>을 그대로 쓴다(연결 스레드가 자기 응답이 실제로 나갈 때까지
///   기다린 뒤, 같은 스레드에서 다음 <c>Read</c> 직전에 타임아웃을 건다 — 별도 타이머나 소켓을
///   강제로 닫는 우회가 필요 없다. 자세한 이유는 <see cref="HandleConnection"/> 주석 참고).
/// - **계층 규칙**: 이 클래스는 WPF 타입(Dispatcher/Window)을 알지 못하고, 프레임 바이트 오프셋도
///   직접 다루지 않는다 — 전부 <see cref="PosMessageFramer"/>/<see cref="PosRequestTelegram"/>
///   (<c>Protocol/Pos/</c>)에 위임한다. STX/길이 필드·SPEC 필드 오프셋 같은 내부 구현은 이 클래스에
///   드러나지 않는다(P14-1, Phase 17에서 실제 SPEC 전문으로 교체돼도 이 규칙 덕분에 이 파일은 타입
///   이름만 바뀌었다 — P17-5).
/// </summary>
internal sealed class PosSocketServer
{
    private const int Port = 8002;
    private const int MaxConcurrentConnections = 16;
    private const int ReceiveBufferSize = 4096;

    /// <summary>P22-6(PRD.md §1.5 경계 표 "POS 소켓") — 요청/응답 공통부의 전문관리번호(<c>#9</c>,
    /// <c>PaymentOrchestrator.LogTxId</c>와 동일한 필드, 3전문 공통 POSITION/길이)와 결과코드
    /// (<c>#7</c>). 요청 수신·응답 송신 로그의 카테고리·코드·거래ID를 채우는 데만 쓴다 — 전문 본문을
    /// 해석하지 않는다(§1.5 "전문 본문 전체는 남기지 않는다"와 별개로, 식별 필드 2개만 읽는다).</summary>
    private const int ManagementNumberFieldNumber = 9;

    private const int ResultCodeFieldNumber = 7;

    /// <summary>
    /// 응답 전송(<see cref="SendResponse"/>)의 최대 대기 시간(ms). (Opus 검증 리뷰 2026-08-24, H-1)
    /// <see cref="SendResponse"/>는 <see cref="TransactionQueue"/>의 **유일한 워커 스레드에서 동기
    /// 호출**된다 — 이 타임아웃이 없으면 응답을 안 읽는 POS 클라이언트 하나 때문에 <c>stream.Write</c>가
    /// 무한 대기하고, 그러면 그 뒤 큐에 쌓인 다른 모든 터미널의 결제 요청이 전부 멈춘다(P14-3의
    /// "워커는 계속 전진한다"는 불변조건을 깨는 지점). 타임아웃이 지나면 <see cref="IOException"/>이
    /// 던져지고 <see cref="SendResponse"/>의 기존 catch가 "응답 폐기" 로그로 흡수해 워커가 다음
    /// 항목으로 넘어간다.
    /// </summary>
    private const int SendTimeoutMilliseconds = 5000;

    /// <summary>
    /// 응답을 보낸 뒤 이 시간(ms) 안에 그 연결에서 다음 요청이 오지 않으면 서버가 먼저 연결을 닫는다
    /// (2026-08-20 사용자 확정 — POS 쪽 개발 실수로 응답을 받고도 연결을 안 닫는 경우 대비). 지속
    /// 연결 자체는 유지한다: 같은 연결로 여러 요청을 보내는 정상 케이스(P14-2)는 매 응답 뒤 이 값으로
    /// <c>stream.ReadTimeout</c>이 다시 걸리고, 그 안에 다음 요청이 오면 정상 처리 후 또 다시
    /// 걸린다 — 영향받지 않는다. **최초 요청을 기다리는 동안**(아직 응답을 한 번도 보내지 않은
    /// 상태)은 <c>ReadTimeout</c>을 건드리지 않아(기본값 <see cref="Timeout.Infinite"/>) 여전히
    /// 무제한 대기한다.
    /// </summary>
    private const int IdleAfterResponseTimeoutMilliseconds = 10000;

    private readonly TransactionQueue _queue;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Thread? _acceptThread;
    private int _connectionCount;

    internal PosSocketServer(TransactionQueue queue)
    {
        _queue = queue;
    }

    /// <summary>바인딩에 성공해 실제로 리스닝 중인지. 포트 점유 등으로 <see cref="Start"/>가 실패하면 false.</summary>
    internal bool IsRunning => _listener != null;

    /// <summary>
    /// 앱 기동 시 1회 호출(App.xaml.cs OnStartup). 포트가 이미 사용 중이면(PRD §9) **앱을 죽이지
    /// 않는다** — 로그만 남기고 소켓 서버 없이 앱은 정상 기동한다. 이 앱은 트레이 상주로 자동
    /// 최소화 기동하므로 기동 시점에 모달을 띄워도 사용자가 보지 못한다(P12-1에서 확립한 방침과 동일).
    /// </summary>
    internal void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
        }
        catch (SocketException ex)
        {
            FileLogger.Error(
                LogCategory.Pos,
                $"[PosSocketServer] {Port} 포트 리스닝 실패({ex.SocketErrorCode}): {ex.Message} — 소켓 서버 없이 앱 계속 기동",
                InternalFaultCodes.ListenFailure, transactionId: null);
            Task.Run(() =>
            {
                try { FaultAlertJudge.OnCodeObserved(InternalFaultCodes.ListenFailure, LogCategory.Pos, null); }
                catch { /* P27-9-(e) 이중 방어 — 삼킨다 */ }
            });
            _listener = null;
            return;
        }

        _cts = new CancellationTokenSource();
        _acceptThread = new Thread(() => AcceptLoop(_cts.Token)) { IsBackground = true, Name = "PosSocketAccept" };
        _acceptThread.Start();
        FileLogger.Info(LogCategory.Pos, $"[PosSocketServer] {Port} 포트 리스닝 시작");
    }

    /// <summary>앱 종료 시 호출(App.xaml.cs OnExit, PRD §9 리소스 정리).</summary>
    internal void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch (Exception ex)
        {
            FileLogger.Warn(LogCategory.Pos, $"[PosSocketServer] 리스너 정지 중 예외(무시): {ex.Message}");
        }

        _acceptThread?.Join(TimeSpan.FromSeconds(2));
        _listener = null;
        FileLogger.Info(LogCategory.Pos, "[PosSocketServer] 정지");
    }

    /// <summary>
    /// 수락 루프. <see cref="Stop"/>이 리스너를 닫으면 <see cref="TcpListener.AcceptTcpClient"/>가
    /// 예외를 던지며 빠져나오는 것이 정상 종료 경로다(PRD §9 — 이 예외를 앱 도메인 밖으로 흘리지 않는다).
    /// 두 경우 다 루프를 더 돌 수 없어 종료하는 것은 같지만(★ 이 루프가 죽으면 그 뒤로 새 연결을 전혀
    /// 못 받는다는 뜻이므로), <see cref="Stop"/>이 원인이 아닌 **의도치 않은** 예외라면 원인을 알 수 있게
    /// ERROR로 남긴다(Opus 검증 리뷰 2026-08-24, M-1 — 예전엔 두 경우를 구분하지 않아 진짜 오류가 나도
    /// 로그 한 줄 없이 조용히 수락이 멈췄다).
    /// </summary>
    private void AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = _listener!.AcceptTcpClient();
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    FileLogger.Error(
                        LogCategory.Pos,
                        $"[PosSocketServer] 수락 루프가 예기치 않은 예외로 종료됨(이후 새 연결을 받지 못함): {ex}",
                        InternalFaultCodes.AcceptLoopDied, transactionId: null);
                    Task.Run(() =>
                    {
                        try { FaultAlertJudge.OnCodeObserved(InternalFaultCodes.AcceptLoopDied, LogCategory.Pos, null); }
                        catch { /* P27-9-(e) 이중 방어 — 삼킨다 */ }
                    });
                }

                break; // token.IsCancellationRequested==true면 Stop()에 의한 정상 종료.
            }

            if (Interlocked.Increment(ref _connectionCount) > MaxConcurrentConnections)
            {
                Interlocked.Decrement(ref _connectionCount);
                FileLogger.Warn(
                    LogCategory.Pos,
                    $"[PosSocketServer] 동시 연결 상한({MaxConcurrentConnections}) 초과 — 연결 거부",
                    InternalFaultCodes.ConnectionLimitExceeded, transactionId: null);
                Task.Run(() =>
                {
                    try { FaultAlertJudge.OnCodeObserved(InternalFaultCodes.ConnectionLimitExceeded, LogCategory.Pos, null); }
                    catch { /* P27-9-(e) 이중 방어 — 삼킨다 */ }
                });
                SafeClose(client);
                continue;
            }

            var connectionThread = new Thread(() => HandleConnection(client, token)) { IsBackground = true, Name = "PosSocketConn" };
            connectionThread.Start();
        }
    }

    /// <summary>
    /// ★ 응답 후 유휴 타임아웃은 네이티브 <c>stream.ReadTimeout</c>(=<c>SO_RCVTIMEO</c>)로만 구현한다
    /// — 별도 타이머나 소켓 강제 닫기 없음. 관건은 **어느 스레드가, 언제** 이 값을 설정하느냐다:
    /// Windows 소켓은 이미 블로킹 진입한 <c>Read</c> 호출에는 <c>ReadTimeout</c> 변경이 소급 적용되지
    /// 않는다 — 그래서 응답을 실제로 보내는 스레드(<see cref="TransactionQueue"/> 워커)가 아니라, 다음
    /// <c>Read</c>를 호출할 **이 스레드 자신이**, 그 호출 직전에 값을 건다. 이를 위해 프레임을 큐에
    /// 넣은 뒤 <paramref name="responseSent"/>로 **그 응답이 실제로 나갈 때까지 대기**한다(POS는 원래
    /// 응답을 기다렸다가 다음 요청을 보내는 동기 프로토콜이라 이 대기가 별도 지연을 만들지 않는다).
    /// 마지막으로 처리한 프레임의 응답까지 다 나간 뒤에야 <see cref="IdleAfterResponseTimeoutMilliseconds"/>를
    /// 걸고 다음 <c>Read</c>로 들어간다. 최초 요청을 기다리는 첫 <c>Read</c>는 아무 응답도 보낸 적이
    /// 없으므로 <c>ReadTimeout</c>을 건드리지 않아 무제한 대기 그대로다.
    /// </summary>
    private void HandleConnection(TcpClient client, CancellationToken token)
    {
        string remote = SafeRemoteEndPoint(client);

        // 2026-09-17 사용자 요청 — 연결(=거래) 1건이 시작되는 지점에 구분선을 남긴다(거래 종료와
        // 대칭). 이 저장소는 "전문마다 TCP 연결을 새로 연다"는 전제라(PRD.md §4.3) 연결 수락이 곧
        // 거래 시작이다. 여러 연결이 시간상 겹칠 때(§1.12, 동시 연결 최대 16개 지원) 로그 줄이
        // 뒤섞이는 걸 완전히 풀어내진 못하지만, 최소한 "이 줄부터 새 거래가 시작됐다"는 지점만큼은
        // 항상 눈에 띄게 한다. 구분선과 "연결 수락" 로그를 WriteBoundaryThenWrite 하나로 원자적으로
        // 기록한다(따로 호출하면 그 사이 틈에 다른 연결의 줄이 끼어드는 문제가 있음 — 거래 종료
        // 쪽에서 실측으로 확인된 것과 같은 문제).
        FileLogger.WriteBoundaryThenWrite(LogCategory.Pos, $"[PosSocketServer] 연결 수락: {remote}", remote);

        var framer = new PosMessageFramer();
        var writeLock = new object();
        using var responseSent = new ManualResetEventSlim(false);

        try
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                stream.WriteTimeout = SendTimeoutMilliseconds; // H-1 — 응답 쓰기가 워커 스레드를 무한정 붙잡지 않도록.
                var buffer = new byte[ReceiveBufferSize];
                while (!token.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        read = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException ex) when (IsReadTimeout(ex))
                    {
                        FileLogger.Warn(LogCategory.Pos, $"[PosSocketServer] {remote} 응답 전송 후 {IdleAfterResponseTimeoutMilliseconds}ms 동안 다음 요청이 없어 서버가 먼저 닫음(POS 개발 실수 대비)");
                        break;
                    }
                    catch (IOException ex)
                    {
                        FileLogger.Info(LogCategory.Pos, $"[PosSocketServer] {remote} 연결 단절(수신 중): {ex.Message}");
                        break;
                    }

                    if (read == 0)
                    {
                        FileLogger.Info(LogCategory.Pos, $"[PosSocketServer] {remote} 정상 종료(FIN)");
                        break;
                    }

                    IReadOnlyList<byte[]> frames;
                    try
                    {
                        var chunk = new byte[read];
                        Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                        frames = framer.Append(chunk);
                    }
                    catch (PosProtocolException ex)
                    {
                        // 길이 필드 하나로만 경계를 정하는 프레이밍이라 재동기화할 방법이 없다(P14-1) —
                        // 이 연결을 통째로 닫는다. 서버·다른 연결은 계속 살아 있다(P14-5).
                        // P27-8-f(fault_alert_catalog.md §2.3) — 침묵하지 않고 E41과 같은 메커니즘으로
                        // 최소 공통부 응답을 만들어 회신한 뒤 연결을 닫는다. #4를 읽을 수단이 아예
                        // 없으므로 placeholder "000000"을 싣는다(E42와 동일 근거, 아래 참고).
                        FileLogger.Warn(
                            LogCategory.Pos,
                            $"[PosSocketServer] {remote} 전문 형식 오류 — 응답 회신 후 연결 종료: {ex.Message}",
                            "E43", transactionId: null);
                        Task.Run(() =>
                        {
                            try { FaultAlertJudge.OnCodeObserved("E43", LogCategory.Pos, null); }
                            catch { /* P27-9-(e) 이중 방어 — 삼킨다 */ }
                        });
                        byte[] framingErrorFrame = PosUnknownTransactionErrorResponse.Build("000000", "E43");
                        WriteFrame(framingErrorFrame, stream, writeLock, remote, "전문 형식 오류(E43)");
                        break;
                    }

                    bool responseSentThisRound = false;
                    foreach (byte[] frame in frames)
                    {
                        responseSent.Reset();
                        if (HandleFrame(frame, stream, writeLock, remote, responseSent))
                        {
                            responseSent.Wait(); // 이 프레임의 응답이 실제로 나갈 때까지 대기(성공/실패 무관, 항상 신호됨).
                            responseSentThisRound = true;
                        }
                    }

                    if (responseSentThisRound)
                    {
                        stream.ReadTimeout = IdleAfterResponseTimeoutMilliseconds; // 다음 Read부터 유휴 타임아웃 적용.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                LogCategory.Pos,
                $"[PosSocketServer] {remote} 처리 중 예외: {ex}",
                InternalFaultCodes.ConnectionHandlingException, transactionId: null);
            Task.Run(() =>
            {
                try { FaultAlertJudge.OnCodeObserved(InternalFaultCodes.ConnectionHandlingException, LogCategory.Pos, null); }
                catch { /* P27-9-(e) 이중 방어 — 삼킨다 */ }
            });
        }
        finally
        {
            Interlocked.Decrement(ref _connectionCount);

            // 2026-09-17 사용자 지적 — 이 연결(=거래) 1건의 모든 종료 경로(정상 FIN/유휴 타임아웃/
            // 연결 단절/프로토콜 예외/처리 중 예외)가 예외 없이 지나가는 지점이 여기뿐이다. 응답을
            // 아예 못 만든 경로(예: 위 catch (PosProtocolException) 분기, 큐잉 전 실패)도 포함해
            // "이 연결의 진짜 마지막 줄"을 보장하려면 SendResponse 쪽(비동기 판정과 경합)이 아니라
            // 여기서 찍어야 한다. 처음엔 "연결 종료" 로그(FileLogger.Info)와 구분선을 따로 호출했는데,
            // 그 사이 틈에 다른 연결의 줄이 끼어드는 게 실측(연결 2개 겹침)으로 확인돼 WriteThenBoundary
            // 하나로 합쳤다(둘을 같은 락 안에서 원자적으로 기록).
            FileLogger.WriteThenBoundary(LogCategory.Pos, $"[PosSocketServer] 연결 종료: {remote}", remote);
        }
    }

    private static bool IsReadTimeout(IOException ex) =>
        ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut;

    /// <summary>
    /// 파싱에 성공해 큐에 넣었거나, 실패 응답을 즉시 써 보냈으면 true(호출자가
    /// <paramref name="responseSent"/>를 기다려야 함). 응답조차 만들 수 없는 형식 오류(전문 종류를
    /// 식별할 최소 16바이트에도 못 미침, P17-3)로 그 프레임만 버렸으면 false.
    /// </summary>
    private bool HandleFrame(byte[] frame, NetworkStream stream, object writeLock, string remote, ManualResetEventSlim responseSent)
    {
        PosRequestParseOutcome outcome;
        try
        {
            outcome = PosRequestTelegram.Parse(frame);
        }
        catch (PosProtocolException ex)
        {
            // 프레임 경계는 이미 지켜졌으므로(형식 오류와 다름) 이 프레임만 실패 처리하고 연결은
            // 유지한다. #4(거래 구분 코드)조차 읽을 수 없을 만큼 짧은 본문만 여기 온다(P17-3).
            //
            // P27-8-f(fault_alert_catalog.md §2.3) — 예전에는 응답을 만들 스키마 근거가 없다는
            // 이유로 침묵했지만, E41과 같은 메커니즘(공통부 70바이트만으로 응답 조립)을 그대로 쓰면
            // #4를 몰라도 응답을 만들 수 있다. #4 자리에는 실제 값을 알 수 없으므로 placeholder
            // "000000"을 쓴다 — 이 코드베이스가 이미 "N형 필드 값이 없으면 0으로 채운다"는 관례를
            // 갖고 있다(PosInquiryResponseTelegram.cs, PRD.md §3.4.5)는 근거로 2026-09-14 사용자
            // 확정. POS가 숫자 파싱에 실패하지 않는 값이다.
            FileLogger.Warn(
                LogCategory.Pos,
                $"[PosSocketServer] {remote} 요청 파싱 오류 — 응답 회신(이 프레임만 실패, 연결 유지): {ex.Message}",
                "E42", transactionId: null);
            Task.Run(() =>
            {
                try { FaultAlertJudge.OnCodeObserved("E42", LogCategory.Pos, null); }
                catch { /* P27-9-(e) 이중 방어 — 삼킨다 */ }
            });
            byte[] tooShortErrorFrame = PosUnknownTransactionErrorResponse.Build("000000", "E42");
            WriteFrame(tooShortErrorFrame, stream, writeLock, remote, "요청 파싱 오류(E42)");
            responseSent.Set();
            return true;
        }

        if (!outcome.IsSuccess)
        {
            // E40(길이 불일치)/E41(알 수 없는 거래구분) — 전문 계층(P17-3)이 이미 완성된 응답 프레임을
            // 만들어 뒀다. Flow(큐)를 거칠 이유가 없는 순수 프로토콜 오류이므로 여기서 바로 써 보낸다.
            // 개선권장 B(P22 리뷰) — 이 두 응답은 SendResponse를 거치지 않아 "응답 송신" 구조화 로그가
            // 없었다. 여기서 코드 슬롯을 채운 로그를 한 줄 남긴 뒤 WriteFrame으로 보낸다(응답 관리번호는
            // 파싱 자체가 실패한 경우가 대부분이라 알 수 없다 — txId는 null).
            FileLogger.Warn(LogCategory.Pos, $"[PosSocketServer] {remote} 전문 오류 — 큐를 거치지 않고 즉시 응답", outcome.ErrorCode, transactionId: null);
            string? e40OrE41Code = outcome.ErrorCode;
            Task.Run(() =>
            {
                try { FaultAlertJudge.OnCodeObserved(e40OrE41Code, LogCategory.Pos, null); }
                catch { /* P27-9-(e) 이중 방어 — 삼킨다 */ }
            });
            WriteFrame(outcome.ErrorResponseFrame!, stream, writeLock, remote, "전문 오류");
            responseSent.Set();
            return true;
        }

        PosRequestTelegram request = outcome.Telegram!;
        string requestTxId = request.Read(ManagementNumberFieldNumber);
        // 사용자 요청(2026-09-01) — 전문 원문(위치기반 마스킹 적용, TelegramLogRedactor 클래스 요약
        // 참고)을 로그에 남긴다. 902614 #46(암호화된 카드정보)만 마스킹되고 나머지는 원문 그대로다.
        //
        // Phase 25 P25-5(PRD.md §4.2 #9) — ToBody()가 만드는 이 복사본은 Redact 호출 한 줄에만
        // 쓰이고 결과(문자열)만 남는다. request.Telegram 자신의 원본 _body(#7)와는 다른 배열이므로
        // 즉시 지워도 요청 처리에 영향이 없다.
        byte[] requestBodyForLog = request.Telegram.ToBody();
        // 2026-09-15 사용자 지적 — 이 로그는 "POS가 실제로 보낸 전문 그대로"를 표방하므로 #0(전문
        // 길이, 소켓에 실제로 나간 바이트의 일부)도 빠지면 안 된다. #0 재구성 + 위치 기반 마스킹은
        // TelegramLogRedactor.RedactFrameForLog로 모아 뒀다(그 메서드 주석 참고 — #0을 본문 POSITION
        // 체계에 실제로 편입시키면 파서/프레이머/4개 스키마까지 다 흔들려서, 로그 표시만을 위해
        // 감수할 위험이 아니라고 판단했다).
        string redactedRequestBody;
        try
        {
            redactedRequestBody = TelegramLogRedactor.RedactFrameForLog(request.TransactionTypeCode, requestBodyForLog);
        }
        finally
        {
            SecureClear.Clear(requestBodyForLog);
        }
        FileLogger.Info(LogCategory.Pos, $"[PosSocketServer] {remote} 요청 수신 전문={request.TransactionTypeCode} 원문={redactedRequestBody}", code: null, requestTxId);

        _queue.Enqueue(request, response =>
        {
            try
            {
                SendResponse(response, stream, writeLock, remote);
            }
            finally
            {
                // 성공/실패(H-1 타임아웃 등) 어느 쪽이든 반드시 신호한다 — 연결 스레드가
                // responseSent.Wait()에서 영원히 멈추지 않도록.
                responseSent.Set();
            }
        });
        return true;
    }

    /// <summary>
    /// ★ <see cref="TransactionQueue"/>의 **유일한** 워커 스레드에서 동기 호출된다(P14-4) — 이 메서드가
    /// 오래 걸리면 그동안 다른 모든 터미널의 결제 요청이 큐에서 대기한다. 회신 시점에 연결이 이미
    /// 끊겨 있거나, 응답을 안 읽는 클라이언트 때문에 <c>Write</c>가 <see cref="SendTimeoutMilliseconds"/>를
    /// 넘기면(H-1, 2026-08-24 Opus 검증 리뷰) 응답을 폐기하고 로그만 남긴다 — 예외를 워커 쪽으로
    /// 던지지 않는다.
    /// </summary>
    private static void SendResponse(IPosOutboundResponse response, NetworkStream stream, object writeLock, string remote)
    {
        byte[] frame;
        try
        {
            frame = response.ToFrame();
        }
        catch (Exception ex)
        {
            FileLogger.Error(
                LogCategory.Pos,
                $"[PosSocketServer] 응답 직렬화 실패: {ex}",
                InternalFaultCodes.ResponseSerializationFailure, transactionId: null);
            Task.Run(() =>
            {
                try { FaultAlertJudge.OnCodeObserved(InternalFaultCodes.ResponseSerializationFailure, LogCategory.Pos, null); }
                catch { /* P27-9-(e) 이중 방어 — 삼킨다 */ }
            });
            // Phase 25 P25-6 — 직렬화 실패로 이 응답을 포기하는 경로도 거래 종료다. 여기서 반환하면
            // 아래 정상 경로의 ClearBody()를 지나치므로 이 조기 return 앞에서 지운다.
            response.ClearBody();
            return;
        }

        // #9(전문관리번호)는 요청 수신 로그와 같은 필드다(request.Read(9)가 비어 있지 않은 정상
        // 케이스라면 PaymentOrchestrator.LogTxId와 값이 같다) — 요청/응답 두 줄이 같은 거래ID로
        // 남는다(P22-6 완료 조건).
        string responseTxId = response.Read(ManagementNumberFieldNumber);
        string resultCode = response.Read(ResultCodeFieldNumber);
        // 사용자 요청(2026-09-01) — 요청 로그와 동일 원칙(TelegramLogRedactor).
        //
        // Phase 25 P25-5(PRD.md §4.2 #9) — 위 요청 로그와 같은 이유로 BodyForLog() 복사본을 즉시
        // 지운다. response 원본 버퍼(#7)와는 다른 배열이다(P26-4 — IPosOutboundResponse.BodyForLog로
        // 일반화, PosResponseTelegram/PosInquiryResponseTelegram 둘 다 같은 계약).
        byte[] responseBodyForLog = response.BodyForLog();
        // 2026-09-15 — 요청 로그와 동일한 이유로 TelegramLogRedactor.RedactFrameForLog를 쓴다(#0
        // 재구성 + 위치 기반 마스킹을 그 메서드 한 곳에 모아 둠). 원캡이 실제로 내보내는 프레임
        // (PosMessageFramer.BuildFrame)도 이 본문 길이를 그대로 "D4"로 인코딩하므로 정확히 일치한다.
        string redactedResponseBody;
        try
        {
            redactedResponseBody = TelegramLogRedactor.RedactFrameForLog(response.RedactionTransactionTypeCode, responseBodyForLog);
        }
        finally
        {
            SecureClear.Clear(responseBodyForLog);
        }
        FileLogger.Info(LogCategory.Pos, $"[PosSocketServer] {remote} 응답 송신 원문={redactedResponseBody}", resultCode, responseTxId);

        try
        {
            WriteFrame(frame, stream, writeLock, remote, "응답");
        }
        finally
        {
            // P27-9-(a)/(d)/(e) — 응답을 실제로 보낸(WriteFrame) 직후, 결제 스레드(TransactionQueue의
            // 유일한 워커 스레드)를 블로킹하지 않도록 판정을 Task.Run으로 위탁한다. §2.1 전부를
            // 커버하는 유일한 지점이다.
            //
            // 2026-09-17 사용자 지적 — 거래 경계(구분선)를 예전엔 이 Task.Run 안에서(판정이 끝난
            // 직후) 찍었는데, 이 Task.Run은 결제 워커 스레드를 안 막으려고 일부러 fire-and-forget으로
            // 던진 것이라 연결 스레드(HandleConnection)의 "정상 종료(FIN)"/"연결 단절"/"연결 종료"
            // 로그와 실행 순서가 보장되지 않는다 — 실측에서 구분선이 그 연결의 마지막 정리 로그보다
            // 먼저 찍히는 게 확인됐다("거래 종료" 구분선 뒤에 "연결 종료" 줄이 더 나옴). 그래서
            // 구분선은 여기서 빼고, 연결 1건의 모든 종료 경로(정상/타임아웃/단절/예외)를 예외 없이
            // 커버하는 진짜 마지막 지점인 HandleConnection의 finally(연결 종료 로그 직후)로 옮겼다.
            Task.Run(() =>
            {
                try
                {
                    FaultAlertJudge.OnCodeObserved(resultCode, LogCategory.Payment, responseTxId);
                }
                catch
                {
                    // P27-9-(e) 이중 방어 — 삼킨다.
                }
            });
            // Phase 25 P25-5(PRD.md §4.2 #13) — 송신 frame(길이 헤더 + ToFrame()의 body 복사본).
            // WriteFrame은 동기 stream.Write 한 번으로 끝나므로, 반환 시점엔 이미 이 배열이 필요
            // 없다. frame은 response 원본 버퍼(#7)와도 다른 배열(ToFrame 내부에서 새로 만듦)이라
            // 여기서 지워도 아래 클리어와 겹치지 않는다.
            SecureClear.Clear(frame);
        }

        // Phase 25 P25-6(PRD.md §4.2 #7, 응답 쪽) — response 원본 버퍼는 위 BodyForLog()/ToFrame()
        // 복사본들과 별개로 아직 살아 있다. 프레임을 실제로 쓴(성공/실패 무관, try/finally로 이미
        // 처리됨) 뒤인 지금이 "송신이 끝난 뒤"(§4.3.3)다 — 이 이후로 이 응답 객체를 다시 읽는 코드는
        // 없다(WriteFrame이 이 메서드의 마지막 소비 지점). P26-4 — IPosOutboundResponse.ClearBody()로
        // 일반화(PosResponseTelegram.ClearBody()/PosInquiryResponseTelegram.ClearBody() 둘 다 위임).
        response.ClearBody();
    }

    /// <summary>완성된 프레임(길이 헤더 포함)을 소켓에 쓰는 공통 지점 — 정상 응답과 P17-3의 프로토콜
    /// 오류 응답(E40/E41)이 함께 쓴다.</summary>
    private static void WriteFrame(byte[] frame, NetworkStream stream, object writeLock, string remote, string logLabel)
    {
        lock (writeLock)
        {
            try
            {
                stream.Write(frame, 0, frame.Length);
            }
            catch (Exception ex)
            {
                FileLogger.Warn(
                    LogCategory.Pos,
                    $"[PosSocketServer] {remote} {logLabel} 전송 실패(연결 끊김 또는 {SendTimeoutMilliseconds}ms 내 미수신으로 추정) — 폐기: {ex.Message}",
                    InternalFaultCodes.ResponseSendFailure, transactionId: null);
                Task.Run(() =>
                {
                    try { FaultAlertJudge.OnCodeObserved(InternalFaultCodes.ResponseSendFailure, LogCategory.Pos, null); }
                    catch { /* P27-9-(e) 이중 방어 — 삼킨다 */ }
                });
            }
        }
    }

    private static void SafeClose(TcpClient client)
    {
        try
        {
            client.Close();
        }
        catch
        {
            // 이미 닫혔거나 소켓 오류 — 거부 처리이므로 결과를 신경 쓰지 않는다.
        }
    }

    private static string SafeRemoteEndPoint(TcpClient client)
    {
        try
        {
            return client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
