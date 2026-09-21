using System.Collections.Generic;
using System.Linq;

namespace KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

/// <summary>
/// 국고 신용카드 승인 요청 전문(보안단말기 거래용, 902614), SPEC p.13~14. 본문 총 길이 1500바이트
/// (SPEC에 총 길이 행이 없어 #54의 POSITION+길이로부터 계산한 값 — <see cref="PosTelegramSchema"/>의
/// 자체 검증이 이 값을 확인해 준다).
///
/// <b>원캡 담당 필드는 8개</b>: #43/#44/#45/#46/#48/#50/#51/#53 (docs/payment_relay/development_plan.md
/// P17-2, 2026-09-01 `pos-onecap-spec-expert` 재확인 — 최초 정리 때 7개로 잘못 캐시됐던 것을 바로잡음).
/// #51(암호화된 비밀번호 정보)은 20260915판까지는 SPEC 표 원문이 kiosk로 표시돼 있어(p.14) p.17
/// 설명절(#44~46과 같은 "보안리더기에서 생성" 그룹)과 상충했고, 사용자가 "설계서 오류, 원캡이 맞다"고
/// 확정(2026-08-26)해 원캡으로 등록했다. 20260921판부터는 표 원문 자체가 원캡으로 바뀌어(p.15) 표와
/// 설명절이 일치하게 됐다 — 결과값(원캡)은 그대로다.
/// #38(카드소유주 주민(사업자)등록번호)은 20260915판까지 SET 장소 표시가 전혀 없어 kiosk로 등록했으나,
/// 20260921판부터 표가 인터넷지로로 명시돼(p.14) 그쪽으로 바꿨다(2026-09-21 사용자 확정 — 표 원문
/// 우선, p.16 설명절과의 잔존 모순은 발주처 질의 대상).
/// 실제 값 채움은 Phase 17에서는 space 스텁이고, Phase 18(PIN 입력)에서 실채움된다.
/// </summary>
internal static class CardApprovalSchema
{
    private const string TransactionTypeCode = "902614";

    /// <summary>거래 구분 코드 고정값(SPEC p.5 선언 표).</summary>
    internal const string FixedTransactionType = TransactionTypeCode;

