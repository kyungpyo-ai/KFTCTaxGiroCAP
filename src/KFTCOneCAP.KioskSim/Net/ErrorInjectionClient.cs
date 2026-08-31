using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KFTCOneCAP.KioskSim.Protocol;

namespace KFTCOneCAP.KioskSim.Net
{
    /// <summary>
    /// "오류 주입" 탭 전용 로우레벨 TCP 클라이언트(Phase 19 실행계획서 P19-7).
    ///
    /// <see cref="OneCapClient"/>는 "정상적으로 완성된 프레임을 보내고 정상적으로 완성된 프레임을
    /// 받는다"는 전제로 설계돼 있어(길이 헤더 검증·부분 수신 누적 등) 여기서 재사용할 수 없다 —
    /// 오류 주입은 그 전제 자체를 깨는 것이 목적이다(선언 길이를 일부러 틀리거나, 본문을 다 안 보내고
    /// 끊거나, 응답을 일부러 안 읽는 등). 그래서 이 클래스는 <see cref="TcpClient"/>/<see cref="NetworkStream"/>을
    /// 직접 다루는 별도의 하네스다. 정상 경로 코드(<see cref="OneCapClient"/>)는 이 클래스에서도, 이
    /// 클래스가 이 클래스를 참조하는 화면에서도 건드리지 않는다.
    ///
    /// 각 메서드는 본 앱(KFTCOneCAP.Wpf)의 <c>Services/Pos/PosSocketServer</c>·
    /// <c>Protocol/Pos/PosMessageFramer</c>의 실제 동작(development_plan.md Phase 19 실행계획서
    /// "착수 전 전제"·P19-7 8개 시나리오)을 그대로 겨냥해서 만들었다 — 본 앱 소스는 참조하지 않고
    /// 문서로 확인한 계약만 근거로 삼는다(P19-2와 같은 원칙).
    /// </summary>
    public static class ErrorInjectionClient
    {
        /// <summary>CP949(본 파일 안에서 독립 정의 — 다른 파일과 공유하지 않는 P19-2 원칙).</summary>
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949);

        /// <summary>이 탭에서 응답을 기다릴 때 쓰는 일반 타임아웃(오류 주입 시나리오는 정상 902614처럼
        /// 오래 걸릴 이유가 없으므로 짧게 잡는다 — 무한정 매달리지 않기 위함).</summary>
        private const int ShortResponseTimeoutMilliseconds = 8_000;

        // ------------------------------------------------------------------
        // 공통 헬퍼
        // ------------------------------------------------------------------

        /// <summary>
        /// #4="501008"만 채운, 나머지는 전부 space(스키마 초기화 규칙)인 최소한의 501008 요청 본문.
        /// P19-4 검증 때 쓴 것과 같은 방식 — 업무적으로 유효한 값을 넣는 것이 목적이 아니라 "정상
        /// 프레이밍 왕복"을 확인하는 것이 목적이므로 이 정도로 충분하다.
        /// </summary>
        private static byte[] BuildMinimal501008Body()
        {
            var buffer = new TelegramBuffer(TelegramSchemas.Notice501008);
            buffer.Write(4, "501008");
            return buffer.ToBytes();
        }

        /// <summary>정상 프레임(길이 헤더 4바이트 + 706바이트 본문, 총 710바이트)을 만든다.</summary>
        private static byte[] BuildValid501008Frame() => TelegramCodec.Encode(BuildMinimal501008Body());

        /// <summary>
        /// 응답 본문의 <c>#7 응답 코드</c>(공통부 POSITION=20, 길이=3 — 3전문 공통, 501008/800000/902614
        /// 스키마 모두 동일한 위치다, <see cref="TelegramSchemas"/> 참고)를 스키마 객체 없이 직접
        /// 슬라이스해서 읽는다. E41 응답처럼 본문 길이가 스키마 총 길이(706/500/1500)와 다른 "최소
        /// 공통부만 있는 응답"도 있기 때문에 <see cref="TelegramBuffer"/>(스키마+본문 생성자, 길이가
        /// 다르면 예외)를 쓰지 않고 이렇게 직접 읽는다.
        /// </summary>
        private static string ReadResponseCodeRaw(byte[] responseBody)
        {
            const int position = 20;
            const int length = 3;
            if (responseBody.Length < position + length)
                return $"(응답 본문이 {responseBody.Length}바이트뿐이라 #7 위치(20~22)를 읽을 수 없음)";
            return Cp949.GetString(responseBody, position, length).TrimEnd(' ');
        }

