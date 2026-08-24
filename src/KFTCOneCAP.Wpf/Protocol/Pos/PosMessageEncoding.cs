using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// POS 소켓 전문(길이 필드 + 본문)의 바이트↔문자열 인코딩을 정하는 단 하나의 지점
/// (docs/payment_relay/development_plan.md P14-1). 지금은 ASCII(임시 전문이 파이프 구분 영숫자뿐이라
/// 충분하다) — 실제 SPEC이 확정되면 EUC-KR/CP949로 바뀔 가능성이 있다(원본 MFC 앱이 CP949 기반).
/// 그때도 이 상수 하나만 바꾸면 되도록, <see cref="PosMessageFramer"/>·요청/응답 파서 모두 이 값만
/// 참조하고 <c>Encoding.ASCII</c> 등을 직접 쓰지 않는다.
/// </summary>
internal static class PosMessageEncoding
{
    internal static readonly Encoding Value = Encoding.ASCII;
}
