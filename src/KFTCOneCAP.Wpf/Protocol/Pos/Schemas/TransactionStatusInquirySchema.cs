using System.Collections.Generic;
using System.Linq;

namespace KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

/// <summary>
/// 거래 상태 조회 전문(PRD.md §3.4, 2026-09-03 설계 확정, Phase 26 P26-3) — SPEC 원문(PDF)에도, 요구사항
/// 원문에도 없던 신규 전문 1종. POS↔원캡 구간에서만 쓰이고 VAN/인터넷지로/디지털예산으로는 절대 나가지
/// 않는다(§3.4.4) — 응답을 받지 못한 원거래(<c>501008</c>/<c>800000</c>/<c>902614</c>)의 결과를 원캡이
/// 직접 보관했다가 되돌려주는 봉투 역할만 한다.
///
/// <b>요청</b>: 공통부(#1~#13)만으로 <b>70바이트</b>, 개별부 없음 — 조회 대상은 #9(요청기관 전문 관리
/// 번호)에 원거래 값을 그대로 재사용해 식별하므로 별도 필드가 필요 없다(§3.4.3/§3.4.4).
///
/// <b>응답 고정부</b>: 공통부 70바이트 + 개별부 2필드(원거래 거래구분 코드 N6, 원거래 응답 전문 길이
/// N4) = <b>80바이트</b>. 뒤따르는 원거래 응답 원문(가변, 결과 있음일 때만)은 이 프로젝트 첫 가변
/// 길이 전문이라 <see cref="PosTelegramSchema"/>의 고정 길이 전제에 맞지 않는다 — 그래서 이 스키마는
/// 80바이트 고정부까지만 정의하고, 뒤따르는 원문은 <see cref="PosInquiryResponseTelegram"/>이 스키마
/// 밖에서 별도 바이트로 이어 붙인다(고정 길이 검증의 이점을 잃지 않기 위한 설계, P26-3).
/// </summary>
internal static class TransactionStatusInquirySchema
{
    /// <summary>
    /// SPEC #4 거래구분 코드 — <b>상태 조회 전용 신규 코드, 미채번(발주처 협의 대기)</b>(PRD.md
    /// §3.4.8). 실제 값은 발주처 협의로 정해지며, <b>확정되면 이 상수 값만 교체</b>한다. 기존 3종
    /// (<c>501008</c>/<c>800000</c>/<c>902614</c>)과 겹치지 않는 임시값을 쓴다 — 겹치면
    /// <see cref="PosSchemaRegistry"/> 등록 시 딕셔너리 키 충돌로 즉시 드러난다(같은 값을 두 번
    /// 등록할 수 없으므로 기동 자체가 실패한다).
    /// </summary>
    internal const string FixedTransactionType = "999900";

    /// <summary>SPEC #14(신설) — 응답 개별부 1번, 원거래 거래구분 코드(N6). 결과 없으면 "000000".</summary>
    internal const int OriginalTransactionTypeFieldNumber = 14;

    /// <summary>SPEC #15(신설) — 응답 개별부 2번, 원거래 응답 전문 길이(N4). 결과 없으면 "0000".</summary>
    internal const int OriginalResponseLengthFieldNumber = 15;

