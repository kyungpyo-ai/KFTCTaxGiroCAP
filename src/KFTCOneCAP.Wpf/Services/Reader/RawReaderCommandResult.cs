namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>ReaderService 내부 전용 — CALLBACK 1건이 SendAndAwaitAsync의 대기를 어떻게
    /// 끝냈는지를 나타낸다. 이 계층에서는 아직 SPEC 업무 응답코드를 해석하지 않는다(Data는 원본
    /// byte[] 그대로) — 해석은 각 공개 Send*Async 메서드가 Protocol/Reader의 파서로 위임한다
    /// (계층 규칙: Services는 Protocol이 만든 결과 객체만 최종적으로 다룬다).</summary>
    internal enum RawReaderCommandKind
    {
        Response,
        Timeout,
        CommunicationError,
        DllCallFailure,
    }

    internal sealed class RawReaderCommandResult
    {
        internal RawReaderCommandKind Kind { get; }
        internal byte[] Data { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }
        internal string Detail { get; }

        private RawReaderCommandResult(RawReaderCommandKind kind, byte[] data, int dllResult, string dllResultName, string detail)
        {
            Kind = kind;
            Data = data;
            DllResult = dllResult;
            DllResultName = dllResultName;
            Detail = detail;
        }

        internal static RawReaderCommandResult Response(byte[] data) =>
            new RawReaderCommandResult(RawReaderCommandKind.Response, data, 0, string.Empty, string.Empty);

        /// <summary>
        /// 2026-09-15 사용자 요청 — Timeout이 두 서로 다른 근원(<see cref="ReaderService.CompletePendingIfMatches"/>의
        /// DLL <c>READER_EVENT_TIMEOUT</c> 콜백 vs <see cref="ReaderService.SendAndAwaitAsync"/>의 앱 자체
        /// 로컬 타이머 만료)에서 동일한 문구로 뭉뚱그려져 로그만 봐서는 원인을 구분할 수 없었다(실기
        /// COM4 무결성체크 재현으로 발견). 호출자가 그 출처를 <paramref name="detail"/>로 명시하도록
        /// 강제한다 — 기본값은 두던 예전 문구를 유지해 테스트 하네스(KeyDownloadTestScenarios 등)의
        /// 인자 없는 호출을 깨지 않는다.
        /// </summary>
        internal static RawReaderCommandResult Timeout(string detail = "응답 대기 시간 초과") =>
            new RawReaderCommandResult(RawReaderCommandKind.Timeout, System.Array.Empty<byte>(), 0, string.Empty, detail);

        internal static RawReaderCommandResult CommunicationError(string detail) =>
            new RawReaderCommandResult(RawReaderCommandKind.CommunicationError, System.Array.Empty<byte>(), 0, string.Empty, detail);

        internal static RawReaderCommandResult DllCallFailure(int dllResult, string dllResultName, string detail) =>
            new RawReaderCommandResult(RawReaderCommandKind.DllCallFailure, System.Array.Empty<byte>(), dllResult, dllResultName, detail);
    }
}
