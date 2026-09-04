using System;
using System.Globalization;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Security;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// 거래 상태 조회 응답(PRD.md §3.4.5, Phase 26 P26-3) — 고정부 80바이트(<see
/// cref="TransactionStatusInquirySchema.ResponseFixedPartSchema"/>) + 원거래 응답 원문(가변, 결과
/// 없으면 0바이트) 꼬리. <see cref="PosResponseTelegram"/>은 <see cref="PosTelegram"/> 생성자의 고정
/// 길이 전제(<c>body.Length == schema.TotalLength</c>) 때문에 이 가변 길이 응답을 표현할 수 없어 이
/// 클래스를 새로 둔다(이 프로젝트 첫 가변 길이 전문).
///
/// <b>원본 보존/relay 원칙</b>: 꼬리는 <see cref="Services.Storage.LastTransactionResponseStore"/>가
/// 돌려준 원거래 응답 바이트를 그대로(파싱·재해석 없이) 붙인다 — 그 바이트는 이미 <c>PosResponseTelegram.
/// ClearCardReadingFields</c>(P26-1)를 거쳐 카드리딩·PIN 필드가 지워진 상태다. 고정부의 공통부(#1~#13)도
/// 마찬가지로 요청 바이트를 그대로 옮겨 붙이고 필드별로 재해석하지 않는다(§3.4.5 "공통부는 요청받은
/// 값을 그대로 유지").
/// </summary>
public sealed class PosInquiryResponseTelegram : IPosOutboundResponse
{
    private const string ResponseTransactionTypeSuffix = "0210";
    private const int TransactionTypeSuffixFieldNumber = 3;
    private const int ResultCodeFieldNumber = 7;

    /// <summary>결과가 없을 때 개별부에 채우는 값(N 타입이므로 "0"을 쓰면 <see cref="PosField.Pad"/>가
    /// 좌측을 '0'으로 채워 "000000"/"0000"이 된다 — PRD.md §3.4.5 "숫자형이므로 0으로 채운다". 빈
    /// 문자열을 쓰면 <see cref="PosField.Pad"/>가 "미입력"으로 판단해 space로 채워버리므로(다른 필드와
    /// 구분이 안 됨) 반드시 "0"을 명시적으로 써야 한다.</summary>
    private const string NoResultFillValue = "0";

    private PosInquiryResponseTelegram(PosTelegram fixedPart, byte[]? tail)
    {
        FixedPart = fixedPart;
        Tail = tail;
    }

    /// <summary>고정부 80바이트(공통부 70 + 개별부 2필드).</summary>
    internal PosTelegram FixedPart { get; }

    /// <summary>원거래 응답 원문(가변). 결과가 없으면 <c>null</c>(0바이트, 아예 붙이지 않음).</summary>
    internal byte[]? Tail { get; }

