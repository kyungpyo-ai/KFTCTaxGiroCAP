using System;

namespace KFTCOneCAP.Wpf.Services.Payment;

/// <summary>
/// <c>902614</c> 승인요청 전문의 <c>#51</c>(암호화된 비밀번호 정보, ANS 100)에 넣을 값을 만드는 유일한
/// 지점(development_plan.md Phase 18 P18-5). SEED 암호화 방식이 확정되면(PRD §10, 2026-08-27 기준 미정)
/// <see cref="ToTelegramValue"/> 본문만 바뀐다 — 호출부(<c>PaymentOrchestrator.FillCardApprovalFields</c>)
/// 는 이 메서드 하나만 알고 있으면 되도록 격리했다.
///
/// <b>타입(Phase 25 P25-4)</b>: 입력·반환 모두 <c>char[]</c>다 — SEED 암호화가 확정되면 이 메서드 본문만
/// 바뀌는 격리 구조는 그대로 유지된다.
/// </summary>
internal static class PinFieldEncoder
{
    /// <summary>
    /// 입력받은 4자리 PIN을 #51에 넣을 값으로 바꾼다.
    /// ★ SEED 암호화 방식이 확정되면 이 메서드 본문만 바뀐다(2026-08-27 미정, PRD §10).
    /// 지금은 평문 4자리를 그대로 돌려주고, space 96 패딩은 <see cref="Protocol.Pos.PosField.Pad"/>가
    /// 처리한다.
    ///
    /// <b>SEED 도입 시 반드시 지킬 것(CP2 Opus 리뷰 개선권장 4, 2026-09-03)</b>: 지금은 입력 <c>pin</c>을
    /// 그대로 반환하므로, 호출부(<c>PaymentOrchestrator.FillCardApprovalFields</c>)가 원본 <c>pin</c>을
    /// 지울 때 이 반환값도 함께 지워진다(같은 배열) — 별도로 지울 게 없다. SEED 암호화가 들어가 이
    /// 메서드가 **새 <c>char[]</c>를 만들어 반환**하게 되면, 그 새 배열은 호출부의 <c>pin</c> 클리어로
    /// 커버되지 않는다 — 그 시점에 이 메서드 본문 안에서 암호화 중간 버퍼를 `try/finally` +
    /// `SecureClear`로 직접 지우도록 반드시 함께 고쳐야 한다(이 메서드가 새로 만드는 모든 버퍼가
    /// 대상 — 암호화 입력 사본, 중간 연산 버퍼 등).
    /// </summary>
    internal static char[] ToTelegramValue(char[]? pin)
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
