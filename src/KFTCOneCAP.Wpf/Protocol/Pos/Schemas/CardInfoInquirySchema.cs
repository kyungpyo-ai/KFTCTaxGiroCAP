using System.Collections.Generic;
using System.Linq;

namespace KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

/// <summary>
/// 카드 정보 조회 전문(800000), SPEC p.12. 본문 총 길이 500바이트.
/// <b>원캡 담당 필드는 #14 BIN 1개뿐</b>이다(docs/payment_relay/development_plan.md P17-2/P17-6) —
/// 카드리딩 응답의 카드번호 앞 8자리를 채운다.
/// </summary>
internal static class CardInfoInquirySchema
{
    private const string TransactionTypeCode = "800000";

    /// <summary>
    /// 거래 구분 코드 고정값. SPEC p.5의 "#3/#4 고정값 선언 표"에는 501008·902614만 등재되어 있고
    /// 800000은 p.12 흐름도(①0200/800000 → ④0210/800000)에서만 유추 가능하다(별도 선언 문장 없음,
    /// 2026-08-26 재확인) — 흐름도가 유일한 근거이므로 그대로 채택한다.
    /// </summary>
    internal const string FixedTransactionType = TransactionTypeCode;

    internal static PosTelegramSchema Create()
    {
        // 공통부 SET 장소(p.12 표): VAN/인터넷지로/kiosk 조합(디지털예산 없음). #10/#13은 표시 없음.
        //
        // **정정(2026-08-28, Phase 19 P19-5 후속 수정 2)**: #6/#8은 초기 전사 당시 VAN 열로
        // 잘못 읽었다 — SPEC 표(p.12)를 사용자가 하이라이트로 표시해 재확인한 결과 실제로는
        // 인터넷지로+kiosk 열이 체크되어 있다(VAN 열이 아니다). 800000 표는 다른 두 전문에 없는
        // VAN 열이 추가로 있어 인터넷지로 열의 위치가 한 칸 밀려 보이는 착시가 원인이었다
        // (src/KFTCOneCAP.KioskSim/Protocol/TelegramSchemas.cs, docs/payment_relay/development_plan.md
        // "P19-5 후속 수정 2" 참고 — 독립 전사본인 KioskSim 쪽에서 먼저 정정됐고 이 파일은 그 뒤
        // 뒤늦게 맞췄다).
        var headerOwners = new[]
        {
            PosFieldOwner.None, // 0 (미사용)
            PosFieldOwner.Kiosk, // 1 업무 구분
            PosFieldOwner.Kiosk, // 2 요청기관 코드
            PosFieldOwner.Van | PosFieldOwner.Kiosk, // 3 전문 종별 코드
            PosFieldOwner.Kiosk, // 4 거래 구분 코드
            PosFieldOwner.InternetGiro | PosFieldOwner.Van, // 5 상태 코드
            PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 6 송·수신 FLAG(정정: VAN이 아니라 kiosk)
            PosFieldOwner.InternetGiro, // 7 응답 코드
            PosFieldOwner.InternetGiro | PosFieldOwner.Kiosk, // 8 전송 일시(정정: VAN이 아니라 kiosk)
            PosFieldOwner.Kiosk, // 9 은행/센터 전문 관리 번호
            PosFieldOwner.None, // 10 이용기관/센터 전문 관리 번호 — SPEC 표시 없음
            PosFieldOwner.Kiosk, // 11 이용기관 발행기관 분류코드
            PosFieldOwner.Kiosk, // 12 이용기관 지로 번호
            PosFieldOwner.None, // 13 FILLER(응답 코드 구분) — SPEC 표시 없음
        };

        IEnumerable<PosField> header = PosCommonHeader.Create(CommonHeaderNameVariant.Shared800000And902614, headerOwners);

        var I = PosFieldOwner.InternetGiro;
        var business = new List<PosField>
        {
            new(14, "BIN", PosFieldType.AN, 8, 70, PosFieldOwner.OneCap),
            new(15, "납부세액", PosFieldType.N, 15, 78, PosFieldOwner.Kiosk),
            new(16, "납세자 유형", PosFieldType.AN, 2, 93, PosFieldOwner.Kiosk),
            new(17, "카드사 코드", PosFieldType.AN, 2, 95, I),
            new(18, "카드사명", PosFieldType.AHN, 30, 97, I),
            new(19, "체크카드 여부", PosFieldType.AN, 1, 127, I),
            new(20, "납부가능 시간 여부", PosFieldType.AN, 1, 128, I),
            new(21, "포인트 납부 가능 여부", PosFieldType.AN, 1, 129, I),
            // 할부개월 LIST: 할부개월수 2Byte 단위 코드를 연속 구성(예: "01020304", space padding),
            // 총 60Byte로 최대 30개(SPEC p.12 각주). 값 조합 로직은 이 전문을 채우는 쪽(VAN/인터넷지로
            // 응답 파싱)의 책임이며, 이 스키마는 60바이트 통짜 필드로만 다룬다(P17-1 원본 보존 원칙).
            new(22, "카드 할부개월 LIST", PosFieldType.AN, 60, 130, I),
            new(23, "포인트 할부개월 LIST", PosFieldType.AN, 60, 190, I),
            new(24, "납부대행 수수료 금액", PosFieldType.N, 12, 250, I),
            new(25, "합계금액", PosFieldType.N, 12, 262, I),
            new(26, "API 세부 응답코드", PosFieldType.AN, 6, 274, I),
            new(27, "예비 정보 FIELD", PosFieldType.AN, 220, 280, I), // SPEC 표(p.12)는 인터넷지로 열 체크(응답 전용) — 직접 재확인
        };

        return new PosTelegramSchema(TransactionTypeCode, header.Concat(business).ToList(), totalLength: 500);
    }
}
