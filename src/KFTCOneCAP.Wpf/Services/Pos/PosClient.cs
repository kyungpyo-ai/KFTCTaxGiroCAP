using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Settings;

namespace KFTCOneCAP.Wpf.Services.Pos;

/// <summary>
/// Phase 29 P29-5(docs/payment_relay/development_plan.md, PRD.md §12.4) — POS(키오스크) 역할을 대행하는
/// 루프백 소켓 클라이언트. <see cref="Diagnostics.PosClientTestScenarios"/>가 이미 쓰던 프레이밍 패턴
/// (<see cref="PosMessageFramer.BuildFrame"/>/<c>Append</c>)을 그대로 재사용한다 — 새로 설계하지 않는다.
///
/// <b>연결 1개당 인스턴스 1개</b>다(<see cref="PosMessageFramer"/>가 그렇듯 내부에 누적 버퍼 상태를
/// 갖는다). <see cref="SendAsync"/>가 <b>어떤 이유로든(타임아웃, 서버의 무응답 연결 종료 등) 예외를
/// 던지면</b> 이 인스턴스는 더 쓸 수 없다 — 실패 경로 전체를 감싼 <c>catch</c>가 예외 종류와 무관하게
/// 소켓을 강제로 닫는다(체크포인트 1 검증 L-1, 2026-09-18 — 처음엔 타임아웃 경로만 스스로 정리하고
/// 무응답 종료 경로는 호출자의 <c>using</c>에 기대는 비일관성이 있었다). 타임아웃 시 소켓을 닫는 이유는
/// .NET Framework의 <c>NetworkStream.ReadAsync</c>가 취소 토큰만으로는 실제 I/O를 멈추지 못하는 경우가
/// 있어서다. 호출자는 실패 후 새 <see cref="PosClient"/>를 만들어 다시 연결해야 한다.
/// </summary>
public sealed class PosClient : IDisposable
{
    private const int Port = 8002;
    private const int ConnectTimeoutMilliseconds = 5000;

    /// <summary>서버의 <c>PosSocketServer.SendTimeoutMilliseconds</c>와 대칭— 우리가 쓰기 자체에서
    /// 막히는 경우도 같은 기준으로 포기한다.</summary>
    private const int WriteTimeoutMilliseconds = 5000;

    /// <summary>카드리딩이 필요한 요청(902614)의 응답 대기 타임아웃 = <c>CardReadTimeoutSeconds</c> +
    /// 이 여유(네트워크 왕복·VAN 처리 시간 대비, PRD §12.4). 기본 120초 설정이면 150초가 된다.</summary>
    private const int CardApprovalResponseTimeoutMarginSeconds = 30;

    /// <summary>카드리딩이 필요 없는 요청(501008/800000)의 기본 응답 대기 — 서버의 유휴 타임아웃
    /// (<c>PosSocketServer.IdleAfterResponseTimeoutMilliseconds</c>=10초)과 대칭으로 잡는다.</summary>
    public static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(10);

    private readonly int _port;
    private readonly TcpClient _tcpClient = new();
    private readonly PosMessageFramer _framer = new();
    private NetworkStream? _stream;
    private bool _disposed;

    /// <summary><paramref name="port"/>는 셀프테스트가 가짜 리스너를 붙이기 위한 것뿐이다 — 실제 배선은
    /// 항상 기본값(8002, PRD §2.1)을 쓴다.</summary>
    public PosClient(int port = Port)
    {
        _port = port;
    }

    public bool IsConnected => !_disposed && _tcpClient.Connected;

    /// <summary>902614(카드 승인)에 쓸 응답 타임아웃을 가맹점 설정의 카드입력 타임아웃 기반으로 계산한다
    /// (임의로 정하지 않는다 — PRD §12.4).</summary>
    public static TimeSpan ComputeCardApprovalResponseTimeout(ShopSettings shopSettings) =>
        TimeSpan.FromSeconds(shopSettings.CardReadTimeoutSeconds + CardApprovalResponseTimeoutMarginSeconds);

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Task connectTask = _tcpClient.ConnectAsync(IPAddress.Loopback, _port);
        Task delayTask = Task.Delay(ConnectTimeoutMilliseconds, cancellationToken);