    internal static PosTelegramSchema Create()
    {
        // 공통부 SET 장소(p.13 표): 인터넷지로/kiosk 조합(VAN 열 없음).
        var headerOwners = new[]
        {
            PosFieldOwner.None, // 0 (미사용)
            PosFieldOwner.Kiosk, // 1 업무 구분
            PosFieldOwner.Kiosk, // 2 요청기관 코드
            PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 3 전문 종별 코드
            PosFieldOwner.Kiosk, // 4 거래 구분 코드
            PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 5 상태 코드
            PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 6 송·수신 FLAG
            PosFieldOwner.InternetGiro, // 7 응답 코드
            PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 8 전송 일시
            PosFieldOwner.Kiosk, // 9 은행/센터 전문 관리 번호
            PosFieldOwner.InternetGiro, // 10 이용기관/센터 전문 관리 번호
            PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 11 이용기관 발행기관 분류코드
            PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 12 이용기관 지로 번호
            PosFieldOwner.InternetGiro, // 13 FILLER(응답 코드 구분)
        };

        IEnumerable<PosField> header = PosCommonHeader.Create(CommonHeaderNameVariant.Shared800000And902614, headerOwners);

        var K = PosFieldOwner.Kiosk;
        var I = PosFieldOwner.InternetGiro;
        var C = PosFieldOwner.OneCap;
        var business = new List<PosField>
        {
            new(14, "주민(사업자,법인)등록번호", PosFieldType.AN, 13, 70, K),
            new(15, "전자납부번호", PosFieldType.AN, 19, 83, K),
            new(16, "납부 순번", PosFieldType.AN, 3, 102, K),
            new(17, "예비 정보 FIELD", PosFieldType.AN, 8, 105, PosFieldOwner.None),
            new(18, "징수 과목 코드(세목 코드)", PosFieldType.N, 7, 113, K),
            new(19, "징수관 계좌번호", PosFieldType.AN, 6, 120, K),
            new(20, "징수 기관명", PosFieldType.AHN, 20, 126, K),
            new(21, "징수 과목명", PosFieldType.AHN, 20, 146, K),
            new(22, "소계정", PosFieldType.N, 1, 166, K),
            new(23, "징수 결의 회계 년도", PosFieldType.N, 4, 167, K),
            new(24, "납부세액(본세)", PosFieldType.N, 15, 171, K),
            new(25, "납부세액(교육세)", PosFieldType.N, 15, 186, K),
            new(26, "납부세액(농어촌특별세)", PosFieldType.N, 15, 201, K),
            new(27, "납부 세액", PosFieldType.N, 15, 216, K),
            new(28, "수수료", PosFieldType.N, 15, 231, K),
            new(29, "총 납부 금액", PosFieldType.N, 15, 246, K),
            new(30, "납기 내후 구분", PosFieldType.AN, 1, 261, K),
            new(31, "납기 일자", PosFieldType.N, 8, 262, K),
            new(32, "납부 일자", PosFieldType.N, 8, 270, K),
            new(33, "카드사 코드", PosFieldType.N, 2, 278, K),
            new(34, "할부 개월 수", PosFieldType.N, 2, 280, K),
            new(35, "연락 전화 번호", PosFieldType.ANS, 14, 282, K),
            new(36, "납부자 주민(사업자)등록번호", PosFieldType.AN, 13, 296, K),
            new(37, "납부자 성명", PosFieldType.AHNS, 10, 309, K),
            // #38: 2026-09-21 SPEC 개정(20260921판)으로 SET 장소가 kiosk → 인터넷지로로 바뀌었다(p.14
            // 표 원문). p.16 설명절은 여전히 "고지내역정보와 동일하게 SET"(kiosk 채움을 암시) 목록에
            // #38을 포함하고 있어 표와 설명절이 새로 상충하지만, 표 원문을 우선하기로 사용자가 확정
            // (2026-09-21) — 요청 화면(kiosk 편집 목록)에서 빠지고 응답 필드로만 노출된다. 이전 판
            // (20260915 이하)에서는 표에 SET 장소 표시가 전혀 없어 kiosk로 등록했었다(옛 코드 이력).
            new(38, "카드소유주 주민(사업자)등록번호", PosFieldType.AN, 13, 319, I),
            new(39, "납부 이용 시스템", PosFieldType.AN, 1, 332, K),
            new(40, "기 납부 이용 시스템", PosFieldType.AN, 1, 333, I),
            new(41, "납부 형태 구분", PosFieldType.AN, 1, 334, K),
            new(42, "키오스크 고유번호", PosFieldType.AN, 20, 335, K),
            new(43, "보안단말기 인증번호", PosFieldType.ANS, 32, 355, C),
            new(44, "FALLBACK CODE", PosFieldType.N, 2, 387, C),
            new(45, "복호화 정보", PosFieldType.AN, 18, 389, C),
            new(46, "암호화된 카드정보", PosFieldType.AN, 196, 407, C),
            new(47, "예비 정보 FIELD", PosFieldType.AN, 6, 603, PosFieldOwner.None),
            new(48, "거래 입력 유형(IC 전문 전용)", PosFieldType.AN, 1, 609, C),
            new(49, "납부카드 구분", PosFieldType.AN, 1, 610, K),
            new(50, "신용카드 승인 인증방식", PosFieldType.AN, 1, 611, C),
            // #51: 2026-09-21 SPEC 개정(20260921판)으로 표 원문 자체가 원캡으로 바뀌어(p.15) 클래스
            // 주석 상단의 옛 override 서사("표는 kiosk, 설명절과 상충해 원캡으로 확정")가 더는 정확하지
            // 않다 — 이제 표·설명절이 둘 다 원캡을 가리킨다. 동작(C)은 그대로 유지.
            new(51, "암호화된 비밀번호 정보", PosFieldType.ANS, 100, 612, C),
            new(52, "선불카드 잔액", PosFieldType.AN, 12, 712, I),
            new(53, "EMV DATA", PosFieldType.ANS, 604, 724, I | C),
            new(54, "예비 정보 FIELD", PosFieldType.AN, 172, 1328, PosFieldOwner.None),
        };

        return new PosTelegramSchema(TransactionTypeCode, header.Concat(business).ToList(), totalLength: 1500);
    }
}
