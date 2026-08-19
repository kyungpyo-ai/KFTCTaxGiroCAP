namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>Reader_OpenPort/Reader_ClosePort 호출 결과. DllResult는 ReaderResult(음수 오류코드,
    /// Interop 계층 전용 타입)를 그대로 노출하지 않고 int로 넘긴다 — Services는 Interop의 enum
    /// 타입을 밖으로 드러내지 않는다(계층 규칙, Protocol/Interop은 Services 상위에서 보이지 않아야 함).</summary>
    internal readonly struct ReaderOpenResult
    {
        internal bool Success { get; }
        internal int ReaderId { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }

        internal ReaderOpenResult(bool success, int readerId, int dllResult, string dllResultName)
        {
            Success = success;
            ReaderId = readerId;
            DllResult = dllResult;
            DllResultName = dllResultName;
        }
    }

    /// <summary>Reader_ClosePort 등 readerId만 넘기는 단순 호출의 결과.</summary>
    internal readonly struct ReaderCallResult
    {
        internal bool Success { get; }
        internal int DllResult { get; }
        internal string DllResultName { get; }

        internal ReaderCallResult(bool success, int dllResult, string dllResultName)
        {
            Success = success;
            DllResult = dllResult;
            DllResultName = dllResultName;
        }
    }
}
