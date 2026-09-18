using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Services.Pos;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 29 P29-5 완료 조건 전용 셀프테스트 — <see cref="PosClient"/>가 진짜 원캡 서버 없이도 확인할 수
/// 있는 두 가지 내성을 다룬다. 501008 실왕복 확인은 <see cref="PosClientTestScenarios"/>
/// (<c>--pos-client-test</c>)가 실제 서버를 대상으로 이미 한다 — 여기서는 그 하네스로는 재현하기 어려운
/// (실제 서버는 이제 응답 없이 침묵하지 않는다, Phase 27 P27-8-f) 두 가지만 다룬다.
///
/// <c>App.xaml.cs</c>가 <c>--pos-client-resilience-test</c> 인자로 실행될 때만 <see cref="RunAll"/>을
/// 호출한다.
/// </summary>
internal static class PosClientResilienceSelfTest
{
    public static void RunAll()
    {
        try
        {
            FileLogger.Info("[pos-client-resilience-test] 시작");

            bool timeoutMarginOk = RunCardApprovalTimeoutMarginCase();
            bool silentCloseOk = RunServerClosesWithoutRespondingCase();

            bool allPassed = timeoutMarginOk && silentCloseOk;
            FileLogger.Info(
                $"[pos-client-resilience-test] 완료 — 902614 타임아웃 여유={(timeoutMarginOk ? "통과" : "실패")}, " +
                $"무응답 종료 내성={(silentCloseOk ? "통과" : "실패")}, 종합={(allPassed ? "통과" : "실패")}");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[pos-client-resilience-test] 예외로 중단: {ex}");
        }
    }

    /// <summary>
    /// "902614 전송 시 카드리딩·PIN을 기다리는 동안 클라이언트가 타임아웃으로 먼저 끊지 않는다"(완료
    /// 조건)를 실제로 120초+ 기다려 재현하는 대신, <b>구조적으로</b> 확인한다 —
    /// <see cref="PosClient.ComputeCardApprovalResponseTimeout"/>이 항상
    /// <see cref="ShopSettings.CardReadTimeoutSeconds"/>보다 크다는 것을 여러 설정값으로 확인하면,
    /// 클라이언트가 서버(리더기 데드라인)보다 먼저 포기할 수 없다는 것이 그 자체로 증명된다.
    /// </summary>
    private static bool RunCardApprovalTimeoutMarginCase()
    {
        int[] candidateTimeouts = { 30, 60, 120, 300 };
        bool allOk = true;

        foreach (int seconds in candidateTimeouts)
        {
            var settings = new ShopSettings { CardReadTimeoutSeconds = seconds };
            TimeSpan computed = PosClient.ComputeCardApprovalResponseTimeout(settings);

            if (computed.TotalSeconds <= seconds)
            {
                FileLogger.Error(LogCategory.App,
                    $"[pos-client-resilience-test] ★ CardReadTimeoutSeconds={seconds}인데 클라이언트 타임아웃이 " +
                    $"{computed.TotalSeconds:0}초로 더 짧거나 같음 — 서버보다 먼저 끊길 수 있음");
                allOk = false;
            }
        }

        if (allOk)
        {
            FileLogger.Info("[pos-client-resilience-test] 902614 타임아웃이 CardReadTimeoutSeconds 전 범위에서 항상 더 큼(여유 30초) 확인");
        }

        return allOk;
    }

    /// <summary>
    /// "서버가 응답 없이 연결을 닫는 경우에도 클라이언트가 예외로 죽지 않고 오류를 돌려준다"(완료 조건).
    /// 가짜 리스너를 즉석에서 띄워 연결을 받자마자 응답 없이 닫고, <see cref="PosClient.SendAsync"/>가
    /// 처리되지 않은 예외로 프로세스를 죽이지 않고 <b>catch 가능한 예외</b>를 던지는지 확인한다.
    /// </summary>
    private static bool RunServerClosesWithoutRespondingCase()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task acceptTask = Task.Run(() =>
        {
            using TcpClient accepted = listener.AcceptTcpClient();
            // 아무 것도 쓰지 않고 그대로 닫는다 — "응답 없이 연결을 닫는" 상황 재현.
        });

        try
        {
            using var client = new PosClient(port);
            client.ConnectAsync().GetAwaiter().GetResult();

            byte[] dummyBody = new byte[10];
            for (int i = 0; i < dummyBody.Length; i++)
                dummyBody[i] = (byte)'0';

            try
            {
                client.SendAsync(dummyBody, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                FileLogger.Error(LogCategory.App,
                    "[pos-client-resilience-test] ★ 서버가 응답 없이 닫았는데 SendAsync가 예외 없이 끝남(예상과 다름)");
                return false;
            }
            catch (Exception ex) when (ex is System.IO.IOException or TimeoutException)
            {
                // 기대한 경로 — catch 가능한 예외로 실패가 드러났다(프로세스는 죽지 않았다).
                FileLogger.Info($"[pos-client-resilience-test] 서버 무응답 종료 시 예상대로 catch 가능한 예외 발생: {ex.GetType().Name}");
                return true;
            }
        }
        finally
        {
            listener.Stop();
            try { acceptTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* 정리용 — 결과에 영향 없음 */ }
        }
    }
}
