using System;
using System.Collections.Generic;

namespace KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

/// <summary>
/// 3전문 스키마를 거래 구분 코드(#4)로 라우팅한다(docs/payment_relay/development_plan.md P17-3). 스키마는
/// 앱 기동 시 1회만 생성해 공유한다 — <see cref="PosTelegramSchema"/> 생성자가 자체 검증(POSITION 연속·
/// 총 길이 일치)을 수행하므로, 이 클래스가 초기화되는 시점(정적 생성자)에 SPEC 표 옮겨 적기 오류가 있으면
/// 앱이 기동하지 못하고 즉시 예외로 드러난다.
/// </summary>
internal static class PosSchemaRegistry
{
    private static readonly Dictionary<string, PosTelegramSchema> ByTransactionType = new()
    {
        [NoticeInquirySchema.FixedTransactionType] = NoticeInquirySchema.Create(),
        [CardInfoInquirySchema.FixedTransactionType] = CardInfoInquirySchema.Create(),
        [CardApprovalSchema.FixedTransactionType] = CardApprovalSchema.Create(),
    };

    /// <summary>
    /// 거래 구분 코드(SPEC #4, 예: "501008")로 스키마를 찾는다. 알 수 없는 코드면 <c>false</c>를
    /// 반환한다 — 호출자(P17-3)가 이를 <c>E41</c>(알 수 없는 거래구분)로 처리한다.
    /// </summary>
    internal static bool TryResolve(string transactionTypeCode, out PosTelegramSchema? schema) =>
        ByTransactionType.TryGetValue(transactionTypeCode, out schema);

    /// <summary>
    /// 앱 기동 시 <c>App.xaml.cs</c>가 호출한다(2026-08-26, 체크포인트 1 검증 M-2 수정). 정적 필드
    /// 초기화는 <b>최초 접근 시점</b>에 일어나므로, 아무도 이 클래스를 건드리지 않으면 "스키마 오류가
    /// 기동 시점에 드러난다"는 P17-2의 설계 약속이 실제로는 성립하지 않는다 — 첫 결제 요청이 들어와서야
    /// <see cref="TypeInitializationException"/>으로 터진다. 이 메서드가 그 약속을 실제로 지키게 한다.
    ///
    /// 함께 <see cref="PosRequestTelegram"/>이 라우팅용으로 하드코딩한 <c>#4</c> 오프셋이 스키마와
    /// 일치하는지도 확인한다(L-1) — 그 상수는 "스키마를 고르려면 먼저 #4를 읽어야 한다"는 닭-달걀
    /// 때문에 불가피한 중복이라, 어긋나면 조용히 엉뚱한 6바이트로 라우팅하게 된다.
    /// </summary>
    internal static void ValidateAtStartup()
    {
        foreach (PosTelegramSchema schema in ByTransactionType.Values)
        {
            PosField transactionTypeField = schema[PosRequestTelegram.TransactionTypeFieldNumber];

            if (transactionTypeField.Position != PosRequestTelegram.TransactionTypePosition ||
                transactionTypeField.Length != PosRequestTelegram.TransactionTypeLength)
            {
                throw new InvalidOperationException(
                    $"[{schema.TransactionTypeCode}] 거래 구분 코드(#{PosRequestTelegram.TransactionTypeFieldNumber}) 위치가 " +
                    $"PosRequestTelegram의 라우팅 상수와 어긋남 — 스키마: POSITION={transactionTypeField.Position}/" +
                    $"길이={transactionTypeField.Length}, 상수: POSITION={PosRequestTelegram.TransactionTypePosition}/" +
                    $"길이={PosRequestTelegram.TransactionTypeLength}");
            }
        }
    }
}
