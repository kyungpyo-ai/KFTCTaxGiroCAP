using KFTCOneCAP.Wpf.Services.Van;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 24(docs/operations/development_plan.md P24-4) — <see cref="KeyDownloadOutcome"/>이 실패로
    /// 끝났을 때 그 실패의 성격. 리더기 구간(①③⑤)과 서버 구간(②④)이 서로 다른 실패 분류 체계
    /// (<see cref="ReaderFailureCategory"/> / <see cref="KeyDownloadVanCallKind"/>)를 갖고 있어, 호출자
    /// (P24-6 화면 배선)가 하나의 값만 보고 분기할 수 있도록 여기서 통합한다.
    /// </summary>
    internal enum KeyDownloadOutcomeKind
    {
        /// <summary>5단계 전체 성공.</summary>
        Success,

        /// <summary>리더기가 정상 응답했지만 응답코드가 <c>"00"</c>이 아님(PRD.md §3.6).</summary>
        ReaderBusinessFailure,

        /// <summary>리더기 DLL 연동 레벨 실패 — 호출 실패/타임아웃/통신 오류.</summary>
        ReaderDllFailure,

        /// <summary><c>KFTC_GIRO.dll</c> 로드 실패 또는 <c>FNAISCRDVAN</c> 통신 실패(<c>nRet != 0</c>).</summary>
        ServerCommunicationFailure,

        /// <summary>서버 응답 전문이 SPEC 형식(길이/"ISO" 개시문자/전문 TYPE)과 다름.</summary>
        ServerResponseParseFailure,

        /// <summary>서버가 정상 응답했지만 P-39 응답코드가 <c>"00"</c>이 아님(PRD.md §3.5). <c>"395"</c>면
        /// <see cref="IsDeviceReplacementRequired"/>가 true다.</summary>
        ServerNonSuccessResponseCode,
    }

    /// <summary>
    /// 키다운로드 5단계 시퀀스(PRD.md §3.2) 1회 실행의 최종 결과. 어느 <see cref="Stage"/>에서
    /// 끝났는지(성공이면 <see cref="KeyDownloadStage.UsingKey"/>까지 전부 완료), 응답코드, 사람이 읽는
    /// 사유를 담는다. P24-6(화면 배선)이 이 값 하나로 성공/실패 문구를 만든다(PRD.md §3.6 "실패
    /// 문구에 단계와 응답코드").
    /// </summary>
    internal sealed class KeyDownloadOutcome
    {
        internal KeyDownloadStage Stage { get; }

        internal KeyDownloadOutcomeKind Kind { get; }

        internal bool IsSuccess => Kind == KeyDownloadOutcomeKind.Success;

        /// <summary>리더기(3.6 표) 또는 서버(P-39) 응답코드. 값이 없을 때(DLL 연동 레벨 실패 등)는
        /// 빈 문자열.</summary>
        internal string ResponseCode { get; }

        /// <summary>[73]/[74]/[75] 중 마지막으로 확보된 모듈 ID. 성공 문구에 쓰인다(PRD.md §3.6).</summary>
        internal string ModuleId { get; }

        /// <summary><see cref="Kind"/>가 <see cref="KeyDownloadOutcomeKind.ServerNonSuccessResponseCode"/>이고
        /// <see cref="ResponseCode"/>가 <c>"395"</c>일 때만 true — "단말기 교체 요망"(PRD.md §3.4 IPEK
        /// 버전 소진 안내)으로 표시해야 한다는 신호다.</summary>
        internal bool IsDeviceReplacementRequired { get; }

        internal string Detail { get; }

        private const string DeviceReplacementResponseCode = "395";

        private KeyDownloadOutcome(KeyDownloadStage stage, KeyDownloadOutcomeKind kind, string responseCode,
            string moduleId, string detail)
        {
            Stage = stage;
            Kind = kind;
            ResponseCode = responseCode;
            ModuleId = moduleId;
            Detail = detail;
            IsDeviceReplacementRequired =
                kind == KeyDownloadOutcomeKind.ServerNonSuccessResponseCode && responseCode == DeviceReplacementResponseCode;
        }

        internal static KeyDownloadOutcome Success(string moduleId) =>
            new(KeyDownloadStage.UsingKey, KeyDownloadOutcomeKind.Success, "00", moduleId, string.Empty);

        /// <summary>①③⑤ 리더기 구간 실패. <paramref name="category"/>가 <see cref="ReaderFailureCategory.ResponseCodeFailure"/>면
        /// 업무 실패, <see cref="ReaderFailureCategory.DllFailure"/>면 DLL 연동 레벨 실패로 분류한다.
        /// <see cref="ReaderFailureCategory.None"/>은 호출자가 넘기지 않는다(성공은 <see cref="Success"/>를 쓴다).</summary>
        internal static KeyDownloadOutcome ReaderFailure(
            KeyDownloadStage stage, ReaderFailureCategory category, string responseCode, string moduleId, string detail)
        {
            var kind = category == ReaderFailureCategory.ResponseCodeFailure
                ? KeyDownloadOutcomeKind.ReaderBusinessFailure
                : KeyDownloadOutcomeKind.ReaderDllFailure;
            return new KeyDownloadOutcome(stage, kind, responseCode ?? string.Empty, moduleId ?? string.Empty, detail ?? string.Empty);
        }

        /// <summary>②④ 서버 구간 실패. <see cref="KeyDownloadVanCallOutcome"/>의 Kind를 그대로 옮긴다
        /// (호출자가 <see cref="KeyDownloadVanCallOutcome.IsSuccess"/>가 아님을 이미 확인했어야 한다).</summary>
        internal static KeyDownloadOutcome ServerFailure(KeyDownloadStage stage, KeyDownloadVanCallOutcome vanOutcome, string moduleId)
        {
            var kind = vanOutcome.Kind switch
            {
                KeyDownloadVanCallKind.NonSuccessResponseCode => KeyDownloadOutcomeKind.ServerNonSuccessResponseCode,
                KeyDownloadVanCallKind.ResponseParseFailure => KeyDownloadOutcomeKind.ServerResponseParseFailure,
                _ => KeyDownloadOutcomeKind.ServerCommunicationFailure,
            };
            return new KeyDownloadOutcome(stage, kind, vanOutcome.ResponseCode ?? string.Empty, moduleId ?? string.Empty, vanOutcome.Detail ?? string.Empty);
        }
    }
}
