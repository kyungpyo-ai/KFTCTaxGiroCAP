using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-5, PRD.md §1.2) 90일 보관 정리.
///
/// - 삭제 기준은 <b>파일명(<c>yyyy-MM-dd.log</c>)에서 파싱한 날짜</b>다. <c>LastWriteTime</c>은
///   복사·백업으로 바뀔 수 있어 쓰지 않는다(PRD.md §1.2).
/// - 파일명 패턴에 맞지 않는 파일(사용자가 넣어둔 파일 등)은 건드리지 않는다.
/// - 실행 시점은 둘 — 앱 기동 시 1회(<see cref="RunAtStartup"/>, <c>App.xaml.cs</c> <c>OnStartup</c>)와
///   날짜가 바뀌어 새 로그 파일을 처음 쓸 때(<see cref="NotifyLogWritten"/>, <see cref="FileLogSink"/>가
///   매 기록마다 호출하지만 실제 정리는 마지막으로 트리거된 날짜와 다를 때만 한 번 돈다). 둘 다
///   <see cref="TriggerIfNeeded"/>로 합류하므로 같은 날짜에 대해 중복 실행되지 않는다.
/// - 정리 자체는 <see cref="Task.Run(Action)"/>으로 백그라운드 스레드에서 수행한다 — 호출자(기동
///   경로, 로그 기록 경로)를 블로킹하지 않는다.
/// - 파일 삭제 실패(다른 프로세스가 잠근 경우 등)는 그 파일만 조용히 건너뛰고 다음 기회에 다시
///   시도한다 — 정리 작업 전체가 예외로 죽지 않는다.
/// - 정리 결과를 <see cref="LogCategory.App"/> 카테고리로 한 줄 남긴다(PRD.md §1.3-c 예시).
/// </summary>
public static class LogRetentionCleaner
{
    /// <summary>보관 기간(일). 상수로 한 곳에 둔다(장래 설정화면 노출 가능성 대비, PRD.md §1.2).</summary>
    public const int RetentionDays = 90;

    // yyyy-MM-dd.log 형태만 삭제 대상 후보로 삼는다. 패턴에 맞지 않으면 정규식 자체가 매치하지 않아
    // 자동으로 "건드리지 않는다" 규칙을 만족한다.
    private static readonly Regex FileNamePattern = new(@"^(\d{4}-\d{2}-\d{2})\.log$", RegexOptions.Compiled);

    private static readonly object TriggerSync = new();

    // 마지막으로 정리를 "트리거"한 날짜. 앱 기동 1회 + 날짜 전환마다의 호출이 모두 이 값을 거쳐
    // 같은 날짜에 대해 한 번만 실제로 실행되도록 합류한다.
    private static DateTime? _lastTriggeredDate;

    /// <summary>앱 기동 시 1회 호출한다(<c>App.xaml.cs</c> <c>OnStartup</c>).</summary>
    public static void RunAtStartup()
    {
        TriggerIfNeeded(DateTime.Now.Date);
    }

    /// <summary>
    /// 로그 한 줄이 기록될 때마다 호출된다(<see cref="FileLogSink.Write"/>). 실제로는 그 기록의
    /// 날짜가 마지막으로 정리를 트리거한 날짜와 다를 때만 백그라운드 정리를 새로 시작한다 — 매
    /// 호출이 정리를 도는 것이 아니라 "날짜가 바뀐 순간"만 걸린다.
    /// </summary>
    public static void NotifyLogWritten(DateTime recordDate)
    {
        TriggerIfNeeded(recordDate.Date);
    }

    private static void TriggerIfNeeded(DateTime date)
    {
        lock (TriggerSync)
        {
            if (_lastTriggeredDate == date)
            {
                return;
            }

            _lastTriggeredDate = date;
        }

        Task.Run(() => RunCore(date));
    }

    private static void RunCore(DateTime today)
    {
        try
        {
            string directory = LogPaths.LogDirectory;
            if (!Directory.Exists(directory))
            {
                return;
            }

            DateTime cutoff = today.AddDays(-RetentionDays);
            int deletedCount = 0;

            foreach (string filePath in Directory.EnumerateFiles(directory, "*.log"))
            {
                string fileName = Path.GetFileName(filePath);
                Match match = FileNamePattern.Match(fileName);
                if (!match.Success)
                {
                    continue;
                }

                if (!DateTime.TryParseExact(
                        match.Groups[1].Value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime fileDate))
                {
                    continue;
                }

                if (fileDate.Date >= cutoff.Date)
                {
                    continue;
                }

                try
                {
                    File.Delete(filePath);
                    deletedCount++;
                }
                catch
                {
                    // 잠긴 파일 등 실패는 조용히 무시한다 — 다음 트리거(다음 날짜 전환 또는 다음
                    // 기동) 때 다시 시도된다.
                }
            }

            FileLogger.Info(LogCategory.App, $"로그 정리 — {RetentionDays}일 초과 {deletedCount}건 삭제");
        }
        catch
        {
            // 정리 작업 자체의 실패(권한 문제 등)가 앱 동작에 영향을 주면 안 된다.
        }
    }
}
