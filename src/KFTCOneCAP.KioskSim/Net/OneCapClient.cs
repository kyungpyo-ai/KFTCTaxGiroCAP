using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KFTCOneCAP.KioskSim.Protocol;

namespace KFTCOneCAP.KioskSim.Net
{
    /// <summary>
    /// 결과 종류. 호출부(P19-5/P19-6 화면)가 "실패"로 뭉뚱그리지 않고 원인을 구분해서
    /// 사용자에게 보여줄 수 있도록 나눈다(Phase 19 실행계획서 P19-4).
    /// </summary>
    public enum OneCapClientResultKind
    {
        /// <summary>정상 왕복 완료.</summary>
        Success,

        /// <summary>수신 타임아웃(180초) 안에 응답이 다 오지 않았다.</summary>
        Timeout,

        /// <summary>연결 자체가 거부됐다 — 본 앱(KFTCOneCAP)이 켜져 있지 않을 때 발생한다.
        /// 타임아웃과 달리 즉시(수 초 이내) 알 수 있다.</summary>
        ConnectionRefused,

        /// <summary>연결은 됐지만 응답을 다 받기 전에 상대가 스트림을 끊었다(TCP 리셋, 정상 종료
        /// 등). 타임아웃(응답이 아예 안 옴)과 구분되는 별개 상황이다.</summary>
        ConnectionClosed,

        /// <summary>위 셋에 해당하지 않는 그 외 예외.</summary>
        OtherError,
    }

    /// <summary>
    /// <see cref="OneCapClient.SendAsync"/>의 결과. 성공 여부만 bool로 뭉개지 않고
    /// <see cref="Kind"/>로 4가지 실패 원인을 구분한다 — "실패"로 뭉뚱그리면 본 앱을 안 띄운
    /// 것인지 응답이 안 온 것인지 알 수 없다(Phase 19 실행계획서 P19-4 지적 사항).
    /// </summary>
    public sealed class OneCapClientResult
    {
        public OneCapClientResultKind Kind { get; }

        /// <summary>성공 시 응답 프레임 전체(길이 헤더 4바이트 + 본문). 실패 시 null.</summary>
        public byte[]? ResponseFrame { get; }

        /// <summary>성공 시 응답 본문만(길이 헤더 제외). 실패 시 null.</summary>
        public byte[]? ResponseBody { get; }

        /// <summary>연결 시작부터 결과가 확정되기까지 걸린 시간.</summary>
        public TimeSpan Elapsed { get; }

        /// <summary>실패 시 원인 예외(있으면). 성공 시 null.</summary>
        public Exception? Error { get; }

        /// <summary>화면에 그대로 보여줄 수 있는 사람이 읽는 요약 메시지.</summary>
        public string Message { get; }

        private OneCapClientResult(
            OneCapClientResultKind kind,
            byte[]? responseFrame,
            byte[]? responseBody,
            TimeSpan elapsed,
            Exception? error,
            string message)
        {
            Kind = kind;
            ResponseFrame = responseFrame;
            ResponseBody = responseBody;
            Elapsed = elapsed;
            Error = error;
            Message = message;
        }

        public static OneCapClientResult Success(byte[] responseFrame, byte[] responseBody, TimeSpan elapsed)
            => new OneCapClientResult(
                OneCapClientResultKind.Success,
                responseFrame,
                responseBody,
                elapsed,
                null,
                $"응답 수신 완료({elapsed.TotalSeconds:F1}초, 본문 {responseBody.Length}바이트).");

        public static OneCapClientResult Timeout(TimeSpan elapsed)
            => new OneCapClientResult(
                OneCapClientResultKind.Timeout,
                null,
                null,
                elapsed,
                null,
                $"응답 수신 타임아웃({elapsed.TotalSeconds:F1}초 경과, 기준 {OneCapClient.ReceiveTimeoutMilliseconds / 1000}초) — " +
                "본 앱은 연결됐지만 응답이 시간 안에 오지 않았다.");

        public static OneCapClientResult ConnectionRefused(Exception error, TimeSpan elapsed)
            => new OneCapClientResult(
                OneCapClientResultKind.ConnectionRefused,
                null,
                null,
                elapsed,
                error,
                $"연결 거부됨({elapsed.TotalSeconds:F1}초) — {OneCapClient.Host}:{OneCapClient.Port}에 연결할 수 없다. " +
                "본 앱(KFTCOneCAP)이 실행 중인지 확인하라.");

        public static OneCapClientResult ConnectionClosed(string reason, Exception? error, TimeSpan elapsed)
            => new OneCapClientResult(
                OneCapClientResultKind.ConnectionClosed,
                null,
                null,
                elapsed,
                error,
                $"연결이 응답 수신 중 끊겼다({elapsed.TotalSeconds:F1}초) — {reason}");

