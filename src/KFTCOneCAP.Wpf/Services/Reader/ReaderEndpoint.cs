using System;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 15(docs/payment_relay/development_plan.md P15-2) — <see cref="IReaderEndpoint"/>의 운영
    /// 구현. <see cref="ReaderService"/>와 <see cref="IntegrityCheckService"/>를 묶는 얇은 어댑터일
    /// 뿐, 이 클래스 자체는 로직을 갖지 않는다(전부 위임). <see cref="ReaderService"/>/
    /// <see cref="IntegrityCheckService"/>/<see cref="ReaderConnectionManager"/> 중 무엇도 이 클래스가
    /// 생기면서 수정되지 않았다.
    /// </summary>
    internal sealed class ReaderEndpoint : IReaderEndpoint
    {
        private readonly ReaderService _reader;
        private readonly IntegrityCheckService _integrityCheckService;

        internal ReaderEndpoint(ReaderService reader, IntegrityCheckService integrityCheckService)
        {
            _reader = reader;
            _integrityCheckService = integrityCheckService;
        }

        /// <summary>
        /// <see cref="ReaderService.PortNumber"/>는 <see cref="ReaderService.OpenPort"/>가 성공/실패와
        /// 무관하게 항상 기억해 두는 값이다(ReaderService 클래스 주석 참고) — 그래서 포트가 지금
        /// 열려 있지 않아도(예: 열기 실패, 재시도 대기 중) 정확한 표시 문자열을 반환한다.
        /// <see cref="ReaderConnectionManager"/>가 설정에 없는(<c>"미사용"</c>) 리더기에는 애초에
        /// <see cref="ReaderService.OpenPort"/>를 호출하지 않으므로, 이 어댑터는 항상 설정된 포트에
        /// 대해서만 만들어진다는 전제(Orchestrator가 참여 후보를 정하는 시점, P15-6)를 그대로 따른다.
        ///
        /// (2026-08-25, Opus 검증 리뷰 L-1 수정) 그 전제가 깨지면(<see cref="ReaderService.PortNumber"/>가
        /// 한 번도 <c>OpenPort</c>를 거치지 않아 기본값 0이면) <c>ComPortFormat.ToDisplay(0)</c>이
        /// <c>"COM 00"</c>이라는 **유효해 보이지만 틀린** 표시 문자열을 조용히 만들어낸다 — 이 값이
        /// 무결성 이력 DB의 조회/저장 키로 그대로 흘러가면 엉뚱한 포트의 이력으로 쌓인다. 전제가
        /// 실제로 깨졌을 때는 자연스러운 반환값 대신 즉시 예외로 드러나는 편이 안전하다.
        /// </summary>
        public string ComPortDisplay
        {
            get
            {
                int portNumber = _reader.PortNumber;
                if (portNumber <= 0)
                {
                    throw new InvalidOperationException(
                        $"ReaderEndpoint.ComPortDisplay: 설정되지 않은 포트(PortNumber={portNumber})로 조회됐습니다 — " +
                        "이 어댑터는 참여 후보(설정된 포트)에만 만들어져야 합니다(Orchestrator P15-6 책임).");
                }

                return ComPortFormat.ToDisplay(portNumber);
            }
        }

        public Task<IntegrityCheckSequenceOutcome> RunIntegrityCheckAsync(TimeSpan statusTimeout, TimeSpan integrityTimeout) =>
            _integrityCheckService.RunAsync(_reader, ComPortDisplay, statusTimeout, integrityTimeout);

        public Task<CardReadCommandOutcome> SendCardReadCommandAsync(TransactionInfoRequest request, TimeSpan timeout) =>
            _reader.SendCardReadCommandAsync(request, timeout);

        public int SendInvalidationInit() => _reader.SendInvalidationInit();
    }
}
