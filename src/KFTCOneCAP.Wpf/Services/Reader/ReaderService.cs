using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Interop;
using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// 리더기 1개 포트에 대한 흐름/상태 계층(Phase 9 파일럿, development_plan.md P9-2/P9-3).
    /// 포트별 인스턴스로 설계한다 — Phase 10에서 리더기 2대 이중화로 확장할 때 이 클래스를 그대로
    /// 재사용한다(ROADMAP P10-3 "리더기1 전용 싱글턴으로 만들지 않는다"). 이번 Phase는 명령
    /// 1종(0x60/0x70)만 다루므로 재연결 래퍼(SendCommandSafe 패턴)·단일 유효 응답 게이트는 아직
    /// 만들지 않는다 — 그 확장은 Phase 10 몫이다.
    ///
    /// 계층 규칙: 이 클래스는 Protocol(InitResponseParser)과 Interop(ReaderSerialNative)만 알고,
    /// WPF 타입(Dispatcher 등)을 전혀 참조하지 않는다. EventReceived는 네이티브 콜백 스레드에서
    /// 그대로 raise되므로, UI로 마샬링하는 책임은 이 이벤트를 구독하는 ViewModel에 있다.
    /// </summary>
    internal sealed class ReaderService
    {
        // ===================== 콜백 델리게이트 수명 (P9-2) =====================
        //
        // Reader_OpenPort에 넘긴 델리게이트 인스턴스를 인스턴스 필드로 계속 참조해야 한다. 지역
        // 변수로만 넘기면 그 델리게이트를 참조하는 관리 코드 쪽 루트가 없어 GC가 회수할 수 있고,
        // 이후 네이티브 수신 스레드가 이미 해제된 함수 포인터를 호출하면
        // CallbackOnCollectedDelegate로 프로세스가 죽는다 — DLL 문제가 아니라 이쪽 책임이다
        // (development_plan.md P9-2, 포트를 앱 수명 내내 열어두는 이 앱에서는 반드시 발생할 함정).
        // 이번 Phase(9)는 핀패드를 쓰지 않으므로(PRD §2.2.1/§10) pinpadCallback에는 항상 null을
        // 그대로 넘긴다(development_plan.md P9-3 지시) — 붙잡아 둘 델리게이트 자체가 없다.
        private readonly ReaderCallback _nativeReaderCallback;

        private int _readerId = -1;

        // Phase 9 파일럿 범위: 한 번에 하나의 초기화 요청만 대기한다(ViewModel의 IsBusy가 이미
        // 동시 요청을 막아준다). 여러 명령을 동시에 다루는 상관관계 매칭은 Phase 10의 단일 유효
        // 응답 게이트(P10-4) 몫이다 — 여기서 미리 일반화하지 않는다.
        private TaskCompletionSource<InitCommandOutcome>? _pendingInit;

        internal ReaderService()
        {
            _nativeReaderCallback = OnReaderCallback;
        }

        internal bool IsConnected => _readerId >= 0;

        internal int ReaderId => _readerId;

        /// <summary>네이티브 콜백 스레드에서 그대로 raise된다(P9-2 규칙) — 구독자가 UI 스레드로
        /// 마샬링해야 한다. data는 이미 이 이벤트가 만들어지기 전에 Marshal.Copy로 복사됐다.</summary>
        internal event EventHandler<ReaderEventArgs>? EventReceived;

        /// <summary>
        /// baudRate는 PRD §2.2.1/§10에 따라 이 계층 호출자가 115200으로 고정해 넘긴다(이 서비스
        /// 자체는 값을 강제하지 않는다 — Reader_OpenPort의 baudRate 파라미터를 그대로 전달만 함).
        /// pinpadCallback은 이번 Phase(핀패드 미사용, PRD §2.2.1/§10)에서 항상 null이다
        /// (development_plan.md P9-3 지시, DLL연동가이드.md §1.1 "readerCallback/pinpadCallback 중
        /// 하나만 필요하면 nullptr").
        /// </summary>
        internal ReaderOpenResult OpenPort(int portNumber, int baudRate)
        {
            int dllResult = ReaderSerialNative.Reader_OpenPort(
                portNumber, baudRate, _nativeReaderCallback, null, IntPtr.Zero, out int newReaderId);

            bool success = dllResult == (int)ReaderResult.READER_OK;
            if (success)
            {
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
        /// (docs/reader_dll/DLL연동가이드.md §1.3, development_plan.md 사전 확인된 원칙).</summary>
        internal int IsPortOpen() => _readerId < 0 ? 0 : ReaderSerialNative.Reader_IsPortOpen(_readerId);

        /// <summary>
        /// 0x60(초기화) 전송 → 0x70 응답 대기 → 첫 2byte(ASCII) 업무 응답코드 판정(P9-3).
        /// Reader_SendCommand 직접 호출 지점은 이 메서드로 한정한다 — 재연결 래퍼(SendCommandSafe
        /// 패턴)는 Phase 10에서 이 메서드를 감싸는 형태로 추가될 예정이며, 이번 Phase에서는 미리
        /// 만들지 않는다(development_plan.md "필요 이상으로 만들지 않는다").
        /// </summary>
        internal async Task<InitCommandOutcome> SendInitCommandAsync(TimeSpan timeout)
        {
            if (_readerId < 0)
                return InitCommandOutcome.DllCallFailure((int)ReaderResult.READER_ERR_PORT_NOT_OPEN,
                    nameof(ReaderResult.READER_ERR_PORT_NOT_OPEN), "포트가 열려 있지 않음");

            var tcs = new TaskCompletionSource<InitCommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingInit = tcs;

            int dllResult = ReaderSerialNative.Reader_SendCommand(_readerId, ReaderCommandCodes.INIT_REQUEST, null, 0);
            if (dllResult != (int)ReaderResult.READER_OK)
            {
                _pendingInit = null;
                return InitCommandOutcome.DllCallFailure(dllResult, ReaderNames.ReaderResultToString(dllResult), "Reader_SendCommand 송신 실패");
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                // 자체 타임아웃 — DLL의 READER_EVENT_TIMEOUT이 나중에 도착해도 아래 콜백에서
                // TrySetResult가 조용히 무시되므로 이중 완료 문제는 없다.
                _pendingInit = null;
                return InitCommandOutcome.Timeout();
            }

            return await tcs.Task.ConfigureAwait(false);
        }

        // ===================== CALLBACK (P9-2) =====================
        //
        // 리더기별 수신 스레드에서 동기 호출된다. 여기서 UI를 절대 건드리지 않는다 — data를 즉시
        // Marshal.Copy로 복사한 뒤 ReaderEventArgs를 만들어 EventReceived로 그대로 raise한다.
        // Dispatcher 마샬링은 이 클래스의 책임이 아니다(계층 규칙 — Services는 WPF 타입을 모른다).
        //
        // data는 이 함수가 실행되는 동안에만 유효하다 — 함수가 반환된 직후 DLL이 내부 임시 버퍼를
        // 0으로 지우고 정리하므로, 반드시 이 안에서 Marshal.Copy로 즉시 복사해야 한다
        // (docs/reader_dll/DLL연동가이드.md §2 "데이터 수명 규칙").
        private void OnReaderCallback(int readerId, int eventType, byte commandCode, IntPtr data, int dataLength, IntPtr userContext)
        {
            byte[] copy = Array.Empty<byte>();
            if (dataLength > 0 && data != IntPtr.Zero)
            {
                copy = new byte[dataLength];
                Marshal.Copy(data, copy, 0, dataLength);
            }

            CompletePendingInitIfMatches(eventType, commandCode, copy);

            EventReceived?.Invoke(this, new ReaderEventArgs(readerId, eventType, commandCode, copy));
        }

        private void CompletePendingInitIfMatches(int eventType, byte commandCode, byte[] data)
        {
            var pending = _pendingInit;
            if (pending == null)
                return;

            switch ((ReaderEventType)eventType)
            {
                case ReaderEventType.READER_EVENT_RESPONSE when commandCode == ReaderCommandCodes.INIT_RESPONSE:
                    {
                        _pendingInit = null;
                        var parsed = InitResponseParser.Parse(data);
                        if (parsed.ParseFailed)
                        {
                            pending.TrySetResult(InitCommandOutcome.CommunicationError("0x70 응답 데이터 길이 부족(2byte 미만)"));
                        }
                        else if (parsed.IsSuccess)
                        {
                            pending.TrySetResult(InitCommandOutcome.Success(parsed.ResponseCode));
                        }
                        else
                        {
                            pending.TrySetResult(InitCommandOutcome.BusinessFailure(parsed.ResponseCode));
                        }

                        break;
                    }

                case ReaderEventType.READER_EVENT_TIMEOUT when commandCode == ReaderCommandCodes.INIT_RESPONSE:
                    _pendingInit = null;
                    pending.TrySetResult(InitCommandOutcome.Timeout());
                    break;

                case ReaderEventType.READER_EVENT_LRC_ERROR when commandCode == ReaderCommandCodes.INIT_RESPONSE:
                case ReaderEventType.READER_EVENT_RECEIVE_ERROR:
                case ReaderEventType.READER_EVENT_FRAME_STALL when commandCode == ReaderCommandCodes.INIT_RESPONSE:
                    _pendingInit = null;
                    pending.TrySetResult(InitCommandOutcome.CommunicationError(ReaderNames.ReaderEventTypeToString(eventType)));
                    break;

                default:
                    // 이 대기와 무관한 이벤트(예: 카드 감지 0x76 UNSOLICITED) — 무시.
                    break;
            }
        }
    }
}