        /// <summary>
        /// 이미 연결된 스트림에서 "[길이 4자리][본문]" 프레임 하나를 읽는다. <see cref="OneCapClient"/>의
        /// <c>ReadExact</c>와 같은 부분 수신 누적 로직이지만, 이 클래스는 정상 클라이언트 구현을
        /// 재사용하지 않는다는 원칙(위 클래스 주석)에 따라 이 파일 안에서 독립적으로 다시 짠다.
        /// </summary>
        private static byte[] ReadFrame(NetworkStream stream, int timeoutMilliseconds)
        {
            stream.ReadTimeout = timeoutMilliseconds;

            var header = new byte[4];
            ReadExact(stream, header, header.Length);

            string lengthText = Cp949.GetString(header);
            foreach (char c in lengthText)
            {
                if (c < '0' || c > '9')
                    throw new InvalidOperationException($"응답 길이 헤더가 숫자가 아니다: \"{lengthText}\".");
            }
            int bodyLength = int.Parse(lengthText, System.Globalization.CultureInfo.InvariantCulture);

            var body = new byte[bodyLength];
            ReadExact(stream, body, bodyLength);
            return body;
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, totalRead, count - totalRead);
                if (read == 0)
                    throw new EndOfStreamException($"{count}바이트 중 {totalRead}바이트만 받은 상태에서 연결이 종료됐다.");
                totalRead += read;
            }
        }

        // ------------------------------------------------------------------
        // 시나리오 1~8
        // ------------------------------------------------------------------

        /// <summary>
        /// 1) 선언 길이 ≠ 실제 본문 길이. 501008(정상 706바이트) 본문의 앞 700바이트만 잘라 길이
        /// 헤더에도 정확히 "0700"을 넣어 보낸다(선언한 길이만큼만 실제로 보낸다 — 그래야 프레이머가
        /// 딱 그 700바이트를 하나의 완성된 프레임으로 뽑아내고, 그 뒤에 찌꺼기가 남지 않는다).
        /// 그 결과 프레임 경계 자체는 깨지지 않지만 "706바이트여야 할 501008치고는 길이가 700"이라
        /// 스키마 총 길이 검사(<c>PosRequestTelegram.Parse</c>)에서 걸려 E40이 나온다.
        ///
        /// 참고(설계 메모, 2026-08-31 갱신): 반대로 "706바이트 정상 본문 전부를 보내면서 헤더만
        /// 0700으로 줄여 적는" 방식도 시도해 봤다. 그러면 프레이머가 선언된 700바이트만 프레임으로
        /// 뽑아가고 남은 6바이트(뒷부분 공백 필드)를 "다음 프레임의 길이 헤더"로 다시 해석하려다
        /// 숫자가 아니라서 예외를 던진다 — <c>PosMessageFramer.Append</c>는 그 시점에 이미 완성된
        /// 첫 프레임은 정상 반환하도록 수정됐으므로(본 앱 결함 수정, development_plan.md
        /// "본 앱 결함 수정 — PosMessageFramer.Append의 프레임 손실" 참고) 이제는 이 방식으로도
        /// E40 응답을 받을 수 있다. 다만 이 메서드는 "프레이머의 잔여 바이트 복구 동작"이 아니라
        /// "요청 자체의 길이 불일치 검사(PosRequestTelegram.Parse)"를 순수하게 겨냥하기 위해
        /// 여전히 "선언한 만큼만 보낸다" 방식을 그대로 쓴다 — 두 방식이 서로 다른 코드 경로를
        /// 검증하므로 굳이 바꿀 이유가 없다.
        /// </summary>
        public static string Scenario1_DeclaredLengthMismatch()
        {
            byte[] fullBody = BuildMinimal501008Body(); // 706바이트
            const int declaredLength = 700;
            var truncatedBody = new byte[declaredLength];
            Array.Copy(fullBody, truncatedBody, declaredLength);

            byte[] header = Cp949.GetBytes(declaredLength.ToString("D4"));
            var toSend = new byte[header.Length + truncatedBody.Length];
            Array.Copy(header, toSend, header.Length);
            Array.Copy(truncatedBody, 0, toSend, header.Length, truncatedBody.Length);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                client.Connect(OneCapClient.Host, OneCapClient.Port);
                using var stream = client.GetStream();
                stream.Write(toSend, 0, toSend.Length);

                byte[] responseBody = ReadFrame(stream, ShortResponseTimeoutMilliseconds);
                string code = ReadResponseCodeRaw(responseBody);
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 응답 수신됨(본문 {responseBody.Length}바이트), " +
                       $"#7 응답 코드=\"{code}\" — {(code == "E40" ? "기대(E40)와 일치." : "기대(E40)와 불일치! 본 앱 결함 여부 확인 필요.")}";
            }
            catch (Exception ex)
            {
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 응답을 받지 못함(연결 종료/타임아웃 등) — {ex.GetType().Name}: {ex.Message}. " +
                       "기대(E40 응답)와 다름 — 본 앱 결함 여부 확인 필요.";
            }
        }

        /// <summary>2) 알 수 없는 거래 구분 코드(#4="999999"). 나머지는 정상 501008 프레이밍(706바이트,
        /// 길이 헤더도 정확) — #4만 존재하지 않는 코드로 바꾼다. 기대: E41.</summary>
        public static string Scenario2_UnknownTransactionType()
        {
            var buffer = new TelegramBuffer(TelegramSchemas.Notice501008);
            buffer.Write(4, "999999");
            byte[] frame = TelegramCodec.Encode(buffer.ToBytes());

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                client.Connect(OneCapClient.Host, OneCapClient.Port);
                using var stream = client.GetStream();
                stream.Write(frame, 0, frame.Length);

                byte[] responseBody = ReadFrame(stream, ShortResponseTimeoutMilliseconds);
                string code = ReadResponseCodeRaw(responseBody);
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 응답 수신됨(본문 {responseBody.Length}바이트, " +
                       $"E41 응답은 공통부 70바이트만 옴이 정상), #7 응답 코드=\"{code}\" — " +
                       $"{(code == "E41" ? "기대(E41)와 일치." : "기대(E41)와 불일치! 본 앱 결함 여부 확인 필요.")}";
            }
            catch (Exception ex)
            {
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 응답을 받지 못함 — {ex.GetType().Name}: {ex.Message}. " +
                       "기대(E41 응답)와 다름 — 본 앱 결함 여부 확인 필요.";
            }
        }

        /// <summary>3) 길이 헤더 4바이트에 "abcd"(숫자가 아님)를 넣고 아무 본문이나 뒤에 붙여 보낸다.
        /// 기대: 서버가 응답 없이 그 연결을 닫는다(재동기화 불가 설계).</summary>
        public static string Scenario3_NonNumericLengthHeader()
        {
            byte[] header = Cp949.GetBytes("abcd");
            byte[] junkBody = Cp949.GetBytes(new string('X', 20));
            var toSend = new byte[header.Length + junkBody.Length];
            Array.Copy(header, toSend, header.Length);
            Array.Copy(junkBody, 0, toSend, header.Length, junkBody.Length);

            var stopwatch = Stopwatch.StartNew();
            TcpClient? client = null;
            try
            {
                client = new TcpClient();
                try
                {
                    client.Connect(OneCapClient.Host, OneCapClient.Port);
                }
                catch (SocketException ex)
                {
                    // 연결 자체가 거부된 것은 "서버가 형식 오류를 감지해 연결을 닫았다"는 관찰과
                    // 전혀 다른 상황이다(본 앱이 아예 켜져 있지 않을 때도 똑같이 SocketException이
                    // 난다) — 예전에는 이 catch가 없어 아래 포괄 catch로 떨어져 "기대와 일치"로
                    // 잘못 보고했다(2026-08-31 검증에서 발견 — M-2, 오탐(false pass) 결함).
                    return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 연결 자체가 거부됨({ex.SocketErrorCode}) — " +
                           "본 앱(KFTCOneCAP)이 실행 중인지 먼저 확인하라. 이 시나리오를 검증한 것이 아니다(확인 필요).";
                }

                using var stream = client.GetStream();
                stream.Write(toSend, 0, toSend.Length);

                stream.ReadTimeout = ShortResponseTimeoutMilliseconds;
                var probe = new byte[1];
                int read = stream.Read(probe, 0, 1); // 0이면 서버가 정상 종료(FIN)로 연결을 닫은 것.
                if (read == 0)
                {
                    return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 응답 없이 연결이 서버 쪽에서 종료됨(FIN 수신) — 기대와 일치.";
                }
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 예상과 달리 {read}바이트가 수신됨(응답이 온 것으로 보임) — " +
                       "기대(응답 없이 연결 종료)와 불일치! 본 앱 결함 여부 확인 필요.";
            }
            catch (IOException ex) when (IsTimeout(ex))
            {
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 타임아웃({ShortResponseTimeoutMilliseconds / 1000}초) 안에 " +
                       "연결 종료도 응답도 없었음 — 기대(즉시 연결 종료)와 다름. 서버가 형식 오류를 못 잡고 계속 응답을 " +
                       "대기하는 것으로 보임(확인 필요).";
            }
            catch (Exception ex)
            {
                // 연결 리셋(IOException/SocketException) 등도 "서버가 연결을 끊었다"는 관찰과 부합한다.
                // (Connect 단계의 SocketException은 위에서 이미 별도로 잡아 걸러냈으므로, 여기
                // 도달하는 것은 송수신 도중 발생한 연결 종료뿐이다.)
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 연결이 예외로 끊김({ex.GetType().Name}: {ex.Message}) — " +
                       "응답 없이 연결이 종료된 것과 같은 관찰이므로 기대와 일치로 판단.";
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>4) 정상 501008 프레임(710바이트)을 100바이트씩 나눠, 조각 사이에 짧은 지연을 두고
        /// 보낸다. 기대: 서버 프레이머가 부분 수신을 누적해 정상 응답한다.</summary>
        public static string Scenario4_ChunkedSend()
        {
            byte[] frame = BuildValid501008Frame(); // 710바이트

            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                client.Connect(OneCapClient.Host, OneCapClient.Port);
                using var stream = client.GetStream();

                const int chunkSize = 100;
                int sent = 0;
                int chunkCount = 0;
                while (sent < frame.Length)
                {
                    int len = Math.Min(chunkSize, frame.Length - sent);
                    stream.Write(frame, sent, len);
                    stream.Flush();
                    sent += len;
                    chunkCount++;
                    Thread.Sleep(80); // 조각 사이 지연 — TCP가 이를 한 번에 합쳐 보내지 않도록.
                }

                byte[] responseBody = ReadFrame(stream, ShortResponseTimeoutMilliseconds);
                string code = ReadResponseCodeRaw(responseBody);
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. {chunkCount}개 조각({chunkSize}바이트씩)으로 나눠 보냈고 " +
                       $"응답 수신됨(본문 {responseBody.Length}바이트), #7 응답 코드=\"{code}\" — " +
                       "정상 응답을 받았으므로 기대(부분 수신 누적 정상 처리)와 일치.";
            }
            catch (Exception ex)
            {
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 응답을 받지 못함 — {ex.GetType().Name}: {ex.Message}. " +
                       "기대(정상 응답)와 다름 — 본 앱 결함 여부 확인 필요.";
            }
        }

        /// <summary>5) 정상 501008 요청을 보낸 직후, 응답을 기다리지 않고 소켓을 바로 닫는다. 그 뒤
        /// 자동으로 정상 501008을 하나 더 보내(같은 <see cref="OneCapClient"/> 경로) 서버가 죽지 않고
        /// 다음 요청을 처리하는지 확인한다.</summary>
        public static string Scenario5_AbortBeforeResponse()
        {
            byte[] frame = BuildValid501008Frame();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect(OneCapClient.Host, OneCapClient.Port);
                    using (var stream = client.GetStream())
                    {
                        stream.Write(frame, 0, frame.Length);
                    }
                    // using 블록을 나가며 client.Dispose() → 응답을 한 바이트도 읽지 않고 바로 닫힘(TCP RST 가능성 포함).
                }
            }
            catch (Exception ex)
            {
                return $"[결과] 요청 전송/즉시 종료 단계에서 예외 발생 — {ex.GetType().Name}: {ex.Message}. " +
                       "후속 정상 요청 확인을 진행하지 않음.";
            }

            double abortSeconds = stopwatch.Elapsed.TotalSeconds;

            // 후속 확인: 서버가 살아 있고 다음 501008을 정상 처리하는지.
            try
            {
                var followUpResult = OneCapClient.SendAsync(frame).GetAwaiter().GetResult();
                if (followUpResult.Kind == OneCapClientResultKind.Success && followUpResult.ResponseBody != null)
                {
                    string code = ReadResponseCodeRaw(followUpResult.ResponseBody);
                    return $"[결과] 강제 종료 자체는 {abortSeconds:F2}초 만에 완료. 후속 정상 501008 요청도 " +
                           $"{followUpResult.Elapsed.TotalSeconds:F2}초 만에 성공(#7=\"{code}\") — " +
                           "서버가 죽지 않고 다음 요청을 정상 처리함, 기대와 일치.";
                }
                return $"[결과] 강제 종료는 {abortSeconds:F2}초 만에 완료. 하지만 후속 정상 501008 요청이 실패함 " +
                       $"(Kind={followUpResult.Kind}, {followUpResult.Message}) — 기대(서버가 죽지 않고 다음 요청 처리)와 " +
                       "불일치! 본 앱 결함 여부 확인 필요.";
            }
            catch (Exception ex)
            {
                return $"[결과] 강제 종료는 {abortSeconds:F2}초 만에 완료. 후속 정상 501008 확인 중 예외 — " +
                       $"{ex.GetType().Name}: {ex.Message}. 기대와 불일치할 수 있음 — 확인 필요.";
            }
        }

        /// <summary>6) 정상 501008 요청을 보내고, 연결은 열어 둔 채 응답 스트림을 절대 Read하지 않는다
        /// (서버 송신 타임아웃 5초, <c>PosSocketServer.SendTimeoutMilliseconds</c> 대상). 몇 초 유지 후
        /// 닫고, 그 뒤 자동으로 정상 501008을 하나 더 보내 워커가 막히지 않았는지 확인한다.</summary>
        public static string Scenario6_HoldResponseUnread()
        {
            byte[] frame = BuildValid501008Frame();
            var stopwatch = Stopwatch.StartNew();
            const int holdMilliseconds = 7_000; // 서버 송신 타임아웃(5초)보다 길게 잡아 둔다.

            try
            {
                using (var client = new TcpClient())
                {
                    // 클라이언트 수신 버퍼를 최대한 작게 잡아 "응답을 안 읽으면 서버 쪽 Write가
                    // 막힌다"는 상황을 재현할 확률을 높인다(응답 본문이 500~1500바이트로 작아, OS
                    // 기본 수신 버퍼 크기에서는 안 읽어도 서버 Write가 즉시 끝나버릴 수 있다 — 그러면
                    // 이 시나리오가 노리는 "5초 타임아웃 경로"를 재현하지 못한다. 재현 여부와 무관하게
                    // 결과는 있는 그대로 기록한다).
                    try { client.ReceiveBufferSize = 1; } catch { /* 일부 환경에서 무시될 수 있음 — 최선 노력. */ }

                    client.Connect(OneCapClient.Host, OneCapClient.Port);
                    using (var stream = client.GetStream())
                    {
                        stream.Write(frame, 0, frame.Length);
                        Thread.Sleep(holdMilliseconds); // 이 동안 절대 Read하지 않는다.
                    }
                }
            }
            catch (Exception ex)
            {
                return $"[결과] 요청 전송/대기 단계에서 예외 — {ex.GetType().Name}: {ex.Message}.";
            }

            double holdSeconds = stopwatch.Elapsed.TotalSeconds;

            try
            {
                var followUp = BuildValid501008Frame();
                var followUpResult = OneCapClient.SendAsync(followUp).GetAwaiter().GetResult();
                if (followUpResult.Kind == OneCapClientResultKind.Success && followUpResult.ResponseBody != null)
                {
                    string code = ReadResponseCodeRaw(followUpResult.ResponseBody);
                    return $"[결과] {holdSeconds:F2}초 동안 응답을 안 읽고 붙들었다가 닫음. 후속 정상 501008 요청은 " +
                           $"{followUpResult.Elapsed.TotalSeconds:F2}초 만에 성공(#7=\"{code}\") — 그 뒤 요청이 막히지 " +
                           "않았음, 기대와 일치. (참고: 응답 본문이 작아 서버 Write가 실제로 5초 타임아웃을 맞았는지는 " +
                           "이 결과만으로는 확정할 수 없다 — 로그(FileLogger) 대조가 필요할 수 있음.)";
                }
                return $"[결과] {holdSeconds:F2}초 동안 붙든 뒤, 후속 정상 501008 요청이 실패함 " +
                       $"(Kind={followUpResult.Kind}, {followUpResult.Message}) — 기대(다음 요청이 막히지 않음)와 " +
                       "불일치! 본 앱 결함 여부 확인 필요(워커가 멈췄을 가능성).";
            }
            catch (Exception ex)
            {
                return $"[결과] {holdSeconds:F2}초 동안 붙든 뒤 후속 확인 중 예외 — {ex.GetType().Name}: {ex.Message}.";
            }
        }

        /// <summary>7) 501008 두 개를 서로 다른 연결로 거의 동시에 보낸다. 기대: 단일 워커 큐가
        /// 직렬화해서 둘 다 정상 응답.</summary>
        public static string Scenario7_ConcurrentRequests()
        {
            byte[] frameA = BuildValid501008Frame();
            byte[] frameB = BuildValid501008Frame();

            var overall = Stopwatch.StartNew();

            Task<OneCapClientResult> taskA = OneCapClient.SendAsync(frameA);
            Task<OneCapClientResult> taskB = OneCapClient.SendAsync(frameB);

            try
            {
                Task.WaitAll(new Task[] { taskA, taskB }, ShortResponseTimeoutMilliseconds);
            }
            catch (AggregateException)
            {
                // 개별 Task 결과에서 원인을 마저 판별한다(아래에서 Kind로 구분).
            }

            var resultA = taskA.IsCompleted ? taskA.Result : null;
            var resultB = taskB.IsCompleted ? taskB.Result : null;

            bool successA = resultA?.Kind == OneCapClientResultKind.Success;
            bool successB = resultB?.Kind == OneCapClientResultKind.Success;

            string codeA = successA ? ReadResponseCodeRaw(resultA!.ResponseBody!) : "(없음)";
            string codeB = successB ? ReadResponseCodeRaw(resultB!.ResponseBody!) : "(없음)";

            string summaryA = resultA == null
                ? "타임아웃(완료 안 됨)"
                : $"{resultA.Kind}, {resultA.Elapsed.TotalSeconds:F2}초, #7=\"{codeA}\"";
            string summaryB = resultB == null
                ? "타임아웃(완료 안 됨)"
                : $"{resultB.Kind}, {resultB.Elapsed.TotalSeconds:F2}초, #7=\"{codeB}\"";

            bool bothOk = successA && successB;
            return $"[결과] 총 {overall.Elapsed.TotalSeconds:F2}초. 연결A: {summaryA} / 연결B: {summaryB} — " +
                   (bothOk
                       ? "둘 다 정상 응답, 기대(단일 워커 큐 직렬화 후 둘 다 처리)와 일치."
                       : "하나 이상 실패! 기대(둘 다 정상 응답)와 불일치 — 본 앱 결함 여부 확인 필요.");
        }

        /// <summary>8) 길이 헤더에 "9999"(4자리로 표현 가능한 최대값)를 써 넣고, 그 뒤로 프레임을 절대
        /// 완성시키지 않을 만큼(64KB 넘게) 의미 없는 바이트를 계속 보낸다. 기대: 서버가 버퍼 상한을
        /// 넘기면 연결을 닫는다.
        ///
        /// 구현 메모(중요, 2026-08-31 갱신): "9999"로 선언 가능한 최대 본문(9999바이트, 헤더 포함
        /// 프레임 10003바이트)은 서버 프레이머의 버퍼 상한(64KB=65536바이트)보다 훨씬 작다 — 즉
        /// 10003바이트 이상만 누적되면 그 시점에 프레이머가 "완성된 프레임"으로 뽑아가 버리고, 그 뒤에
        /// 보낸 나머지 쓰레기 바이트가 "다음 프레임의 길이 헤더"로 다시 해석되다 숫자가 아니라서
        /// 예외가 난다. <c>PosMessageFramer.Append</c> 수정(본 앱 결함 수정, 2026-08-31) 이후에는 그
        /// 시점에 이미 뽑아둔 첫 "프레임"(길이 헤더가 우연히 뒤섞인 쓰레기 바이트라도 형식상 완성된
        /// 프레임으로 잡힐 수 있다)이 처리되어 응답이 하나 더 나갈 수 있다 — 실제로 8번 시나리오
        /// 재검증에서 예상 못한 E41 응답이 하나 더 나가는 것이 관찰됐다(연결은 여전히 정상 종료됨,
        /// development_plan.md "본 앱 결함 수정" 기록의 시나리오 8행 참고, 사용자가 무해하다고
        /// 수용함). 즉 이 경로로는 "64KB 넘게 쌓여서" 닫히는 게 아니라 "10KB 언저리에서, 다른
        /// 이유로(+부작용으로 응답 프레임 하나가 더 옴)" 닫힐 가능성이 높다. 그래도 관찰 가능한
        /// 최종 결과(응답 없이 연결이 닫힌다 — 또는 이 부작용 후 닫힌다)는 이 시나리오가 기대하는
        /// 결과와 같으므로 버튼은 그대로 실행하고, 실제로 몇 바이트에서/어떤 예외로 닫혔는지를 있는
        /// 그대로 기록한다(추측하지 않는다 — development_plan.md P19-7 완료 조건).
        /// </summary>
        public static string Scenario8_BufferOverflowAttempt()
        {
            var stopwatch = Stopwatch.StartNew();
            const int junkChunkSize = 4096;
            const long targetTotalBytes = 80_000; // 64KB(65536)를 넉넉히 넘는 총량.
            var junkChunk = new byte[junkChunkSize];
            for (int i = 0; i < junkChunk.Length; i++)
                junkChunk[i] = (byte)'Z'; // 숫자가 아닌 값 — 우연히 유효한 길이 헤더로 오인되지 않게.

            long totalSent = 0;
            TcpClient? client = null;
            try
            {
                client = new TcpClient();
                try
                {
                    client.Connect(OneCapClient.Host, OneCapClient.Port);
                }
                catch (SocketException ex)
                {
                    // 시나리오3과 같은 이유(M-2, 2026-08-31) — 연결 거부는 "서버가 연결을 끊었다"는
                    // 기대 결과와 다른 상황(본 앱 미실행)이므로 별도로 걸러 오탐을 막는다.
                    return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 연결 자체가 거부됨({ex.SocketErrorCode}) — " +
                           "본 앱(KFTCOneCAP)이 실행 중인지 먼저 확인하라. 이 시나리오를 검증한 것이 아니다(확인 필요).";
                }

                using var stream = client.GetStream();

                byte[] header = Cp949.GetBytes("9999"); // 4자리로 표현 가능한 최대 선언 길이.
                stream.Write(header, 0, header.Length);
                totalSent += header.Length;

                while (totalSent < targetTotalBytes)
                {
                    stream.Write(junkChunk, 0, junkChunk.Length);
                    totalSent += junkChunk.Length;
                }

                // 여기까지 예외 없이 다 보내졌다면, 서버가 연결을 안 끊고 계속 받아준 것 — 응답을
                // 기다려 본다(응답이 온다면 그 자체가 이상 상황).
                stream.ReadTimeout = ShortResponseTimeoutMilliseconds;
                var probe = new byte[1];
                int read = stream.Read(probe, 0, 1);
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. {totalSent}바이트를 전부 보냈는데도 연결이 끊기지 " +
                       $"않음(Read 결과={read}) — 기대(버퍼 상한 초과 시 연결 종료)와 불일치! 본 앱 결함 여부 확인 필요.";
            }
            catch (Exception ex)
            {
                // 쓰기 도중(상대가 이미 닫아서) 또는 읽기 도중 예외가 나는 것이 "연결이 끊겼다"는 관찰이다.
                return $"[결과] {stopwatch.Elapsed.TotalSeconds:F2}초. 총 {totalSent}바이트를 보낸 시점에 연결이 끊김 " +
                       $"({ex.GetType().Name}: {ex.Message}) — 응답 없이 연결이 종료됐으므로 기대(서버가 연결을 닫는다)와 " +
                       "관찰 결과는 일치한다(정확한 트리거 지점은 위 구현 메모 참고 — 버퍼 64KB 초과가 아니라 더 " +
                       "이른 시점의 비숫자 길이 헤더 예외일 가능성이 높음).";
            }
            finally
            {
                client?.Dispose();
            }
        }

        private static bool IsTimeout(IOException ex)
            => ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut;
    }
}
