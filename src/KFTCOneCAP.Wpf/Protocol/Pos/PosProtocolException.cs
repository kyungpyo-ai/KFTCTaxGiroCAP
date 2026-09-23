using System;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// POS 소켓 전문(프레이밍/파싱) 처리 중 발생하는 형식 오류. <see cref="PosMessageFramer"/>,
/// <see cref="PosRequestTelegram"/> 파서, 그리고 고정길이 필드 코덱(<see cref="PosField.Pad"/>의 길이
/// 초과 검사)이 던진다(docs/payment_relay/development_plan.md P14-1/P14-5, P17-1).
/// 일반 <see cref="Exception"/>이 아니라 이 타입으로 구분해야, 호출자(Services/Pos)가 "형식 오류"와
/// "그 외 예외"를 로그·처리 방식에서 구분할 수 있다.
/// </summary>
public sealed class PosProtocolException : Exception
{
    public PosProtocolException(string message) : base(message)
    {
    }

    /// <summary>원본 예외를 <see cref="Exception.InnerException"/>으로 보존하는 오버로드(.NET 표준
    /// 패턴). P30 체크포인트 지적(M-3, 2026-09-23) — <see cref="TelegramFieldChainConverter"/>의
    /// 합산 변환이 비숫자 입력에 <see cref="FormatException"/>을 그대로 전파하던 것을, 이 프로젝트의
    /// 형식 오류 관례(<see cref="PosProtocolException"/>)로 감싸 던지기 위해 추가했다.</summary>
    public PosProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
