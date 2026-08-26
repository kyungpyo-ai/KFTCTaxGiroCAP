using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Reader;
using KFTCOneCAP.Wpf.Services.Reader;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-10) 검증용 가짜 <see cref="IReaderEndpoint"/>.
/// **최종 산출물이 아니다** — 실장비 없이 <c>PaymentOrchestrator</c>(P15-6~P15-9)의 모든 분기
/// (정상/FALLBACK/12 재시도/기타 응답코드/DLL 실패/Timeout, 이중화 선착순 채택)를 재현하기 위한
/// 스크립트 가능한 스텁이다. P15-2가 <c>IReaderEndpoint</c>를 둔 목적이 정확히 이것이다 — 하드웨어
/// 없이는 검증 불가능했을 경로들을 이 클래스로 실행해 본다.
///
/// <see cref="EnqueueCardReadOutcome"/>로 라운드별 결과를 순서대로 등록한다(지연 시간도 함께 지정할
/// 수 있어 이중화 선착순 채택을 재현할 수 있다). 등록된 결과를 **전부 소비하면**(큐가 비면) 마지막
/// 으로 실제 소비했던 결과를 계속 반환한다(라운드 수를 정확히 맞추지 않아도 테스트가 죽지 않는다) —
/// 아직 안 쓴 마지막 항목을 미리 들여다보기만 하고 남겨 두는 방식은 쓰지 않는다: 그렇게 하면 같은
/// 인스턴스를 두 번째 거래(연속 거래 시나리오, P15-10 시나리오14)에 재사용할 때 "새로 추가한 결과가
/// 아니라 이전에 안 쓰인 결과가 먼저 나가는" 순서 꼬임이 생긴다(큐에 2개가 쌓인 뒤에야 첫 번째가
/// 소비되므로).
/// </summary>
internal sealed class FakeReaderEndpoint : IReaderEndpoint
{
    private readonly object _lock = new();
    private readonly Queue<(CardReadCommandOutcome Outcome, TimeSpan Delay)> _scriptedCardReadOutcomes = new();
    private CardReadCommandOutcome? _lastConsumedOutcome;

    internal FakeReaderEndpoint(string comPortDisplay)
    {
        ComPortDisplay = comPortDisplay;
    }

    public string ComPortDisplay { get; }

    /// <summary><see cref="SendInvalidationInit"/> 호출 횟수 — 무효화/취소/타임아웃/실패 종료 시
    /// "0x60이 나갔는가"를 검증 하네스가 확인하는 용도.</summary>
    internal int InvalidationCount { get; private set; }

    /// <summary><see cref="SendCardReadCommandAsync"/> 호출 횟수 — 라운드 수를 검증하는 용도.</summary>
    internal int CardReadCallCount { get; private set; }

    /// <summary><see cref="RunIntegrityCheckAsync"/> 호출 횟수 — "금일 성공 이력이 있으면 무결성
    /// 체크를 건너뛴다"(PRD §4.2)를 Orchestrator가 실제로 지키는지 검증하는 용도. 0이어야 하는
    /// 케이스(이력 있음)와 1이어야 하는 케이스(이력 없음)를 구분해서 확인한다.</summary>
    internal int IntegrityCheckCallCount { get; private set; }

    /// <summary>가장 최근 <see cref="SendCardReadCommandAsync"/> 호출의 요청 — FALLBACK/12 재요청
    /// 시 거래구분이 실제로 바뀌었는지(<see cref="TransactionInfoRequest.TransactionTypeCode"/>)
    /// 검증하는 용도.</summary>
    internal TransactionInfoRequest? LastCardReadRequest { get; private set; }

    /// <summary>가장 최근 <see cref="SendCardReadCommandAsync"/> 호출의 <c>timeout</c> 인자
    /// (2026-08-25, Opus 검증 리뷰 L-1 수정) — 예전엔 이 값을 아예 기록하지 않아, PRD §4.9의
    /// 120초 카드 입력 대기 상한이 실제로 <see cref="IReaderEndpoint.SendCardReadCommandAsync"/>까지
    /// 전달되는지는 15개 시나리오 전부와 무관하게 검증되지 않았다(상수를 잘못 바꿔도 테스트가 전부
    /// 통과했을 것이다). 검증 하네스가 이 값을 실제 <c>PaymentOrchestrator.CardReadTimeout</c>과
    /// 대조할 수 있게 한다.</summary>
    internal TimeSpan LastCardReadTimeout { get; private set; }

