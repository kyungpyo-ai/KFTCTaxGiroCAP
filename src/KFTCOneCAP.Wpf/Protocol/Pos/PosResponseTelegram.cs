using System;
using System.Globalization;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// OneCAP→POS 응답 전문(docs/payment_relay/development_plan.md P17-3). 임시 전문 시절의
/// <c>PosPaymentResponse</c>를 대체한다.
///
/// <b>두 가지 생성 경로</b>(설계 근거는 클래스 요약이 아니라 development_plan.md P17-3 본문 참고 — 매우
/// 중요한 구분이라 여기서도 다시 요약한다): SPEC 흐름도(p.7/12/13)에서 응답이 KFTCVAN→OneCAP→POS까지
/// 같은 라벨(예: ④0210)로 이어진다는 것은, 응답이 각 경계마다 새로 만드는 전문이 아니라 <b>같은 바이트를
/// 그대로 통과시키는 중계</b>라는 뜻이다. 실제 응답 필드 대부분은 kiosk가 아니라 디지털예산/인터넷지로/
/// VAN이 채우므로 OneCAP이 요청만으로 만들어낼 수 있는 값이 아니다.
/// <list type="bullet">
/// <item><see cref="Relay"/> — VAN까지 도달해 실제 응답을 받은 성공 경로. VAN이 준 바이트를 그대로
///   감싸고 어떤 필드도 재작성하지 않는다.</item>
/// <item><see cref="Failure(PosRequestTelegram, string)"/> / <see cref="Failure(PosTelegramSchema, string)"/>
///   — OneCAP이 VAN에 도달하기 전 자체 실패(취소/Timeout/리더기 실패/전문 오류)한 경로. VAN 응답이
///   없으므로 합성한다. 요청 텔레그램(Clone) 또는 스키마(요청이 무효했던 경우 CreateEmpty)를 바탕으로
///   <c>#3</c>/<c>#6</c>/<c>#7</c>/<c>#8</c>만 덮어쓴다 — 서버가 채우는 필드는 kiosk도 원 요청에 채우지
///   않아 이미 공백이므로 clone해도 값이 어색해지지 않는다(SET 장소가 디지털예산/인터넷지로/VAN 단독인
///   필드는 kiosk 열에 표시가 없다는 전제가 성립하기 때문).</item>
/// </list>
/// </summary>
public sealed class PosResponseTelegram
{
    private const string ResponseTransactionTypeSuffix = "0210";
    private const string SendFlagFromOneCap = "G";

    private PosResponseTelegram(PosTelegram telegram)
    {
        Telegram = telegram;
    }

    internal PosTelegram Telegram { get; }

    /// <summary>VAN이 돌려준 응답 바이트를 그대로 감싼다 — 어떤 필드도 다시 쓰지 않는다(relay 경로).</summary>
    public static PosResponseTelegram Relay(PosTelegramSchema schema, byte[] vanResponseBody) =>
        new(PosTelegram.FromBytes(schema, vanResponseBody));

    /// <summary>
    /// 유효했던 요청을 clone해 실패 응답을 합성한다(실패 경로, 요청 자체는 정상 파싱됨 — 취소/Timeout/
    /// 리더기 실패 등 OneCAP 자체 판단으로 VAN에 도달하지 못한 경우).
    /// </summary>
    public static PosResponseTelegram Failure(PosRequestTelegram request, string resultCode) =>
        BuildFailure(request.Telegram.Clone(), resultCode);

    /// <summary>
    /// 요청 자체가 무효(길이 불일치 등)라 clone할 수 없을 때, 스키마만으로 빈 응답을 합성한다(E40 전용).
    /// </summary>
    public static PosResponseTelegram Failure(PosTelegramSchema schema, string resultCode) =>
        BuildFailure(PosTelegram.CreateEmpty(schema), resultCode);

    private static PosResponseTelegram BuildFailure(PosTelegram telegram, string resultCode)
    {
        telegram.Write(3, ResponseTransactionTypeSuffix);
        telegram.Write(6, SendFlagFromOneCap);
        telegram.Write(7, resultCode);
        telegram.Write(8, DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture));
        return new PosResponseTelegram(telegram);
    }

    /// <summary>
    /// <c>[길이 4자리][본문]</c> 프레임 바이트를 만든다. 길이 필드 자릿수(4)·형식은
    /// <see cref="PosMessageFramer"/>가 기대하는 것과 반드시 일치해야 한다(P14-1 프레이밍 규칙 계승).
    /// </summary>
    public byte[] ToFrame()
    {
        byte[] bodyBytes = Telegram.ToBody();

        if (bodyBytes.Length > 9999)
            throw new PosProtocolException($"응답 본문이 길이 필드(4자리) 범위를 초과함: {bodyBytes.Length}바이트");

        byte[] lengthBytes = PosMessageEncoding.Value.GetBytes(bodyBytes.Length.ToString("D4", CultureInfo.InvariantCulture));

        byte[] frame = new byte[lengthBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(lengthBytes, 0, frame, 0, lengthBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, frame, lengthBytes.Length, bodyBytes.Length);
        return frame;
    }
}
