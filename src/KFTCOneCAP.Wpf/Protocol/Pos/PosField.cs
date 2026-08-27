using System;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// SPEC 표의 필드 한 줄을 그대로 옮긴 값 객체(docs/payment_relay/development_plan.md P17-1). 번호·이름은
/// SPEC 원문 표기를 그대로 쓴다(코드가 아니라 사람이 SPEC과 대조할 때 근거가 되어야 한다).
/// </summary>
public sealed class PosField
{
    public PosField(int number, string name, PosFieldType type, int length, int position, PosFieldOwner owners)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "필드 길이는 1바이트 이상이어야 함");
        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position), position, "POSITION은 음수일 수 없음");

        Number = number;
        Name = name;
        Type = type;
        Length = length;
        Position = position;
        Owners = owners;
    }

    /// <summary>SPEC 표의 필드 번호(#).</summary>
    public int Number { get; }

    /// <summary>SPEC 표의 DATA 항목명(한글 원문).</summary>
    public string Name { get; }

    public PosFieldType Type { get; }

    /// <summary>필드 길이(바이트 수 — 문자 수 아님. 한글 1자 = CP949 2바이트).</summary>
    public int Length { get; }

    /// <summary>본문(BODY) 기준 0-based 시작 오프셋.</summary>
    public int Position { get; }

    /// <summary>이 필드가 끝나는 다음 오프셋(= 다음 필드의 기대 Position).</summary>
    public int EndPosition => Position + Length;

    /// <summary>SPEC 표의 "SET 장소" 열. <see cref="PosFieldOwner.OneCap"/>이 있으면 이 앱이 채워야 한다.</summary>
    public PosFieldOwner Owners { get; }

    /// <summary>
    /// 값을 이 필드의 정렬 규칙으로 채운다(N=우측정렬 '0', 그 외=좌측정렬 space). <paramref name="valueBytes"/>가
    /// <see cref="Length"/>를 초과하면 예외를 던진다 — 한글 2바이트를 글자 수로 착각한 실수를 여기서 드러낸다
    /// (development_plan.md P17-1 완료 조건).
    /// </summary>
    internal byte[] Pad(byte[] valueBytes)
    {
        if (valueBytes.Length > Length)
        {
            throw new PosProtocolException(
                $"필드 #{Number}({Name})에 쓸 값이 필드 길이({Length}바이트)를 초과함: {valueBytes.Length}바이트. " +
                "한글은 CP949로 1자당 2바이트임을 확인할 것.");
        }

        byte[] result = new byte[Length];

        // 빈 값은 타입과 무관하게 전체 space로 채운다 — SPEC p.5 각주 "요청 시 채우지 않는 필드는
        // space로 채워 총 길이로 전문 생성". N 필드를 '0'으로 채우면 "미입력"이 "0"(금액 0원, 코드
        // 000 등)으로 오인된다(2026-08-26, 체크포인트 1 검증 M-1 수정).
        byte fillByte = valueBytes.Length == 0 || Type != PosFieldType.N ? (byte)' ' : (byte)'0';

        for (int i = 0; i < result.Length; i++)
            result[i] = fillByte;

        if (valueBytes.Length == 0)
            return result;

        if (Type == PosFieldType.N)
        {
            // 우측정렬: 값을 뒤쪽에 붙인다.
            Buffer.BlockCopy(valueBytes, 0, result, Length - valueBytes.Length, valueBytes.Length);
        }
        else
        {
            // 좌측정렬: 값을 앞쪽에 붙인다.
            Buffer.BlockCopy(valueBytes, 0, result, 0, valueBytes.Length);
        }

        return result;
    }

    /// <summary>
    /// 필드 구간에서 <b>명백히 패딩인 것만</b> 제거한다.
    ///
    /// <b>N 필드의 앞자리 '0'은 제거하지 않는다</b>(2026-08-26, 체크포인트 1 검증에서 발견한 결함 H-1
    /// 수정). SPEC의 N 필드에는 수량뿐 아니라 <b>코드</b>가 다수 있어(<c>#2</c> 요청기관 코드 "095",
    /// <c>#33</c> 카드사 코드 "01", <c>#15</c> 납부 순번 "001"(SPEC p.9가 '001'부터라고 명시),
    /// <c>#47</c> 수납은행 점별 코드, <c>#18</c> 징수 과목 코드 등) 앞자리 0을 지우면 값 자체가
    /// 달라진다 — "095"가 "95"가 되고 "01"이 "1"이 된다. 원래 구현은 이를 지워 코드성 필드를
    /// 손상시켰고, 저장된 바이트는 정상이라 겉으로 드러나지 않는 종류의 결함이었다.
    ///
    /// 그래서 지금은 <b>어느 타입이든 값을 있는 그대로 돌려준다</b>. 예외는 하나: 전체가 space인 필드는
    /// "채우지 않은 필드"(SPEC p.5 각주)이므로 빈 문자열로 정규화한다. 숫자로 다뤄야 하는 호출자는
    /// <c>int.Parse</c>/<c>long.Parse</c>를 쓰면 되고(앞자리 0을 알아서 처리한다), 문자 필드의 뒤쪽
    /// space는 패딩이 확실하므로 제거한다.
    /// </summary>
    internal static string Trim(PosFieldType type, string paddedValue)
    {
        // 전체 space = 미입력 필드(SPEC p.5) — 타입과 무관하게 빈 문자열로 정규화.
        if (paddedValue.Trim(' ').Length == 0)
            return string.Empty;

        // N: 앞자리 '0'은 값의 일부일 수 있으므로 그대로 둔다(위 주석 참고). 다만 미입력 상태로 남아
        // 있던 자리에 값이 들어간 경우를 대비해 양끝 space만 걷어낸다.
        if (type == PosFieldType.N)
            return paddedValue.Trim(' ');

        return paddedValue.TrimEnd(' ');
    }
}
