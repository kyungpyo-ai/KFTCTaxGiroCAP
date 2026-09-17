using System;
using System.Globalization;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-0/P22-3/P22-5, PRD.md §1.1.1) 로그 파일이
/// 저장되는 디렉터리 경로와 파일명 규칙의 유일한 출처.
///
/// P22-3에서는 이 상수가 <c>FileLogSink</c> 안에만 있었다 — P22-5(90일 보관 정리)가 같은 경로를
/// 필요로 하게 되면서 여기로 뽑아 <see cref="FileLogSink"/>와 <see cref="LogRetentionCleaner"/>가
/// 공용으로 참조한다(문자열 하드코딩 중복 금지).
///
/// 2026-09-17 사용자 요청 — 파일명을 <c>yyyy-MM-dd.log</c>에서 <c>KFTCTaxCAP{yyMMdd}.log</c>로
/// 바꿨다(예: <c>KFTCTaxCAP260917.log</c>). 리더기 로그(<c>KFTCReaderLog</c>)가 이미 같은
/// "접두어+yyMMdd" 규칙(<c>KFTCReader260917.log</c>)을 쓰고 있어 맞춘 것 — 파일명만으로 어느
/// 프로그램의 로그인지 구분할 수 있다.
/// </summary>
public static class LogPaths
{
    /// <summary>로그 파일 저장 디렉터리(<see cref="BuildFileName"/> 파일들이 여기 쌓인다).</summary>
    public const string LogDirectory = @"C:\KFTC_PosAgent\KFTCTaxLog";

    private const string FileNamePrefix = "KFTCTaxCAP";
    private const string DateFormat = "yyMMdd";

    /// <summary>주어진 날짜의 로그 파일명(디렉터리 제외)을 만든다 — <see cref="FileLogSink"/>와
    /// <see cref="LogFileReader"/>가 "날짜 → 파일명" 방향으로 공유한다.</summary>
    public static string BuildFileName(DateTime date) =>
        $"{FileNamePrefix}{date.ToString(DateFormat, CultureInfo.InvariantCulture)}.log";

    /// <summary><see cref="LogRetentionCleaner"/> 전용 — 파일명(디렉터리 제외)에서 날짜를 파싱한다.
    /// 패턴에 맞지 않으면 <see langword="false"/>(그 파일은 삭제 후보에서 제외된다, PRD.md §1.2
    /// "패턴에 맞지 않는 파일은 건드리지 않는다").</summary>
    public static bool TryParseDateFromFileName(string fileName, out DateTime date)
    {
        date = default;

        if (!fileName.StartsWith(FileNamePrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(".log", StringComparison.Ordinal))
        {
            return false;
        }

        string datePart = fileName.Substring(
            FileNamePrefix.Length,
            fileName.Length - FileNamePrefix.Length - ".log".Length);

        return DateTime.TryParseExact(
            datePart,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
