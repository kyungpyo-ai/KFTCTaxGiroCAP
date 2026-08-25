using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// Phase 15(docs/payment_relay/development_plan.md P15-5) — VAN 승인 요청에 필요한 값의 순수 DTO.
/// **전문 바이트를 만들지 않는다** — 실제 VAN 요청 전문 생성은 Phase 17의 `Protocol/Van/` 몫이다
/// (계층 규칙: `Services/`는 전문을 직접 조립하지 않는다).
///
/// <see cref="CardData"/>는 0x3B 응답 파싱 결과(<see cref="Protocol.Reader.CardReadResponseParser"/>)를
/// 그대로 받는다 — 이미 구조화돼 있는 값을 VAN 전문용으로 다시 파싱할 이유가 없다.
/// </summary>
internal sealed class VanApprovalRequest
{
    internal VanApprovalRequest(CardReadData cardData, string amount, string transactionDateTime)
    {
        CardData = cardData;
        Amount = amount;
        TransactionDateTime = transactionDateTime;
    }

    internal CardReadData CardData { get; }

    internal string Amount { get; }

    internal string TransactionDateTime { get; }
}
