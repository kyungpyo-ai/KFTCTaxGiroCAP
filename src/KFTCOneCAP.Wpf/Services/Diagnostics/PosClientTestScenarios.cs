using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

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
                WriteRequestFrame(stream, txId);
                string? body = ReadResponseFrame(stream, TimeSpan.FromSeconds(10));
                string correlation = body != null ? ReadCorrelationId(body) : "(타임아웃)";
                FileLogger.Info($"[pos-client-test][1] 응답 수신 txId={txId} #9(전문관리번호)={correlation}");
                if (body != null)
                {
                    if (correlation != txId)
                        FileLogger.Error($"[pos-client-test][1] ★ 상관관계 불일치: 요청 txId={txId}인데 응답 #9={correlation}");

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
            WriteRequestFrame(stream, txId);
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
                WriteRequestFrame(stream, "ABRUPT-1");
            }

            client.Client.Close(0); // 응답을 기다리지 않고 즉시 닫는다(RST에 가까움).
        }

        FileLogger.Info("[pos-client-test][4] 강제 종료 완료 — 서버 로그에 '응답 폐기'가 남는지 확인할 것");
        Thread.Sleep(2500); // 워커가 위 거래를 마저 처리하고 회신을 시도(실패)할 시간.

        using var recoverClient = new TcpClient();
        recoverClient.Connect(IPAddress.Loopback, Port);
        using var recoverStream = recoverClient.GetStream();
        WriteRequestFrame(recoverStream, "AFTER-ABRUPT");
        string? body = ReadResponseFrame(recoverStream, TimeSpan.FromSeconds(10));
        FileLogger.Info($"[pos-client-test][4] 완료 — 강제 종료 뒤 다음 요청 응답: {body ?? "(타임아웃 — ★ 큐가 막혔을 가능성)"}");
    }

    /// <summary>
    /// P14-3: 워커 예외 후에도 다음 요청을 처리하는지 확인. **Phase 17 범위 조정**: 임시 전문 시절엔
    /// <c>amount="THROW"</c> 같은 인위적 sentinel로 Orchestrator 예외를 직접 유도할 수 있었지만,
    /// SPEC 501008 경로(카드리딩 없음, 순수 relay)에는 그런 sentinel이 없다 — 정상 입력만으로는
    /// Orchestrator가 예외를 던질 지점이 없다(그게 맞는 설계다). 워커의 예외 복원력 자체는
    /// `TransactionQueue` 리플렉션 하네스(P17-4 검증, `InternalError`→E99 폴백 확인)로 이미 별도
    /// 검증됐으므로, 여기서는 **연속 2건이 정상 처리되는지**(워커가 요청 사이에 멈추지 않는지)만
    /// 확인하는 것으로 축소한다.
    /// </summary>
    private static void Scenario5_ProcessorException()
    {
        FileLogger.Info("[pos-client-test][5] 시작 — 연속 2건 정상 처리 확인(워커 생존, P14-3) — " +
            "예외 복원력 자체는 P17-4 TransactionQueue 하네스가 별도로 검증함(위 클래스 주석 참고)");

        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, Port);
            using NetworkStream stream = client.GetStream();
            WriteRequestFrame(stream, "SEQ-1");
            string? body = ReadResponseFrame(stream, TimeSpan.FromSeconds(10));
            FileLogger.Info($"[pos-client-test][5] 1번째 요청 응답: {body ?? "(타임아웃)"}");
        }

        using var followUpClient = new TcpClient();
        followUpClient.Connect(IPAddress.Loopback, Port);
        using var followUpStream = followUpClient.GetStream();
        WriteRequestFrame(followUpStream, "SEQ-2");
        string? followUpBody = ReadResponseFrame(followUpStream, TimeSpan.FromSeconds(10));
        FileLogger.Info($"[pos-client-test][5] 완료 — 2번째 요청 응답: {followUpBody ?? "(타임아웃 — ★ 워커가 멈췄을 가능성)"}");
    }

    /// <summary>
    /// H-1 재검증(Opus 검증 리뷰 2026-08-24): 응답을 전혀 읽지 않는 "먹통" 클라이언트가 있어도, 뒤이어
    /// 들어온 다른 클라이언트의 요청이 무한정 막히지 않고 <c>PosSocketServer</c>의
    /// <c>SendTimeoutMilliseconds</c>(5초) 안팎에서 풀려나는지 확인한다.
    ///
    /// **Phase 17 범위 조정**: 임시 전문 시절엔 응답 본문을 9,900바이트까지 인위적으로 부풀릴 수
    /// 있었지만, SPEC 고정길이 전문은 최대(902614)도 1,500바이트뿐이라 그 정도로는 OS 루프백 소켓
    /// 버퍼를 채워 실제 쓰기 블로킹을 강제로 재현하기 어렵다 — 그래도 501008을 써서 구조적으로
    /// 같은 경로(느린 소비자가 있어도 다른 연결이 막히지 않는지)는 그대로 확인한다. 실제 블로킹이
    /// 재현되지 않아도(=응답이 빨리 옴) 실패가 아니다 — 재현 여부와 무관하게 "막히지 않았다"는
    /// 것만 확인하면 되는 시나리오이기 때문이다.
    ///
    /// **Phase 21 정정(2026-08-31, P21-3)**: 원래 이 시나리오는 "3전문 중 가장 큰 응답(1500바이트)"
    /// 이라는 이유로 902614를 썼다. 그런데 이 하네스는 <c>App.Orchestrator</c>(실제 하드웨어에 연결된
    /// 진짜 인스턴스)를 그대로 쓰므로, 리더기가 실제로 연결돼 있으면 902614는 **진짜 카드 리딩
    /// 대기**(최대 120초, 카드가 없으면 리더기 명령 자체가 실패 응답을 주기까지도 수십 초)에
    /// 들어간다 — 이 시나리오가 확인하려는 건 "먹통 클라이언트가 큐를 막는가"이지 카드리딩이 아닌데,
    /// 리더기 타이밍에 우연히 얽혀 15초 타임아웃을 넘겨 거짓 실패가 났다(실제로는 워커도 큐도 멀쩡
    /// 했고, 약 60초 뒤 리더기 응답코드 04로 정상 종료됨 — 실장비로 재현·확인). 카드리딩이 필요 없는
    /// 501008로 바꿔 하드웨어 상태와 무관하게 원래 의도(느린 소비자 내성)만 검증하도록 정정한다.
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
        WriteRequestFrame(stuckStream, "501008", "STUCK-1"); // 카드리딩 없이 즉시 VAN 중계 — 리더기 상태와 무관하게 빠르게 끝남(P21-3 정정)
        FileLogger.Info("[pos-client-test][6] 먹통 클라이언트 요청 전송 완료 — 이후 응답을 절대 읽지 않음");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using (var normalClient = new TcpClient())
        {
            normalClient.Connect(IPAddress.Loopback, Port);
            using NetworkStream normalStream = normalClient.GetStream();
            WriteRequestFrame(normalStream, "AFTER-STUCK");
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

        WriteRequestFrame(stream, "IDLE-TEST-1");
        string? body = ReadResponseFrame(stream, TimeSpan.FromSeconds(10));
        FileLogger.Info($"[pos-client-test][7] 첫 응답 수신: {body ?? "(타임아웃 — ★ 확인 필요)"}, 이제 12초간 아무것도 안 보내고 대기");

        Thread.Sleep(12000); // 서버 쪽 유휴 타이머(10초)보다 여유 있게 기다린다.

        bool closed = WaitForConnectionClose(stream, TimeSpan.FromSeconds(3));
        FileLogger.Info(closed
            ? "[pos-client-test][7] 완료 — 서버가 유휴 연결을 먼저 닫음(기대한 동작)"
            : "[pos-client-test][7] 완료 — ★ 실패: 12초가 지나도 서버가 연결을 닫지 않음");
    }

    // ---- 공용 헬퍼 — Phase 17(P17-7)부터 실제 SPEC 전문(501008)을 보낸다. 501008은 카드리딩이 없어
    // 리더기 하드웨어/설정 여부와 무관하게 결정적으로 동작하므로, 소켓/큐 배관 자체를 검증하는 이
    // 시나리오들(Phase 14)에 가장 적합하다 — 상관관계 추적용 txId는 #9(전문 관리 번호, AN12)에 심고
    // 응답에서 같은 자리를 읽어 대조한다(StubVanRelayService가 clone 기반이라 이 필드가 그대로
    // 왕복한다). ----

    private static void WriteRequestFrame(NetworkStream stream, string transactionId) =>
        WriteRequestFrame(stream, "501008", transactionId);

    private static void WriteRequestFrame(NetworkStream stream, string transactionType, string correlationId)
    {
        if (!PosSchemaRegistry.TryResolve(transactionType, out PosTelegramSchema? schema) || schema is null)
            throw new InvalidOperationException($"알 수 없는 거래구분: {transactionType}");

        var telegram = PosTelegram.CreateEmpty(schema);
        telegram.Write(1, "IGN");
        telegram.Write(2, "095");
        telegram.Write(3, "0200");
        telegram.Write(4, transactionType);
        telegram.Write(6, "G");
        telegram.Write(9, correlationId); // AN12 — 12자 넘으면 PosField.Pad가 예외를 던져 실수를 바로 드러냄

        byte[] bodyBytes = telegram.ToBody();
        byte[] lengthBytes = PosMessageEncoding.Value.GetBytes(bodyBytes.Length.ToString("D4"));
        stream.Write(lengthBytes, 0, lengthBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
    }

    /// <summary>응답 본문에서 #9(전문 관리 번호)를 읽어 상관관계를 대조한다 — relay 응답이든 clone 기반
    /// 실패 응답이든 이 필드는 요청 값을 그대로 보존한다(P17-3 원본 보존 원칙).</summary>
    private static string ReadCorrelationId(string responseBody)
    {
        byte[] bytes = PosMessageEncoding.Value.GetBytes(responseBody);
        // #9는 공통부에서 POSITION 35, 길이 12(공통부 정의는 3전문 동일 — PosCommonHeader 참고).
        string raw = PosMessageEncoding.Value.GetString(bytes, 35, 12);
        return raw.TrimEnd(' ');
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
