using System;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// POS 소켓 전문(프레이밍/파싱) 처리 중 발생하는 형식 오류. <see cref="PosMessageFramer"/>와
/// <see cref="PosPaymentRequest"/> 파서가 던진다(docs/payment_relay/development_plan.md P14-1/P14-5).
/// 일반 <see cref="Exception"/>이 아니라 이 타입으로 구분해야, 호출자(Services/Pos)가 "형식 오류"와
/// "그 외 예외"를 로그·처리 방식에서 구분할 수 있다.
/// </summary>
public sealed class PosProtocolException : Exception
{
    public PosProtocolException(string message) : base(message)
    {
    }
}
