using System;
using System.Globalization;
using System.Linq;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// 거래 구분 코드(#4)가 3전문 중 어디에도 해당하지 않는 요청(E41)에 대한 최소 응답
/// (docs/payment_relay/development_plan.md P17-3). 어떤 업무부 레이아웃을 써야 할지 알 수 없으므로,
/// 3전문이 공유하는 <b>공통부(70바이트)만으로</b> 응답을 만든다 — "완전히 침묵(응답 없음)"보다는 POS가
/// 최소한 실패 사실과 사유(E41)를 알 수 있는 편이 낫다는 판단(2026-08-26). SPEC이 명시적으로 다루지
/// 않는 예외 경로이므로, 실제 POS 구현체와 맞춰 봐야 할 수 있다는 점을 알아 둔다(P17-7 검증 항목).
/// </summary>
internal static class PosUnknownTransactionErrorResponse
{
    private const int CommonHeaderLength = 70;

    internal static byte[] Build(string unrecognizedTransactionTypeCode)
    {
        var headerOwners = Enumerable.Repeat(PosFieldOwner.None, 14).ToArray();
        var fields = PosCommonHeader.Create(CommonHeaderNameVariant.Shared800000And902614, headerOwners).ToList();
        var schema = new PosTelegramSchema(unrecognizedTransactionTypeCode, fields, CommonHeaderLength);

        var telegram = PosTelegram.CreateEmpty(schema);
        telegram.Write(1, "IGN");
        telegram.Write(2, "095");
        telegram.Write(3, "0210");
        telegram.Write(4, unrecognizedTransactionTypeCode);
        telegram.Write(6, "G");
        telegram.Write(7, "E41");
        telegram.Write(8, DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture));

        byte[] bodyBytes = telegram.ToBody();
        byte[] lengthBytes = PosMessageEncoding.Value.GetBytes(bodyBytes.Length.ToString("D4", CultureInfo.InvariantCulture));

        byte[] frame = new byte[lengthBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(lengthBytes, 0, frame, 0, lengthBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, frame, lengthBytes.Length, bodyBytes.Length);
        return frame;
    }
}
