using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 27(docs/operations/development_plan.md P27-4, PRD.md §1.8.4) <see cref="LogFileReader"/>
/// (P27-2/P27-3)와 <see cref="LogLineParser"/>(P27-1) 검증 전용 진단 하네스. <c>App.xaml.cs</c>가
/// <c>--log-file-reader-test</c> 인자로 실행될 때만 <see cref="RunAll"/>을 호출한다.
///
/// <para><b>자기참조 금지(★)</b> — 이 하네스가 <c>[log-file-reader-test][OK]</c>/<c>[FAIL]</c>로
/// 남기는 판정 로그 자체도 결국 로그 파일에 쌓이지만, 그 판정 로그를 <see cref="LogFileReader"/>로
/// 되읽어 스스로를 검증하는 일은 하지 않는다(순환 검증). 대신 <see cref="LogFileReader"/>는
/// (1) 이 클래스가 직접 만든 합성 로그 파일(과거 날짜, 실제 운영 로그와 겹치지 않음)을 대상으로
/// 호출해 반환된 문자열을 기대값과 그대로 비교하거나, (2) 동시성 검증(#6)에서는 오늘자 실제 로그
/// 파일에 대해 호출하되 그 성공 판정은 <see cref="LogFileReader"/>가 아니라 별도의 원시 파일 읽기로
/// 한다.</para>
///
/// <para>합성 로그 파일은 <see cref="LogPaths.LogDirectory"/> 아래 2005년 날짜(운영 로그와 절대
/// 겹치지 않는 과거)로 만들고, 실행이 끝나면 전부 삭제한다(완료 조건 "임시 파일 정리").</para>
/// </summary>
internal static class LogFileReaderTestScenarios
{
    private static int _passCount;
    private static int _failCount;
    private static readonly List<string> CreatedFiles = new();

