using System;
using KFTCOneCAP.Wpf.Security;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// 전문 본문(BODY) 바이트를 스키마로 읽고 쓰는 컨테이너(docs/payment_relay/development_plan.md P17-1).
///
/// <b>원본 보존 원칙</b>: 전문을 강타입 객체로 전부 분해했다가 재조립하지 않는다. 원캡이 채우는 필드는
/// 3전문 통틀어 8개뿐이고(P17-2) 나머지 수십 개는 kiosk/VAN/인터넷지로/디지털예산이 채운 값을 해석하지
/// 않고 그대로 옮기기만 한다 — 분해 후 재조립하면 우리가 의미를 모르는 필드에서 값이 유실될 위험을
/// 스스로 만든다. 그래서 이 클래스는 원본 <see cref="byte"/> 배열을 들고 있다가 지정한 필드 구간만
/// <see cref="Write"/>로 덮어쓴다.
///
/// <b>두 가지 생성 경로</b>(P17-3 응답 설계 — 성공은 relay, 실패는 clone):
/// <list type="bullet">
/// <item><see cref="FromBytes"/> — 이미 완성된 본문(POS가 보낸 요청, 또는 VAN이 돌려준 응답)을 그대로
///   감싼다. VAN 성공 응답을 POS에 relay할 때 이 경로를 쓰며, 어떤 필드도 다시 쓰지 않는다.</item>
/// <item><see cref="Clone"/> — 기존 텔레그램의 바이트를 복사해 새 인스턴스를 만든다. OneCAP이 VAN에
///   도달하기 전 자체 실패(취소/Timeout/리더기 실패/전문 오류) 응답을 합성할 때만 쓴다 — 서버가 채우는
///   필드는 kiosk도 원 요청에 채우지 않아 이미 공백이므로 clone해도 값이 어색해지지 않는다.</item>
/// </list>
/// </summary>
public sealed class PosTelegram
{
    private readonly byte[] _body;

    private PosTelegram(PosTelegramSchema schema, byte[] body)
    {
        if (body.Length != schema.TotalLength)
        {
            throw new PosProtocolException(
                $"[{schema.TransactionTypeCode}] 본문 길이({body.Length})가 스키마 총 길이({schema.TotalLength})와 다름");
        }

        Schema = schema;
        _body = body;
    }

    public PosTelegramSchema Schema { get; }

    /// <summary>이미 완성된 본문 바이트를 그대로 감싼다(길이만 검증, 내용은 손대지 않음).</summary>
    public static PosTelegram FromBytes(PosTelegramSchema schema, byte[] body) => new(schema, body);

    /// <summary>선언된 총 길이만큼 전체를 space(0x20)로 채운 새 본문을 만든다.</summary>
    public static PosTelegram CreateEmpty(PosTelegramSchema schema)
    {
        byte[] body = new byte[schema.TotalLength];
        for (int i = 0; i < body.Length; i++)
            body[i] = (byte)' ';

        return new PosTelegram(schema, body);
    }

    /// <summary>현재 바이트를 복사해 새 인스턴스를 만든다(실패 응답 합성 전용, 클래스 주석 참고).</summary>
    public PosTelegram Clone()
    {
        byte[] copy = new byte[_body.Length];
        Buffer.BlockCopy(_body, 0, copy, 0, _body.Length);
        return new PosTelegram(Schema, copy);
    }

    /// <summary>해당 필드 구간을 CP949로 디코딩하고 패딩을 제거해 돌려준다.</summary>
    public string Read(int fieldNumber)
    {
        PosField field = Schema[fieldNumber];
        string padded = PosMessageEncoding.Value.GetString(_body, field.Position, field.Length);
        return PosField.Trim(field.Type, padded);
    }

    /// <summary>
    /// 값을 CP949로 인코딩해 패딩을 적용한 뒤 해당 필드 구간만 덮어쓴다. 다른 필드의 바이트는 전혀
    /// 건드리지 않는다(원본 보존 원칙). 인코딩 결과가 필드 길이를 넘으면 <see cref="PosField.Pad"/>가
    /// 예외를 던진다.
    /// </summary>
    public void Write(int fieldNumber, string value)
    {
        PosField field = Schema[fieldNumber];
        byte[] valueBytes = PosMessageEncoding.Value.GetBytes(value);
        byte[] padded = field.Pad(valueBytes);
        Buffer.BlockCopy(padded, 0, _body, field.Position, field.Length);
    }

    /// <summary><see cref="Write(int,string)"/>의 <c>char[]</c> 버전(Phase 25 P25-3) — 카드정보처럼
    /// <c>string</c>으로 만들면 지울 수 없는 값을 위한 경로다. 동작은 동일(CP949 인코딩 + 패딩)하고
    /// 값 자체를 문자열화하지 않는다. <c>valueBytes</c>/<c>padded</c>는 이 메서드 안에서 만들어져
    /// <c>_body</c>로 옮겨 적은 뒤 더 필요 없으므로 즉시 지운다(Phase 25 P25-5, PRD.md §4.2 #8 —
    /// <see cref="Write(int,string)"/>은 민감하지 않은 필드만 계속 쓰므로 지우지 않는다). <c>GetBytes</c>
    /// 호출부터 <c>try</c>로 감싼다 — <see cref="PosField.Pad"/>가 필드 길이 초과로 예외를 던지는
    /// 경로에서도 이미 만들어진 <c>valueBytes</c>가 지워지지 않고 남는 문제를 CP2 Opus 리뷰
    /// 개선권장 2(2026-09-03)에서 잡았다.</summary>
    public void Write(int fieldNumber, char[] value)
    {
        PosField field = Schema[fieldNumber];
        byte[]? valueBytes = null;
        byte[]? padded = null;
        try
        {
            valueBytes = PosMessageEncoding.Value.GetBytes(value);
            padded = field.Pad(valueBytes);
            Buffer.BlockCopy(padded, 0, _body, field.Position, field.Length);
        }
        finally
        {
            SecureClear.Clear(valueBytes);
            SecureClear.Clear(padded);
        }
    }

    /// <summary>원본 버퍼의 복사본. 소켓에 실제로 나가는 본문(길이 헤더는 프레이머가 별도로 붙인다).</summary>
    public byte[] ToBody()
    {
        byte[] copy = new byte[_body.Length];
        Buffer.BlockCopy(_body, 0, copy, 0, _body.Length);
        return copy;
    }

    /// <summary>
    /// Phase 25 P25-6(PRD.md §4.2 #7) — 원본 <c>_body</c>를 3회 덮어쓴다. 이 거래가 더 이상 이 인스턴스를
    /// 참조하지 않는 시점(요청은 <c>TransactionQueue.WorkerLoop</c>가 응답 송신까지 끝난 뒤, 응답은
    /// <c>PosSocketServer.SendResponse</c>가 프레임을 쓴 뒤)에만 호출한다. <c>_body</c>를 그대로
    /// 노출하지 않고 이 메서드로만 지우게 해 원본 보존 원칙(클래스 요약)이 클리어 시점에도 깨지지
    /// 않도록 한다 — 밖에서 배열을 꺼내 임의로 조작할 수 없다.
    /// </summary>
    internal void ClearBody() => SecureClear.Clear(_body);
}
