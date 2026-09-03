using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Services.Reader;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 24(docs/operations/development_plan.md P24-5) 검증용 가짜 <see cref="IKeyDownloadReaderEndpoint"/>.
/// **최종 산출물이 아니다** — <see cref="FakeReaderEndpoint"/>(Phase 15)와 같은 이유로 존재한다: 실장비
/// 없이 <see cref="KeyDownloadService"/>(P24-4)의 5단계 호출 순서·중단 로직·바이트 slicing을 검증하기
/// 위한 스크립트 가능한 스텁이다.
///
/// <see cref="FakeReaderEndpoint"/>와 달리 이 클래스는 명령 3종(<c>[63]</c>/<c>[64]</c>/<c>[65]</c>)
/// 각각이 **시퀀스당 정확히 한 번**만 불린다(재시도 없음, PRD.md §3.6) — 그래서 큐 대신 명령별로
/// "다음에 반환할 결과 1개"를 property로 스크립트하는 방식을 쓴다(더 단순하고, 시나리오 코드에서
/// 실패를 주입하기도 더 쉽다). 기본값은 전부 성공이며, 바이트 slicing 검증(P24-5 완료 조건)을 위해
/// 각 필드에 서로 다른 반복 문자 패턴(구분 가능하도록)을 채워 둔다.
/// </summary>
internal sealed class FakeKeyDownloadReaderEndpoint : IKeyDownloadReaderEndpoint
{
    private readonly List<string>? _callLog;

    internal FakeKeyDownloadReaderEndpoint(List<string>? callLog = null)
    {
        _callLog = callLog;
    }

    /// <summary>[63]→[73] 호출 횟수.</summary>
    internal int StartCallCount { get; private set; }

    /// <summary>[64]→[74] 호출 횟수.</summary>
    internal int AuthCallCount { get; private set; }

    /// <summary>[65]→[75] 호출 횟수.</summary>
    internal int UsingKeyCallCount { get; private set; }

    /// <summary>가장 최근 [64] 요청 인자 — ③ slicing 검증(HASH/RND/SIGN이 0110 P-28에서 그대로
    /// 잘렸는지)에 쓴다.</summary>
    internal string? LastAuthHash { get; private set; }
    internal string? LastAuthRnd { get; private set; }
    internal string? LastAuthSign { get; private set; }

    /// <summary>가장 최근 [65] 요청 인자 — ⑤ slicing 검증(암호화데이터/MAC이 0130 P-29에서 그대로
    /// 잘렸는지)에 쓴다.</summary>
    internal string? LastUsingKeyEncryptedData { get; private set; }
    internal string? LastUsingKeyMac { get; private set; }

    /// <summary>키버전(2)+모듈ID(10) — 기본값은 성공, 정확히 12문자로 ② P-28 slicing 검증에
    /// 쓰인다.</summary>
    internal KeyDownloadStartCommandOutcome StartOutcome { get; set; } =
        KeyDownloadStartCommandOutcome.Success("00", "01", "FAKE-READER-NAME", "FAKE-READER-VER", "MODULE0001");

    /// <summary>암호화데이터(512) — 기본값은 성공, 'E' 반복 패턴으로 ④ P-29 slicing 검증에 쓰인다.</summary>
    internal KeyDownloadAuthCommandOutcome AuthOutcome { get; set; } =
        KeyDownloadAuthCommandOutcome.Success("00", "01", "FAKE-READER-NAME", "FAKE-READER-VER", "MODULE0001",
            new string('E', 512));

    internal KeyDownloadUsingKeyCommandOutcome UsingKeyOutcome { get; set; } =
        KeyDownloadUsingKeyCommandOutcome.Success("00", "MODULE0001");

    public Task<KeyDownloadStartCommandOutcome> SendKeyDownloadStartCommandAsync(TimeSpan timeout)
    {
        StartCallCount++;
        _callLog?.Add("①[63]");
        return Task.FromResult(StartOutcome);
    }

    public Task<KeyDownloadAuthCommandOutcome> SendKeyDownloadAuthCommandAsync(string hash, string rnd, string sign, TimeSpan timeout)
    {
        AuthCallCount++;
        LastAuthHash = hash;
        LastAuthRnd = rnd;
        LastAuthSign = sign;
        _callLog?.Add("③[64]");
        return Task.FromResult(AuthOutcome);
    }

    public Task<KeyDownloadUsingKeyCommandOutcome> SendKeyDownloadUsingKeyCommandAsync(string encryptedData, string mac, TimeSpan timeout)
    {
        UsingKeyCallCount++;
        LastUsingKeyEncryptedData = encryptedData;
        LastUsingKeyMac = mac;
        _callLog?.Add("⑤[65]");
        return Task.FromResult(UsingKeyOutcome);
    }
}
