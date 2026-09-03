using System.Threading.Tasks;

namespace KFTCOneCAP.Wpf.Services.Van;

/// <summary>
/// Phase 24(docs/operations/development_plan.md P24-4) — <see cref="Reader.KeyDownloadService"/>가
/// 서버 구간(② 0100→0110 상호인증, ④ 0120→0130 Key Bundling)에 대해 필요로 하는 것 전부를 담은 최소
/// 계약. <see cref="KeyDownloadVanClient"/>(구체 클래스)를 직접 잡으면 실장비·서버 없이는
/// <see cref="Reader.KeyDownloadService"/>의 5단계 시퀀스를 검증할 수 없다 — <see cref="Reader.IKeyDownloadReaderEndpoint"/>와
/// 같은 이유로 존재한다. 운영 구현은 <see cref="KeyDownloadVanClient"/> 자신이 이 인터페이스를 직접
/// 구현한다(P24-3이 이미 만든 두 메서드의 시그니처를 그대로 사용 — 메서드 본문은 이 Task에서 건드리지
/// 않는다).
/// </summary>
internal interface IKeyDownloadVanClient
{
    /// <summary>② Key Download 요청(0100) → 응답(0110). <paramref name="p28"/>은 정확히 12문자
    /// (AN 12 — 키버전 2 + 모듈ID 10, 리더기 <c>[73]</c> 응답에서 그대로 잘라온 값).</summary>
    Task<KeyDownloadVanCallOutcome> SendKeyDownloadRequestAsync(string p28);

    /// <summary>④ Key Bundling 요청(0120) → 응답(0130). <paramref name="p29"/>은 정확히 524문자
    /// (AN 524 — 키버전 2 + 모듈ID 10 + 암호화데이터 512, 리더기 <c>[74]</c> 응답에서 그대로
    /// 잘라온 값).</summary>
    Task<KeyDownloadVanCallOutcome> SendKeyBundlingRequestAsync(string p29);
}
