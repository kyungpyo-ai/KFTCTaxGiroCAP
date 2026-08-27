namespace KFTCOneCAP.Wpf.Services.Van;

internal enum VanRelayOutcomeKind
{
    /// <summary>VAN이 실제로 응답했다 — 승인/거절 여부는 응답 바이트 안의 <c>#7</c>이 말해 준다.
    /// OneCAP은 해석하지 않고 그대로 relay한다.</summary>
    Success,

    /// <summary>VAN과의 통신 자체가 실패 — 응답을 받지 못했으므로 relay할 바이트가 없다. OneCAP이
    /// <c>D</c> 코드로 직접 실패 응답을 합성해야 한다(P17-3 Failure/clone 경로).</summary>
    CommunicationFailure,
}
