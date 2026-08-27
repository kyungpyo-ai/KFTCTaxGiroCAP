using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// POS 소켓 전문(길이 필드 + 본문)의 바이트↔문자열 인코딩을 정하는 단 하나의 지점
/// (docs/payment_relay/development_plan.md P14-1, Phase 17에서 CP949로 교체). SPEC의 업무부에
/// 한글 필드(AHN/AHNS)가 다수 있어 ASCII로는 조용히 깨진다(P14-1이 예상해 둔 상황) — CP949는 원본 MFC
/// 앱과 동일 계열이다. 이 상수 하나만 바꾸면 되도록, <see cref="PosMessageFramer"/>·전문 코덱
/// (<see cref="PosField"/>) 모두 이 값만 참조하고 <c>Encoding.ASCII</c>/<c>Encoding.UTF8</c> 등을
/// 직접 쓰지 않는다. 발주처가 EUC-KR을 명시하면 이 한 줄만 바뀐다(development_plan.md P17 남은 미확정 1).
/// </summary>
internal static class PosMessageEncoding
{
    internal static readonly Encoding Value = Encoding.GetEncoding(949);
}
