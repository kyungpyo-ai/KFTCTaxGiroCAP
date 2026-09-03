using System.Collections.Generic;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 24(docs/operations/development_plan.md P24-5) 검증용 가짜 <see cref="IKeyDownloadVanClient"/>.
/// **최종 산출물이 아니다** — <see cref="CapturingVanRelayService"/>(Phase 17/21, 결제 경로)와 같은
/// 이유로 존재한다: <see cref="KeyDownloadService"/>(P24-4)가 붙이는 서버 요청 바이트(P-28/P-29)를
/// 그대로 캡처해 P24-5 완료 조건의 slicing 검사에 쓰고, 0110/0130 응답을 실서버 없이 스크립트한다.
///
/// <see cref="FakeKeyDownloadReaderEndpoint"/>와 같은 이유로 명령(②/④)마다 "다음에 반환할 결과 1개"
/// property 방식을 쓴다 — 시퀀스당 정확히 한 번만 불린다(재시도 없음).
/// </summary>
internal sealed class FakeKeyDownloadVanClient : IKeyDownloadVanClient
{
    private readonly List<string>? _callLog;

    internal FakeKeyDownloadVanClient(List<string>? callLog = null)
    {
        _callLog = callLog;
    }

    /// <summary>기본값 — 0110 P-28(AN 610) = 키버전(2, "01") + HASH(64, 'H') + RND(32, 'R') +
    /// SIGN(512, 'S'). ③ slicing 검증(HASH/RND/SIGN이 앞 2byte를 뗀 뒤 그대로 [64]로 넘어가는지)에
    /// 쓰인다.</summary>
    internal const string DefaultKeyVersion = "01";
    internal static readonly string DefaultHash = new string('H', 64);
    internal static readonly string DefaultRnd = new string('R', 32);
    internal static readonly string DefaultSign = new string('S', 512);
    internal static readonly string DefaultResponse0110Payload = DefaultKeyVersion + DefaultHash + DefaultRnd + DefaultSign;

    /// <summary>기본값 — 0130 P-29(AN 146) = 키버전(2, "01") + 암호화데이터(128, 'X') + MAC(16, 'M').
    /// ⑤ slicing 검증에 쓰인다.</summary>
    internal static readonly string DefaultUsingKeyEncryptedData = new string('X', 128);
    internal static readonly string DefaultUsingKeyMac = new string('M', 16);
    internal static readonly string DefaultResponse0130Payload = DefaultKeyVersion + DefaultUsingKeyEncryptedData + DefaultUsingKeyMac;

    /// <summary>② 0100→0110 호출 횟수.</summary>
    internal int KeyDownloadCallCount { get; private set; }

    /// <summary>④ 0120→0130 호출 횟수.</summary>
    internal int KeyBundlingCallCount { get; private set; }

    /// <summary>가장 최근 ② 요청 P-28 — 슬라이싱 검증([73] 키버전+모듈ID가 그 순서 그대로 넘어갔는지)에 쓴다.</summary>
    internal string? LastP28 { get; private set; }

    /// <summary>가장 최근 ④ 요청 P-29 — 슬라이싱 검증([74] 키버전+모듈ID+암호화데이터가 그 순서
    /// 그대로 넘어갔는지)에 쓴다.</summary>
    internal string? LastP29 { get; private set; }

    internal KeyDownloadVanCallOutcome KeyDownloadOutcome { get; set; } =
        KeyDownloadVanCallOutcome.Success(DefaultResponse0110Payload, "00");

    internal KeyDownloadVanCallOutcome KeyBundlingOutcome { get; set; } =
        KeyDownloadVanCallOutcome.Success(DefaultResponse0130Payload, "00");

    public Task<KeyDownloadVanCallOutcome> SendKeyDownloadRequestAsync(string p28)
    {
        KeyDownloadCallCount++;
        LastP28 = p28;
        _callLog?.Add("②0100");
        return Task.FromResult(KeyDownloadOutcome);
    }

    public Task<KeyDownloadVanCallOutcome> SendKeyBundlingRequestAsync(string p29)
    {
        KeyBundlingCallCount++;
        LastP29 = p29;
        _callLog?.Add("④0120");
        return Task.FromResult(KeyBundlingOutcome);
    }
}
