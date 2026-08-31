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
    /// <see cref="PosProtocolException"/>으로 던진다.
    ///
    /// <b>단, 이번 호출에서 이미 성공적으로 완성된 프레임이 하나라도 있으면 예외를 던지지 않고
    /// 그 프레임들만 정상 반환한다</b>(Phase 19 오류 주입 시뮬레이터 검증 중 발견·수정, 2026-08-31
    /// — 사용자가 "크리티컬"로 지정). 예전 동작은 "형식이 깨진 스트림에서 그 앞의 프레임도 신뢰할
    /// 근거가 없다"는 논리로 이미 완성된 프레임까지 통째로 버렸는데, 실제로는 이 상황이 대부분
    /// "POS가 길이 헤더를 실수로 틀리게 계산해 보낸 요청 하나"였다 — 프레이머가 그 거짓 길이를
    /// 믿고 앞쪽을 "프레임 1"로 잘라내고 남은 꼬리를 "프레임 2의 길이 헤더"로 다시 해석하려다
    /// 숫자가 아니라서 실패하는 패턴이다. 예전 동작대로면 이때 POS는 정상적인 오류 응답(E40 등,
    /// <see cref="PosRequestTelegram.Parse"/>가 이미 만들어 두는 것)조차 못 받고 응답 없이 연결만
    /// 끊겨, POS 쪽이 자체 타임아웃까지 무작정 기다려야 했다. 지금은 이미 완성된 프레임(그 자체는
    /// 길이 필드 규칙을 완전히 지켜 추출된 것이다)을 정상 처리해 오류 응답을 보내고, 남은 손상된
    /// 잔여 바이트는 버퍼에 그대로 남겨 둔다(<see cref="TryExtractFrame"/>은 예외를 던지기 전에
    /// <c>_buffer</c>를 건드리지 않으므로 안전하다) — 다음 <see cref="Append"/> 호출(또는 그 사이
    /// 아무 데이터도 안 오면 <c>PosSocketServer</c>의 유휴 연결 타임아웃)에서 정리된다.
    ///
    /// 반대로 이번 호출에서 프레임을 단 하나도 완성하지 못한 채 형식 오류를 만나면(예: 길이
    /// 헤더 자체가 처음부터 숫자가 아님) 예전처럼 그대로 예외를 던진다 — 이 경우는 정말로 아무런
    /// 근거가 없어 재동기화가 불가능하므로, 호출자가 연결을 닫는 것이 맞다.
    /// </summary>
    internal IReadOnlyList<byte[]> Append(byte[] chunk)
    {
        if (chunk is null || chunk.Length == 0)
            return Array.Empty<byte[]>();

        _buffer.AddRange(chunk);
        if (_buffer.Count > MaxBufferBytes)
            throw new PosProtocolException($"수신 버퍼 상한 초과({_buffer.Count} > {MaxBufferBytes}바이트) — 프레임이 완성되지 않은 채 버퍼가 계속 자람");

        var frames = new List<byte[]>();
        try
        {
            while (TryExtractFrame(out byte[]? frame))
            {
                frames.Add(frame!);
            }
        }
        catch (PosProtocolException) when (frames.Count > 0)
        {
            // 이미 완성된 프레임이 있다 — 그것들은 정상 반환하고, 손상된 나머지는 버퍼에 남겨 둔다
            // (위 클래스 주석 참고). frames.Count == 0이면 이 catch에 걸리지 않고 그대로 던져진다.
            return frames;
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
