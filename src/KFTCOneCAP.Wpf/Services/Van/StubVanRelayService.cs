using System;
using System.Globalization;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Services.Payment;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// <see cref="IVanRelayService"/>의 개발용 스텁(docs/payment_relay/development_plan.md P17-5). 실제
/// <c>FNAISCRDVAN</c> 호출은 Phase 20이 이 자리에 진짜 구현을 꽂는다.
///
/// 기본 동작은 **"VAN이 성공(#7=000)으로 응답했다"를 흉내**낸다 — 요청 전문을 clone해 `#3`을 `0210`,
/// `#6`을 `C`(통합센터가 응답을 송신, SPEC p.6), `#7`을 `000`, `#8`을 응답 시각으로 덮어쓴 바이트를
/// <see cref="VanRelayOutcome.Success"/>로 돌려준다. **이건 진짜 VAN 응답이 아니다** — 실제로는 VAN이
/// 디지털예산/인터넷지로/카드사가 채운 값을 담아 응답하지만, 그 값들을 지금 이 스텁은 알 수 없다.
/// Phase 17 검증(카드리딩→원캡 필드 채움→relay 배선이 끊기지 않았는지) 목적으로만 쓴다 — Phase 20이
/// 실제 VAN 호출로 이 클래스를 통째로 대체한다.
///
/// <see cref="SetNextOutcome"/>로 검증 하네스가 성공/통신실패를 스크립트할 수 있다(P15-5의
/// <c>StubVanService</c> 패턴 계승 — 소비 후 기본값 복귀).
/// </summary>
internal sealed class StubVanRelayService : IVanRelayService
{
    private static readonly TimeSpan FixedDelay = TimeSpan.FromSeconds(1);

    private readonly object _lock = new();
    private VanRelayOutcome? _nextOutcome;

    /// <summary>가장 최근 <see cref="RelayAsync"/> 호출의 인자 — 검증 하네스가 "원캡 필드가 실제로
    /// VAN 요청까지 채워져 도달했는가"를 확인하는 용도(P15-5의 <c>LastRequest</c> 패턴 계승).</summary>
    internal PosRequestTelegram? LastRequest { get; private set; }

    /// <summary>다음 호출이 반환할 결과를 미리 지정한다(검증 하네스 전용). 소비 후 기본값(성공 흉내)
    /// 으로 되돌아간다.</summary>
    internal void SetNextOutcome(VanRelayOutcome outcome)
    {
        lock (_lock)
        {
            _nextOutcome = outcome;
        }
    }

    public async Task<VanRelayOutcome> RelayAsync(PosRequestTelegram populatedRequest)
    {
        await Task.Delay(FixedDelay).ConfigureAwait(false);

        lock (_lock)
        {
            LastRequest = populatedRequest;

            if (_nextOutcome is { } injected)
            {
                _nextOutcome = null; // 소비 후 기본값 복귀.
                return injected;
            }

            return BuildFakeSuccess(populatedRequest);
        }
    }

    private static VanRelayOutcome BuildFakeSuccess(PosRequestTelegram request)
    {
        PosTelegram cloned = request.Telegram.Clone();
        cloned.Write(3, "0210");
        cloned.Write(6, "C");
        cloned.Write(7, "000");
        cloned.Write(8, DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture));
        return VanRelayOutcome.Success(cloned.ToBody());
    }
}
