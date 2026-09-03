namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 24(docs/operations/development_plan.md P24-4) — 키다운로드 5단계 시퀀스(PRD.md §3.2)의
    /// 각 단계. <see cref="KeyDownloadOutcome"/>이 "어느 단계에서 끝났는지"를 표현하는 데 쓴다.
    /// 리더기 3단계(Start/Auth/UsingKey) + 서버 2단계(ServerAuth/ServerKeyBundling), 총 5개다.
    /// </summary>
    internal enum KeyDownloadStage
    {
        /// <summary>① 원캡 → 리더기 [63]→[73] 키 다운로드 시작.</summary>
        Start,

        /// <summary>② 원캡 → 서버 0100→0110 상호인증(Key Download).</summary>
        ServerAuth,

        /// <summary>③ 원캡 → 리더기 [64]→[74] 키 다운로드 상호 인증.</summary>
        Auth,

        /// <summary>④ 원캡 → 서버 0120→0130 Key Bundling.</summary>
        ServerKeyBundling,

        /// <summary>⑤ 원캡 → 리더기 [65]→[75] Using Key 전송.</summary>
        UsingKey,
    }
}
