using System.Threading.Tasks;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// P10-4 "단일 유효 응답 게이트"의 핵심 단위 — 한 라운드(하나의 명령 송신 + 그 응답을 기다리는
    /// 구간)를 나타내는 불변 토큰이다. ReaderService는 이 객체를 딱 하나만 "현재 유효한 라운드"로
    /// 필드(_pending)에 들고 있으며, 다음 세 경로가 전부 "이 객체 참조 자체"를 CAS(Interlocked.
    /// CompareExchange)로 원자적으로 교체/소비하는 방식으로 겹치지 않게 조정된다:
    ///   1) CALLBACK이 응답을 수신해 완료를 시도할 때
    ///   2) 로컬 Task.Delay 타임아웃이 먼저 끝나 완료를 시도할 때
    ///   3) 다음 라운드가 시작되며 이 라운드를 대체할 때
    /// 세 경로 중 "_pending 필드 값이 정확히 이 PendingReaderCommand 인스턴스일 때만 null로
    /// 바꿔치기에 성공"하는 단 하나만 실제로 Tcs를 완료시킬 수 있다 — 이것이 곧
    ///   - 중복 CALLBACK 방지(PRD §8.2): 두 번째 CALLBACK은 이미 null로 바뀐 필드에 대해
    ///     CAS(comparand=자기 자신)를 시도하므로 실패해 무시된다.
    ///   - 이전 라운드의 뒤늦은 응답 무시(PRD §8.4): 라운드 2가 시작되면 필드 값이 이미 라운드
    ///     2의 새 인스턴스로 바뀌어 있으므로, 라운드 1의 뒤늦은 CALLBACK은 자신이 들고 있던(라운드
    ///     1) 인스턴스로 CAS를 시도하지만 현재 필드 값(라운드 2)과 달라 실패해 무시된다.
    /// 두 성질을 하나의 메커니즘(CAS)으로 얻는다 — development_plan.md P10-4 "따로 만들면 반드시
    /// 어긋나므로 하나의 메커니즘으로 구현" 지시를 이렇게 만족시킨다. RoundToken은 로그 표시용
    /// 참고 값일 뿐 필터링 자체는 객체 참조 동일성(CAS)으로 이뤄진다.
    ///
    /// N개 리더기 동시 전송(요구사항 2, PRD §2.2.3)의 "먼저 응답한 하나만 채택"도 정확히 같은
    /// 성질이다 — 다만 그 CAS는 리더기마다 독립된 ReaderService 인스턴스의 _pending 필드에서
    /// 개별적으로 일어나고("이 리더기 응답은 이 리더기 라운드에서만 유효"), "여러 리더기 중
    /// 어느 쪽이 먼저 끝났는가"의 채택은 그 위의 CardReadBroadcaster(P10-5)가
    /// Task.WhenAny로 판정한다 — ReaderService 자신은 "리더기가 몇 대인지" 알 필요가 없다(N=1이
    /// 자연스러운 축약이 되는 이유).
    /// </summary>
    internal sealed class PendingReaderCommand
    {
        internal long RoundToken { get; }
        internal byte ExpectedResponseCode { get; }
        internal TaskCompletionSource<RawReaderCommandResult> Tcs { get; }

        internal PendingReaderCommand(long roundToken, byte expectedResponseCode)
        {
            RoundToken = roundToken;
            ExpectedResponseCode = expectedResponseCode;
            Tcs = new TaskCompletionSource<RawReaderCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
