using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using KFTCOneCAP.Wpf.Protocol.Pos;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 14(docs/payment_relay/development_plan.md P14-6) 개발/회귀 검증용 테스트 클라이언트.
/// **최종 산출물이 아니다** — <c>App.xaml.cs</c>가 <c>--pos-client-test</c> 인자로 실행될 때만
/// <see cref="RunAll"/>을 백그라운드 스레드에서 호출한다. 앱 자신에게 루프백으로 접속해
/// P14-3(동시 요청 순차 처리)/P14-4(응답 회신 경로)/P14-5(오류 내성)의 시나리오를 재현한다.
/// 결과는 <see cref="FileLogger"/>로 남고, 실제 판정(순서/타이밍 일치 여부)은 그 로그를 사람이
/// 확인한다 — 이 클래스는 시나리오 재현까지만 책임진다.
/// </summary>
internal static class PosClientTestScenarios
{
    private const int Port = 8002;

    internal static void RunAll()
    {
        Thread.Sleep(300); // 소켓 서버가 리스닝을 시작할 시간을 넉넉히 준다.

        try
        {
            FileLogger.Info("[pos-client-test] 시작");
            Scenario1_ConcurrentRequestsOrdering();
            Scenario2_PersistentConnectionMultipleRequests();
            Scenario3_MalformedLengthField();
            Scenario4_AbruptDisconnectThenRecover();
            Scenario5_ProcessorException();
            Scenario6_UnresponsiveClientDoesNotBlockQueue();
            Scenario7_ServerClosesIdleConnectionAfterResponse();
            FileLogger.Info("[pos-client-test] 전체 완료 — 로그 파일에서 [TransactionQueue] 처리 시작/종료 순서와 각 시나리오 결과를 대조할 것");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[pos-client-test] 예외로 중단: {ex}");
        }
    }

