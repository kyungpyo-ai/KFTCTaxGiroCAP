using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 21(docs/payment_relay/development_plan.md P21-4) 개발/회귀 검증용 — **최종 산출물 아님**.
/// <c>App.xaml.cs</c>가 <c>--repeat-transactions-test</c> 인자로 실행될 때만 <see cref="Run"/>을
/// 백그라운드에서 호출한다.
///
/// <b>목적</b>: PRD §9 "장시간 실행 시 메모리 누수가 발생하지 않도록 관리한다"를 확인한다. 같은
/// 프로세스 안에서 501008(카드리딩 없음 — 하드웨어 상태와 무관하게 빠르게 끝남, P21-3 Scenario6
/// 정정과 같은 이유)을 <see cref="Iterations"/>회 반복 처리하며 5회마다 현재 프로세스의 핸들 수와
/// 작업 세트(Working Set)를 로그에 남긴다. 판정은 사람이 로그를 보고 한다 — "계속 우상향하는가"가
/// 기준이지 절대값이 아니다(GC 특성상 변동은 정상).
/// </summary>
internal static class RepeatedTransactionResourceTest
{
    private const int Port = 8002;
    private const int Iterations = 50;

    internal static void Run()
    {
        Thread.Sleep(300); // 소켓 서버가 리스닝을 시작할 시간을 넉넉히 준다.

        try
        {
            FileLogger.Info($"[repeat-tx-test] 시작 — 501008을 {Iterations}회 반복 처리하며 " +
                "핸들/메모리 추이를 5회마다 기록한다(PRD §9 장시간 실행 누수 확인, P21-4)");

            Process self = Process.GetCurrentProcess();
            LogResourceSnapshot(self, 0);

            int failCount = 0;
            for (int i = 1; i <= Iterations; i++)
            {
                if (!SendOneAndWait(i))
                {
                    failCount++;
                }

                if (i % 5 == 0)
                {
                    self.Refresh();
                    LogResourceSnapshot(self, i);
                }
            }

            FileLogger.Info($"[repeat-tx-test] 완료 — {Iterations}건 중 실패 {failCount}건. " +
                "판정은 사람이 위 스냅샷들의 핸들/WorkingSet 추이가 계속 우상향하는지 보고 내릴 것" +
                "(GC 변동은 정상 — 단조 증가 여부가 기준).");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[repeat-tx-test] 하네스 자체 예외로 중단: {ex}");
        }
    }

    private static void LogResourceSnapshot(Process self, int iteration)
    {
        FileLogger.Info($"[repeat-tx-test][스냅샷] {iteration}건 처리 후 — " +
            $"핸들={self.HandleCount}, WorkingSet={self.WorkingSet64 / 1024}KB, " +
            $"GC스레드={Thread.CurrentThread.ManagedThreadId}(참고용)");
    }

    private static bool SendOneAndWait(int index)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, Port);
            using NetworkStream stream = client.GetStream();

            if (!PosSchemaRegistry.TryResolve("501008", out PosTelegramSchema? schema) || schema is null)
                throw new InvalidOperationException("알 수 없는 거래구분: 501008");

            PosTelegram telegram = PosTelegram.CreateEmpty(schema);
            telegram.Write(1, "IGN");
            telegram.Write(2, "095");
            telegram.Write(3, "0200");
            telegram.Write(4, "501008");
            telegram.Write(6, "G");
            telegram.Write(9, "0EC0" + index.ToString("D8"));

            byte[] bodyBytes = telegram.ToBody();
            byte[] lengthBytes = PosMessageEncoding.Value.GetBytes(bodyBytes.Length.ToString("D4"));
            stream.Write(lengthBytes, 0, lengthBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);

            string? response = ReadResponseFrame(stream, TimeSpan.FromSeconds(10));
            return response != null;
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"[repeat-tx-test] {index}번째 요청 실패: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string? ReadResponseFrame(NetworkStream stream, TimeSpan timeout)
    {
        var framer = new PosMessageFramer();
        var buffer = new byte[4096];
        stream.ReadTimeout = (int)timeout.TotalMilliseconds;

        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                return null;

            var chunk = new byte[read];
            Buffer.BlockCopy(buffer, 0, chunk, 0, read);
            var frames = framer.Append(chunk);
            if (frames.Count > 0)
                return PosMessageEncoding.Value.GetString(frames[0]);
        }
    }
}
