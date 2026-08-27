using System;
using System.Collections.Generic;
using System.Linq;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// 전문 하나(501008/800000/902614)의 필드 목록 + 총 길이(docs/payment_relay/development_plan.md P17-1).
/// 생성 시점에 POSITION 연속성과 총 길이 일치를 스스로 검증한다 — 손으로 옮겨 적은 SPEC 표의 오타를
/// 런타임 이상값이 아니라 **앱 기동 시점**에 예외로 드러내기 위함이다.
/// </summary>
public sealed class PosTelegramSchema
{
    private readonly Dictionary<int, PosField> _byNumber;

    public PosTelegramSchema(string transactionTypeCode, IReadOnlyList<PosField> fields, int totalLength)
    {
        TransactionTypeCode = transactionTypeCode;
        Fields = fields.OrderBy(f => f.Position).ToList();
        TotalLength = totalLength;

        Validate();

        _byNumber = Fields.ToDictionary(f => f.Number);
    }

    /// <summary>SPEC의 "#4 거래 구분 코드" 값(예: "501008"). 요청 라우팅의 판별 키다.</summary>
    public string TransactionTypeCode { get; }

    /// <summary>POSITION 순으로 정렬된 필드 목록.</summary>
    public IReadOnlyList<PosField> Fields { get; }

    /// <summary>본문(BODY) 총 길이(바이트). 501008=706, 800000=500, 902614=1500(계산값).</summary>
    public int TotalLength { get; }

    public PosField this[int fieldNumber] =>
        _byNumber.TryGetValue(fieldNumber, out PosField? field)
            ? field
            : throw new ArgumentOutOfRangeException(nameof(fieldNumber), fieldNumber, $"스키마에 없는 필드 번호: #{fieldNumber}");

    /// <summary>
    /// <see cref="PosFieldOwner.OneCap"/>(원캡)이 SET 장소로 표시된 필드만 뽑는다 — 501008은 빈 목록이다
    /// (원캡 열 자체가 없는 전문).
    /// </summary>
    public IReadOnlyList<PosField> FieldsOwnedByOneCap() =>
        Fields.Where(f => f.Owners.HasFlag(PosFieldOwner.OneCap)).ToList();

    private void Validate()
    {
        int expectedPosition = 0;

        foreach (PosField field in Fields)
        {
            if (field.Position != expectedPosition)
            {
                throw new InvalidOperationException(
                    $"[{TransactionTypeCode}] 필드 #{field.Number}({field.Name})의 POSITION({field.Position})이 " +
                    $"직전 필드가 끝난 지점({expectedPosition})과 어긋남 — SPEC 표 옮겨 적기 오류 가능성");
            }

            expectedPosition = field.EndPosition;
        }

        if (expectedPosition != TotalLength)
        {
            throw new InvalidOperationException(
                $"[{TransactionTypeCode}] 필드 전체 길이({expectedPosition})가 선언된 총 길이({TotalLength})와 다름");
        }
    }
}
