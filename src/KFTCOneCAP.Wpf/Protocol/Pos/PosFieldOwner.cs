using System;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// SPEC 표의 "SET 장소" 열(docs/payment_relay/development_plan.md P17-2). 한 필드가 여러 주체에 동시에
/// 표시될 수 있어(예: 902614 #53 EMV DATA는 인터넷지로·원캡 둘 다) 플래그로 둔다.
/// <see cref="OneCap"/>(원캡)이 표시된 필드만 이 앱이 카드리딩 결과로 채운다 — 나머지는 kiosk가 채운
/// 값을 그대로 통과시키거나(요청), VAN/인터넷지로/디지털예산이 채운 값을 그대로 중계한다(응답).
/// </summary>
[Flags]
public enum PosFieldOwner
{
    None = 0,
    Kiosk = 1 << 0,
    OneCap = 1 << 1,
    InternetGiro = 1 << 2,
    Van = 1 << 3,
    DigitalBudget = 1 << 4,
}
