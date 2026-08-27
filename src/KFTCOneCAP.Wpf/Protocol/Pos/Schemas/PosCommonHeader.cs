using System.Collections.Generic;

namespace KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

/// <summary>
/// SPEC 공통부분(#0~#13, POSITION 0~70) 필드 생성기(docs/payment_relay/development_plan.md P17-2).
///
/// 오프셋·표현·길이는 3전문 모두 완전히 동일하지만, <b>필드 이름 표기와 SET 장소는 전문마다 다르다</b>
/// (501008 쪽 이름이 800000·902614와 다르고, 2026-08-26 재확인 결과 세 전문의 SET 장소 조합도 서로
/// 다르다). 그래서 이 클래스는 공용 상수를 두지 않고, <b>전문마다 필드 이름과 소유자를 받아 그 전문 전용
/// 목록을 만드는 팩토리</b>로 둔다 — SPEC 자신이 표를 전문마다 반복해서 그리는 것과 같은 방식이다.
///
/// 고정값(구현 시 <c>Protocol/Pos/Schemas/PosCommonValues</c> 등에서 실제로 채울 것):
/// <list type="bullet">
/// <item>#1 업무 구분 = "IGN" 고정</item>
/// <item>#2 요청기관 코드 = "095" 고정</item>
/// <item>#3 전문 종별 코드 = "0200"(요청) / "0210"(응답)</item>
/// <item>#6 송·수신 FLAG = "C"(통합센터) / "G"(요청기관 OneCAP)</item>
/// <item>#8 전송 일시 = YYMMDDhhmmss</item>
/// </list>
/// </summary>
internal static class PosCommonHeader
{
    /// <summary>
    /// 공통부 14필드를 만든다. <paramref name="nameVariant"/>가 이름 표기(전문별로 다름)를,
    /// <paramref name="owners"/>가 필드 0~13번 순서의 SET 장소를 정한다.
    /// </summary>
    internal static IEnumerable<PosField> Create(CommonHeaderNameVariant nameVariant, PosFieldOwner[] owners)
    {
        string[] names = nameVariant == CommonHeaderNameVariant.NoticeInquiry501008
            ? Names501008
            : NamesShared800000And902614;

        var lengths = new (PosFieldType Type, int Length)[]
        {
            (PosFieldType.N, 4),   // 0 전문 길이 — 프레이머가 별도 처리하므로 스키마 본문에는 포함하지 않음(주석 참고)
            (PosFieldType.A, 3),   // 1 업무 구분
            (PosFieldType.N, 3),   // 2 요청기관 코드
            (PosFieldType.N, 4),   // 3 전문 종별 코드
            (PosFieldType.N, 6),   // 4 거래 구분 코드
            (PosFieldType.N, 3),   // 5 상태 코드
            (PosFieldType.AN, 1),  // 6 송·수신 FLAG
            (PosFieldType.AN, 3),  // 7 응답 코드
            (PosFieldType.N, 12),  // 8 전송 일시
            (PosFieldType.AN, 12), // 9 (요청기관/은행)/센터 전문 관리 번호
            (PosFieldType.AN, 12), // 10 이용기관/센터 전문 관리 번호
            (PosFieldType.N, 2),   // 11 (지로)이용기관 (발행기관)분류코드
            (PosFieldType.N, 7),   // 12 (지로)이용기관 (지로)번호
            (PosFieldType.N, 2),   // 13 FILLER
        };

        int position = 0;
        for (int i = 0; i < lengths.Length; i++)
        {
            // #0(전문 길이)은 본문(BODY) 밖 헤더이므로 스키마에는 넣지 않는다 — PosMessageFramer가
            // 이미 그 4바이트를 처리한다(development_plan.md P17 확정 사항 1). 여기서는 #1부터 POSITION 0.
            if (i == 0)
                continue;

            yield return new PosField(i, names[i], lengths[i].Type, lengths[i].Length, position, owners[i]);
            position += lengths[i].Length;
        }
    }

    private static readonly string[] Names501008 =
    {
        "전문 길이", "업무 구분", "요청기관 코드", "전문 종별 코드", "거래 구분 코드", "상태 코드",
        "송·수신 FLAG", "응답 코드", "전송 일시", "요청기관 전문 관리 번호", "이용기관/센터 전문 관리 번호",
        "지로 이용기관 분류코드", "지로 이용기관 지로번호", "FILLER",
    };

    private static readonly string[] NamesShared800000And902614 =
    {
        "전문 길이", "업무 구분", "요청기관 코드", "전문 종별 코드", "거래 구분 코드", "상태 코드",
        "송·수신 FLAG", "응답 코드", "전송 일시", "은행/센터 전문 관리 번호", "이용기관/센터 전문 관리 번호",
        "이용기관 발행기관 분류코드", "이용기관 지로 번호", "FILLER(응답 코드 구분)",
    };
}

internal enum CommonHeaderNameVariant
{
    NoticeInquiry501008,
    Shared800000And902614,
}
