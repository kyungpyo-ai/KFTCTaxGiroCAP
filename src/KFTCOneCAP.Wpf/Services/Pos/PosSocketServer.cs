using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using KFTCOneCAP.Wpf.Protocol.Pos;
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
            FileLogger.Error($"[PosSocketServer] {Port} 포트 리스닝 실패({ex.SocketErrorCode}): {ex.Message} — 소켓 서버 없이 앱 계속 기동");
            _listener = null;
            return;
        }

        _cts = new CancellationTokenSource();
        _acceptThread = new Thread(() => AcceptLoop(_cts.Token)) { IsBackground = true, Name = "PosSocketAccept" };
        _acceptThread.Start();
        FileLogger.Info($"[PosSocketServer] {Port} 포트 리스닝 시작");
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
            FileLogger.Warn($"[PosSocketServer] 리스너 정지 중 예외(무시): {ex.Message}");
        }

        _acceptThread?.Join(TimeSpan.FromSeconds(2));
        _listener = null;
        FileLogger.Info("[PosSocketServer] 정지");
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
                    FileLogger.Error($"[PosSocketServer] 수락 루프가 예기치 않은 예외로 종료됨(이후 새 연결을 받지 못함): {ex}");
                }

                break; // token.IsCancellationRequested==true면 Stop()에 의한 정상 종료.
            }

            if (Interlocked.Increment(ref _connectionCount) > MaxConcurrentConnections)
            {
                Interlocked.Decrement(ref _connectionCount);
                FileLogger.Warn($"[PosSocketServer] 동시 연결 상한({MaxConcurrentConnections}) 초과 — 연결 거부");
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
        FileLogger.Info($"[PosSocketServer] 연결 수락: {remote}");

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
                        FileLogger.Warn($"[PosSocketServer] {remote} 응답 전송 후 {IdleAfterResponseTimeoutMilliseconds}ms 동안 다음 요청이 없어 서버가 먼저 닫음(POS 개발 실수 대비)");
                        break;
                    }
                    catch (IOException ex)
                    {
                        FileLogger.Info($"[PosSocketServer] {remote} 연결 단절(수신 중): {ex.Message}");
                        break;
                    }

                    if (read == 0)
                    {
                        FileLogger.Info($"[PosSocketServer] {remote} 정상 종료(FIN)");
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
                        FileLogger.Warn($"[PosSocketServer] {remote} 전문 형식 오류 — 연결 종료: {ex.Message}");
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
            FileLogger.Error($"[PosSocketServer] {remote} 처리 중 예외: {ex}");
        }
        finally
        {
            Interlocked.Decrement(ref _connectionCount);
            FileLogger.Info($"[PosSocketServer] 연결 종료: {remote}");
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
            // 프레임 경계는 이미 지켜졌으므로(형식 오류와 다름) 이 프레임만 버리고 연결은 유지한다.
            // #4(거래 구분 코드)조차 읽을 수 없을 만큼 짧은 본문만 여기 온다(P17-3) — 이 경우는
            // 응답을 만들 스키마 근거가 전혀 없어 침묵 외에 대안이 없다.
            FileLogger.Warn($"[PosSocketServer] {remote} 요청 파싱 오류(이 프레임만 폐기): {ex.Message}");
            return false;
        }

        if (!outcome.IsSuccess)
        {
            // E40(길이 불일치)/E41(알 수 없는 거래구분) — 전문 계층(P17-3)이 이미 완성된 응답 프레임을
            // 만들어 뒀다. Flow(큐)를 거칠 이유가 없는 순수 프로토콜 오류이므로 여기서 바로 써 보낸다.
            FileLogger.Warn($"[PosSocketServer] {remote} 전문 오류({outcome.ErrorCode}) — 큐를 거치지 않고 즉시 응답");
            WriteFrame(outcome.ErrorResponseFrame!, stream, writeLock, remote, "전문 오류");
            responseSent.Set();
            return true;
        }

        PosRequestTelegram request = outcome.Telegram!;
        FileLogger.Info($"[PosSocketServer] {remote} 요청 수신 전문={request.TransactionTypeCode}");

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
    private static void SendResponse(PosResponseTelegram response, NetworkStream stream, object writeLock, string remote)
    {
        byte[] frame;
        try
        {
            frame = response.ToFrame();
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[PosSocketServer] 응답 직렬화 실패: {ex}");
            return;
        }

        WriteFrame(frame, stream, writeLock, remote, "응답");
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
                FileLogger.Warn($"[PosSocketServer] {remote} {logLabel} 전송 실패(연결 끊김 또는 {SendTimeoutMilliseconds}ms 내 미수신으로 추정) — 폐기: {ex.Message}");
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
