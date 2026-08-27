using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// POS→OneCAP 요청 전문 파서(docs/payment_relay/development_plan.md P17-3). 임시 전문 시절의
/// <c>PosPaymentRequest</c>를 대체한다.
/// </summary>
public sealed class PosRequestTelegram
{
    /// <summary>SPEC 거래 구분 코드의 필드 번호.</summary>
    internal const int TransactionTypeFieldNumber = 4;

    /// <summary>
    /// SPEC #4 거래 구분 코드의 POSITION(공통부, 3전문 동일 레이아웃). 스키마에도 같은 값이 있지만
    /// <b>"스키마를 고르려면 먼저 #4를 읽어야 한다"</b>는 닭-달걀 때문에 여기 중복이 불가피하다 —
    /// 대신 <c>PosSchemaRegistry.ValidateAtStartup</c>이 기동 시 두 값이 일치하는지 확인한다(L-1).
    /// </summary>
    internal const int TransactionTypePosition = 10;

    /// <summary>SPEC #4 거래 구분 코드의 길이(N 6). 위 POSITION과 같은 이유로 중복·기동 검증 대상.</summary>
    internal const int TransactionTypeLength = 6;

    private const int MinimumBytesToIdentify = TransactionTypePosition + TransactionTypeLength;

    private PosRequestTelegram(PosTelegram telegram)
    {
        Telegram = telegram;
    }

    internal PosTelegram Telegram { get; }

    /// <summary>거래 구분 코드(예: "501008"). 라우팅에 이미 쓰였지만 로깅 등에서 다시 필요할 수 있다.</summary>
    public string TransactionTypeCode => Telegram.Schema.TransactionTypeCode;

    /// <summary>이 요청이 속한 전문 스키마 — 응답을 만들 때(relay 대상 스키마 지정 등) 필요하다.</summary>
    public PosTelegramSchema Schema => Telegram.Schema;

    /// <summary>해당 필드를 CP949로 디코딩하고 패딩을 제거해 읽는다.</summary>
    public string Read(int fieldNumber) => Telegram.Read(fieldNumber);

    /// <summary>
    /// 본문을 파싱한다. 성공/실패(그리고 실패 시 POS에 그대로 보낼 응답 프레임까지)를 예외가 아니라
    /// <see cref="PosRequestParseOutcome"/>으로 돌려준다 — <c>E40</c>/<c>E41</c>은 "그 프레임만 실패
    /// 응답하고 연결은 유지"(P14-5 규칙 계승)해야 하므로, 호출자가 예외 처리 대신 이 결과를 그대로
    /// 소켓에 써 보내면 된다.
    ///
    /// 본문이 <see cref="MinimumBytesToIdentify"/>바이트보다 짧아 #4조차 읽을 수 없는 경우만
    /// <see cref="PosProtocolException"/>을 던진다 — 이 경우는 P14-1/P14-5의 기존 "파싱 불가 프레임은
    /// 조용히 버림(응답 없음, 연결 유지)" 경로를 그대로 탄다(호출자의 기존 catch가 처리).
    /// </summary>
    public static PosRequestParseOutcome Parse(byte[] body)
    {
        if (body.Length < MinimumBytesToIdentify)
        {
            throw new PosProtocolException(
                $"본문이 너무 짧아 거래 구분 코드(#4)를 읽을 수 없음: {body.Length}바이트(최소 {MinimumBytesToIdentify}바이트 필요)");
        }

        string transactionTypeCode = PosMessageEncoding.Value.GetString(body, TransactionTypePosition, TransactionTypeLength);

        if (!PosSchemaRegistry.TryResolve(transactionTypeCode, out PosTelegramSchema? schema) || schema is null)
        {
            // E41: 스키마 자체를 식별할 수 없다 — 실제 전문 레이아웃을 모르므로 최소 공통부만으로 응답한다.
            byte[] minimalErrorFrame = PosUnknownTransactionErrorResponse.Build(transactionTypeCode);
            return PosRequestParseOutcome.Failure("E41", minimalErrorFrame);
        }

        if (body.Length != schema.TotalLength)
        {
            // E40: 스키마는 식별됐지만 길이가 안 맞아 요청 바이트를 신뢰할 수 없다(필드 오프셋을 그대로
            // 믿고 clone하면 잘못된 값을 되돌려 보낼 위험) — 스키마만으로 빈 응답을 새로 만든다.
            PosResponseTelegram failureResponse = PosResponseTelegram.Failure(schema, "E40");
            return PosRequestParseOutcome.Failure("E40", failureResponse.ToFrame());
        }

        var telegram = PosTelegram.FromBytes(schema, body);
        return PosRequestParseOutcome.Success(new PosRequestTelegram(telegram));
    }
}

/// <summary>
/// <see cref="PosRequestTelegram.Parse"/>의 결과. 성공하면 <see cref="Telegram"/>이, 실패하면
/// <see cref="ErrorCode"/>와 곧바로 소켓에 쓸 수 있는 <see cref="ErrorResponseFrame"/>이 채워진다.
/// </summary>
public sealed class PosRequestParseOutcome
{
    private PosRequestParseOutcome(PosRequestTelegram? telegram, string? errorCode, byte[]? errorResponseFrame)
    {
        Telegram = telegram;
        ErrorCode = errorCode;
        ErrorResponseFrame = errorResponseFrame;
    }

    public bool IsSuccess => Telegram is not null;

    public PosRequestTelegram? Telegram { get; }

    public string? ErrorCode { get; }

    public byte[]? ErrorResponseFrame { get; }

    internal static PosRequestParseOutcome Success(PosRequestTelegram telegram) => new(telegram, null, null);

    internal static PosRequestParseOutcome Failure(string errorCode, byte[] errorResponseFrame) =>
        new(null, errorCode, errorResponseFrame);
}