    /// <summary>라운드마다 전달된 <c>timeout</c> 인자를 순서대로 모두 기록한다(2026-08-25, Phase 16
    /// 체크포인트 리뷰 L-2). Phase 16부터 이 값은 거래 데드라인의 남은 시간에서 파생되므로
    /// (<c>PaymentOrchestrator.ClampCommandTimeout(deadline.Remaining)</c>), 라운드별 값을 비교하면
    /// <b>데드라인이 실제로 몇 초 연장됐는지</b>를 외부에서 정밀하게 계산해 낼 수 있다 —
    /// <see cref="LastCardReadTimeout"/> 하나만으로는 "연장이 일어났다"까지만 알 수 있고 "정확히
    /// +30초인지"는 확인할 수 없었다.</summary>
    internal List<TimeSpan> CardReadTimeouts { get; } = new();

    /// <summary>기본값은 성공(0x71/0x72 둘 다 "00") — 대부분의 시나리오가 무결성은 그냥 통과시키고
    /// 카드 리딩 분기에 집중하고 싶어할 것이므로.</summary>
    internal IntegrityCheckSequenceOutcome IntegrityOutcome { get; set; } =
        IntegrityCheckSequenceOutcome.FromIntegrityOutcome(
            StatusCommandOutcome.Success("00", "AUTH-FAKE", "MODULE-FAKE"),
            IntegrityCommandOutcome.Success("00"));

    /// <summary>다음 <see cref="SendCardReadCommandAsync"/> 호출(들)이 순서대로 반환할 결과를
    /// 등록한다. <paramref name="delay"/>를 주면 그 시간만큼 지연 후 반환한다(2대 중 어느 쪽이
    /// 먼저 응답하는지를 결정하는 유일한 방법 — <see cref="CardReadBroadcaster"/>는 실제 순서를
    /// 전혀 특별 취급하지 않고 <c>Task.WhenAny</c>로만 판정하기 때문).</summary>
    internal void EnqueueCardReadOutcome(CardReadCommandOutcome outcome, TimeSpan? delay = null)
    {
        lock (_lock)
        {
            _scriptedCardReadOutcomes.Enqueue((outcome, delay ?? TimeSpan.Zero));
        }
    }

    public Task<IntegrityCheckSequenceOutcome> RunIntegrityCheckAsync(TimeSpan statusTimeout, TimeSpan integrityTimeout)
    {
        lock (_lock)
        {
            IntegrityCheckCallCount++;
        }

        return Task.FromResult(IntegrityOutcome);
    }

    public async Task<CardReadCommandOutcome> SendCardReadCommandAsync(TransactionInfoRequest request, TimeSpan timeout)
    {
        LastCardReadRequest = request;
        LastCardReadTimeout = timeout;

        CardReadCommandOutcome outcome;
        TimeSpan delay;
        lock (_lock)
        {
            CardReadCallCount++;
            CardReadTimeouts.Add(timeout);
            if (_scriptedCardReadOutcomes.Count > 0)
            {
                (outcome, delay) = _scriptedCardReadOutcomes.Dequeue();
                _lastConsumedOutcome = outcome;
            }
            else if (_lastConsumedOutcome != null)
            {
                // 큐가 완전히 비었을 때만 마지막으로 실제 소비했던 결과를 반복한다(지연은 반복하지
                // 않는다 — 안 그러면 라운드가 늘어날수록 테스트가 계속 느려진다).
                outcome = _lastConsumedOutcome;
                delay = TimeSpan.Zero;
            }
            else
            {
                outcome = CardReadCommandOutcome.CommunicationError("FakeReaderEndpoint: 스크립트된 카드 리딩 결과가 없음");
                delay = TimeSpan.Zero;
            }
        }

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay).ConfigureAwait(false);

        return outcome;
    }

    public int SendInvalidationInit()
    {
        lock (_lock)
        {
            InvalidationCount++;
        }

        return 0;
    }
}
