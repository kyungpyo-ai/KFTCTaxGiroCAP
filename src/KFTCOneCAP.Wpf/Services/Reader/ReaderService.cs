using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Interop;
using KFTCOneCAP.Wpf.Protocol.Reader;
using KFTCOneCAP.Wpf.Security;
using KFTCOneCAP.Wpf.Services.Diagnostics;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// 리더기 1개 포트에 대한 흐름/상태 계층. 포트별 인스턴스로 설계한다(Phase 10 P10-3, PRD
    /// §2.2.3 이중화 전제) — "리더기1 전용 싱글턴"으로 만들지 않는다. Phase 9 파일럿(0x60/0x70
    /// 1종)을 Phase 10에서 명령 4종(0x60/0x61/0x62/0x2B)으로 확장하면서, 공통 부분(재연결 래퍼,
    /// 단일 유효 응답 게이트)을 SendAndAwaitAsync 하나로 모았다 — 명령별 공개 메서드는 요청 Data
    /// 조립(Protocol 위임)과 응답 파싱(Protocol 위임)만 다르고 나머지는 전부 공유한다.
    ///
    /// 계층 규칙: 이 클래스는 Protocol(Reader 응답 파서/요청 빌더)과 Interop(ReaderSerialNative)만
    /// 알고, WPF 타입(Dispatcher 등)을 전혀 참조하지 않는다. EventReceived는 네이티브 콜백 스레드에서
    /// 그대로 raise되므로, UI로 마샬링하는 책임은 이 이벤트를 구독하는 ViewModel에 있다.
    /// </summary>
    internal sealed class ReaderService : IKeyDownloadReaderEndpoint
    {
        // ===================== 콜백 델리게이트 수명 (P9-2, Phase 10에서도 불변) =====================
        //
        // Reader_OpenPort에 넘긴 델리게이트 인스턴스를 인스턴스 필드로 계속 참조해야 한다. 지역
        // 변수로만 넘기면 그 델리게이트를 참조하는 관리 코드 쪽 루트가 없어 GC가 회수할 수 있고,
        // 이후 네이티브 수신 스레드가 이미 해제된 함수 포인터를 호출하면
        // CallbackOnCollectedDelegate로 프로세스가 죽는다 — DLL 문제가 아니라 이쪽 책임이다.
        // 이 프로젝트는 핀패드를 쓰지 않으므로(PRD §2.2.1/§10) pinpadCallback에는 항상 null을
        // 그대로 넘긴다.
        private readonly ReaderCallback _nativeReaderCallback;

        private int _readerId = -1;

        // P10-3 재연결 래퍼가 "readerId 없음"일 때 스스로 재오픈하려면 마지막으로 요청받은
        // 포트/보드레이트를 기억해야 한다(vendor/ReaderSerial/CSharpSample/MainForm.cs의
        // TryAutoOpenReader와 동일한 필요성 — 그쪽은 UI 텍스트박스에서 다시 읽지만, 이 서비스는
        // UI를 모르므로 마지막 OpenPort 호출값을 그대로 저장해 재사용한다).
        private int _portNumber;
        private int _baudRate;

        // P10-4 단일 유효 응답 게이트의 "현재 유효한 라운드". PendingReaderCommand.cs의 클래스
        // 주석에 CAS 기반 동작 원리를 정리해 뒀다 — 이 필드에 대한 모든 교체는
        // Interlocked.CompareExchange로만 이뤄지고, 읽기는 Volatile.Read로만 이뤄진다(필드 자체에
        // volatile 키워드를 붙이면 CS0420 경고 없이 Interlocked에 ref로 넘길 수 없어, 대신
        // Volatile.Read/Interlocked.CompareExchange 조합으로 등가의 메모리 가시성을 확보한다).
        private PendingReaderCommand? _pending;
        private long _roundToken;

        internal ReaderService()
        {
            _nativeReaderCallback = OnReaderCallback;
        }

        internal bool IsConnected => _readerId >= 0;

        internal int ReaderId => _readerId;

        /// <summary>
        /// 마지막으로 <see cref="OpenPort"/>에 넘긴 포트 번호(닫혀 있어도 기억된 값이 남는다 —
        /// 위 필드 주석 참고). 2026-08-20(P12 수정) — 리더기 설정 화면이 "화면에 선택된 콤보 값
        /// = 실제 연결 대상"을 항상 유지하려면(확인/취소로 저장되기 전에도), 액션 버튼을 누르기
        /// 전에 이 값과 현재 콤보 선택이 같은지 비교해야 한다(<see cref="ReaderConnectionManager.EnsureOpenForSelection"/>).
        /// </summary>
        internal int PortNumber => _portNumber;

        /// <summary>네이티브 콜백 스레드에서 그대로 raise된다(P9-2 규칙) — 구독자가 UI 스레드로
        /// 마샬링해야 한다. data는 이미 이 이벤트가 만들어지기 전에 Marshal.Copy로 복사됐다.</summary>
        internal event EventHandler<ReaderEventArgs>? EventReceived;

        /// <summary>
        /// baudRate는 PRD §2.2.1/§10에 따라 이 계층 호출자가 115200으로 고정해 넘긴다(이 서비스
        /// 자체는 값을 강제하지 않는다). pinpadCallback은 이 프로젝트 범위에서 항상 null이다
        /// (PRD §2.2.1/§10, 핀패드 미사용). 성공 여부와 무관하게 portNumber/baudRate는 항상
        /// 기억해 둔다 — 첫 시도가 실패해도(리더기 미연결 등) 이후 SendCommandSafe가 같은 값으로
        /// 재시도할 수 있어야 하기 때문이다(PRD §2.2.2 "결제 요청 시 다시 시도").
        /// </summary>
        internal ReaderOpenResult OpenPort(int portNumber, int baudRate)
        {
            _portNumber = portNumber;
            _baudRate = baudRate;

            int dllResult = ReaderSerialNative.Reader_OpenPort(
                portNumber, baudRate, _nativeReaderCallback, null, IntPtr.Zero, out int newReaderId);

            bool success = dllResult == (int)ReaderResult.READER_OK;
            if (success)
            {
                // 재오픈 성공 시 새 readerId로 반드시 덮어쓴다(P10-3, 옛 id 재사용 금지).
                _readerId = newReaderId;
            }

            return new ReaderOpenResult(success, success ? newReaderId : -1, dllResult, ReaderNames.ReaderResultToString(dllResult));
        }

        internal ReaderCallResult ClosePort()
        {
            if (_readerId < 0)
                return new ReaderCallResult(true, (int)ReaderResult.READER_OK, nameof(ReaderResult.READER_OK));

            int dllResult = ReaderSerialNative.Reader_ClosePort(_readerId);
            bool success = dllResult == (int)ReaderResult.READER_OK;
            if (success)
            {
                _readerId = -1;
            }

            return new ReaderCallResult(success, dllResult, ReaderNames.ReaderResultToString(dllResult));
        }

        /// <summary>상태 표시용으로만 호출한다 — 명령 송신 전 사전 게이트로 쓰지 않는다
        /// (docs/reader_dll/DLL연동가이드.md §1.3, PRD §2.2.4).</summary>
        internal int IsPortOpen() => _readerId < 0 ? 0 : ReaderSerialNative.Reader_IsPortOpen(_readerId);

        // ===================== 공개 명령 4종 (P10-1/P10-2) =====================

        /// <summary>0x60(초기화) 전송 → 0x70 응답 대기 → 첫 2byte(ASCII) 업무 응답코드 판정.</summary>
        internal async Task<InitCommandOutcome> SendInitCommandAsync(TimeSpan timeout)
        {
            var raw = await SendAndAwaitAsync(ReaderCommandCodes.INIT_REQUEST, ReaderCommandCodes.INIT_RESPONSE, null, 0, timeout).ConfigureAwait(false);
            return MapInitOutcome(raw);
        }

        /// <summary>0x61(상태체크) 전송 → 0x71 응답 대기 → 응답코드 + 리더기 인증 식별번호/모듈ID
        /// 파싱(PRD §4.2/§6.2).</summary>
        internal async Task<StatusCommandOutcome> SendStatusCommandAsync(TimeSpan timeout)
        {
            var raw = await SendAndAwaitAsync(ReaderCommandCodes.STATUS_REQUEST, ReaderCommandCodes.STATUS_RESPONSE, null, 0, timeout).ConfigureAwait(false);
            return MapStatusOutcome(raw);
        }

        /// <summary>0x62(무결성 체크) 전송 → 0x72 응답 대기(PRD §4.2/§6.4). 상태체크(0x61/0x71)
        /// 선행은 호출자(결제 Flow/설정 화면) 책임이다 — 이 메서드는 무결성 체크 한 단계만
        /// 담당한다.</summary>
        internal async Task<IntegrityCommandOutcome> SendIntegrityCommandAsync(TimeSpan timeout)
        {
            var raw = await SendAndAwaitAsync(ReaderCommandCodes.INTEGRITY_CHECK_REQUEST, ReaderCommandCodes.INTEGRITY_CHECK_RESPONSE, null, 0, timeout).ConfigureAwait(false);
            return MapIntegrityOutcome(raw);
        }

        /// <summary>0x2B(거래정보/카드 리딩) 전송 → 0x3B 응답 대기(PRD §4.3~§4.6). request는
        /// Protocol/Reader/TransactionInfoRequestBuilder로 조립된 값을 그대로 받는다 — 이 메서드는
        /// 바이트를 직접 만들지 않는다(계층 규칙).</summary>
        internal async Task<CardReadCommandOutcome> SendCardReadCommandAsync(TransactionInfoRequest request, TimeSpan timeout)
        {
            byte[] data = TransactionInfoRequestBuilder.Build(request);
            RawReaderCommandResult raw;
            try
            {
                // Phase 25 P25-5(PRD.md §4.2 #2) — 0x2B 요청 원본 배열. DLL 호출 직후 지운다
                // (키다운로드 요청 3종과 동일 패턴).
                raw = await SendAndAwaitAsync(ReaderCommandCodes.TRANSACTION_INFO_REQUEST, ReaderCommandCodes.CARD_READ_RESPONSE, data, data.Length, timeout).ConfigureAwait(false);
            }
            finally
            {
                SecureClear.Clear(data);
            }

            CardReadCommandOutcome outcome = MapCardReadOutcome(raw);
            if (raw.Kind == RawReaderCommandKind.Response)
            {
                // Phase 25 P25-5(PRD.md §4.2 #1) — 0x3B 응답 원본 배열(카드번호·암호화데이터 등
                // 포함). CardReadResponseParser.Parse가 필요한 필드를 CardReadData(char[])로
                // 전부 복사해낸 뒤이므로 여기서 지운다(키다운로드 [74] 응답과 동일 패턴).
                SecureClear.Clear(raw.Data);
            }

            return outcome;
        }

        // ===================== 공개 명령 3종 — 키다운로드(P24-2, PRD §3.4) =====================

        /// <summary>[63](키 다운로드 시작) 전송 → [73] 응답 대기 → 응답코드 + 키버전/리더기이름/
        /// 리더기버전/모듈ID 파싱. 요청 data는 없다(§3.4).</summary>
        internal async Task<KeyDownloadStartCommandOutcome> SendKeyDownloadStartCommandAsync(TimeSpan timeout)
        {
            // C-A(Phase 24 2차 Opus 리뷰, 치명적 회귀) — R-8-2에서 "죽은 코드 제거" 목적으로
            // KeyDownloadRequestBuilder.BuildStartRequest()(= Array.Empty<byte>())를 쓰도록
            // 바꿨었는데, net48/x86 P/Invoke 마샬러는 Array.Empty<byte>()를 non-null 포인터로
            // 넘긴다 — 실제 DLL(ReaderApi.cpp)의 인자 검증은 "data != nullptr && dataLength <= 0"
            // 이면 READER_ERR_INVALID_ARGUMENT(-1001)를 반환하므로, 이 변경은 [63] 키다운로드
            // 시작 요청을 DLL 레벨에서 항상 실패시켰다(리더기 없는 COM 포트로 실증: null,0 ->
            // READER_OK, Array.Empty<byte>(),0 -> -1001). 아래 Interop/ReaderSerialNative.cs:155
            // 근처 주석이 이미 이 사실을 경고해뒀다. null, 0을 직접 넘기는 원래 형태로 되돌린다.
            var raw = await SendAndAwaitAsync(ReaderCommandCodes.KEY_DOWNLOAD_START_REQUEST, ReaderCommandCodes.KEY_DOWNLOAD_START_RESPONSE, null, 0, timeout).ConfigureAwait(false);
            return MapKeyDownloadStartOutcome(raw);
        }

        /// <summary>[64](키 다운로드 상호 인증) 전송 → [74] 응답 대기(§3.4). hash/rnd/sign은
        /// Protocol/Reader/KeyDownloadRequestBuilder가 요구하는 정확한 길이(64/32/512)의 ASCII
        /// 문자열이어야 한다 — 이 메서드는 바이트를 직접 만들지 않는다(계층 규칙).
        ///
        /// 메모리 클리어(development_plan.md P24-2 신규 요구사항): 조립한 요청 원본 배열은
        /// DLL 호출(SendAndAwaitAsync 경유) 직후 SecureClear로(3회 덮어쓰기) 지운다. 응답(암호화 데이터 512byte
        /// 포함)도 파서가 필요한 필드를 KeyDownloadAuthCommandOutcome으로 복사해낸 뒤 원본
        /// raw byte[]를 SecureClear로 지운다.</summary>
        internal async Task<KeyDownloadAuthCommandOutcome> SendKeyDownloadAuthCommandAsync(string hash, string rnd, string sign, TimeSpan timeout)
        {
            byte[] data = KeyDownloadRequestBuilder.BuildAuthRequest(hash, rnd, sign);
            RawReaderCommandResult raw;
            try
            {
                raw = await SendAndAwaitAsync(ReaderCommandCodes.KEY_DOWNLOAD_AUTH_REQUEST, ReaderCommandCodes.KEY_DOWNLOAD_AUTH_RESPONSE, data, data.Length, timeout).ConfigureAwait(false);
            }
            finally
            {
                // [64] 요청 원본 배열(HASH+RND+SIGN) — DLL 호출 직후 지운다. Reader_SendCommand는
                // 이 호출이 완료되는 시점에 이미 데이터를 native 버퍼로 전달했으므로(P/Invoke는
                // 동기 호출), await가 끝난 지금 지워도 전송에는 영향이 없다.
                SecureClear.Clear(data);
            }

            var outcome = MapKeyDownloadAuthOutcome(raw);
            if (raw.Kind == RawReaderCommandKind.Response)
            {
                // [74] 응답 원본 배열(암호화 데이터 512byte 포함) — 필요한 필드를 outcome으로
                // 복사해낸 뒤 지운다.
                SecureClear.Clear(raw.Data);
            }

            return outcome;
        }

        /// <summary>[65](Using Key 전송) 전송 → [75] 응답 대기(§3.4). encryptedData/mac은
        /// Protocol/Reader/KeyDownloadRequestBuilder가 요구하는 정확한 길이(128/16)의 ASCII
        /// 문자열이어야 한다 — 이 메서드는 바이트를 직접 만들지 않는다(계층 규칙).
        ///
        /// 메모리 클리어(development_plan.md P24-2 신규 요구사항): 조립한 요청 원본 배열은
        /// DLL 호출 직후 SecureClear로 지운다. [75] 응답(응답코드+모듈ID 12byte)은 민감정보를
        /// 담지 않으므로 클리어 대상이 아니다.</summary>
        internal async Task<KeyDownloadUsingKeyCommandOutcome> SendKeyDownloadUsingKeyCommandAsync(string encryptedData, string mac, TimeSpan timeout)
        {
            byte[] data = KeyDownloadRequestBuilder.BuildUsingKeyRequest(encryptedData, mac);
            RawReaderCommandResult raw;
            try
            {
                raw = await SendAndAwaitAsync(ReaderCommandCodes.KEY_DOWNLOAD_USING_KEY_REQUEST, ReaderCommandCodes.KEY_DOWNLOAD_USING_KEY_RESPONSE, data, data.Length, timeout).ConfigureAwait(false);
            }
            finally
            {
                // [65] 요청 원본 배열(암호화 데이터+MAC) — DLL 호출 직후 지운다.
                SecureClear.Clear(data);
            }

            return MapKeyDownloadUsingKeyOutcome(raw);
        }

        // ===================== IKeyDownloadReaderEndpoint 명시적 구현(P24-4) =====================
        //
        // 위 세 메서드는 internal이라 암시적 인터페이스 구현이 안 된다(인터페이스 자체가
        // internal이라도, 구현 멤버는 최소 public이어야 한다) — 그렇다고 메서드 접근자를 public으로
        // 넓히면 이 클래스의 다른 internal 명령 4종과 접근성이 어긋난다. 명시적 인터페이스
        // 구현으로 위 메서드 본문은 그대로 두고 얇은 위임만 추가한다(development_plan.md P24-4
        // 지시 — "P24-2가 만든 메서드 본문은 절대 건드리지 마라").
        Task<KeyDownloadStartCommandOutcome> IKeyDownloadReaderEndpoint.SendKeyDownloadStartCommandAsync(TimeSpan timeout) =>
            SendKeyDownloadStartCommandAsync(timeout);

        Task<KeyDownloadAuthCommandOutcome> IKeyDownloadReaderEndpoint.SendKeyDownloadAuthCommandAsync(string hash, string rnd, string sign, TimeSpan timeout) =>
            SendKeyDownloadAuthCommandAsync(hash, rnd, sign, timeout);

        Task<KeyDownloadUsingKeyCommandOutcome> IKeyDownloadReaderEndpoint.SendKeyDownloadUsingKeyCommandAsync(string encryptedData, string mac, TimeSpan timeout) =>
            SendKeyDownloadUsingKeyCommandAsync(encryptedData, mac, timeout);

        /// <summary>
        /// P10-5 페일오버 무효화 — 이 리더기가 아직 응답 대기 중인 명령(보통 0x2B)을 0x60으로
        /// 무효화한다. 0x60은 WAITING_RESPONSE 상태에서도 항상 허용되며 대기 중이던 명령을 무엇이든
        /// 무효화하도록 DLL이 설계돼 있다(docs/reader_dll/DLL연동가이드.md §1.4). 참조 구현
        /// (ReaderSerialTestUIDlg.cpp의 무효화 전송)과 동일하게 결과를 기다리지 않는다 — 무효화용
        /// 0x60 자체도 SendCommandSafe를 거친다(P10-3, 이 리더기 케이블이 그 사이 끊겼어도 동일한
        /// 자동 복구가 적용됨). 이 호출은 그 리더기의 _pending(원래 0x2B 라운드)을 건드리지 않는다
        /// — 무효화는 새로운 라운드를 시작하지 않는 fire-and-forget 명령이기 때문이다.
        /// </summary>
        internal int SendInvalidationInit() => SendCommandSafe(ReaderCommandCodes.INIT_REQUEST, null, 0);

        // ===================== 응답 → 결과 매핑 (Protocol 위임, P10-1/P10-6) =====================

        private static InitCommandOutcome MapInitOutcome(RawReaderCommandResult raw)
        {
            switch (raw.Kind)
            {
                case RawReaderCommandKind.Response:
                    var parsed = InitResponseParser.Parse(raw.Data);
                    if (parsed.ParseFailed)
                        return InitCommandOutcome.CommunicationError("0x70 응답 데이터 길이 부족(2byte 미만)");
                    return parsed.IsSuccess ? InitCommandOutcome.Success(parsed.ResponseCode) : InitCommandOutcome.BusinessFailure(parsed.ResponseCode);
                case RawReaderCommandKind.Timeout:
                    return InitCommandOutcome.Timeout();
                case RawReaderCommandKind.CommunicationError:
                    return InitCommandOutcome.CommunicationError(raw.Detail);
                default:
                    return InitCommandOutcome.DllCallFailure(raw.DllResult, raw.DllResultName, raw.Detail);
            }
        }

        private static StatusCommandOutcome MapStatusOutcome(RawReaderCommandResult raw)
        {
            switch (raw.Kind)
            {
                case RawReaderCommandKind.Response:
                    return StatusCommandOutcome.FromParsed(StatusResponseParser.Parse(raw.Data));
                case RawReaderCommandKind.Timeout:
                    return StatusCommandOutcome.Timeout();
                case RawReaderCommandKind.CommunicationError:
                    return StatusCommandOutcome.CommunicationError(raw.Detail);
                default:
                    return StatusCommandOutcome.DllCallFailure(raw.DllResult, raw.DllResultName, raw.Detail);
            }
        }

        private static IntegrityCommandOutcome MapIntegrityOutcome(RawReaderCommandResult raw)
        {
            switch (raw.Kind)
            {
                case RawReaderCommandKind.Response:
                    return IntegrityCommandOutcome.FromParsed(IntegrityResponseParser.Parse(raw.Data));
                case RawReaderCommandKind.Timeout:
                    return IntegrityCommandOutcome.Timeout();
                case RawReaderCommandKind.CommunicationError:
                    return IntegrityCommandOutcome.CommunicationError(raw.Detail);
                default:
                    return IntegrityCommandOutcome.DllCallFailure(raw.DllResult, raw.DllResultName, raw.Detail);
            }
        }

        private static CardReadCommandOutcome MapCardReadOutcome(RawReaderCommandResult raw)
        {
            switch (raw.Kind)
            {
                case RawReaderCommandKind.Response:
                    return CardReadCommandOutcome.FromParsed(CardReadResponseParser.Parse(raw.Data));
                case RawReaderCommandKind.Timeout:
                    return CardReadCommandOutcome.Timeout();
                case RawReaderCommandKind.CommunicationError:
                    return CardReadCommandOutcome.CommunicationError(raw.Detail);
                default:
                    return CardReadCommandOutcome.DllCallFailure(raw.DllResult, raw.DllResultName, raw.Detail);
            }
        }

        private static KeyDownloadStartCommandOutcome MapKeyDownloadStartOutcome(RawReaderCommandResult raw)
        {
            switch (raw.Kind)
            {
                case RawReaderCommandKind.Response:
                    return KeyDownloadStartCommandOutcome.FromParsed(KeyDownloadStartResponseParser.Parse(raw.Data));
                case RawReaderCommandKind.Timeout:
                    return KeyDownloadStartCommandOutcome.Timeout();
                case RawReaderCommandKind.CommunicationError:
                    return KeyDownloadStartCommandOutcome.CommunicationError(raw.Detail);
                default:
                    return KeyDownloadStartCommandOutcome.DllCallFailure(raw.DllResult, raw.DllResultName, raw.Detail);
            }
        }

        private static KeyDownloadAuthCommandOutcome MapKeyDownloadAuthOutcome(RawReaderCommandResult raw)
        {
            switch (raw.Kind)
            {
                case RawReaderCommandKind.Response:
                    return KeyDownloadAuthCommandOutcome.FromParsed(KeyDownloadAuthResponseParser.Parse(raw.Data));
                case RawReaderCommandKind.Timeout:
                    return KeyDownloadAuthCommandOutcome.Timeout();
                case RawReaderCommandKind.CommunicationError:
                    return KeyDownloadAuthCommandOutcome.CommunicationError(raw.Detail);
                default:
                    return KeyDownloadAuthCommandOutcome.DllCallFailure(raw.DllResult, raw.DllResultName, raw.Detail);
            }
        }

        private static KeyDownloadUsingKeyCommandOutcome MapKeyDownloadUsingKeyOutcome(RawReaderCommandResult raw)
        {
            switch (raw.Kind)
            {
                case RawReaderCommandKind.Response:
                    return KeyDownloadUsingKeyCommandOutcome.FromParsed(KeyDownloadUsingKeyResponseParser.Parse(raw.Data));
                case RawReaderCommandKind.Timeout:
                    return KeyDownloadUsingKeyCommandOutcome.Timeout();
                case RawReaderCommandKind.CommunicationError:
                    return KeyDownloadUsingKeyCommandOutcome.CommunicationError(raw.Detail);
                default:
                    return KeyDownloadUsingKeyCommandOutcome.DllCallFailure(raw.DllResult, raw.DllResultName, raw.Detail);
            }
        }

        // ===================== 공용 코어: 재연결 래퍼 + 단일 유효 응답 게이트 (P10-3/P10-4) =====================

        /// <summary>
        /// 명령 4종이 전부 공유하는 핵심 경로: SendCommandSafe(재연결 래퍼)로 송신 → 새 라운드를
        /// _pending에 등록 → CALLBACK 또는 로컬 타임아웃 중 먼저 끝나는 쪽을 기다린다. N=1
        /// 리더기용 코드와 이중화용 코드가 다르지 않다 — 이 메서드는 "리더기가 몇 대인지" 전혀
        /// 모른다(P10-4 완료 조건 "N=1이 별도 분기 없이 동일 코드로 동작"). 이중화 조합은
        /// CardReadBroadcaster(P10-5)가 이 메서드가 반환하는 Task 여러 개를 Task.WhenAny로 묶어
        /// 처리한다.
        /// </summary>
        private async Task<RawReaderCommandResult> SendAndAwaitAsync(byte requestCommandCode, byte expectedResponseCode, byte[]? data, int dataLength, TimeSpan timeout)
        {
            long myRound = Interlocked.Increment(ref _roundToken);
            var pendingCmd = new PendingReaderCommand(myRound, expectedResponseCode);

            // 새 라운드를 현재 라운드로 세운다. 직전 라운드가 아직 남아 있었다면(호출자가 이전
            // Task 완료를 기다리지 않고 곧바로 새 명령을 보낸 비정상적 사용) 그 라운드는 이 순간
            // 자동으로 "더 이상 유효하지 않은 라운드"가 된다 — 그 라운드의 뒤늦은 CALLBACK은
            // CAS 실패로 조용히 무시된다(PendingReaderCommand.cs 클래스 주석 참고).
            Interlocked.Exchange(ref _pending, pendingCmd);

            int dllResult = SendCommandSafe(requestCommandCode, data, dataLength);
            if (dllResult != (int)ReaderResult.READER_OK)
            {
                // 송신 자체가 실패했으니 이 라운드는 응답을 받을 수 없다 — 아직 우리 라운드가
                // 살아있다면(다른 스레드가 먼저 손대지 않았다면) 회수한다.
                Interlocked.CompareExchange(ref _pending, null, pendingCmd);
                return RawReaderCommandResult.DllCallFailure(dllResult, ReaderNames.ReaderResultToString(dllResult), "SendCommandSafe 송신 실패");
            }

            var completed = await Task.WhenAny(pendingCmd.Tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed == pendingCmd.Tcs.Task)
                return await pendingCmd.Tcs.Task.ConfigureAwait(false);

            // 로컬 타임아웃 — CAS로 라운드를 회수할 수 있으면(=CALLBACK이 그사이 아직 완료시키지
            // 않았으면) Timeout으로 확정한다. 회수에 실패했다면 CALLBACK이 근소한 차이로 먼저
            // 완료시킨 것이므로 그 결과를 그대로 따른다 — "취소/Timeout과의 경합" 요구사항의
            // 최소 형태(Phase 16에서 사용자 취소까지 포함해 이 게이트를 확장한다).
            if (Interlocked.CompareExchange(ref _pending, null, pendingCmd) != pendingCmd)
                return await pendingCmd.Tcs.Task.ConfigureAwait(false);

            // 라운드 누수 방어(P10-4 요구사항 3의 잔여 위험 축소): 우리가 앱 레벨에서 먼저
            // 포기했을 뿐, DLL 쪽은 여전히 이 명령의 응답을 기다리는 상태(WAITING_RESPONSE)일 수
            // 있다 — 그 상태에서 다음 라운드가 같은 expectedResponseCode로 곧바로 시작되면, 이
            // 늦게 도착하는 실제 하드웨어 응답이 새 라운드의 _pending과 commandCode가 우연히
            // 일치해 잘못 채택될 여지가 이론적으로 남는다(CAS는 "현재 _pending 객체와의 일치"만
            // 보장하고, DLL 프로토콜 자체의 명령 식별자까지는 알지 못하기 때문). 0x60은
            // WAITING_RESPONSE에서도 항상 허용되며 대기 중이던 명령을 무엇이든 무효화하므로
            // (docs/reader_dll/DLL연동가이드.md §1.4), 여기서 결과를 기다리지 않고 방어적으로
            // 보내 둔다 — 새 라운드를 만들지 않는 fire-and-forget이므로 이 _pending 슬롯(이미
            // null로 비워짐)에 영향을 주지 않는다. 이 방어는 "DLL이 무효화 이후 옛 명령의 응답을
            // 더 이상 새 기대 코드로 착각해 전달하지 않는다"는 전제에 의존하며, 이 전제는 SPEC
            // 문서에 100% 명시되어 있지 않다(실기 검증 필요 — Phase 16에서 취소/Timeout 동시성을
            // 다룰 때 재확인).
            if (requestCommandCode != ReaderCommandCodes.INIT_REQUEST)
            {
                SendCommandSafe(ReaderCommandCodes.INIT_REQUEST, null, 0);
            }

            return RawReaderCommandResult.Timeout();
        }

        // ===================== 재연결 래퍼 (P10-3, SendCommandSafe 패턴) =====================
        //
        // vendor/ReaderSerial/CSharpSample/MainForm.cs의 SendCommandSafe를 그대로 따른다(새로
        // 설계하지 않음). 모든 Reader_SendCommand 호출은 이 메서드를 거친다 — 명령 4종의 공개
        // 메서드(SendAndAwaitAsync 경유)와 이 메서드 안의 방어적 0x60 재동기화 전송 2곳뿐이다.
        private int SendCommandSafe(byte commandCode, byte[]? data, int dataLength)
        {
            const string autoPrefix = "[자동복구] ";

            if (_readerId < 0)
            {
                int openResult = TryAutoOpenReader(autoPrefix);
                if (openResult != (int)ReaderResult.READER_OK)
                    return openResult;
            }

            int result = ReaderSerialNative.Reader_SendCommand(_readerId, commandCode, data, dataLength);

            if (result == (int)ReaderResult.READER_ERR_PORT_NOT_OPEN)
            {
                FileLogger.Warn($"{autoPrefix}COM{_portNumber} 전송 중 포트 계열 에러 감지(result={ReaderNames.FormatResult(result)}) -> Close 후 재연결 시도");

                ReaderSerialNative.Reader_ClosePort(_readerId);
                _readerId = -1;

                int reopenResult = TryAutoOpenReader(autoPrefix);
                if (reopenResult != (int)ReaderResult.READER_OK)
                {
                    FileLogger.Warn($"{autoPrefix}COM{_portNumber} 재연결 실패 — readerId를 초기화합니다(다음 명령에서 다시 Open부터 시도)");
                    return reopenResult;
                }

                // 재오픈 성공 시 새로 발급된 readerId로 이미 덮어썼다(TryAutoOpenReader 안에서
                // OpenPort를 통해 처리됨) — 옛 id로 재전송하면 무조건 실패한다(P10-3 금지 사항).
                result = ReaderSerialNative.Reader_SendCommand(_readerId, commandCode, data, dataLength);
                FileLogger.Info(result == (int)ReaderResult.READER_OK
                    ? $"{autoPrefix}COM{_portNumber} 재연결 성공(readerId={_readerId}) -> 재전송 성공"
                    : $"{autoPrefix}COM{_portNumber} 재연결 성공(readerId={_readerId}) -> 재전송도 실패(result={ReaderNames.FormatResult(result)})");
            }
            else if (result == (int)ReaderResult.READER_ERR_SEND_FAIL)
            {
                // DLL이 이미 operationState를 즉시 IDLE로 복귀시켰으므로 이 0x60 재전송은 필수가
                // 아니다 — 리더기 쪽이 여전히 깨진 프레임을 붙잡고 있을 잔여 가능성에 대비한
                // 방어적 조치일 뿐이다. 결과를 기다리지 않고(이 재전송 자체가 SendAndAwaitAsync의
                // _pending 라운드를 새로 만들지 않는다 — fire-and-forget) 원래의 SEND_FAIL은 그대로
                // 호출자에게 반환한다.
                FileLogger.Warn($"[자동복구] COM{_portNumber} 전송 실패(result={ReaderNames.FormatResult(result)}) 감지 -> 프레임 재동기화용 초기화 요청(0x60) 방어적 전송");
                ReaderSerialNative.Reader_SendCommand(_readerId, ReaderCommandCodes.INIT_REQUEST, null, 0);
            }
            // READER_ERR_BUSY 등은 복구 대상이 아니다(P10-3) — 이미 다른 명령이 정상 진행 중이라는
            // 뜻이므로 여기서 Close하면 그 명령을 강제로 죽인다. 그대로 반환한다.

            return result;
        }

        /// <summary>SendCommandSafe가 "readerId 없음" 또는 포트 계열 에러로 Close한 직후에만
        /// 호출된다 — 항상 닫힌/없는 상태에서 불리므로 이미 연결되어 있는지 확인하지 않는다.</summary>
        private int TryAutoOpenReader(string logPrefix)
        {
            if (_portNumber <= 0)
            {
                // 이 인스턴스에서 OpenPort가 한 번도 호출된 적이 없다 — 재시도할 대상 포트 자체를
                // 모르므로 포트를 찾을 수 없다는 오류를 그대로 반환한다.
                return (int)ReaderResult.READER_ERR_PORT_NOT_FOUND;
            }

            var openResult = OpenPort(_portNumber, _baudRate);
            FileLogger.Info(openResult.Success
                ? $"{logPrefix}COM{_portNumber}, {_baudRate}bps -> READER_OK, readerId={openResult.ReaderId}"
                : $"{logPrefix}COM{_portNumber}, {_baudRate}bps -> 실패(result={openResult.DllResultName}({openResult.DllResult}))");

            return openResult.DllResult;
        }

        // ===================== CALLBACK (P9-2 규칙 유지, P10-4 게이트 적용) =====================
        //
        // 리더기별 수신 스레드에서 동기 호출된다. 여기서 UI를 절대 건드리지 않는다 — data를 즉시
        // Marshal.Copy로 복사한 뒤 ReaderEventArgs를 만들어 EventReceived로 그대로 raise한다.
        // Dispatcher 마샬링은 이 클래스의 책임이 아니다(계층 규칙 — Services는 WPF 타입을 모른다).
        //
        // data는 이 함수가 실행되는 동안에만 유효하다 — 함수가 반환된 직후 DLL이 내부 임시 버퍼를
        // 0으로 지우고 정리하므로, 반드시 이 안에서 Marshal.Copy로 즉시 복사해야 한다.
        private void OnReaderCallback(int readerId, int eventType, byte commandCode, IntPtr data, int dataLength, IntPtr userContext)
        {
            byte[] copy = Array.Empty<byte>();
            if (dataLength > 0 && data != IntPtr.Zero)
            {
                copy = new byte[dataLength];
                Marshal.Copy(data, copy, 0, dataLength);
            }

            // I-4(CP1 Opus 리뷰) — 이 `copy` 배열 인스턴스 하나가 아래 CompletePendingIfMatches와
            // EventReceived 양쪽에 그대로 전달된다. 키다운로드 경로(SendKeyDownloadAuthCommandAsync
            // 등)는 pending 쪽에서 받은 RawReaderCommandResult.Data(=이 copy)를 필요한 필드로 옮긴
            // 뒤 SecureClear로 지운다(위 ":203" 근처) — 지금은 EventReceived 구독자가 없어 문제가
            // 없지만, 나중에 누가 EventReceived를 구독하면 CompletePendingIfMatches 쪽이 먼저 지운
            // (이미 SecureClear로 채워진) 데이터를 볼 위험이 있다. 구독자를 추가할 때는 배열을
            // 공유하지 말고 각자 복사본을 갖도록 바꿔야 한다.
            bool handedOff = CompletePendingIfMatches(eventType, commandCode, copy);

            EventReceived?.Invoke(this, new ReaderEventArgs(readerId, eventType, commandCode, copy));

            // Phase 25 최종 전체 리뷰(2026-09-03) — **아무도 인수하지 않은 CALLBACK 데이터의 클리어**.
            // PRD.md §4.2 #1은 이 `copy`가 지워진다고 적고 있지만, 실제로 지우는 주체는
            // SendCardReadCommandAsync(카드리딩)/SendKeyDownload*(키다운로드)처럼 **대기 중인 라운드가
            // 이 배열을 결과로 받아간 경우**뿐이다. 다음 경로에서는 `copy`가 어디에도 전달되지 않고
            // 그대로 버려져 3회 덮어쓰기를 거치지 않은 채 힙에 남았다:
            //   - 로컬 타임아웃으로 라운드를 이미 회수한 뒤 실제 0x3B가 뒤늦게 도착(PRD.md §8.4,
            //     P25-10 실장비 검증에서 실제로 통과시킨 타임아웃 경로가 여기에 해당) → `_pending`이
            //     null이라 CompletePendingIfMatches가 즉시 return
            //   - 같은 라운드에 대한 중복 CALLBACK(PRD.md §8.2) → CAS 실패로 조용히 폐기
            //   - 이 대기와 무관한 UNSOLICITED 이벤트(0x76 카드 감지 등) → default에서 return
            // 어느 쪽이든 카드리딩 응답(0x3B, 카드번호·암호화 데이터 포함)일 수 있으므로 여기서 지운다.
            // `handedOff`가 true면 인수한 쪽(Send*Async)이 자기 시점에 지우므로 여기서는 손대지 않는다
            // — 지우면 그쪽이 파싱하기 전에 0으로 덮여 카드리딩이 통째로 깨진다.
            // EventReceived 호출 **뒤**에 지운다: 위 I-4 주석이 말하는 구독자가 생기더라도 이 클리어
            // 때문에 빈 데이터를 보는 일은 없도록(구독자는 이미 값을 받은 뒤다).
            if (!handedOff)
            {
                SecureClear.Clear(copy);
            }
        }

        /// <summary>
        /// P10-4 단일 유효 응답 게이트의 CALLBACK 쪽 절반. Phase 9의 CompletePendingInitIfMatches와
        /// 이벤트별 매칭 규칙(어떤 eventType이 commandCode를 요구하는지)은 동일하게 유지한다 —
        /// READER_EVENT_RECEIVE_ERROR만 commandCode가 항상 0으로 오므로(docs/reader_dll/
        /// DLL연동가이드.md §2 이벤트 표) commandCode 매칭 없이 그 자체로 통신 오류 확정 처리한다.
        /// 실제 "이 CALLBACK이 이 라운드를 완료시킬 자격이 있는가"는 맨 마지막의
        /// Interlocked.CompareExchange 한 줄로만 결정된다(PendingReaderCommand.cs 클래스 주석).
        /// </summary>
        /// <returns>이 CALLBACK의 <paramref name="data"/> 배열을 대기 중이던 라운드가 결과로
        /// **인수했으면** true(그 라운드의 <c>Send*Async</c>가 자기 시점에 <c>SecureClear</c>로 지운다).
        /// false면 이 배열은 어디에도 전달되지 않고 버려지므로 호출자가 지워야 한다(Phase 25 최종
        /// 전체 리뷰, <see cref="OnReaderCallback"/> 주석 참고).</returns>
        private bool CompletePendingIfMatches(int eventType, byte commandCode, byte[] data)
        {
            var pending = Volatile.Read(ref _pending);
            if (pending == null)
                return false;

            RawReaderCommandResult? result = null;
            switch ((ReaderEventType)eventType)
            {
                case ReaderEventType.READER_EVENT_RESPONSE when commandCode == pending.ExpectedResponseCode:
                    result = RawReaderCommandResult.Response(data);
                    break;

                case ReaderEventType.READER_EVENT_TIMEOUT when commandCode == pending.ExpectedResponseCode:
                    result = RawReaderCommandResult.Timeout();
                    break;

                case ReaderEventType.READER_EVENT_LRC_ERROR when commandCode == pending.ExpectedResponseCode:
                case ReaderEventType.READER_EVENT_RECEIVE_ERROR:
                case ReaderEventType.READER_EVENT_FRAME_STALL when commandCode == pending.ExpectedResponseCode:
                    result = RawReaderCommandResult.CommunicationError(ReaderNames.ReaderEventTypeToString(eventType));
                    break;

                default:
                    // 이 대기와 무관한 이벤트(예: 카드 감지 0x76 UNSOLICITED) — 무시.
                    return false;
            }

            // CAS: "현재 필드 값이 여전히 이 pending 인스턴스인 경우에만" null로 바꾸면서 완료
            // 자격을 획득한다. 실패하면(=이미 다른 경로가 이 라운드를 먼저 끝냈거나, 새 라운드로
            // 넘어갔음) 이 이벤트는 조용히 버린다 — 이것이 중복 CALLBACK 방지(PRD §8.2)와 이전
            // 라운드 뒤늦은 응답 무시(PRD §8.4)를 동시에 만족시키는 지점이다.
            if (Interlocked.CompareExchange(ref _pending, null, pending) != pending)
                return false;

            pending.Tcs.TrySetResult(result!);

            // data를 실제로 들고 나가는 것은 Response 결과뿐이다(Timeout/CommunicationError는
            // Array.Empty<byte>()를 담는다 — RawReaderCommandResult 참고). 그 외에는 이 배열이
            // 버려지므로 호출자가 지우도록 false를 돌려준다.
            return result!.Kind == RawReaderCommandKind.Response;
        }
    }
}
