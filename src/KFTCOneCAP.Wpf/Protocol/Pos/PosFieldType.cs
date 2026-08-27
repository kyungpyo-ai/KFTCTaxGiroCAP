namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// SPEC(docs/payment_relay/spec/국세 베리어프리 키오스크용 전산설계서(POS-원캡)_20260826.pdf)의 "표현" 열
/// 표기(N/A/AN/AHN/ANS/AHNS)를 그대로 옮긴 것. 패딩 방향·채움 문자를 결정하는 유일한 지점이다
/// (docs/payment_relay/development_plan.md P17-1 확정 사항 10) — SPEC이 패딩 규칙을 명시하지 않아 국내
/// 고정길이 금융전문의 표준 관례(N=우측정렬 '0' 채움, 그 외=좌측정렬 space 채움)를 채택했다. 발주처 확인
/// 결과가 다르면 <see cref="PosField.Pad"/>/<see cref="PosField.Trim"/> 두 메서드만 바뀐다.
/// </summary>
public enum PosFieldType
{
    /// <summary>숫자(Numeric). 우측정렬 + 앞을 '0'으로 채움.</summary>
    N,

    /// <summary>영문(Alpha). 좌측정렬 + 뒤를 space로 채움.</summary>
    A,

    /// <summary>영숫자(Alpha-Numeric). 좌측정렬 + 뒤를 space로 채움.</summary>
    AN,

    /// <summary>영숫자+한글(Alpha-Hangul-Numeric). 좌측정렬 + 뒤를 space로 채움.</summary>
    AHN,

    /// <summary>영숫자+특수문자(Alpha-Numeric-Special). 좌측정렬 + 뒤를 space로 채움.</summary>
    ANS,

    /// <summary>영숫자+한글+특수문자(Alpha-Hangul-Numeric-Special). 좌측정렬 + 뒤를 space로 채움.</summary>
    AHNS,
}