        public static OneCapClientResult OtherError(Exception error, TimeSpan elapsed)
            => new OneCapClientResult(
                OneCapClientResultKind.OtherError,
                null,
                null,
                elapsed,
                error,
                $"예상하지 못한 오류({elapsed.TotalSeconds:F1}초) — {error.GetType().Name}: {error.Message}");
    }

    /// <summary>
    /// KFTCOneCAP(본 앱)과 통신하는 TCP 클라이언트.
    ///
    /// **한 번의 요청·응답이 곧 한 번의 연결이다**(Phase 19 실행계획서 P19-4, ROADMAP 확정) —
    /// 연결 → <see cref="TelegramCodec"/>로 만든 프레임 전송 → 응답 수신 → 연결 닫기를 매 전문마다
    /// 반복한다. 지속 연결(keep-alive)은 쓰지 않는다.
    ///
    /// **부분 수신 누적을 반드시 구현한다** — TCP는 스트림이라 1500바이트 응답이 한 번의
    /// <see cref="NetworkStream.Read"/> 호출로 다 오는 것이 보장되지 않는다(업체가 가장 자주
    /// 틀리는 지점). 그래서 이 클래스는
    ///   1) 길이 헤더 4바이트가 다 모일 때까지 반복해서 Read를 호출해 누적하고,
    ///   2) <see cref="TelegramCodec.ReadLengthHeader"/>로 본문 길이를 알아낸 뒤,
    ///   3) 그 길이만큼 본문이 다 올 때까지 다시 반복해서 Read를 호출해 누적한다.
    /// 한 번의 Read가 몇 바이트를 줄지는 알 수 없다는 전제로 짰다(1바이트씩만 줄 수도 있다).
    ///
    /// 이 클래스는 순수 네트워킹 계층이다 — WinForms/WPF 등 UI 프레임워크를 전혀 모른다.
    /// UI 스레드로 결과를 되돌리는 것(Control.Invoke 등)은 호출부(P19-5/6)의 책임이다.
    /// </summary>
    public static class OneCapClient
    {
        /// <summary>본 앱 소켓 서버 주소. 로드맵/실행계획서 전제대로 루프백 고정.</summary>
        public const string Host = "127.0.0.1";

        /// <summary>본 앱 소켓 서버 포트(<c>Services/Pos/PosSocketServer</c>).</summary>
        public const int Port = 8002;

        /// <summary>
        /// 수신 타임아웃(밀리초). 180초 고정 — Phase 18 실장비 검증에서 PIN 입력을 포함한
        /// 902614 응답이 150.1초 걸린 실측이 있어, 여유를 두고 180초로 잡았다(실행계획서 결정 5).
        /// 이 값은 연결 후 "응답을 기다리는" 구간에만 적용된다. 연결 자체가 거부되는 경우
        /// (본 앱 미구동)는 이 타임아웃과 무관하게 즉시 실패한다.
        /// </summary>
        public const int ReceiveTimeoutMilliseconds = 180_000;

        /// <summary>진행 콜백(경과 시간)을 몇 밀리초마다 부를지. 화면의 "응답 대기 중… (n초)" 표시용.</summary>
        private const int ProgressIntervalMilliseconds = 500;

        /// <summary>
        /// 이미 <see cref="TelegramCodec.Encode"/>로 만든 요청 프레임(길이 헤더 4바이트 + 본문)을
        /// 보내고 응답 프레임을 받는다. 연결·송수신은 백그라운드 스레드(<see cref="Task.Run(Action)"/>)에서
        /// 동기적으로 수행하므로, 호출부(UI 스레드)는 이 Task를 await하는 동안 막히지 않는다.
        /// </summary>
        /// <param name="requestFrame">전송할 프레임 전체 바이트(<see cref="TelegramCodec.Encode"/> 결과).</param>
        /// <param name="onElapsed">
        /// 응답을 기다리는 동안 주기적으로(약 <see cref="ProgressIntervalMilliseconds"/>ms 간격)
        /// 경과 시간을 알려주는 콜백. 이 콜백은 백그라운드 스레드에서 호출되므로, UI 컨트롤을
        /// 직접 건드리는 호출부라면 그 안에서 Control.Invoke 등으로 UI 스레드로 넘겨야 한다.
        /// null이면 진행 통지를 하지 않는다.
        /// </param>
        public static Task<OneCapClientResult> SendAsync(byte[] requestFrame, Action<TimeSpan>? onElapsed = null)
        {
            if (requestFrame == null)
                throw new ArgumentNullException(nameof(requestFrame));

            return Task.Run(() => SendSync(requestFrame, onElapsed));
        }

