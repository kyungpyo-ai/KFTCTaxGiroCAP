using System;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 15(docs/payment_relay/development_plan.md P15-2) — 결제 Flow(<c>Services/Payment/
    /// PaymentOrchestrator</c>)가 리더기 한 대에 대해 필요로 하는 것 전부를 담은 최소 계약.
    ///
    /// <see cref="ReaderService"/>(sealed 구체 클래스)를 Flow가 직접 잡으면 하드웨어 없이는 정상/
    /// FALLBACK/`12` 등 카드 리딩 분기를 한 번도 실행해 볼 수 없다 — 이 인터페이스가 있어야
    /// 검증 하네스(P15-10의 <c>FakeReaderEndpoint</c>)가 같은 자리에 꽂힐 수 있다. 운영 구현은
    /// <see cref="ReaderEndpoint"/>(<see cref="ReaderService"/> + <see cref="IntegrityCheckService"/>를
    /// 감싸는 얇은 어댑터)다.
    ///
    /// <see cref="CardReadBroadcaster"/>가 이 인터페이스를 받도록 참여자 타입이 바뀌었지만, 페일오버
    /// 알고리즘(동시 전송 → 최초 응답 채택 → 나머지 무효화) 자체는 그대로다 — 타입만 좁혔다.
    /// </summary>
    internal interface IReaderEndpoint
    {
        /// <summary>결제 전 무결성 이력 조회(PRD §4.2)의 DB 조회 키로 쓰이는 표시 문자열
        /// (예: "COM 05", P12-2가 확정한 형식). 저장 시 쓰인 것과 같은 형식이어야 하며, 정규화는
        /// 이 프로퍼티 구현체의 책임이다 — 호출자(Orchestrator)는 추가로 가공하지 않는다.</summary>
        string ComPortDisplay { get; }

        /// <summary>0x61→0x71 상태체크 → 0x62→0x72 무결성 체크 2단계 시퀀스 + DB 저장까지 한 번에
        /// 수행한다(<see cref="IntegrityCheckService.RunAsync"/> 위임).</summary>
        Task<IntegrityCheckSequenceOutcome> RunIntegrityCheckAsync(TimeSpan statusTimeout, TimeSpan integrityTimeout);

        /// <summary>0x2B(거래정보) 전송 → 0x3B 응답 대기(PRD §4.3~§4.6). 재연결 래퍼(P10-3)·단일
        /// 유효 응답 게이트(P10-4)가 이미 내부에 있다(<see cref="ReaderService.SendCardReadCommandAsync"/>
        /// 위임).</summary>
        Task<CardReadCommandOutcome> SendCardReadCommandAsync(TransactionInfoRequest request, TimeSpan timeout);

        /// <summary>0x60(초기화) 전송으로 대기 중인 명령을 무효화한다(PRD §2.2.3/§4.8/§4.9). 결과를
        /// 기다리지 않는 Fire-and-forget 전송이며 반환값은 로그용이다.
        ///
        /// <b>2026-09-17 사용자 지적으로 사용 중단(신규 호출부는 <see cref="SendInvalidationInitAsync"/>를
        /// 쓴다)</b> — 이 메서드는 DLL에 0x60을 "보내는 syscall"이 끝났다는 것만 보장하지, 리더기가
        /// 실제로 0x70 응답을 돌려줘 자유로워졌다는 것은 보장하지 않는다. 실기 테스트(902614 두 건을
        /// 거의 동시에 보내는 시나리오)에서 이 간극 때문에 앞 거래의 정리(0x60)가 아직 안 끝난 채로
        /// 다음 거래가 카드 리딩(0x2B)을 시작해 <c>READER_ERR_BUSY</c>가 재현됐다.</summary>
        int SendInvalidationInit();

        /// <summary>
        /// 2026-09-17 사용자 지적으로 신설 — <see cref="SendInvalidationInit"/>와 달리 0x70 응답(또는
        /// <paramref name="timeout"/> 만료)까지 실제로 기다린다(<see cref="ReaderService.SendInitCommandAsync"/>
        /// 위임, 카드리딩 4종 명령과 동일한 <c>SendAndAwaitAsync</c> 게이트를 거친다). 거래 종료 시
        /// 리더기를 정리하는 모든 호출부가 이 메서드로 옮겨가, 그 정리가 실제로 끝난 뒤에야 POS
        /// 응답을 보내고 다음 거래를 큐에서 꺼내도록 한다 — 그래야 "직전 거래의 0x60 정리"와
        /// "다음 거래의 0x2B 시작"이 리더기 포트에서 서로 겹치지 않는다.
        /// </summary>
        Task<InitCommandOutcome> SendInvalidationInitAsync(TimeSpan timeout);
    }
}
