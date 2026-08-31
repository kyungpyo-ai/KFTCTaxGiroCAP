using System;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// <c>902614</c> 승인요청 전문의 <c>#51</c>(암호화된 비밀번호 정보, ANS 100)에 넣을 값을 만드는 유일한
/// 지점(development_plan.md Phase 18 P18-5). SEED 암호화 방식이 확정되면(PRD §10, 2026-08-27 기준 미정)
/// <see cref="ToTelegramValue"/> 본문만 바뀐다 — 호출부(<c>PaymentOrchestrator.FillCardApprovalFields</c>)
/// 는 이 메서드 하나만 알고 있으면 되도록 격리했다.
/// </summary>
internal static class PinFieldEncoder
{
    /// <summary>
    /// 입력받은 4자리 PIN을 #51에 넣을 값으로 바꾼다.
    /// ★ SEED 암호화 방식이 확정되면 이 메서드 본문만 바뀐다(2026-08-27 미정, PRD §10).
    /// 지금은 평문 4자리를 그대로 돌려주고, space 96 패딩은 <see cref="Protocol.Pos.PosField.Pad"/>가
    /// 처리한다.
    /// </summary>
    internal static string ToTelegramValue(string pin)
    {
        // VAN이 조용히 거절하기 전에 여기서 드러나야 한다(FillCardApprovalFields의 #43 길이 체크와
        // 같은 방어적 검증 스타일). PIN 값 자체는 예외 메시지에도 담지 않는다 — 로그 금지 규칙은
        // 예외 텍스트에도 동일하게 적용된다.
        if (pin == null || pin.Length != 4)
        {
            throw new InvalidOperationException(
                $"#51에 넣을 PIN이 4자리가 아님(값은 로그·예외 메시지에 남기지 않음): 길이={pin?.Length.ToString() ?? "null"}");
        }

        foreach (char c in pin)
        {
            if (c < '0' || c > '9')
                throw new InvalidOperationException("#51에 넣을 PIN에 숫자가 아닌 문자가 포함됨(값은 로그·예외 메시지에 남기지 않음)");
        }

        return pin;
    }
}
