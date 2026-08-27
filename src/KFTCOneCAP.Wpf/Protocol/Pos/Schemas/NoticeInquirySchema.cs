using System.Collections.Generic;
using System.Linq;

namespace KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

/// <summary>
/// 국고 상세 고지내역 조회 전문(501008), SPEC p.7~8. 본문 총 길이 706바이트.
/// <b>원캡 담당(SET 장소 "원캡") 필드는 없다</b> — 카드리딩이 없는 순수 중계 전문이다
/// (docs/payment_relay/development_plan.md P17-2/P17-5).
/// </summary>
internal static class NoticeInquirySchema
{
    private const string TransactionTypeCode = "501008";

    /// <summary>거래 구분 코드 고정값(SPEC p.5 선언 표).</summary>
    internal const string FixedTransactionType = TransactionTypeCode;

    internal static PosTelegramSchema Create()
    {
        // 공통부 SET 장소(#1~#13, p.7 표): 디지털예산/인터넷지로/kiosk 조합.
        var headerOwners = new[]
        {
            PosFieldOwner.None, // 0 (미사용, 프레이머 담당)
            PosFieldOwner.Kiosk, // 1 업무 구분
            PosFieldOwner.Kiosk, // 2 요청기관 코드
            PosFieldOwner.DigitalBudget | PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 3 전문 종별 코드
            PosFieldOwner.Kiosk, // 4 거래 구분 코드
            PosFieldOwner.DigitalBudget | PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 5 상태 코드
            PosFieldOwner.DigitalBudget | PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 6 송·수신 FLAG
            PosFieldOwner.DigitalBudget | PosFieldOwner.InternetGiro, // 7 응답 코드
            PosFieldOwner.DigitalBudget | PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 8 전송 일시
            PosFieldOwner.Kiosk, // 9 요청기관 전문 관리 번호
            PosFieldOwner.DigitalBudget, // 10 이용기관/센터 전문 관리 번호
            PosFieldOwner.DigitalBudget | PosFieldOwner.Kiosk, // 11 지로 이용기관 분류코드
            PosFieldOwner.DigitalBudget | PosFieldOwner.Kiosk, // 12 지로 이용기관 지로번호
            PosFieldOwner.DigitalBudget | PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 13 FILLER
        };

        IEnumerable<PosField> header = PosCommonHeader.Create(CommonHeaderNameVariant.NoticeInquiry501008, headerOwners);

        // 업무부(#14~56, p.7~8) — SET 장소는 전부 "디지털예산" 단독이고, #14는 kiosk, #16만 디지털예산+kiosk.
        var D = PosFieldOwner.DigitalBudget;
        var business = new List<PosField>
        {
            new(14, "전자납부번호", PosFieldType.AN, 19, 70, PosFieldOwner.Kiosk),
            new(15, "납부 순번", PosFieldType.N, 3, 89, D),
            new(16, "실 납부자번호(고객관리번호)", PosFieldType.AN, 13, 92, D | PosFieldOwner.Kiosk),
            new(17, "납세 의무자 번호", PosFieldType.AN, 13, 105, D),
            new(18, "납세 의무자 명", PosFieldType.AHN, 40, 118, D),
            new(19, "징수 기관명", PosFieldType.AHN, 40, 158, D),
            new(20, "징수 과목 코드(세목 코드)", PosFieldType.AN, 7, 198, D),
            new(21, "징수 과목명", PosFieldType.AHN, 40, 205, D),
            new(22, "징수관 계좌번호", PosFieldType.AN, 6, 245, D),
            new(23, "소계정", PosFieldType.N, 1, 251, D),
            new(24, "징수 결의 회계 년도", PosFieldType.N, 4, 252, D),
            new(25, "납기내 금액", PosFieldType.N, 15, 256, D),
            new(26, "납기일(납기내)", PosFieldType.N, 8, 271, D),
            new(27, "납기후 금액", PosFieldType.N, 15, 279, D),
            new(28, "납기일(납기후)", PosFieldType.N, 8, 294, D),
            new(29, "고지서 유형", PosFieldType.AN, 1, 302, D),
            new(30, "본세", PosFieldType.N, 15, 303, D),
            new(31, "농어촌 특별세", PosFieldType.N, 15, 318, D),
            new(32, "교육세", PosFieldType.N, 15, 333, D),
            new(33, "특별 소비세", PosFieldType.N, 15, 348, D),
            new(34, "주세", PosFieldType.N, 15, 363, D),
            new(35, "부가가치세", PosFieldType.N, 15, 378, D),
            new(36, "교통세", PosFieldType.N, 15, 393, D),
            new(37, "방위세", PosFieldType.N, 15, 408, D),
            new(38, "예비 정보 FIELD", PosFieldType.N, 15, 423, D),
            new(39, "가산금", PosFieldType.N, 15, 438, D),
            new(40, "회계명", PosFieldType.AHN, 40, 453, D),
            new(41, "소관명", PosFieldType.AHN, 40, 493, D),
            new(42, "납부자 주소", PosFieldType.AHN, 80, 533, D),
            new(43, "수입 신고 번호", PosFieldType.AN, 15, 613, D),
            new(44, "납기 내후 구분", PosFieldType.AN, 1, 628, D),
            new(45, "납부 금액 수정 허용 유무", PosFieldType.AN, 1, 629, D),
            new(46, "연대 납부 대상 유무", PosFieldType.AN, 1, 630, D),
            new(47, "수납은행 점별 코드", PosFieldType.N, 7, 631, D),
            new(48, "납부 일시", PosFieldType.N, 14, 638, D),
            new(49, "고지 일자", PosFieldType.N, 8, 652, D),
            new(50, "대리 납부 허용 유무", PosFieldType.N, 1, 660, D),
            new(51, "기 납부 금액", PosFieldType.N, 15, 661, D),
            new(52, "잔여 납부할 금액", PosFieldType.N, 15, 676, D),
            new(53, "분야(기능) 코드", PosFieldType.AN, 3, 691, D),
            new(54, "신용카드 납부 가능 여부", PosFieldType.AN, 1, 694, D),
            new(55, "납세자 유형", PosFieldType.AN, 2, 695, D),
            new(56, "예비 정보 FIELD", PosFieldType.AN, 9, 697, D),
        };

        return new PosTelegramSchema(TransactionTypeCode, header.Concat(business).ToList(), totalLength: 706);
    }
}