    /// <summary>P14-3 완료 조건: 3건을 동시에 밀어 넣으면 정확히 순차 1건씩, 순서대로 처리된다.</summary>
    private static void Scenario1_ConcurrentRequestsOrdering()
    {
        FileLogger.Info("[pos-client-test][1] 시작 — 3건 동시 요청, 순차 처리/순서 보존 확인(P14-3)");

        string[] txIds = { "ORDER-A", "ORDER-B", "ORDER-C" };
        var responseOrder = new List<string>();
        var responseOrderLock = new object();
        var threads = new List<Thread>();
        using var startGate = new ManualResetEventSlim(false);

        foreach (string txId in txIds)
        {
            var thread = new Thread(() =>
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, Port);
                using var stream = client.GetStream();
                startGate.Wait();
                WriteRequestFrame(stream, "1000", txId);
                string? body = ReadResponseFrame(stream, TimeSpan.FromSeconds(10));
                FileLogger.Info($"[pos-client-test][1] 응답 수신 txId={txId} body={body ?? "(타임아웃)"}");
                if (body != null)
                {
                    lock (responseOrderLock) { responseOrder.Add(txId); }
                }
            })
            { IsBackground = true };
            threads.Add(thread);
            thread.Start();
        }

        Thread.Sleep(200); // 세 스레드 모두 연결하고 대기 상태에 들어갈 시간을 준다.
        startGate.Set();

        foreach (Thread thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(15));
        }

        FileLogger.Info($"[pos-client-test][1] 완료 — 요청 순서 {string.Join(",", txIds)} / 응답 도착 순서 {string.Join(",", responseOrder)}" +
            " (워커가 하나뿐이므로 [TransactionQueue] 로그의 처리 시작/종료가 겹치지 않고 이 순서와 일치해야 함)");
    }

    /// <summary>P14-2 완료 조건: 한 연결로 연속 요청해도(연결을 끊지 않아도) 각각 정상 처리된다.</summary>
    private static void Scenario2_PersistentConnectionMultipleRequests()
    {
        FileLogger.Info("[pos-client-test][2] 시작 — 한 연결로 연속 3회 요청(연결 유지, P14-2)");

        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, Port);
        using var stream = client.GetStream();

        for (int i = 1; i <= 3; i++)
        {
            string txId = $"PERSIST-{i}";
            WriteRequestFrame(stream, "500", txId);
            string? body = ReadResponseFrame(stream, TimeSpan.FromSeconds(10));
            FileLogger.Info($"[pos-client-test][2] {i}번째 요청 응답 txId={txId} body={body ?? "(타임아웃)"}");
        }

        FileLogger.Info("[pos-client-test][2] 완료 — 3건 모두 응답이 왔으면 성공");
    }

    /// <summary>P14-1/P14-5: 길이 필드가 숫자가 아니면 서버가 재동기화 없이 그 연결만 닫는다.</summary>
    private static void Scenario3_MalformedLengthField()
    {
        FileLogger.Info("[pos-client-test][3] 시작 — 잘못된 길이 필드 전송, 서버가 그 연결만 닫는지 확인(P14-1/P14-5)");

        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, Port);
        using var stream = client.GetStream();

        byte[] garbage = PosMessageEncoding.Value.GetBytes("ABCDgarbage-body");
        stream.Write(garbage, 0, garbage.Length);

        bool closed = WaitForConnectionClose(stream, TimeSpan.FromSeconds(5));
        FileLogger.Info(closed
            ? "[pos-client-test][3] 완료 — 서버가 연결을 닫음(기대한 동작)"
            : "[pos-client-test][3] 완료 — ★ 서버가 시간 내에 연결을 닫지 않음, 확인 필요");
    }

    /// <summary>P14-4/P14-5: 요청 직후 연결을 강제로 끊어도 서버가 살아 있고 다음 요청을 정상 처리한다.</summary>
    private static void Scenario4_AbruptDisconnectThenRecover()
    {
        FileLogger.Info("[pos-client-test][4] 시작 — 요청 직후 강제 연결 종료, 서버 생존/다음 요청 처리 확인(P14-4/P14-5)");

        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, Port);
            using (NetworkStream stream = client.GetStream())
            {
                WriteRequestFrame(stream, "700", "ABRUPT-1");
            }

            client.Client.Close(0); // 응답을 기다리지 않고 즉시 닫는다(RST에 가까움).
        }

        FileLogger.Info("[pos-client-test][4] 강제 종료 완료 — 서버 로그에 '응답 폐기'가 남는지 확인할 것");
        Thread.Sleep(2500); // 워커가 위 거래를 마저 처리하고 회신을 시도(실패)할 시간.

        using var recoverClient = new TcpClient();
        recoverClient.Connect(IPAddress.Loopback, Port);
        using var recoverStream = recoverClient.GetStream();
        WriteRequestFrame(recoverStream, "800", "AFTER-ABRUPT");
        string? body = ReadResponseFrame(recoverStream, TimeSpan.FromSeconds(10));
        FileLogger.Info($"[pos-client-test][4] 완료 — 강제 종료 뒤 다음 요청 응답: {body ?? "(타임아웃 — ★ 큐가 막혔을 가능성)"}");
    }

    /// <summary>P14-3: 처리 스텁이 예외를 던져도(amount="THROW") 워커가 죽지 않고 다음 요청을 처리한다.</summary>
    private static void Scenario5_ProcessorException()
    {
        FileLogger.Info("[pos-client-test][5] 시작 — 처리 스텁이 예외를 던져도 워커 생존 확인(P14-3)");

        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, Port);
            using NetworkStream stream = client.GetStream();
            WriteRequestFrame(stream, "THROW", "THROW-1");
            string? body = ReadResponseFrame(stream, TimeSpan.FromSeconds(10));
            FileLogger.Info($"[pos-client-test][5] 예외 유발 요청 응답: {body ?? "(타임아웃)"}"); // 내부 오류 응답(99)이 정상
        }

        using var followUpClient = new TcpClient();
        followUpClient.Connect(IPAddress.Loopback, Port);
        using var followUpStream = followUpClient.GetStream();
        WriteRequestFrame(followUpStream, "900", "AFTER-THROW");
        string? followUpBody = ReadResponseFrame(followUpStream, TimeSpan.FromSeconds(10));
        FileLogger.Info($"[pos-client-test][5] 완료 — 예외 뒤 다음 요청 응답: {followUpBody ?? "(타임아웃 — ★ 워커가 죽었을 가능성)"}");
    }

    /// <summary>
    /// H-1 재검증(Opus 검증 리뷰 2026-08-24): 응답을 전혀 읽지 않는 "먹통" 클라이언트가 있어도, 뒤이어
    /// 들어온 다른 클라이언트의 요청이 무한정 막히지 않고 <c>PosSocketServer</c>의
    /// <c>SendTimeoutMilliseconds</c>(5초) 안팎에서 풀려나는지 확인한다. 응답 본문을 9,900바이트로
    /// 부풀리고(App.xaml.cs <c>StubPaymentProcessor</c>의 <c>amount="BIGRESPONSE"</c> 경로) 먹통
    /// 클라이언트의 수신 버퍼를 최소로 줄여 실제 소켓 쓰기 블로킹을 유도한다 — OS/루프백 버퍼 크기에
    /// 따라 실제로 블로킹이 재현되지 않을 수도 있는데, 그 경우도 정상으로 보고 로그로 구분해 남긴다.
    /// </summary>
    private static void Scenario6_UnresponsiveClientDoesNotBlockQueue()
    {
        FileLogger.Info("[pos-client-test][6] 시작 — 응답을 안 읽는 클라이언트가 있어도 큐가 막히지 않는지 확인(H-1 재검증)");

        var stuckClient = new TcpClient();
        try
        {
            stuckClient.ReceiveBufferSize = 1; // OS가 거부/클램프해도 시나리오는 계속 진행한다.
        }
        catch
        {
            // 무시 — 아래에서 어차피 결과로 블로킹 재현 여부를 판정한다.
        }

        stuckClient.Connect(IPAddress.Loopback, Port);
        NetworkStream stuckStream = stuckClient.GetStream();
        WriteRequestFrame(stuckStream, "BIGRESPONSE", "STUCK-1");
        FileLogger.Info("[pos-client-test][6] 먹통 클라이언트 요청 전송 완료 — 이후 응답을 절대 읽지 않음");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using (var normalClient = new TcpClient())
        {
            normalClient.Connect(IPAddress.Loopback, Port);
            using NetworkStream normalStream = normalClient.GetStream();
            WriteRequestFrame(normalStream, "300", "AFTER-STUCK");
            string? body = ReadResponseFrame(normalStream, TimeSpan.FromSeconds(15));
            stopwatch.Stop();

            if (body == null)
            {
                FileLogger.Error("[pos-client-test][6] 완료 — ★ 실패: 15초 안에 응답을 못 받음(큐가 막혔을 가능성, H-1 재발)");
            }
            else
            {
                FileLogger.Info($"[pos-client-test][6] 완료 — {stopwatch.ElapsedMilliseconds}ms 만에 응답 수신: {body} " +
                    "(수 초 이내면 정상. OS 버퍼가 응답을 그냥 흡수했다면 실제 블로킹은 재현되지 않았을 수 있음 — 그래도 정상)");
            }
        }

        try { stuckStream.Dispose(); } catch { /* 정리용 — 결과에 영향 없음 */ }
        try { stuckClient.Dispose(); } catch { /* 정리용 — 결과에 영향 없음 */ }
    }

    /// <summary>
    /// 2026-08-24 사용자 확정: 응답을 받은 뒤 POS가 연결을 안 닫는 개발 실수 대비 — 서버가
    /// <c>PosSocketServer.IdleAfterResponseTimeoutMilliseconds</c>(10초) 안에 다음 요청이 없으면
    /// 먼저 연결을 닫는지 확인한다. 총 12초 이상 걸리는 시나리오(10초 대기 + 여유)다.
    /// </summary>
    private static void Scenario7_ServerClosesIdleConnectionAfterResponse()
    {
        FileLogger.Info("[pos-client-test][7] 시작 — 응답 후 유휴 연결을 서버가 10초 뒤 먼저 닫는지 확인");

        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, Port);
        using var stream = client.GetStream();

        WriteRequestFrame(stream, "100", "IDLE-TEST-1");
        string? body = ReadResponseFrame(stream, TimeSpan.FromSeconds(10));
        FileLogger.Info($"[pos-client-test][7] 첫 응답 수신: {body ?? "(타임아웃 — ★ 확인 필요)"}, 이제 12초간 아무것도 안 보내고 대기");

        Thread.Sleep(12000); // 서버 쪽 유휴 타이머(10초)보다 여유 있게 기다린다.

        bool closed = WaitForConnectionClose(stream, TimeSpan.FromSeconds(3));
        FileLogger.Info(closed
            ? "[pos-client-test][7] 완료 — 서버가 유휴 연결을 먼저 닫음(기대한 동작)"
            : "[pos-client-test][7] 완료 — ★ 실패: 12초가 지나도 서버가 연결을 닫지 않음");
    }

    // ---- 공용 헬퍼 — 서버와 동일한 임시 전문 형식(P14-1: [길이4(ASCII)][본문])을 그대로 재사용한다 ----

    private static void WriteRequestFrame(NetworkStream stream, string amount, string transactionId)
    {
        string body = $"PAY|{amount}|{transactionId}";
        byte[] bodyBytes = PosMessageEncoding.Value.GetBytes(body);
        byte[] lengthBytes = PosMessageEncoding.Value.GetBytes(bodyBytes.Length.ToString("D4"));
        stream.Write(lengthBytes, 0, lengthBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
    }

    private static string? ReadResponseFrame(NetworkStream stream, TimeSpan timeout)
    {
        var framer = new PosMessageFramer();
        var buffer = new byte[4096];
        stream.ReadTimeout = (int)timeout.TotalMilliseconds;

        try
        {
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    return null; // 연결 종료

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                var frames = framer.Append(chunk);
                if (frames.Count > 0)
                    return PosMessageEncoding.Value.GetString(frames[0]);
            }
        }
        catch (IOException)
        {
            return null; // 타임아웃/연결 단절
        }
    }

    private static bool WaitForConnectionClose(NetworkStream stream, TimeSpan timeout)
    {
        stream.ReadTimeout = (int)timeout.TotalMilliseconds;
        try
        {
            var buffer = new byte[64];
            int read = stream.Read(buffer, 0, buffer.Length);
            return read == 0; // 서버가 FIN으로 정상 종료
        }
        catch (IOException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut)
        {
            return false; // 시간 안에 닫히지 않음
        }
        catch (IOException)
        {
            return true; // 그 외(RST 등)도 닫힌 것으로 본다
        }
    }
}
