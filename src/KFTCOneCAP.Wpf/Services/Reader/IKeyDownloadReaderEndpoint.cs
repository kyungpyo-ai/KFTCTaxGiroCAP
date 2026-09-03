using System;
using System.Threading.Tasks;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 24(docs/operations/development_plan.md P24-4) — <see cref="KeyDownloadService"/>가 리더기
    /// 구간(키다운로드 3단계, `[63]`/`[64]`/`[65]`)에 대해 필요로 하는 것 전부를 담은 최소 계약.
    ///
    /// <see cref="ReaderService"/>(sealed 구체 클래스)를 <see cref="KeyDownloadService"/>가 직접 잡으면
    /// 실장비 없이는 성공/실패/타임아웃 분기와 5단계 호출 순서를 한 번도 검증해 볼 수 없다 — Phase 15의
    /// <see cref="IReaderEndpoint"/>가 결제 Flow에 대해 정확히 같은 이유로 존재하는 것과 동일한 문제다.
    /// 이 인터페이스가 있어야 검증 하네스(P24-5의 <c>FakeKeyDownloadReaderEndpoint</c>)가 같은 자리에
    /// 꽂힐 수 있다. 운영 구현은 <see cref="ReaderService"/> 자신이 이 인터페이스를 직접 구현한다
    /// (P24-2가 이미 만든 세 메서드의 시그니처를 그대로 사용 — 메서드 본문은 이 Task에서 건드리지
    /// 않는다).
    /// </summary>
    internal interface IKeyDownloadReaderEndpoint
    {
        /// <summary>[63](키 다운로드 시작) 전송 → [73] 응답 대기 → 응답코드 + 키버전/리더기이름/
        /// 리더기버전/모듈ID 파싱. 요청 data는 없다(PRD.md §3.4).</summary>
        Task<KeyDownloadStartCommandOutcome> SendKeyDownloadStartCommandAsync(TimeSpan timeout);

        /// <summary>[64](키 다운로드 상호 인증) 전송 → [74] 응답 대기(PRD.md §3.4). hash/rnd/sign은
        /// 정확한 길이(64/32/512)의 ASCII 문자열이어야 한다.</summary>
        Task<KeyDownloadAuthCommandOutcome> SendKeyDownloadAuthCommandAsync(string hash, string rnd, string sign, TimeSpan timeout);

        /// <summary>[65](Using Key 전송) 전송 → [75] 응답 대기(PRD.md §3.4). encryptedData/mac은
        /// 정확한 길이(128/16)의 ASCII 문자열이어야 한다.</summary>
        Task<KeyDownloadUsingKeyCommandOutcome> SendKeyDownloadUsingKeyCommandAsync(string encryptedData, string mac, TimeSpan timeout);
    }
}
