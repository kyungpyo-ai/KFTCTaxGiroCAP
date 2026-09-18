using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// Phase 29 P29-4(docs/payment_relay/development_plan.md, PRD.md §12.3) — 국세 결제 화면이 전문 왕복부터
/// 세우기 위해 쓰는 임의값 생성기. <b>표현(<see cref="PosFieldType"/>)과 길이(바이트)만 본다</b> —
/// 필드 이름이나 업무 의미로 값을 바꾸지 않는다. 전문 간 필드 연쇄(PRD §3.3.2)를 피하려는 이유가
/// 여기서 다시 어겨지면 안 된다.
///
/// <b>길이는 바이트 기준</b>이고 인코딩은 CP949라 한글 1자 = 2바이트다. 이 클래스는 문자 수가 아니라
/// 바이트 예산으로 값을 채워 <see cref="PosField.Pad"/>가 초과 예외를 던지지 않도록 한다.
///
/// <see cref="PosTelegramSchema.FieldsOwnedByOneCap"/>에 속한 필드는 건드리지 않는다 — 원캡이 카드리딩
/// 결과로 채울 자리이므로 <see cref="PosTelegram.CreateEmpty"/>가 채운 공백을 그대로 둔다.
/// </summary>
public static class PosRandomValueGenerator
{
    private const string DigitCharset = "0123456789";
    private const string AlphaCharset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string AlphaNumericCharset = AlphaCharset + DigitCharset;

    // space는 일부러 뺐다 — PosField.Trim이 전체 space를 "미입력"으로 정규화하므로(PosField.cs), 생성된
    // 임의값 안에 space가 섞이면 화면에서 "왜 이 필드만 비어 보이나"는 혼란을 만들 수 있다.
    private const string SpecialCharset = "!@#$%^&*()-_=+[]{};:,.<>/?";
    private const string AlphaNumericSpecialCharset = AlphaNumericCharset + SpecialCharset;

    // CP949(EUC-KR 확장, UHC)는 완성형 한글 11,172자(U+AC00~U+D7A3) 전체를 2바이트로 인코딩한다.
    private const int HangulStart = 0xAC00;
    private const int HangulCount = 11172;

    /// <summary>
    /// 스키마의 모든 필드를 임의값으로 채운 요청 전문을 만든다. 원캡 담당 필드는 공백으로 남긴다
    /// (PRD §12.3 — "화면에서 수정 가능하게, 카드리딩 필드는 읽기전용 공백으로").
    /// </summary>
    public static PosTelegram GenerateRandomRequest(PosTelegramSchema schema, Random? random = null)
    {
        random ??= new Random();
        PosTelegram telegram = PosTelegram.CreateEmpty(schema);

        var ownedByOneCap = new HashSet<int>(schema.FieldsOwnedByOneCap().Select(f => f.Number));

        foreach (PosField field in schema.Fields)
        {
            if (ownedByOneCap.Contains(field.Number))
                continue;

            telegram.Write(field.Number, GenerateValue(field.Type, field.Length, random));
        }

        return telegram;
    }

    /// <summary>
    /// 한 필드의 표현·길이(바이트)에 맞는 임의값 원시 문자열을 만든다. 패딩은 하지 않는다
    /// (<see cref="PosField.Pad"/>가 <see cref="PosTelegram.Write(int,string)"/> 안에서 담당).
    /// </summary>
    public static string GenerateValue(PosFieldType type, int lengthBytes, Random random)
    {
        if (lengthBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(lengthBytes), lengthBytes, "필드 길이는 1바이트 이상이어야 함");

        return type switch
        {
            PosFieldType.N => FillAsciiCharset(lengthBytes, random, DigitCharset),
            PosFieldType.A => FillAsciiCharset(lengthBytes, random, AlphaCharset),
            PosFieldType.AN => FillAsciiCharset(lengthBytes, random, AlphaNumericCharset),
            PosFieldType.AHN => FillWithHangul(lengthBytes, random, AlphaNumericCharset),
            PosFieldType.ANS => FillAsciiCharset(lengthBytes, random, AlphaNumericSpecialCharset),
            PosFieldType.AHNS => FillWithHangul(lengthBytes, random, AlphaNumericSpecialCharset),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "매핑되지 않은 PosFieldType"),
        };
    }

    /// <summary>ASCII 문자만 쓰는 표현(N/A/AN/ANS) — CP949에서도 1자=1바이트라 문자 수 그대로 채운다.</summary>
    private static string FillAsciiCharset(int lengthBytes, Random random, string charset)
    {
        var sb = new StringBuilder(lengthBytes);
        for (int i = 0; i < lengthBytes; i++)
            sb.Append(charset[random.Next(charset.Length)]);

        return sb.ToString();
    }

    /// <summary>
    /// 한글을 섞는 표현(AHN/AHNS) — 한글 1자는 CP949로 2바이트다. 남은 바이트 예산이 1바이트뿐이면
    /// (홀수 길이 경계) ASCII 1자로 마무리해 예산을 절대 넘기지 않는다.
    /// </summary>
    private static string FillWithHangul(int lengthBytes, Random random, string asciiCharset)
    {
        var sb = new StringBuilder();
        int remaining = lengthBytes;

        while (remaining > 0)
        {
            bool canPlaceHangul = remaining >= 2;
            bool placeHangul = canPlaceHangul && random.Next(2) == 0;

            if (placeHangul)
            {
                sb.Append((char)(HangulStart + random.Next(HangulCount)));
                remaining -= 2;
            }
            else
            {
                sb.Append(asciiCharset[random.Next(asciiCharset.Length)]);
                remaining -= 1;
            }
        }

        return sb.ToString();
    }
}