        Task first = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
        if (first == delayTask)
        {
            throw new TimeoutException($"localhost:{_port} 연결이 {ConnectTimeoutMilliseconds}ms 안에 끝나지 않음");
        }

        await connectTask.ConfigureAwait(false); // 연결 자체가 실패했으면(포트 닫힘 등) 원래 예외를 그대로 전파.

        _stream = _tcpClient.GetStream();
        _stream.WriteTimeout = WriteTimeoutMilliseconds;
    }

    /// <summary>
    /// 완성된 본문(이미 스키마 총 길이로 패딩된 706/500/1500바이트)을 프레임으로 감싸 보내고, 응답
    /// 프레임(본문만, 길이 헤더 제외)을 돌려받는다. <paramref name="responseTimeout"/>은 호출자가 전문
    /// 종류에 맞게 넉넉히 잡아야 한다 — 902614는 <see cref="ComputeCardApprovalResponseTimeout"/>을 쓰고,
    /// 나머지는 <see cref="DefaultResponseTimeout"/>을 쓴다.
    /// </summary>
    public async Task<byte[]> SendAsync(byte[] bodyBytes, TimeSpan responseTimeout, CancellationToken cancellationToken = default)
    {
        if (_stream is null)
            throw new InvalidOperationException($"{nameof(ConnectAsync)}를 먼저 호출해야 함");

        try
        {
            byte[] frame = PosMessageFramer.BuildFrame(bodyBytes);
            await _stream.WriteAsync(frame, 0, frame.Length, cancellationToken).ConfigureAwait(false);

            Task<byte[]> readTask = ReadUntilFrameCompleteAsync(cancellationToken);
            Task delayTask = Task.Delay(responseTimeout, cancellationToken);

            Task first = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
            if (first == delayTask)
            {
                // 대기 중이던 읽기를 확실히 끝내려고 소켓을 강제로 닫는다(클래스 주석 참고). readTask가
                // 그 뒤 예외로 완료돼도 아무도 await하지 않은 채 방치되지 않도록(unobserved task exception)
                // 여기서 직접 소비한다. 실제 Dispose()는 아래 catch(finally 아님, 클래스 주석 참고)가
                // 담당 — 이 타임아웃 예외도 그 경로를 그대로 타게 한다.
                _ = readTask.ContinueWith(
                    t => { _ = t.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                throw new TimeoutException($"응답을 {responseTimeout.TotalSeconds:0}초 안에 받지 못함(연결을 버림 — 새 {nameof(PosClient)}로 재시도할 것)");
            }

            return await readTask.ConfigureAwait(false);
        }
        catch
        {
            // 체크포인트 1 검증(L-1, 2026-09-18) — 실패 경로마다 제각각 정리하지 않고, SendAsync가
            // 던지는 모든 예외(타임아웃/IOException/그 외)에서 공통으로 소켓을 닫는다. 클래스 계약
            // ("실패 후 이 인스턴스는 항상 폐기 대상")을 코드로 강제해, 호출자가 using을 빠뜨려도
            // 핸들이 살아남지 않게 한다.
            Dispose();
            throw;
        }
    }

    private async Task<byte[]> ReadUntilFrameCompleteAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (true)
        {
            int read = await _stream!.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("서버가 응답 없이 연결을 닫음");

            byte[] chunk = new byte[read];
            Buffer.BlockCopy(buffer, 0, chunk, 0, read);

            IReadOnlyList<byte[]> frames = _framer.Append(chunk);
            if (frames.Count > 0)
                return frames[0]; // 요청 1건 → 응답 1건. 첫 프레임이면 충분하다.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stream?.Dispose();
        _tcpClient.Dispose();
    }
}