    /// <summary>
    /// 조회 요청과 조회 결과로부터 응답을 조립한다. 원거래를 찾지 못했을 때는
    /// <paramref name="originalTransactionTypeCode"/>/<paramref name="originalResponseBody"/> 둘 다
    /// <c>null</c>로 준다("000000"+"0000"+꼬리 없음으로 채워진다, PRD.md §3.4.5).
    /// </summary>
    /// <param name="inquiryRequest">이 상태 조회 요청 전문(70바이트) — 공통부를 그대로 echo하는
    /// 원본이다.</param>
    /// <param name="fixedPartSchema"><see cref="TransactionStatusInquirySchema.ResponseFixedPartSchema"/>를
    /// 넘긴다(호출자가 스키마를 선택하지 않고 그대로 주입하는 기존 패턴, <see cref="PosResponseTelegram"/>과
    /// 동일).</param>
    /// <param name="resultCode">SPEC <c>#7</c>에 실을 값 — 결과가 있으면 원거래 응답의 <c>#7</c>을
    /// 그대로(호출자가 미리 읽어 옴), 없으면 <c>PosResultCodeMapper</c>가 만든 <c>E07</c>.</param>
    /// <param name="originalTransactionTypeCode">원거래 거래구분 코드(예: "902614"). 결과 없으면
    /// <c>null</c>.</param>
    /// <param name="originalResponseBody">원거래 응답 원문. 결과 없으면 <c>null</c>. 이 배열은 이
    /// 메서드 안에서 복제해 꼬리로 소유하므로(호출자 소유 배열을 그대로 들고 있지 않는다), 호출자는
    /// 이후에도 이 배열을 자유롭게 쓰거나 지울 수 있다.</param>
    internal static PosInquiryResponseTelegram Build(
        PosRequestTelegram inquiryRequest,
        PosTelegramSchema fixedPartSchema,
        string resultCode,
        string? originalTransactionTypeCode,
        byte[]? originalResponseBody)
    {
        // 공통부(#1~#13)는 요청받은 값을 그대로 유지한다(§3.4.5) — 요청 본문(70바이트, 이미 space/'0'
        // 로만 채워진 유효한 전문)을 80바이트 고정부 앞쪽에 그대로 복사하고, 뒤쪽(개별부 10바이트
        // 자리)은 우선 space로 채운 뒤 필요한 값만 덮어쓴다. 필드별로 다시 읽고 옮겨 적지 않는 이유는
        // §4.10 relay 원칙과 같다 — 우리가 의미를 모르는(또는 새로 해석할 필요가 없는) 값을 다시
        // 만들어내지 않는다.
        byte[] requestBody = inquiryRequest.Telegram.ToBody();
        byte[] fixedBody = new byte[fixedPartSchema.TotalLength];
        try
        {
            for (int i = 0; i < fixedBody.Length; i++)
                fixedBody[i] = (byte)' ';

            Buffer.BlockCopy(requestBody, 0, fixedBody, 0, requestBody.Length);

            // PosTelegram.FromBytes는 배열을 복제하지 않고 그대로 소유한다(PosTelegram 클래스 요약) —
            // 그래서 fixedBody는 아래 Write 호출들의 대상이 되는 이 telegram의 실제 원본이다. 여기서
            // 지우면 안 된다(finally에서 지우는 것은 requestBody 하나뿐).
            var fixedTelegram = PosTelegram.FromBytes(fixedPartSchema, fixedBody);

            fixedTelegram.Write(TransactionTypeSuffixFieldNumber, ResponseTransactionTypeSuffix);
            fixedTelegram.Write(ResultCodeFieldNumber, resultCode);
            fixedTelegram.Write(
                TransactionStatusInquirySchema.OriginalTransactionTypeFieldNumber,
                originalTransactionTypeCode ?? NoResultFillValue);
            fixedTelegram.Write(
                TransactionStatusInquirySchema.OriginalResponseLengthFieldNumber,
                originalResponseBody is null
                    ? NoResultFillValue
                    : originalResponseBody.Length.ToString(CultureInfo.InvariantCulture));

            byte[]? tail = originalResponseBody is null ? null : (byte[])originalResponseBody.Clone();

            return new PosInquiryResponseTelegram(fixedTelegram, tail);
        }
        finally
        {
            SecureClear.Clear(requestBody);
        }
    }

    public string Read(int fieldNumber) => FixedPart.Read(fieldNumber);

    public byte[] BodyForLog()
    {
        byte[] fixedBytes = FixedPart.ToBody();
        try
        {
            return Concat(fixedBytes, Tail);
        }
        finally
        {
            SecureClear.Clear(fixedBytes);
        }
    }

    /// <summary>이 응답 고유의 거래 구분 코드("999900" 가칭)를 그대로 쓴다 — <c>TelegramLogRedactor</c>는
    /// 이 코드를 <c>PosSchemaRegistry</c>에서 찾지 못해(요청 스키마만 등록돼 있고 그나마도 총 길이가
    /// 다름) 위치 기반 마스킹 없이 원문 그대로 로그에 남기지만, 꼬리는 이미 P26-1이 카드리딩·PIN
    /// 필드를 지운 뒤의 바이트라 안전하다.</summary>
    public string RedactionTransactionTypeCode => FixedPart.Schema.TransactionTypeCode;

    public byte[] ToFrame()
    {
        byte[] fixedBytes = FixedPart.ToBody();
        byte[] fullBody = Concat(fixedBytes, Tail);
        try
        {
            return PosMessageFramer.BuildFrame(fullBody);
        }
        finally
        {
            SecureClear.Clear(fixedBytes);
            SecureClear.Clear(fullBody);
        }
    }

    public void ClearBody()
    {
        FixedPart.ClearBody();
        SecureClear.Clear(Tail);
    }

    private static byte[] Concat(byte[] fixedBytes, byte[]? tail)
    {
        int tailLength = tail?.Length ?? 0;
        byte[] full = new byte[fixedBytes.Length + tailLength];
        Buffer.BlockCopy(fixedBytes, 0, full, 0, fixedBytes.Length);
        if (tailLength > 0)
            Buffer.BlockCopy(tail!, 0, full, fixedBytes.Length, tailLength);

        return full;
    }
}
