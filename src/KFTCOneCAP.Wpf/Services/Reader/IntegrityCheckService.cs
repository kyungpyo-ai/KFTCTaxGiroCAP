using System;
using System.Threading.Tasks;
using KFTCOneCAP.Wpf.Services.Diagnostics;
using KFTCOneCAP.Wpf.Services.Storage;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 12(docs/payment_relay/development_plan.md P12-4) — 무결성체크 2단계 시퀀스
    /// (0x61→0x71 상태체크 → 0x62→0x72 무결성)와 그 결과의 DB 저장을 함께 담당하는 공용 서비스.
    ///
    /// **화면(리더기 설정)과 결제 Flow(Phase 15)가 동일하게 재사용한다** — ViewModel에 이 시퀀스를
    /// 두면 결제 Flow의 선행 판정(PRD §4.2)이 같은 로직을 다시 구현해야 해서 두 벌이 어긋나기
    /// 시작한다. 이 클래스는 <see cref="ReaderService"/>(Protocol/Interop 경유)와
    /// <see cref="IntegrityCheckStore"/>(SQLite)만 알고 WPF 타입을 전혀 참조하지 않는다(계층 규칙).
    /// </summary>
    internal sealed class IntegrityCheckService
    {
        private readonly IntegrityCheckStore _store;

        internal IntegrityCheckService()
            : this(new IntegrityCheckStore())
        {
        }

        /// <summary>테스트/진단 하네스가 임시 Store를 주입할 수 있도록 열어 둔다.</summary>
        internal IntegrityCheckService(IntegrityCheckStore store)
        {
            _store = store;
        }

        /// <summary>
        /// 시퀀스를 실행하고 결과를 DB에 저장한다. 성공/실패 모두 저장한다(PRD §7). 1단계(0x71)에서
        /// 실패해 응답코드가 없으면 <see cref="IntegrityCheckRecord.ResponseCode"/>/ModuleId/
        /// ReaderAuthId를 null로 저장한다(Phase 11 스키마가 nullable로 이미 설계돼 있다).
        ///
        /// <paramref name="comPortDisplay"/>는 P12-2가 확정한 표시 문자열 형식("COM 05")이어야
        /// 한다 — 호출자가 <c>ComPortFormat.StripUnavailableSuffix</c>로 정규화해서 넘긴다(이
        /// 클래스는 그 정규화를 다시 하지 않는다 — 계층 규칙상 콤보 표시 규칙은 이 클래스가 알 필요가
        /// 없는 View/ViewModel 관심사다).
        /// </summary>
        internal async Task<IntegrityCheckSequenceOutcome> RunAsync(
            ReaderService reader, string comPortDisplay, TimeSpan statusTimeout, TimeSpan integrityTimeout)
        {
            // 계층 규칙(development_plan.md 공통 규칙 5, P12-3 스레드 주의): Services 내부는
            // ConfigureAwait(false)를 유지한다 — UI 스레드 복귀는 이 서비스를 호출하는 ViewModel의
            // await(ConfigureAwait 없음)가 책임진다.
            var statusOutcome = await reader.SendStatusCommandAsync(statusTimeout).ConfigureAwait(false);
            if (statusOutcome.Kind != ReaderCommandOutcomeKind.Success)
            {
                var failed = IntegrityCheckSequenceOutcome.FromStatusFailure(statusOutcome);
                Save(comPortDisplay, failed);
                return failed;
            }

            var integrityOutcome = await reader.SendIntegrityCommandAsync(integrityTimeout).ConfigureAwait(false);
            var result = IntegrityCheckSequenceOutcome.FromIntegrityOutcome(statusOutcome, integrityOutcome);
            Save(comPortDisplay, result);
            return result;
        }

        /// <summary>
        /// 2026-08-20 사용자 확정 정책(P11-4/P12-4): 저장 실패는 체크 자체의 성공/실패와 다른 축이다
        /// — 체크가 성공했다면 저장이 실패해도 로그만 남기고 성공 결과를 그대로 반환한다(호출자가
        /// 성공 문구를 실패로 바꿔 보여주지 않는다).
        /// </summary>
        private void Save(string comPort, IntegrityCheckSequenceOutcome outcome)
        {
            var record = new IntegrityCheckRecord(
                DateTime.Now, comPort, outcome.IsSuccess, outcome.ResponseCode, outcome.ModuleId, outcome.ReaderAuthId);

            var saveResult = _store.Save(record);
            if (!saveResult.Success)
            {
                FileLogger.Warn($"[무결성체크] DB 저장 실패({comPort}): {saveResult.ErrorMessage} — 체크 결과({(outcome.IsSuccess ? "성공" : "실패")})는 그대로 유지");
            }
        }
    }
}
