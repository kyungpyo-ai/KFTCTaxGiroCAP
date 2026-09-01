using System;
using System.Globalization;
using System.Linq;
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
///
/// <b>Phase 21 P21-1 정정(2026-08-31)</b>: 예전에는 이 클래스가 <c>LastRequest</c> 프로퍼티로 가장
/// 최근 요청 전문(카드번호·PIN 등 원캡이 채운 필드 전부 포함)을 무기한 들고 있었다 — 대입만 있고
/// 비우는 코드가 없어, 다음 거래가 올 때까지(또는 그날 마지막 거래라면 앱 종료 때까지) 이전 거래
/// 데이터가 메모리에 남는 PRD §8.4 위반이었다. 이 클래스는 <b>지금도 `App.xaml.cs`가 실제로 배선해
/// 쓰는 구현체</b>(Phase 20 결정 1 — 서버 준비 전까지 스텁 유지)이므로, 검증 하네스 전용 필드가
/// 프로덕션 경로에 그대로 노출돼 있던 셈이다. 그 필드를 완전히 제거했다 — 검증 하네스가 필요로
/// 하는 "가장 최근 요청 캡처" 기능은 테스트 전용 래퍼
/// (<see cref="Diagnostics.CapturingVanRelayService"/>)로 분리해, 프로덕션 경로는 아예 전문을
/// 붙들지 않는다.
/// </summary>
internal sealed class StubVanRelayService : IVanRelayService
{
    private static readonly TimeSpan FixedDelay = TimeSpan.FromSeconds(1);

    private readonly object _lock = new();
    private VanRelayOutcome? _nextOutcome;

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
            if (_nextOutcome is { } injected)
            {
                _nextOutcome = null; // 소비 후 기본값 복귀.
                return injected;
            }

            return BuildFakeSuccess(populatedRequest);
        }
    }

    /// <summary><c>#51</c> 암호화된 비밀번호 정보 — 902614에만 있는 필드. clone 기반 흉내 응답이
    /// 요청의 값을 그대로 물고 있으면 안 되는 이유는 <see cref="PosResponseTelegram"/>의 같은 이름
    /// 상수 주석 참고(2026-08-27 Phase 18 실장비 검증 중 실제 재현됨 — 사용자가 실물 키패드로 입력한
    /// PIN이 이 스텁의 "성공" 응답에 그대로 실려 테스트 클라이언트 화면/로그에 노출됐다). **이건 진짜
    /// VAN이 그렇게 응답한다는 뜻이 아니다** — Phase 20이 실제 호출로 교체되면 실제 VAN 응답에 `#51`이
    /// 오는지 별도로 확인해야 한다(development_plan.md Phase 18 "남은 미확정" #4). 지금은 이 스텁이
    /// 실장비 검증 도구로 계속 쓰이는 동안 같은 유출이 반복되지 않도록 막는다.</summary>
    private const int EncryptedPinFieldNumber = 51;

    private static VanRelayOutcome BuildFakeSuccess(PosRequestTelegram request)
    {
        PosTelegram cloned = request.Telegram.Clone();
        cloned.Write(3, "0210");
        cloned.Write(6, "C");
        cloned.Write(7, "000");
        cloned.Write(8, DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture));

        if (cloned.Schema.Fields.Any(f => f.Number == EncryptedPinFieldNumber))
        {
            cloned.Write(EncryptedPinFieldNumber, string.Empty);
        }

        return VanRelayOutcome.Success(cloned.ToBody());
    }
}
