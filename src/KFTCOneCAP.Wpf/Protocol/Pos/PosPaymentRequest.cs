namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// POS→앱 결제 요청 전문의 임시 파싱 결과(docs/payment_relay/development_plan.md P14-1). 실제 POS
/// 전문은 미확정이므로(PRD §10) 검증에 필요한 최소 필드만 둔다: 금액, 거래고유번호.
///
/// 이 파서는 <see cref="PosMessageFramer"/>와 별도 클래스다 — 프레이밍 규칙(프레임 경계를 어떻게
/// 자르는지)과 필드 구성 규칙(자른 본문을 어떻게 해석하는지)은 서로 독립적으로 바뀔 수 있다. 실제
/// SPEC 확정 시 이 클래스 안(<see cref="Parse"/>)만 새로 짜면 되고, 프레이머·소켓 서버는 그대로 둔다.
/// </summary>
internal sealed class PosPaymentRequest
{
    private const string Tag = "PAY";

    private PosPaymentRequest(string amount, string transactionId)
    {
        Amount = amount;
        TransactionId = transactionId;
    }

    internal string Amount { get; }

    internal string TransactionId { get; }

    /// <summary>
    /// 임시 BODY 포맷: <c>PAY|&lt;금액&gt;|&lt;거래고유번호&gt;</c>. 프레임 경계는 이미
    /// <see cref="PosMessageFramer"/>가 지켰으므로, 이 파싱이 실패해도(알 수 없는 태그 등) 연결을
    /// 닫을 필요는 없다 — 호출자가 그 프레임만 버리고 다음 프레임을 계속 받을 수 있다(P14-5).
    /// </summary>
    internal static PosPaymentRequest Parse(byte[] body)
    {
        string text = PosMessageEncoding.Value.GetString(body);
        string[] parts = text.Split('|');

        if (parts.Length != 3 || parts[0] != Tag)
            throw new PosProtocolException($"알 수 없는 요청 전문: '{text}'");

        string amount = parts[1];
        string transactionId = parts[2];

        if (transactionId.Length == 0)
            throw new PosProtocolException($"거래고유번호가 비어 있음: '{text}'");

        return new PosPaymentRequest(amount, transactionId);
    }
}
