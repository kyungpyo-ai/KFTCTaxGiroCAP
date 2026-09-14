using System;
using System.Globalization;
using System.Linq;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// 거래 구분 코드(#4)를 신뢰할 수 없거나(E41: 3전문 중 어디에도 해당하지 않음) 아예 읽지 못한
/// 요청(E42/E43, Phase 27 P27-8-f)에 대한 최소 응답(docs/payment_relay/development_plan.md P17-3).
/// 어떤 업무부 레이아웃을 써야 할지 알 수 없으므로, 3전문이 공유하는 <b>공통부(70바이트)만으로</b>
/// 응답을 만든다 — "완전히 침묵(응답 없음)"보다는 POS가 최소한 실패 사실과 사유를 알 수 있는 편이
/// 낫다는 판단(2026-08-26 E41 최초 도입, 2026-09-14 E42/E43로 일반화). SPEC이 명시적으로 다루지
/// 않는 예외 경로이므로, 실제 POS 구현체와 맞춰 봐야 할 수 있다는 점을 알아 둔다(P17-7 검증 항목).
/// </summary>
internal static class PosUnknownTransactionErrorResponse
{
    private const int CommonHeaderLength = 70;

    /// <param name="transactionTypeCode">
    /// #4(거래 구분 코드)에 실을 값. E41은 실제로 읽은(그러나 미식별) 값을 그대로 돌려주지만,
    /// E42는 본문이 너무 짧아 #4 자체를 읽지 못했으므로 호출자가 placeholder(P27-8-f 결정: "000000")를
    /// 넘긴다.
    /// </param>
    /// <param name="errorCode">#7(결과 코드)에 실을 값(E41/E42/E43).</param>
    internal static byte[] Build(string transactionTypeCode, string errorCode)
    {
        var headerOwners = Enumerable.Repeat(PosFieldOwner.None, 14).ToArray();
        var fields = PosCommonHeader.Create(CommonHeaderNameVariant.Shared800000And902614, headerOwners).ToList();
        var schema = new PosTelegramSchema(transactionTypeCode, fields, CommonHeaderLength);

        var telegram = PosTelegram.CreateEmpty(schema);
        telegram.Write(1, "IGN");
        telegram.Write(2, "095");
        telegram.Write(3, "0210");
        telegram.Write(4, transactionTypeCode);
        telegram.Write(6, "G");
        telegram.Write(7, errorCode);
        telegram.Write(8, DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture));

        byte[] bodyBytes = telegram.ToBody();
        byte[] lengthBytes = PosMessageEncoding.Value.GetBytes(bodyBytes.Length.ToString("D4", CultureInfo.InvariantCulture));

        byte[] frame = new byte[lengthBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(lengthBytes, 0, frame, 0, lengthBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, frame, lengthBytes.Length, bodyBytes.Length);
        return frame;
    }
}