    /// <summary>
    /// 이 전문은 VAN/인터넷지로/디지털예산으로 나가지 않으므로(§3.4.4) SET 장소가 kiosk/원캡 둘뿐이다.
    /// SPEC에 없는 신규 전문이라 이 소유자 배정 자체가 이 프로젝트의 설계 판단이다(PRD.md §3.4.8과
    /// 같은 성격의 "확정 전 우리 설계안"). §3.4.5 "공통부는 요청받은 값을 그대로 유지하되, #3을
    /// 0210으로, #7을 결과 코드로 채운다"에 맞춰 <b>#3만 kiosk|원캡 양쪽, #7만 원캡 단독</b>으로 두고,
    /// 나머지는 kiosk가 채운 고정값을 원캡이 재해석 없이 그대로 echo한다(원캡이 새로 쓰지 않으므로
    /// Owner에도 원캡을 포함하지 않는다).
    ///
    /// <b>선언 순서 주의</b>: 이 필드는 아래 <see cref="ResponseFixedPartSchema"/>의 초기화 식이
    /// 간접적으로(<see cref="CreateResponseFixedPart"/> 경유) 참조한다 — C#의 정적 필드 초기화는
    /// 같은 클래스 안에서 <b>선언된 순서대로</b> 실행되므로, 이 필드가 <see
    /// cref="ResponseFixedPartSchema"/>보다 <b>먼저</b> 선언돼 있어야 한다. 순서가 바뀌면
    /// <c>ResponseFixedPartSchema</c> 초기화 시점에 이 필드가 아직 <c>null</c>이라 <see
    /// cref="PosCommonHeader.Create"/> 안에서 <see cref="System.NullReferenceException"/>이 난다
    /// (P26-3 구현 중 실제로 겪은 결함 — 처음엔 이 필드를 파일 맨 아래에 뒀다가 기동 시점
    /// <c>TypeInitializationException</c>으로 드러났다).
    /// </summary>
    private static readonly PosFieldOwner[] HeaderOwners =
    {
        PosFieldOwner.None, // 0 (미사용, 프레이머 담당)
        PosFieldOwner.Kiosk, // 1 업무 구분
        PosFieldOwner.Kiosk, // 2 요청기관 코드
        PosFieldOwner.Kiosk | PosFieldOwner.OneCap, // 3 전문 종별 코드(요청 0200 / 응답 0210)
        PosFieldOwner.Kiosk, // 4 거래 구분 코드(상태 조회 전용 신규 코드, 미채번)
        PosFieldOwner.Kiosk, // 5 상태 코드
        PosFieldOwner.Kiosk, // 6 송·수신 FLAG
        PosFieldOwner.OneCap, // 7 응답 코드(조회 결과 코드)
        PosFieldOwner.Kiosk, // 8 전송 일시(조회 전문 자신의 전송 시각)
        PosFieldOwner.Kiosk, // 9 요청기관 전문 관리 번호(원거래 값 재사용, §3.4.4 — 원캡은 echo만 함)
        PosFieldOwner.Kiosk, // 10 이용기관/센터 전문 관리 번호
        PosFieldOwner.Kiosk, // 11 지로 이용기관 분류코드
        PosFieldOwner.Kiosk, // 12 지로 이용기관 지로번호
        PosFieldOwner.Kiosk, // 13 FILLER
    };

    /// <summary>
    /// 응답 고정부 스키마(80바이트). 정적 필드로 한 번만 생성해 공유한다 — <see cref="PosTelegramSchema"/>
    /// 생성자의 자체 검증(POSITION 연속·총 길이 일치)이 이 필드의 <b>최초 접근 시점</b>에 일어나므로,
    /// <see cref="PosSchemaRegistry.ValidateAtStartup"/>이 이 필드도 건드려 앱 기동 시점에 검증이
    /// 끝나도록 한다(요청 스키마와 달리 이 스키마는 레지스트리에 등록하지 않으므로 별도 조치가
    /// 필요하다 — P26-3 설계: "응답은 가변 길이라 레지스트리의 고정 스키마 모델에 맞지 않는다").
    /// </summary>
    internal static readonly PosTelegramSchema ResponseFixedPartSchema = CreateResponseFixedPart();

    /// <summary>요청 전문 스키마(70바이트, 개별부 없음) — PRD.md §3.4.3. <see cref="PosSchemaRegistry"/>에
    /// 등록되는 유일한 이 전문 스키마다.</summary>
    internal static PosTelegramSchema CreateRequest()
    {
        IEnumerable<PosField> header = PosCommonHeader.Create(CommonHeaderNameVariant.TransactionStatusInquiry, HeaderOwners);
        return new PosTelegramSchema(FixedTransactionType, header.ToList(), totalLength: 70);
    }

    private static PosTelegramSchema CreateResponseFixedPart()
    {
        IEnumerable<PosField> header = PosCommonHeader.Create(CommonHeaderNameVariant.TransactionStatusInquiry, HeaderOwners);

        var individual = new List<PosField>
        {
            new(OriginalTransactionTypeFieldNumber, "원거래 거래구분 코드", PosFieldType.N, 6, 70, PosFieldOwner.OneCap),
            new(OriginalResponseLengthFieldNumber, "원거래 응답 전문 길이", PosFieldType.N, 4, 76, PosFieldOwner.OneCap),
        };

        return new PosTelegramSchema(FixedTransactionType, header.Concat(individual).ToList(), totalLength: 80);
    }
}
