using System;
using System.Collections.Generic;
using System.Globalization;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// POS 소켓 임시 전문의 프레이밍(docs/payment_relay/development_plan.md P14-1) — TCP는 스트림이라
/// 메시지 경계가 없으므로, 수신 바이트를 누적하며 완성된 프레임(BODY)만 골라낸다.
///
/// ★ 공개 계약은 <see cref="Append"/> 하나뿐이다: "바이트를 넣으면 완성된 프레임이 나온다." 프레임
/// 내부 형식(현재는 <c>[길이 4자리(ASCII)][본문]</c>, STX/ETX 없음 — 2026-08-24 결정, PRD §10.1)은
/// 이 클래스 밖으로 절대 드러나지 않는다. 실제 SPEC이 이 구조를 안 쓰기로 확정돼도(그 가능성이 크다는
/// 것이 이 결정의 전제) 이 클래스 내부만 새로 짜면 되고, 이 클래스를 호출하는 <c>Services/Pos/</c>는
/// 손대지 않는다.
///
/// 길이 필드 하나로만 경계를 정하므로(제어문자 마커 없음) **프레임 시작점을 잃으면 재동기화할 방법이
/// 없다** — 길이 필드가 숫자가 아니거나 버퍼 상한을 넘으면 <see cref="PosProtocolException"/>을 던지고,
/// 호출자(<c>PosSocketServer</c>)는 그 연결을 통째로 닫는다(같은 연결 안에서 복구를 시도하지 않는다).
/// 인스턴스 상태(누적 버퍼)를 가지므로 **연결 1개당 1개**를 만들어 써야 한다 — 여러 연결이 하나의
/// 프레이머를 공유하면 서로 다른 연결의 바이트가 한 버퍼에 섞인다.
/// </summary>
internal sealed class PosMessageFramer
{
    private const int LengthFieldSize = 4;
    private const int MaxFrameBodyBytes = 9999;

    /// <summary>누적 버퍼 상한(바이트). 상한이 없으면 잘못된 길이 필드 하나로 버퍼가 무한히 자란다.</summary>
    private const int MaxBufferBytes = 64 * 1024;

    private readonly List<byte> _buffer = new();

    /// <summary>
    /// 수신한 바이트 조각을 누적하고, 그 결과 완성된 프레임(BODY만, 길이 필드 제외)을 순서대로
    /// 돌려준다. 한 번의 호출로 0개/1개/N개가 완성될 수 있다. 형식 오류는
    /// <see cref="PosProtocolException"/>으로 던진다 — 그 시점까지 이미 완성된 프레임은 없었던 것으로
    /// 간주한다(형식이 깨진 스트림에서 그 앞의 프레임을 신뢰할 근거가 없다).
    /// </summary>
    internal IReadOnlyList<byte[]> Append(byte[] chunk)
    {
        if (chunk is null || chunk.Length == 0)
            return Array.Empty<byte[]>();

        _buffer.AddRange(chunk);
        if (_buffer.Count > MaxBufferBytes)
            throw new PosProtocolException($"수신 버퍼 상한 초과({_buffer.Count} > {MaxBufferBytes}바이트) — 프레임이 완성되지 않은 채 버퍼가 계속 자람");

        var frames = new List<byte[]>();
        while (TryExtractFrame(out byte[]? frame))
        {
            frames.Add(frame!);
        }

        return frames;
    }

    private bool TryExtractFrame(out byte[]? frame)
    {
        frame = null;

        if (_buffer.Count < LengthFieldSize)
            return false; // 길이 필드조차 다 안 옴 — 다음 Append를 기다린다.

        string lengthText = PosMessageEncoding.Value.GetString(_buffer.GetRange(0, LengthFieldSize).ToArray());
        if (!int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out int bodyLength))
            throw new PosProtocolException($"길이 필드가 숫자가 아님: '{lengthText}'");

        if (bodyLength > MaxFrameBodyBytes)
            throw new PosProtocolException($"길이 필드 범위 초과: {bodyLength} > {MaxFrameBodyBytes}");

        int totalFrameLength = LengthFieldSize + bodyLength;
        if (_buffer.Count < totalFrameLength)
            return false; // 본문이 아직 다 안 옴 — 다음 Append를 기다린다.

        frame = _buffer.GetRange(LengthFieldSize, bodyLength).ToArray();
        _buffer.RemoveRange(0, totalFrameLength);
        return true;
    }
}
