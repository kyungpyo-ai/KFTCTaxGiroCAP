using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>SendKeyDownloadStartCommandAsync([63]-&gt;[73])의 결과. KeyVersion/ReaderName/
    /// ReaderVersion/ModuleId는 Success/BusinessFailure일 때 채워진다(KeyDownloadStartResponseParser
    /// 참고 — 46byte 전체가 항상 함께 온다).</summary>
    internal sealed class KeyDownloadStartCommandOutcome
    {
        internal ReaderCommandOutcomeKind Kind { get; }
        internal string ResponseCode { get; }
        internal string KeyVersion { get; }
        internal string ReaderName { get; }
        internal string ReaderVersion { get; }
        internal string ModuleId { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }
        internal string Detail { get; }

        internal ReaderFailureCategory FailureCategory => Kind.ToFailureCategory();

        private KeyDownloadStartCommandOutcome(ReaderCommandOutcomeKind kind, string responseCode, string keyVersion,
            string readerName, string readerVersion, string moduleId, int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            ResponseCode = responseCode;
            KeyVersion = keyVersion;
            ReaderName = readerName;
            ReaderVersion = readerVersion;
            ModuleId = moduleId;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        internal static KeyDownloadStartCommandOutcome Success(string responseCode, string keyVersion,
            string readerName, string readerVersion, string moduleId) =>
            new KeyDownloadStartCommandOutcome(ReaderCommandOutcomeKind.Success, responseCode, keyVersion, readerName,
                readerVersion, moduleId, 0, string.Empty, string.Empty);

        internal static KeyDownloadStartCommandOutcome BusinessFailure(string responseCode, string keyVersion,
            string readerName, string readerVersion, string moduleId) =>
            new KeyDownloadStartCommandOutcome(ReaderCommandOutcomeKind.BusinessFailure, responseCode, keyVersion,
                readerName, readerVersion, moduleId, 0, string.Empty, string.Empty);

        internal static KeyDownloadStartCommandOutcome DllCallFailure(int dllResult, string dllResultName, string detail) =>
            new KeyDownloadStartCommandOutcome(ReaderCommandOutcomeKind.DllCallFailure, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, dllResult, dllResultName, detail);

        internal static KeyDownloadStartCommandOutcome Timeout() =>
            new KeyDownloadStartCommandOutcome(ReaderCommandOutcomeKind.Timeout, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, 0, string.Empty, "응답 대기 시간 초과");

        internal static KeyDownloadStartCommandOutcome CommunicationError(string detail) =>
            new KeyDownloadStartCommandOutcome(ReaderCommandOutcomeKind.CommunicationError, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, 0, string.Empty, detail);

        /// <summary>Protocol/Reader/KeyDownloadStartResponseResult에서 그대로 매핑한다(계층 규칙
        /// — Services는 바이트를 직접 다루지 않고 Protocol의 결과 객체만 받는다).</summary>
        internal static KeyDownloadStartCommandOutcome FromParsed(KeyDownloadStartResponseResult parsed)
        {
            // R-6/R-7(Phase 24 전체 Opus 리뷰) — KeyDownloadStartResponseParser에 비-ASCII 감지(I-1)가
            // 추가되면서 ParseFailed가 길이 부족 외에 비-ASCII 데이터 포함으로도 발생할 수 있다.
            if (parsed.ParseFailed)
                return CommunicationError("[73] 응답 데이터 길이 부족(46byte 미만) 또는 비-ASCII 데이터 포함");

            return parsed.IsSuccess
                ? Success(parsed.ResponseCode, parsed.KeyVersion, parsed.ReaderName, parsed.ReaderVersion, parsed.ModuleId)
                : BusinessFailure(parsed.ResponseCode, parsed.KeyVersion, parsed.ReaderName, parsed.ReaderVersion, parsed.ModuleId);
        }
    }
}
