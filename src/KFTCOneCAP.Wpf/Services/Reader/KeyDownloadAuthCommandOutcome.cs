using KFTCOneCAP.Wpf.Protocol.Reader;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>SendKeyDownloadAuthCommandAsync([64]-&gt;[74])의 결과. EncryptedData(512byte)는
    /// Success/BusinessFailure일 때만 채워진다 — 다음 단계([65] 조립)에 필요한 값만 여기 옮겨 담고,
    /// 원본 raw byte[]는 ReaderService가 이 outcome을 만든 직후 SecureClear로(3회 덮어쓰기) 지운다
    /// (development_plan.md P24-2/Phase 25 P25-2). 이 outcome 자체가 보관하는
    /// EncryptedData 문자열도 다음 단계 조립이 끝나면 호출자가 더 이상 참조하지 않아야 한다 —
    /// string은 불변이라 이 타입 수준에서는 지우지 않는다.</summary>
    internal sealed class KeyDownloadAuthCommandOutcome
    {
        internal ReaderCommandOutcomeKind Kind { get; }
        internal string ResponseCode { get; }
        internal string KeyVersion { get; }
        internal string ReaderName { get; }
        internal string ReaderVersion { get; }
        internal string ModuleId { get; }
        internal string EncryptedData { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }
        internal string Detail { get; }

        internal ReaderFailureCategory FailureCategory => Kind.ToFailureCategory();

        private KeyDownloadAuthCommandOutcome(ReaderCommandOutcomeKind kind, string responseCode, string keyVersion,
            string readerName, string readerVersion, string moduleId, string encryptedData,
            int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            ResponseCode = responseCode;
            KeyVersion = keyVersion;
            ReaderName = readerName;
            ReaderVersion = readerVersion;
            ModuleId = moduleId;
            EncryptedData = encryptedData;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        internal static KeyDownloadAuthCommandOutcome Success(string responseCode, string keyVersion, string readerName,
            string readerVersion, string moduleId, string encryptedData) =>
            new KeyDownloadAuthCommandOutcome(ReaderCommandOutcomeKind.Success, responseCode, keyVersion, readerName,
                readerVersion, moduleId, encryptedData, 0, string.Empty, string.Empty);

        internal static KeyDownloadAuthCommandOutcome BusinessFailure(string responseCode, string keyVersion,
            string readerName, string readerVersion, string moduleId, string encryptedData) =>
            new KeyDownloadAuthCommandOutcome(ReaderCommandOutcomeKind.BusinessFailure, responseCode, keyVersion,
                readerName, readerVersion, moduleId, encryptedData, 0, string.Empty, string.Empty);

        internal static KeyDownloadAuthCommandOutcome DllCallFailure(int dllResult, string dllResultName, string detail) =>
            new KeyDownloadAuthCommandOutcome(ReaderCommandOutcomeKind.DllCallFailure, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty, dllResult, dllResultName, detail);

        internal static KeyDownloadAuthCommandOutcome Timeout() =>
            new KeyDownloadAuthCommandOutcome(ReaderCommandOutcomeKind.Timeout, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty, 0, string.Empty, "응답 대기 시간 초과");

        internal static KeyDownloadAuthCommandOutcome CommunicationError(string detail) =>
            new KeyDownloadAuthCommandOutcome(ReaderCommandOutcomeKind.CommunicationError, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty, 0, string.Empty, detail);

        /// <summary>Protocol/Reader/KeyDownloadAuthResponseResult에서 그대로 매핑한다(계층 규칙
        /// — Services는 바이트를 직접 다루지 않고 Protocol의 결과 객체만 받는다).</summary>
        internal static KeyDownloadAuthCommandOutcome FromParsed(KeyDownloadAuthResponseResult parsed)
        {
            // R-7(Phase 24 전체 Opus 리뷰) — ParseFailed는 길이 부족뿐 아니라 비-ASCII 데이터 포함
            // (I-1, KeyDownloadAuthResponseParser.ContainsNonAscii)으로도 발생할 수 있다.
            if (parsed.ParseFailed)
                return CommunicationError("[74] 응답 데이터 길이 부족(558byte 미만) 또는 비-ASCII 데이터 포함");

            return parsed.IsSuccess
                ? Success(parsed.ResponseCode, parsed.KeyVersion, parsed.ReaderName, parsed.ReaderVersion, parsed.ModuleId, parsed.EncryptedData)
                : BusinessFailure(parsed.ResponseCode, parsed.KeyVersion, parsed.ReaderName, parsed.ReaderVersion, parsed.ModuleId, parsed.EncryptedData);
        }
    }
}