    public static void RunAll()
    {
        try
        {
            FileLogger.Info("[log-file-reader-test] Phase 27 P27-4 검증 시작(Collect 재설계, 2026-09-10)");

            Scenario1_SkipsIntervalTransactionsAfterFaultAndCountsPrecedingCorrectly();
            Scenario2_IncludesDashTransactionIdLinesAndStopsAtMaxLineGuard();
            Scenario3_BlankLineWithinSliceIsIncluded();
            Scenario4_MidnightBoundary();
            Scenario5_MissingFileReturnsEmptyWithoutException();
            Scenario6_ConcurrentWriteDuringReadDoesNotFailLogging();
            Scenario7_TransactionIdFallbackOver12CharsMatchesExactly();
            Scenario8_KoreanMessageNotCorrupted();
            Scenario9_ChunkBoundaryDoesNotCorruptLines();
            Scenario10_MaxLineGuardIsAbsoluteAcrossMidnight();

            FileLogger.Info($"[log-file-reader-test] 완료 — 통과 {_passCount}건, 실패 {_failCount}건");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[log-file-reader-test] 하네스 자체 예외로 중단: {ex}");
        }
        finally
        {
            CleanupCreatedFiles();
        }
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            _passCount++;
            FileLogger.Info($"[log-file-reader-test][OK] {name}");
        }
        else
        {
            _failCount++;
            FileLogger.Error($"[log-file-reader-test][FAIL] {name}");
        }
    }

    // ---- 합성 로그 파일 빌더 -------------------------------------------------------------

    private static LogRecord Rec(DateTime ts, LogCategory category, string? transactionId, string message) =>
        new(ts, LogLevel.Info, category, code: null, transactionId, message);

    private static string Render(LogRecord record) => LogLineRenderer.Render(record);

    /// <summary>지정한 날짜의 로그 파일을 원문 줄 목록으로 새로 만든다(있으면 덮어씀). 실제
    /// <see cref="FileLogSink"/>와 같은 인코딩(BOM 없는 UTF-8)으로 쓴다.</summary>
    private static string WriteLinesToFile(DateTime date, IReadOnlyList<string> lines)
    {
        Directory.CreateDirectory(LogPaths.LogDirectory);
        string path = Path.Combine(LogPaths.LogDirectory, $"{date:yyyy-MM-dd}.log");
        string content = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        CreatedFiles.Add(path);
        return path;
    }

    private static void CleanupCreatedFiles()
    {
        foreach (string path in CreatedFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 정리 실패는 하네스 판정에 영향을 주지 않는다 — 다음 실행이 같은 날짜를 다시 덮어쓴다.
            }
        }

        CreatedFiles.Clear();
    }

    // ---- 시나리오 --------------------------------------------------------------------------

    /// <summary>#1 — 장애 시각 이후에 끼어든 무관한 거래를 카운트에서 건너뛰고, 그 이하 시각부터
    /// "다른" 거래 직전 N건을 정확히 센다. 이번 재설계의 핵심 동작(끼어든 거래는 카운트 제외되지만
    /// 최종 결과에는 EOF까지 그대로 포함돼야 한다)을 명시적으로 검증한다.
    ///
    /// <b>다줄 거래 흡수(2026-09-10 리뷰 발견 버그 회귀 방지)</b> — 직전 N건 중 가장 오래된(N번째)
    /// 거래(<c>p2</c>, TXBBB000002)를 일부러 2줄짜리 다줄 거래로 만든다. 뒤에서부터 훑으면 이
    /// 거래의 <b>나중 줄("완료")을 먼저 만나</b> 그 시점에 목표 건수(N=2)를 채우는데, 예전 버그는
    /// 그 즉시 멈춰 이 거래의 <b>첫 줄("요청 수신")을 결과에서 잘라냈다.</b> 이 시나리오는 그 첫
    /// 줄까지 결과에 포함되는지를 명시적으로 검증한다.</summary>
    private static void Scenario1_SkipsIntervalTransactionsAfterFaultAndCountsPrecedingCorrectly()
    {
        DateTime date = new(2005, 6, 10);
        // 장애 거래 이전 거래 3건(직전 N=2로 요청할 것이므로 그 중 뒤 2건만 포함돼야 한다).
        LogRecord p1 = Rec(date.AddHours(10), LogCategory.Pos, "TXAAA000001", "TXA 요청 수신");
        // p2 = 직전 N건 중 가장 오래된(N번째) 거래 — 2줄짜리 다줄 거래로 만들어 "첫 줄까지 포함"을 검증한다.
        LogRecord p2First = Rec(date.AddHours(10).AddSeconds(1), LogCategory.Reader, "TXBBB000002", "TXB 요청 수신");
        LogRecord p2Last = Rec(date.AddHours(10).AddSeconds(1.5), LogCategory.Reader, "TXBBB000002", "TXB 처리 완료");
        LogRecord p3 = Rec(date.AddHours(10).AddSeconds(2), LogCategory.Payment, "TXCCC000003", "TXC 요청 수신");
        // 장애 거래 자신.
        LogRecord fault = Rec(date.AddHours(10).AddSeconds(3), LogCategory.Payment, "TXFFF000009", "TXF 장애 발생");
        // 장애 시각 이후에 끼어든 무관한 거래(카운트 제외 대상, 결과에는 포함돼야 함).
        LogRecord interval = Rec(date.AddHours(10).AddSeconds(4), LogCategory.Pos, "TXZZZ000099", "장애 시각 이후 끼어든 무관 거래");

        WriteLinesToFile(date, new[] { Render(p1), Render(p2First), Render(p2Last), Render(p3), Render(fault), Render(interval) });

        DateTime faultDetectedAt = fault.Timestamp;
        string actual = LogFileReader.Collect(faultDetectedAt, "TXFFF000009", precedingCount: 2, maxLineGuard: 200);

        // 직전 N=2건은 시각순으로 거슬러 올라가며 만나는 "다른" 거래 2건 = TXCCC000003, TXBBB000002.
        // TXBBB000002는 다줄 거래이므로 그 첫 줄(p2First)까지 흡수돼 시작점이 된다. 거기서부터
        // EOF(끼어든 interval 포함)까지 전체가 결과.
        string expected = string.Join(
            Environment.NewLine,
            new[] { Render(p2First), Render(p2Last), Render(p3), Render(fault), Render(interval) });

        Check("#1 장애 시각 이후 끼어든 거래는 카운트 제외되지만 결과(EOF까지)에는 포함된다", actual == expected);

        bool p1Excluded = actual.Length > 0 && !actual.Contains(Render(p1));
        Check("#1 직전 N건을 정확히 세어 그보다 이전 거래(p1)는 결과에서 제외된다", p1Excluded);

        bool multiLineFirstLineIncluded = actual.StartsWith(Render(p2First), StringComparison.Ordinal);
        Check("#1(★) 다줄 거래(N번째)의 첫 줄까지 결과에 포함된다(뒤에서부터 훑다가 나중 줄에서 목표를 채워도 앞부분이 안 잘림)", multiLineFirstLineIncluded);
    }

    /// <summary>#2 — 결과가 거래ID '-'인 줄(앱 기동 등)까지 포함하고, maxLineGuard에 닿으면
    /// precedingCount 미달이어도 즉시 멈춘다.</summary>
    private static void Scenario2_IncludesDashTransactionIdLinesAndStopsAtMaxLineGuard()
    {
        DateTime date = new(2005, 6, 12);
        LogRecord boot1 = Rec(date.AddHours(9), LogCategory.App, null, "애플리케이션 기동 시작");
        LogRecord boot2 = Rec(date.AddHours(9).AddSeconds(1), LogCategory.Reader, null, "DLL 로드 스모크 성공");
        LogRecord only = Rec(date.AddHours(9).AddSeconds(2), LogCategory.Payment, "TXCCC000003", "유일 거래 처리");

        WriteLinesToFile(date, new[] { Render(boot1), Render(boot2), Render(only) });

        // 시스템 레벨 장애(거래ID 없음) — 제외할 자기 자신 ID가 없으므로 처음 만나는 거래부터 그대로 센다.
        // precedingCount=1이면 TXCCC000003 한 건만으로 목표 달성 → boot1/boot2를 포함해 시작점을 더
        // 거슬러 올라가지 않지만, "결과에는 '-'인 줄도 포함" 요건을 보려면 시작점을 boot1까지 밀어야
        // 하므로 precedingCount를 넉넉히(3) 주고 실제 거래는 1건뿐이라 파일 처음까지 도달하게 한다.
        string actual = LogFileReader.Collect(only.Timestamp, faultTransactionId: null, precedingCount: 3, maxLineGuard: 200);
        string expected = string.Join(Environment.NewLine, new[] { Render(boot1), Render(boot2), Render(only) });

        Check("#2a 거래 1건뿐이라도 파일 처음까지 거슬러 올라가고 '-'인 줄까지 결과에 포함된다", actual == expected);

        // maxLineGuard=2로 주면 목표(3건)를 못 채워도 2줄째에서 멈춘다 — 뒤(최신)에서부터 2줄만
        // 스캔했으므로 boot2, only 두 줄만 결과에 남는다(boot1은 가드에 막혀 더 못 감).
        string guarded = LogFileReader.Collect(only.Timestamp, faultTransactionId: null, precedingCount: 3, maxLineGuard: 2);
        string guardedExpected = string.Join(Environment.NewLine, new[] { Render(boot2), Render(only) });

        Check("#2b maxLineGuard에 닿으면 precedingCount 미달이어도 즉시 멈춘다", guarded == guardedExpected);
    }

    /// <summary>#3 — 빈 줄에서 파서가 죽지 않고, 슬라이스 구간 안에 있는 빈 줄은 결과 텍스트에
    /// 그대로 포함된다.</summary>
    private static void Scenario3_BlankLineWithinSliceIsIncluded()
    {
        DateTime date = new(2005, 6, 14);
        LogRecord before = Rec(date.AddHours(11), LogCategory.Payment, "TXBBB000002", "이전 거래");
        LogRecord confirmed = Rec(date.AddHours(11).AddSeconds(1), LogCategory.Payment, "TXDDD000004", "[PaymentOrchestrator] 거래 확정 - 성공");
        LogRecord followUp = Rec(date.AddHours(11).AddSeconds(2), LogCategory.Payment, "TXDDD000004", "후속 로그");

        WriteLinesToFile(
            date,
            new[]
            {
                Render(before),
                string.Empty, // 이전 거래 뒤 구분용 빈 줄
                Render(confirmed),
                string.Empty, // 거래 확정 뒤 구분용 빈 줄(FileLogSink 계약) — 슬라이스 구간 안이므로 포함돼야 함
                Render(followUp),
            });

        // 장애 거래 = TXDDD000004(confirmed/followUp), precedingCount=1 → 직전 다른 거래(TXBBB000002)
        // 하나가 나올 때까지 거슬러 올라간다. 시작점은 before 줄이고, 그 사이 빈 줄들은 전부 결과에 포함.
        string actual = LogFileReader.Collect(followUp.Timestamp, "TXDDD000004", precedingCount: 1, maxLineGuard: 200);
        string expected = string.Join(
            Environment.NewLine,
            new[] { Render(before), string.Empty, Render(confirmed), string.Empty, Render(followUp) });

        Check("#3 슬라이스 구간 안의 빈 줄은 예외 없이 결과에 그대로 포함된다", actual == expected);
    }

    /// <summary>#4 — 자정 경계에서 어제 파일까지 읽고, 없으면 조용히 건너뛴다.</summary>
    private static void Scenario4_MidnightBoundary()
    {
        DateTime today = new(2005, 6, 20);
        DateTime yesterday = today.AddDays(-1);

        LogRecord yPrev = Rec(yesterday.AddHours(23).AddMinutes(58), LogCategory.Payment, "TXYYY000007", "어제 이전 거래");
        LogRecord tOnly = Rec(today.AddSeconds(0.5), LogCategory.Payment, "TXTTT000008", "오늘 유일 거래");

        WriteLinesToFile(yesterday, new[] { Render(yPrev) });
        WriteLinesToFile(today, new[] { Render(tOnly) });

        // 오늘 파일에는 tOnly 하나뿐이라 precedingCount=1을 채우려면 자정을 넘어 어제 파일까지 가야 한다.
        string actual = LogFileReader.Collect(tOnly.Timestamp, "TXTTT000008", precedingCount: 1, maxLineGuard: 200);
        string expected = string.Join(Environment.NewLine, new[] { Render(yPrev), Render(tOnly) });

        Check("#4a 자정 경계 — 어제 파일까지 거슬러 올라가 시간순으로 합쳐진다", actual == expected);

        // 어제 파일이 아예 없는 경우 — 조용히 건너뛰고 예외 없이 오늘 몫만(파일 전체) 돌려준다.
        DateTime todayNoYesterday = new(2005, 6, 30);
        LogRecord onlyToday = Rec(todayNoYesterday.AddSeconds(30), LogCategory.Payment, "TXNNN000009", "어제 파일 없음 케이스");
        WriteLinesToFile(todayNoYesterday, new[] { Render(onlyToday) });

        bool threw = false;
        string actual2 = string.Empty;
        try
        {
            actual2 = LogFileReader.Collect(onlyToday.Timestamp, "TXNNN000009", precedingCount: 1, maxLineGuard: 200);
        }
        catch
        {
            threw = true;
        }

        Check("#4b 자정 경계 — 어제 파일 없으면 예외 없이 조용히 건너뛰고 있는 만큼만 돌려준다", !threw && actual2 == Render(onlyToday));
    }

    /// <summary>#5 — 파일 부재·읽기 실패 시 예외 없이 빈 결과.</summary>
    private static void Scenario5_MissingFileReturnsEmptyWithoutException()
    {
        DateTime missingDate = new(2005, 7, 5);
        bool threw = false;
        string actual = "존재하지않음";
        try
        {
            actual = LogFileReader.Collect(missingDate.AddHours(12), "NOSUCHTX0001", precedingCount: 3, maxLineGuard: 200);
        }
        catch
        {
            threw = true;
        }

        Check("#5 파일 부재 — 예외 없이 빈 문자열", !threw && actual == string.Empty);
    }

    /// <summary>#6(★) — 기록 중 동시 읽기가 로그 기록을 실패시키지 않는다(실제 동시 실행 검증).
    /// 앱이 이미 배선돼 있어 <see cref="FileLogSink"/>가 오늘자 파일을 잡고 있는 상태에서 돈다.</summary>
    private static void Scenario6_ConcurrentWriteDuringReadDoesNotFailLogging()
    {
        string marker = "LOGFILEREADERTEST_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        const int writeCount = 200;
        bool readerThrew = false;

        var writer = new Thread(() =>
        {
            for (int i = 0; i < writeCount; i++)
            {
                FileLogger.Info(LogCategory.App, $"{marker} 동시성 검증 {i}");
            }
        });

        var reader = new Thread(() =>
        {
            try
            {
                DateTime now = DateTime.Now;
                for (int i = 0; i < 50; i++)
                {
                    LogFileReader.Collect(now, faultTransactionId: null, precedingCount: 3, maxLineGuard: 200);
                }
            }
            catch
            {
                readerThrew = true;
            }
        });

        writer.Start();
        reader.Start();
        writer.Join();
        reader.Join();

        // 판정은 LogFileReader가 아니라 원시 파일 읽기로 한다(자기참조 금지 — 클래스 요약 참고).
        int foundCount = CountMarkerLinesInTodayFileRaw(marker);

        Check("#6 동시 기록/읽기 — 읽는 동안 예외 없음", !readerThrew);
        Check("#6 동시 기록/읽기 — 기록된 줄이 실제로 파일에 전부 남음", foundCount == writeCount);
    }

    private static int CountMarkerLinesInTodayFileRaw(string marker)
    {
        string path = Path.Combine(LogPaths.LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            int count = 0;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Contains(marker))
                {
                    count++;
                }
            }

            return count;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>#7 — 거래ID fallback 값(12자 초과)도 정확히 매칭·카운트된다.</summary>
    private static void Scenario7_TransactionIdFallbackOver12CharsMatchesExactly()
    {
        DateTime date = new(2005, 6, 16);
        const string fallbackId = "902614-NOID-A1B2C3D4E5F6"; // PaymentOrchestrator.LogTxId fallback 형태, 12자 초과
        LogRecord other = Rec(date.AddHours(12), LogCategory.Payment, "TXOTHER00001", "무관 거래");
        LogRecord f1 = Rec(date.AddHours(12).AddSeconds(1), LogCategory.Pos, fallbackId, "비정상 요청 처리");
        LogRecord f2 = Rec(date.AddHours(12).AddSeconds(2), LogCategory.Payment, fallbackId, "비정상 요청 완료");

        WriteLinesToFile(date, new[] { Render(other), Render(f1), Render(f2) });

        // 장애 거래 자신 = fallbackId. precedingCount=1이면 자신과 다른 TXOTHER00001 한 건이 나올
        // 때까지 거슬러 올라간다 — fallbackId가 자기 자신과 정확히 길이 무관하게 매칭돼 카운트에서
        // 제외되는지가 핵심이다(잘못 비교하면 f1도 "다른 거래"로 잘못 세어 시작점이 달라진다).
        string actual = LogFileReader.Collect(f2.Timestamp, fallbackId, precedingCount: 1, maxLineGuard: 200);
        string expected = string.Join(Environment.NewLine, new[] { Render(other), Render(f1), Render(f2) });

        Check("#7 거래ID fallback(12자 초과) — 길이 가정 없이 값으로 정확히 매칭·자기 자신 제외", fallbackId.Length > 12 && actual == expected);
    }

    /// <summary>#8 — 한글 메시지가 깨지지 않는다.</summary>
    private static void Scenario8_KoreanMessageNotCorrupted()
    {
        DateTime date = new(2005, 6, 18);
        LogRecord k1 = Rec(
            date.AddHours(13),
            LogCategory.Payment,
            "TXKKK000008",
            "카드리딩 성공 — 결제 확정: 금액 12,000원, 승인번호 998877");

        WriteLinesToFile(date, new[] { Render(k1) });

        string expected = Render(k1);
        string actual = LogFileReader.Collect(k1.Timestamp, "TXKKK000008", precedingCount: 3, maxLineGuard: 200);

        Check("#8 한글 메시지 — 슬라이스 결과가 원문과 바이트 단위로 동일", actual == expected);
    }

    /// <summary>#9(★, 2026-09-14 CP1 리뷰 회귀 방지) — 파일이 <see cref="LogFileReader.BackwardChunkSize"/>를
    /// 넘어 여러 청크로 나뉘고, 하필 <b>CRLF의 LF 바이트가 청크 경계에 정확히 걸리는</b> 파일에서도
    /// 줄이 깨지지 않는다.
    ///
    /// <para>이 정렬을 우연에 맡기지 않고 계산해서 만든다 — 마지막 줄 길이를 d바이트 늘리면 (파일이
    /// 커진 만큼) 청크 경계가 d바이트 뒤로 밀리므로, d를 "경계 ~ 그 뒤 첫 LF" 거리로 잡으면 경계가
    /// 정확히 그 LF에 얹힌다. 수정 전 구현은 이 케이스에서 경계에 걸린 한 줄 끝에만 유령 <c>\r</c>가
    /// 붙어(다른 줄은 멀쩡해) 눈으로는 거의 발견되지 않았다. 같은 파일이 UTF-8 멀티바이트(한글)를
    /// 경계에 걸치게도 하므로 §1.8.4 #5의 극단 케이스도 함께 덮는다.</para></summary>
    private static void Scenario9_ChunkBoundaryDoesNotCorruptLines()
    {
        DateTime date = new(2005, 8, 10);
        List<string> lines = new();
        for (int i = 0; i < 2000; i++)
        {
            // 거래ID를 전부 '-'로 두어 카운트가 차지 않게 한다(파일 전체를 훑게 만들어 모든 청크 경계를 지난다).
            lines.Add(Render(Rec(date.AddHours(1).AddMilliseconds(i), LogCategory.Payment, null, $"라인 {i:D6} 한글패딩가나다라마바사")));
        }

        string path = WriteLinesToFile(date, lines);
        byte[] bytes = File.ReadAllBytes(path);
        long effectiveLength = bytes.Length - Environment.NewLine.Length; // 리더가 잘라내는 마지막 종결자
        long chunkStart = effectiveLength - LogFileReader.BackwardChunkSize;
        if (chunkStart <= 0)
        {
            Check("#9 준비 — 합성 파일이 청크 크기를 넘지 못함(시나리오 무효)", false);
            return;
        }

        long firstLfAtOrAfterBoundary = -1;
        for (long i = chunkStart; i < effectiveLength; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                firstLfAtOrAfterBoundary = i;
                break;
            }
        }

        int pad = (int)(firstLfAtOrAfterBoundary - chunkStart);
        lines[lines.Count - 1] += new string('x', pad);
        WriteLinesToFile(date, lines);

        byte[] aligned = File.ReadAllBytes(path);
        long alignedBoundary = aligned.Length - Environment.NewLine.Length - LogFileReader.BackwardChunkSize;
        Check(
            "#9 준비 — 청크 경계가 CRLF의 LF 바이트에 정확히 얹혔다",
            alignedBoundary > 0 && aligned[alignedBoundary] == (byte)'\n' && aligned[alignedBoundary - 1] == (byte)'\r');

        string actual = LogFileReader.Collect(date.AddHours(23), faultTransactionId: null, precedingCount: 5, maxLineGuard: 100000);
        string expected = string.Join(Environment.NewLine, lines);

        Check("#9(★) 청크 경계에 걸린 CRLF/멀티바이트 — 슬라이스 결과가 원문 전체와 정확히 일치", actual == expected);

        bool anyGhostCarriageReturn = false;
        foreach (string line in actual.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
        {
            if (line.EndsWith("\r", StringComparison.Ordinal))
            {
                anyGhostCarriageReturn = true;
                break;
            }
        }

        Check("#9(★) 어떤 줄도 유령 '\\r'로 끝나지 않는다", !anyGhostCarriageReturn);
    }

    /// <summary>#10(★, 2026-09-14 CP1 리뷰 회귀 방지) — <c>maxLineGuard</c>가 <b>자정 경계를 넘어서도</b>
    /// 절대 상한으로 작동한다. 오늘 파일의 줄 수가 가드와 정확히 같으면 "가드에 걸려 멈춘 것"과
    /// "파일을 끝까지 다 읽은 것"이 같은 지점에서 겹치는데, 수정 전 구현은 파일 첫 줄에서의 정지
    /// 신호를 흘려버려 "끝까지 읽었다"로 보고했고 호출부가 어제 파일을 열어 <b>가드+1줄</b>을
    /// 돌려줬다.</summary>
    private static void Scenario10_MaxLineGuardIsAbsoluteAcrossMidnight()
    {
        DateTime today = new(2005, 9, 10);
        DateTime yesterday = today.AddDays(-1);

        List<string> yesterdayLines = new();
        for (int i = 0; i < 5; i++)
        {
            yesterdayLines.Add(Render(Rec(yesterday.AddHours(20).AddSeconds(i), LogCategory.Payment, $"TXY{i:D9}", $"어제 거래 {i}")));
        }

        List<string> todayLines = new();
        for (int i = 0; i < 5; i++)
        {
            // 전부 거래ID '-' — precedingCount를 절대 채울 수 없어 가드만이 정지 조건이 된다.
            todayLines.Add(Render(Rec(today.AddHours(1).AddSeconds(i), LogCategory.App, null, $"오늘 시스템 로그 {i}")));
        }

        WriteLinesToFile(yesterday, yesterdayLines);
        WriteLinesToFile(today, todayLines);

        string actual = LogFileReader.Collect(today.AddHours(2), faultTransactionId: null, precedingCount: 3, maxLineGuard: 5);
        string expected = string.Join(Environment.NewLine, todayLines);

        Check("#10(★) maxLineGuard는 자정 경계를 넘어서도 절대 상한 — 어제 파일에서 한 줄도 더 먹지 않는다", actual == expected);
    }
}