        private static OneCapClientResult SendSync(byte[] requestFrame, Action<TimeSpan>? onElapsed)
        {
            var stopwatch = Stopwatch.StartNew();
            Timer? progressTimer = null;

            try
            {
                using (var client = new TcpClient())
                {
                    // 1) 연결. 본 앱이 꺼져 있으면 여기서 즉시(수 초 이내) SocketException이
                    //    발생한다 — 응답 타임아웃(180초)과는 완전히 별개의 실패라 먼저 구분해서 잡는다.
                    try
                    {
                        client.Connect(Host, Port);
                    }
                    catch (SocketException ex)
                    {
                        return OneCapClientResult.ConnectionRefused(ex, stopwatch.Elapsed);
                    }

                    using (var stream = client.GetStream())
                    {
                        // 진행 콜백: 연결 이후(전송+수신 대기) 구간 동안 주기적으로 경과 시간을 알린다.
                        if (onElapsed != null)
                        {
                            progressTimer = new Timer(
                                _ => onElapsed(stopwatch.Elapsed),
                                null,
                                ProgressIntervalMilliseconds,
                                ProgressIntervalMilliseconds);
                        }

                        // 2) 프레임 전송.
                        stream.WriteTimeout = ReceiveTimeoutMilliseconds;
                        stream.Write(requestFrame, 0, requestFrame.Length);

                        // 3) 응답 수신 — 부분 수신 누적. 이 구간에서만 ReadTimeout이 의미를 갖는다.
                        stream.ReadTimeout = ReceiveTimeoutMilliseconds;

                        var header = new byte[TelegramCodec.LengthHeaderSize];
                        ReadExact(stream, header, header.Length);

                        int bodyLength = TelegramCodec.ReadLengthHeader(header, 0);
                        var body = new byte[bodyLength];
                        ReadExact(stream, body, bodyLength);

                        var responseFrame = new byte[header.Length + body.Length];
                        Array.Copy(header, 0, responseFrame, 0, header.Length);
                        Array.Copy(body, 0, responseFrame, header.Length, body.Length);

                        return OneCapClientResult.Success(responseFrame, body, stopwatch.Elapsed);
                    }
                }
            }
            catch (EndOfStreamException ex)
            {
                // 응답을 다 받기 전에 상대가 스트림을 정상 종료(0바이트 Read)한 경우.
                return OneCapClientResult.ConnectionClosed(ex.Message, ex, stopwatch.Elapsed);
            }
            catch (IOException ex) when (IsTimeoutException(ex))
            {
                // NetworkStream.Read/Write가 ReadTimeout/WriteTimeout을 넘기면 .NET Framework에서는
                // IOException(내부에 SocketException(SocketErrorCode=TimedOut))으로 던져진다.
                return OneCapClientResult.Timeout(stopwatch.Elapsed);
            }
            catch (IOException ex)
            {
                // 타임아웃이 아닌 그 외 IOException — 상대가 연결을 리셋(RST)하는 등, 응답을
                // 다 받기 전에 연결이 비정상적으로 끊긴 경우다.
                return OneCapClientResult.ConnectionClosed(ex.Message, ex, stopwatch.Elapsed);
            }
            catch (SocketException ex)
            {
                // Connect 단계 SocketException은 위에서 이미 ConnectionRefused로 잡았으므로,
                // 여기 도달하는 것은 송수신 도중 소켓 계층에서 발생한 그 외 오류다 — 연결이 끊긴
                // 것으로 분류한다(타임아웃도 아니고 연결 거부도 아니므로).
                return OneCapClientResult.ConnectionClosed(ex.Message, ex, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                return OneCapClientResult.OtherError(ex, stopwatch.Elapsed);
            }
            finally
            {
                progressTimer?.Dispose();
            }
        }

        /// <summary>
        /// buffer가 다 채워질 때까지 stream.Read를 반복 호출해 누적한다(부분 수신 누적).
        /// 한 번의 Read가 buffer.Length보다 적은 바이트만 줄 수 있다는 전제로 루프를 돈다.
        /// 상대가 다 받기 전에 스트림을 정상 종료하면(Read가 0을 반환) <see cref="EndOfStreamException"/>을 던진다.
        /// </summary>
        private static void ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, totalRead, count - totalRead);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"응답 {count}바이트 중 {totalRead}바이트만 받은 상태에서 연결이 종료됐다.");
                }
                totalRead += read;
            }
        }

        /// <summary>
        /// NetworkStream의 Read/Write 타임아웃 초과 시 던져지는 IOException인지 판별한다.
        /// .NET Framework의 NetworkStream은 내부적으로 Socket.Receive/Send를 쓰고, 타임아웃이면
        /// SocketException(SocketErrorCode=TimedOut)을 IOException으로 감싸서 던진다.
        /// </summary>
        private static bool IsTimeoutException(IOException ex)
        {
            return ex.InnerException is SocketException socketEx
                && socketEx.SocketErrorCode == SocketError.TimedOut;
        }
    }
}
