namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-0/P22-3/P22-5, PRD.md §1.1.1) 로그 파일이
/// 저장되는 디렉터리 경로의 유일한 출처.
///
/// P22-3에서는 이 상수가 <c>FileLogSink</c> 안에만 있었다 — P22-5(90일 보관 정리)가 같은 경로를
/// 필요로 하게 되면서 여기로 뽑아 <see cref="FileLogSink"/>와 <see cref="LogRetentionCleaner"/>가
/// 공용으로 참조한다(문자열 하드코딩 중복 금지).
/// </summary>
public static class LogPaths
{
    /// <summary>로그 파일 저장 디렉터리(<c>yyyy-MM-dd.log</c> 파일들이 여기 쌓인다).</summary>
    public const string LogDirectory = @"C:\KFTC_PosAgent\KFTCTaxLog";
}
