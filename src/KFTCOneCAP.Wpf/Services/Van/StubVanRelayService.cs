using System;
using System.Globalization;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Services.Diagnostics;
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

    /// <summary>SPEC <c>#9</c> 전문관리번호 — P22-6 로깅용(<see cref="VanService"/>의 같은 이름
    /// 상수와 동일한 목적). App.xaml.cs가 Phase 20 이후에도 아직 이 스텁을 실제 배선으로 쓰고
    /// 있으므로(클래스 주석 "Phase 21 P21-1 정정" 참고), VAN 경계 로그는 여기서도 남겨야 실제로
    /// 흐르는 결제 1건에서 관측된다.</summary>
    private const int ManagementNumberFieldNumber = 9;

    /// <summary>P30 체크포인트 지적(L-2, 2026-09-23) — <c>BuildFakeSuccess</c>가 매 호출마다
    /// <c>new Random()</c>을 새로 만들었는데, .NET Framework의 <see cref="Random()"/>은 시스템 시간
    /// 기반 시드라 짧은 시간(약 15ms) 안에 생성된 인스턴스들이 같은 난수 시퀀스를 낸다. 이 서비스는
    /// <c>App.xaml.cs</c>에서 단 한 번 생성돼 앱 수명 동안 공유되는 사실상 싱글턴이므로, 인스턴스
    /// 전체가 하나의 <see cref="Random"/>을 공유하도록 static 필드로 옮겼다. <see cref="BuildFakeSuccess"/>
    /// 호출은 항상 <see cref="RelayAsync"/>의 <c>lock (_lock)</c> 블록 안에서만 일어나므로(그 락이
    /// 스레드 안전성을 보장) 이 필드 자체에는 별도 락이 필요 없다.</summary>
    private static readonly Random SharedRandom = new();

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
        string txId = populatedRequest.Read(ManagementNumberFieldNumber);
        // 사용자 요청(2026-09-01) — 전문 원문(위치기반 마스킹, TelegramLogRedactor 클래스 요약 참고).
        string redactedRequestBody = TelegramLogRedactor.Redact(populatedRequest.TransactionTypeCode, populatedRequest.Telegram.ToBody());
        FileLogger.Info(LogCategory.Van, $"[StubVanRelayService] 거래구분={populatedRequest.TransactionTypeCode} FNAISCRDVAN 호출(스텁) 원문={redactedRequestBody}", code: null, txId);

        await Task.Delay(FixedDelay).ConfigureAwait(false);

        VanRelayOutcome outcome;
        lock (_lock)
        {
            if (_nextOutcome is { } injected)
            {
                _nextOutcome = null; // 소비 후 기본값 복귀.
                outcome = injected;
            }
            else
            {
                outcome = BuildFakeSuccess(populatedRequest);
            }
        }

        string redactedResponseBody = outcome.ResponseBody is { } responseBody
            ? TelegramLogRedactor.Redact(populatedRequest.TransactionTypeCode, responseBody)
            : "(응답 본문 없음)";
        FileLogger.Info(LogCategory.Van, $"[StubVanRelayService] 거래구분={populatedRequest.TransactionTypeCode} 반환(Kind={outcome.Kind}, 스텁) 원문={redactedResponseBody}", code: null, txId);
        return outcome;
    }

    /// <summary>
    /// <b>이건 진짜 VAN이 그렇게 응답한다는 뜻이 아니다</b> — 이 스텁은 요청 전문을 clone해 공통부
    /// (#3/#6/#7/#8)만 성공값으로 덮어쓸 뿐, 실제 VAN이 채워 보내는 업무 필드 값은 알지 못한다.
    /// 카드리딩·PIN 필드(#45/#46/#51/#53)를 요청에서 그대로 물고 있던 문제(2026-08-27 Phase 18
    /// 실장비 검증 중 실제 재현 — 사용자가 실물 키패드로 입력한 PIN이 이 스텁의 "성공" 응답에 그대로
    /// 실려 테스트 클라이언트 화면/로그에 노출됨)는 이제 <see cref="PosResponseTelegram.Relay"/>가
    /// 이 클래스를 감싸는 시점에 902614 응답이면 항상 지운다(Phase 26 P26-1) — 이 스텁이 개별적으로
    /// 지울 필요가 없어졌다. Phase 20이 실제 호출로 교체되면 실제 VAN 응답에 어떤 값이 오는지 별도로
    /// 확인해야 한다(development_plan.md Phase 18 "남은 미확정" #4).
    ///
    /// <b>Phase 30 P30-1 확장(PRD §13.8, 2026-09-23) — 개발용 장치.</b> 원래는 공통부 4개만 덮고 나머지는
    /// 요청 바이트를 그대로 돌려줬다 — kiosk가 채우지 않는 업무 필드(디지털예산/인터넷지로 담당)는
    /// 요청에서부터 공백이므로 응답도 공백이었고, 그러면 전문 간 필드 연쇄(Phase 30 본 목적)를 검증할
    /// "받아올 값" 자체가 없었다(2026-09-21 P29-8 실기 로그로 실제 확인). 그래서 지금은
    /// <b>kiosk 소유도 아니고 원캡 소유도 아닌 필드</b>를 <see cref="PosRandomValueGenerator"/>로 채워
    /// 돌려준다 — kiosk 소유 필드는 요청에 이미 값이 있어 손댈 필요가 없고, 원캡 소유 필드(예:
    /// <c>800000 #14</c> BIN, <c>902614</c>의 원캡 담당 8개)는 원캡이 실제 카드리딩으로 채운 진짜 값이라
    /// 스텁이 덮으면 그 검증(Phase 29에서 실기로 이미 끝난 것)이 무의미해진다. <b>이건 스텁의 한계를
    /// 메우는 개발용 장치일 뿐이다</b> — 실 VAN이 붙으면 `App.xaml.cs` 한 줄 교체(PRD §10)로 이 클래스
    /// 전체가 함께 사라진다.
    /// </summary>
    /// <summary>P30 체크포인트 지적(M-2, 2026-09-23) — <see cref="TelegramFieldChainMap"/>의 합산 연쇄
    /// (<c>902614 #27</c> = <c>501008 #30</c>+<c>#31</c>+<c>#32</c>, <c>902614 #29</c> = <c>#27</c>+<c>#28</c>,
    /// <c>800000 #15</c> = <c>501008 #30</c>+<c>#31</c>+<c>#32</c>)의 소스/대상이 전부 N15(또는 N12→N15
    /// 확장)인데, <see cref="PosRandomValueGenerator.GenerateValue"/>는 N15 필드를 15자리 전부 무작위로
    /// 채워(거의 10^15에 가까운 값도 나옴) 세 값만 더해도 약 83% 확률로 N15 한도(10^15)를 넘어
    /// <see cref="PosField.Pad"/>가 예외를 던진다. 실제 세금은 이렇게 크지 않으므로(보통 몇백만~몇천만원
    /// 수준), 이 4개 금액 필드만 자릿수를 줄인 현실적인 범위의 난수로 채운다 — 나머지 필드는 기존대로
    /// <see cref="PosRandomValueGenerator.GenerateValue"/>를 쓴다.
    /// <list type="bullet">
    /// <item><c>501008 #30</c>(본세)/<c>#31</c>(농어촌특별세)/<c>#32</c>(교육세), 전부 N15 — 최대 8자리
    /// (0~99,999,999)로 제한. 세 값을 합해도 최대 약 3억(9자리 이내)이라 N15에 여유 있게 들어온다.</item>
    /// <item><c>800000 #24</c>(납부대행 수수료 금액, N12) — 최대 6자리(0~999,999)로 제한. 연쇄 대상인
    /// <c>902614 #28</c>(N15로 확장)이 최대 99만이라, <c>#29 = #27+#28</c>(#27은 위 세 필드 합, 최대
    /// 약 3억)도 최대 약 3억1백만으로 N15 한도에 넉넉히 들어온다.</item>
    /// </list>
    /// <see cref="PosField.Pad"/>가 N형 필드를 우측정렬 0-패딩하므로 여기서는 짧은 숫자 문자열만
    /// 만들면 되고 직접 0-패딩할 필요가 없다.</summary>
    private const int RealisticAmountMaxExclusive8Digits = 100_000_000; // 0~99,999,999

    private const int RealisticFeeMaxExclusive6Digits = 1_000_000; // 0~999,999

    private static VanRelayOutcome BuildFakeSuccess(PosRequestTelegram request)
    {
        PosTelegram cloned = request.Telegram.Clone();

        // kiosk 소유도 아니고 원캡 소유도 아닌 필드(디지털예산/인터넷지로 담당)만 임의값으로 채운다.
        // 공통부 #3/#6/#7/#8도 이 조건에 걸릴 수 있으므로(예: #7 응답 코드는 kiosk 소유가 아님) 반드시
        // 아래 성공값 덮어쓰기보다 먼저 실행한다 — 순서를 바꾸면 "000" 등이 임의값에 덮여 사라진다.
        //
        // P30 체크포인트 지적(L-1, 2026-09-23) — PRD §13.8은 "SET 장소로 kiosk가 표시되지 않은 응답
        // 필드"만 채우라고 했는데, 예전 조건은 SET 장소 자체가 없는 필드(PosFieldOwner.None, 예비 정보
        // FIELD/FILLER 등)까지 포함해버렸다. 특히 800000 #11/#12는 2026-09-22 SPEC 개정으로 "공백 확정"
        // 자리라 난수가 실리면 안 된다 — Owners != None 조건을 추가해 소유자가 아예 없는 필드는 건드리지
        // 않는다(요청 clone 그대로 공백으로 남는다).
        foreach (PosField field in cloned.Schema.Fields)
        {
            if (field.Owners == PosFieldOwner.None)
                continue;

            if (field.Owners.HasFlag(PosFieldOwner.Kiosk) || field.Owners.HasFlag(PosFieldOwner.OneCap))
                continue;

            string value = IsRealisticAmountField(cloned.Schema.TransactionTypeCode, field.Number)
                ? SharedRandom.Next(0, RealisticAmountMaxExclusive8Digits).ToString(CultureInfo.InvariantCulture)
                : IsRealisticFeeField(cloned.Schema.TransactionTypeCode, field.Number)
                    ? SharedRandom.Next(0, RealisticFeeMaxExclusive6Digits).ToString(CultureInfo.InvariantCulture)
                    : PosRandomValueGenerator.GenerateValue(field.Type, field.Length, SharedRandom);

            cloned.Write(field.Number, value);
        }

        cloned.Write(3, "0210");
        cloned.Write(6, "C");
        cloned.Write(7, "000");
        cloned.Write(8, DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture));

        return VanRelayOutcome.Success(cloned.ToBody());
    }

    /// <summary>M-2 주석 참고 — <c>501008 #30/#31/#32</c>(본세/농어촌특별세/교육세).</summary>
    private static bool IsRealisticAmountField(string transactionTypeCode, int fieldNumber) =>
        transactionTypeCode == NoticeInquirySchema.FixedTransactionType
        && (fieldNumber == 30 || fieldNumber == 31 || fieldNumber == 32);

    /// <summary>M-2 주석 참고 — <c>800000 #24</c>(납부대행 수수료 금액).</summary>
    private static bool IsRealisticFeeField(string transactionTypeCode, int fieldNumber) =>
        transactionTypeCode == CardInfoInquirySchema.FixedTransactionType && fieldNumber == 24;
}
