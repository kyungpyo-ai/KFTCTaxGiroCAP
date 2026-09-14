using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 27(docs/operations/development_plan.md P27-2/P27-3, PRD.md §1.8.2~§1.8.4) 로그 파일에서
/// "직전 거래 N건부터 로그 끝(EOF)까지"를 슬라이스해 원문 텍스트로 돌려준다. 옛 메모리 링버퍼
/// (<c>LogRingBuffer</c>/<c>RingBufferSink</c>, P27-5에서 제거)를 대체하는 장애정보 확보 경로다.
///
/// <para><b>공개 계약은 <see cref="Collect"/> 하나뿐이다</b>(2026-09-10 재확정) — 최초 설계였던
/// "거래ID 전체 스캔"/"시간창 단독" 독립 API 두 개는 두지 않는다. 거래ID 단독 매칭은 실제 호출부가
/// 없고(옛 링버퍼의 <c>SnapshotByTransactionId()</c>도 같은 이유로 제거된 전례), 시간창 단독 조회는
/// "직전 N건부터 EOF까지"라는 연속 구간 안에 거래ID <c>-</c>인 줄이 자연히 포함되므로 흡수된다
/// (PRD.md §1.8.3-a).</para>
///
/// <para><b>공유 모드(★, PRD §1.8.4-4)</b> — <see cref="FileLogSink"/>는 <c>FileShare.Read</c>(=
/// 남의 읽기만 허용)로 파일을 연다. 이 클래스가 쓰기를 배제하는 모드로 열면 그 순간 로그 기록이
/// (조용히 무시되는 설계상) 증상 없이 실패한다 — 반드시 <see cref="FileShare.ReadWrite"/> |
/// <see cref="FileShare.Delete"/>로 연다. <see cref="FileShare.Delete"/>가 필요한 이유는
/// <see cref="LogRetentionCleaner"/>가 백그라운드에서 같은 파일을 지울 수 있기 때문이다.</para>
///
/// <para><b>뒤에서부터, 줄 경계로만 읽는다</b> — 장애는 항상 최근이라 파일 앞쪽까지 훑지 않는다.
/// 임의 오프셋 <c>Seek</c> 후 바로 디코딩하면 UTF-8 멀티바이트(한글)를 중간에서 쪼갤 위험이 있으므로,
/// LF(<c>0x0A</c>) 바이트로만 줄 경계를 판단한다 — UTF-8 연속 바이트(<c>0x80</c>~<c>0xBF</c>)와 리드
/// 바이트는 모두 최상위 비트가 1이라 <c>0x0A</c>와 절대 겹치지 않으므로, 완성된 줄의 바이트 범위를
/// 모은 뒤에만 한 번에 디코딩하면 문자가 쪼개지지 않는다.</para>
///
/// <para><b>파일명 규칙 공유 판단(P27-2, PRD §1.8.4-2)</b> — <c>yyyy-MM-dd.log</c> 파일명을 이
/// 클래스는 "날짜 → 파일명" 방향으로만 쓴다(<see cref="LogRetentionCleaner.FileNamePattern"/>은
/// 반대 방향인 "파일명 → 날짜"용 정규식이라 그대로 재사용할 수 없다 — 새 정규식이 필요한 게 아니라
/// 서식 문자열 하나만 있으면 된다). PRD.md §1.8.5가 이번 Phase에서 <c>LogPaths.cs</c>를 건드리지
/// 말라고 명시했으므로, 상수를 그쪽으로 승격하지 않고 <see cref="FileLogSink"/>가 이미 쓰는 것과
/// 같은 <c>"{0:yyyy-MM-dd}.log"</c> 리터럴을 이 파일 안에서만 재사용한다(리터럴 자체가 단순해 중복
/// 위험이 낮다).</para>
///
/// <para><b>실패하지 않는다</b> — 파일 부재·권한·삭제 경합 모두 예외를 밖으로 던지지 않고 있는
/// 만큼을 돌려준다(<see cref="Services.Storage.ObservedIdentityStore"/>·<see cref="FileLogger"/>와
/// 같은 계약).</para>
///
/// <para><b>반환은 텍스트 원문이다</b>(PRD §1.8.3-c) — 파싱(<see cref="LogLineParser"/>)은 슬라이스
/// 판정에만 쓰고, 결과 문자열은 파일에 찍힌 원문 줄을 그대로 이어 붙인다. 구조화 객체로 바꾸면 서버가
/// "실제로 뭐라고 찍혔나"를 복원할 수 없다.</para>
/// </summary>
public static class LogFileReader
{
    /// <summary>뒤에서부터 읽을 때 한 번에 당겨오는 바이트 수. <see cref="LogFileReaderTestScenarios"/>가
    /// "청크 경계에 CRLF/멀티바이트가 정확히 걸리는" 합성 파일을 만들 때 같은 값을 써야 하므로
    /// <c>internal</c>이다(값을 하네스에 복제하면 둘이 어긋나도 테스트가 조용히 헛돈다).</summary>
    internal const int BackwardChunkSize = 64 * 1024;

    /// <summary>
    /// "직전 거래 N건부터 로그 끝(EOF)까지" 슬라이스(PRD §1.8.3-a, 2026-09-10 재확정)의 유일한
    /// 공개 진입점.
    ///
    /// <para><b>앵커는 문자열 매칭이 아니라 시각이다</b> — <paramref name="faultDetectedAt"/>보다
    /// 이후 시각인 줄은 건너뛴다(카운트하지 않는다. 단 결과 범위에는 포함될 수 있다). 판정이 결제
    /// 경로 뒤 백그라운드에서 돌고 POS가 다중 접속을 허용하므로, EOF가 항상 장애 거래 자신이라고
    /// 가정할 수 없다.</para>
    ///
    /// <para><paramref name="faultDetectedAt"/> 이하로 내려온 지점부터 <paramref
    /// name="faultTransactionId"/>와 다른 거래ID가 서로 다르게 <paramref name="precedingCount"/>건
    /// 나올 때까지 거슬러 올라간다(거래ID <c>-</c>인 줄은 개수에 세지 않되 결과에는 포함된다). 그
    /// 시작점을 찾으면 <b>거기서부터 실제 파일 끝(EOF)까지 전체</b>를 돌려준다 — 판정 사이에 끼어든
    /// 무관한 거래가 있어도 끊지 않고 그대로 포함한다. <paramref name="maxLineGuard"/>(누적 스캔
    /// 줄수, 오늘+어제 파일 합산)에 먼저 닿으면 <paramref name="precedingCount"/>를 못 채웠어도
    /// 즉시 멈춘다.</para>
    ///
    /// <para><b>다줄 거래 흡수(★, 2026-09-10 리뷰에서 발견·수정)</b> — 실제 거래 하나는 여러 줄에
    /// 걸쳐 찍힌다(요청 수신 → 처리 중 → 거래 확정 등). 뒤에서부터 훑으므로 한 거래의 <b>가장 나중
    /// 줄을 먼저 만나</b> 그 시점에 거래ID가 distinct 집합에 처음 들어가 목표 건수(N)를 채울 수 있는데,
    /// 그 거래의 <b>더 이전 줄들은 아직 더 과거(더 큰 인덱스)에 남아 있다.</b> 목표를 채운 즉시
    /// 멈추면 "N번째(가장 오래된) 직전 거래"의 앞부분이 잘린다 — 설계 의도는 그 거래도 첫 줄부터
    /// 포함하는 것이다. 그래서 목표 달성(<c>targetReached</c>) 이후에도 스캔을 즉시 멈추지 않고,
    /// <b>이미 카운트된 거래ID이거나 <c>-</c>인 줄은 계속 흡수</b>한다. 진짜로 멈추는 조건은 "아직
    /// 카운트되지 않은 새로운(=N+1번째) 실제 거래ID를 만났을 때"이며, 그 줄은 포함하지 않고 그 직전
    /// 줄에서 멈춘다. <paramref name="maxLineGuard"/>는 이 흡수 단계에서도 여전히 절대 상한이다 —
    /// 가드에 먼저 닿으면 흡수 중이던 거래가 완전히 안 잡혀도(앞부분이 잘리더라도) 그 자리에서
    /// 멈춘다.</para>
    /// </summary>
    /// <param name="faultDetectedAt">장애 판정 시각(앵커).</param>
    /// <param name="faultTransactionId">장애 거래 자신의 거래ID. 시스템 레벨 장애는 <c>null</c> 또는
    /// <c>-</c> — 이 경우 제외할 ID가 없으므로 처음 만나는 거래부터 그대로 센다.</param>
    /// <param name="precedingCount">직전 몇 건의 "다른" 거래까지 거슬러 올라갈지(호출부가 정한다 —
    /// 기본값을 이 클래스에 박지 않는다).</param>
    /// <param name="maxLineGuard">누적 스캔 줄수 상한(호출부가 정한다). 앞에 거래가 0건인 상황
    /// (예: 기동 직후 장애)에서 파일 처음까지·어제 파일까지 무한정 거슬러 올라가는 것을 막는다.</param>
    public static string Collect(DateTime faultDetectedAt, string? faultTransactionId, int precedingCount, int maxLineGuard)
    {
        // newestFirst[0] = 파일 끝(EOF)에 가장 가까운 줄, 인덱스가 커질수록 더 과거(오늘 파일을 다
        // 훑으면 이어서 어제 파일 줄이 더 뒤에 쌓인다) — "인덱스 증가 = 시간상 더 과거"가 두 파일에
        // 걸쳐 그대로 유지된다.
        List<string> newestFirst = new();
        HashSet<string> distinctOtherTransactionIds = new(StringComparer.Ordinal);
        int visitedCount = 0;
        int startIndex = -1;
        bool targetReached = false;

        // 시스템 레벨 장애(null 또는 "-")는 제외할 자기 자신 ID가 없다.
        bool hasOwnTransactionId = !string.IsNullOrEmpty(faultTransactionId) && faultTransactionId != "-";

        bool OnLine(string line)
        {
            visitedCount++;
            bool guardHit = visitedCount >= maxLineGuard;

            bool parsed = LogLineParser.TryParse(line, out DateTime timestamp, out string transactionId);
            bool countableId = parsed
                && timestamp <= faultDetectedAt
                && transactionId != "-"
                && !(hasOwnTransactionId && string.Equals(transactionId, faultTransactionId, StringComparison.Ordinal));
            bool isNewDistinctCandidate = countableId && !distinctOtherTransactionIds.Contains(transactionId);

            if (targetReached && isNewDistinctCandidate && !guardHit)
            {
                // 목표 건수를 이미 채운 뒤 아직 카운트되지 않은 새로운(N+1번째) 거래를 만났다 —
                // 이 줄은 포함하지 않고(startIndex는 그대로) 그 직전 줄에서 멈춘다. 가드가 이미
                // 걸린 경우(guardHit)는 절대 상한이 우선이므로 이 분기를 타지 않고 아래에서 그대로
                // 포함시킨 뒤 멈춘다(클래스 요약의 "흡수" 절 참고).
                return false;
            }

            newestFirst.Add(line);
            startIndex = newestFirst.Count - 1;

            if (countableId)
            {
                distinctOtherTransactionIds.Add(transactionId);
                if (!targetReached && distinctOtherTransactionIds.Count >= precedingCount)
                {
                    targetReached = true;
                }
            }

            return !guardHit; // 가드에 닿았으면 목표 미달·다줄 거래 미완성이어도 여기서 멈춘다.
        }

        DateTime today = faultDetectedAt.Date;
        bool reachedStartOfTodayWithoutStop = ReadLinesBackwardFromFile(BuildFilePath(today), OnLine);

        if (!targetReached && reachedStartOfTodayWithoutStop)
        {
            // 오늘 파일을 처음까지 다 읽었는데도 목표를 못 채웠다 — 자정 경계로 어제 파일까지
            // 이어서 훑는다(파일이 없으면 ReadLinesBackwardFromFile이 조용히 아무것도 추가하지 않는다).
            ReadLinesBackwardFromFile(BuildFilePath(today.AddDays(-1)), OnLine);
        }

        if (startIndex < 0)
        {
            // 아무 줄도 못 읽었다(두 파일 모두 없음/빈 파일) — 예외 없이 빈 결과.
            return string.Empty;
        }

        // 시작점(startIndex, 가장 과거)부터 EOF(인덱스 0, 가장 최신)까지 시간순으로 이어 붙인다.
        StringBuilder sb = new();
        for (int i = startIndex; i >= 0; i--)
        {
            if (i != startIndex)
            {
                sb.Append(Environment.NewLine);
            }

            sb.Append(newestFirst[i]);
        }

        return sb.ToString();
    }

    private static string BuildFilePath(DateTime date) =>
        Path.Combine(LogPaths.LogDirectory, $"{date:yyyy-MM-dd}.log");

    /// <summary>
    /// 파일 하나를 끝에서부터 청크 단위로 거슬러 올라가며 완성된 줄을 최신 순으로
    /// <paramref name="onLine"/>에 전달한다. 콜백이 <c>false</c>를 돌려주면 그 시점에서 멈추고
    /// <c>false</c>를 돌려준다(파일을 끝까지 훑지 않았다는 뜻). 콜백이 한 번도 멈추라고 하지 않고
    /// 파일 시작(오프셋 0)까지 도달하면 <c>true</c>를 돌려준다(호출부가 이 값을 보고 자정 경계로
    /// 전날 파일까지 이어서 훑을지 판단한다).
    ///
    /// 파일이 없거나 열기/읽기에 실패하면(권한, <see cref="LogRetentionCleaner"/>와의 삭제 경합 등)
    /// 예외를 던지지 않고 그때까지 읽은 만큼만 콜백한 뒤 <c>true</c>를 돌려준다(더 읽을 것이 없다는
    /// 뜻이므로 자정 경계 이어읽기와 동일하게 다룬다).
    /// </summary>
    private static bool ReadLinesBackwardFromFile(string filePath, Func<string, bool> onLine)
    {
        FileStream? stream;
        try
        {
            // ★ 공유 모드 — FileLogSink의 기록을 막지 않기 위해 ReadWrite|Delete로 연다(클래스 요약 참고).
            stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
        }
        catch
        {
            // 파일 부재·권한·삭제 경합 — 아무 것도 못 읽은 것으로 조용히 처리한다.
            return true;
        }

        try
        {
            long fileLength;
            try
            {
                fileLength = stream.Length;
            }
            catch
            {
                return true;
            }

            long effectiveLength = fileLength;
            if (effectiveLength > 0)
            {
                try
                {
                    // FileLogSink는 Environment.NewLine(Windows에서 "\r\n")을 줄 끝에 붙인다 — LF
                    // 하나만 보고 자르면 CR이 앞줄 내용 끝에 그대로 남아 매 줄마다 보이지 않는 '\r'가
                    // 붙는다. 마지막 최대 2바이트를 확인해 CRLF/LF 종결자를 통째로 잘라낸다.
                    byte[] lastTwo = new byte[2];
                    long readStart = Math.Max(0, effectiveLength - 2);
                    stream.Seek(readStart, SeekOrigin.Begin);
                    int lastReadCount = stream.Read(lastTwo, 0, (int)(effectiveLength - readStart));
                    if (lastReadCount > 0 && lastTwo[lastReadCount - 1] == (byte)'\n')
                    {
                        // 마지막 실제 줄의 종결자(CRLF 또는 LF)일 뿐, "빈 줄"이 아니다 — 처리 대상
                        // 구간에서 아예 제외해 두면 뒤 로직에서 "빈 세그먼트가 종결자인지 진짜 빈
                        // 줄인지" 구분하는 특수 케이스가 사라진다.
                        effectiveLength -= (lastReadCount >= 2 && lastTwo[lastReadCount - 2] == (byte)'\r') ? 2 : 1;
                    }
                }
                catch
                {
                    return true;
                }
            }

            long position = effectiveLength;
            byte[] pendingTail = Array.Empty<byte>();

            // 직전 청크의 첫 바이트가 LF였다는 표시 — 그 LF의 짝인 CR은 이 청크의 마지막 바이트에
            // 있으므로(CRLF가 청크 경계에 정확히 걸린 경우) 앞줄 내용에서 잘라내야 한다. 청크 안에서
            // LF를 만났을 때 하는 end = i - 1 보정(아래)과 같은 일을 청크 경계를 넘어 이어서 한다 —
            // 이 보정이 없으면 경계에 걸린 그 한 줄만 끝에 유령 '\r'가 붙는다(2026-09-14 CP1 리뷰에서
            // 실측 재현·수정).
            bool stripTrailingCrOfPreviousChunk = false;

            while (position > 0)
            {
                int readSize = (int)Math.Min(BackwardChunkSize, position);
                long chunkStart = position - readSize;
                byte[] chunk = new byte[readSize];

                try
                {
                    stream.Seek(chunkStart, SeekOrigin.Begin);
                    int totalRead = 0;
                    while (totalRead < readSize)
                    {
                        int n = stream.Read(chunk, totalRead, readSize - totalRead);
                        if (n <= 0)
                        {
                            break;
                        }

                        totalRead += n;
                    }

                    if (totalRead < readSize)
                    {
                        Array.Resize(ref chunk, totalRead);
                    }
                }
                catch
                {
                    // 읽는 도중 삭제·잘림 등 경합 — 지금까지 읽은 만큼만 인정하고 종료.
                    return true;
                }

                byte[] combined = Concat(chunk, pendingTail);
                int end = combined.Length;

                if (stripTrailingCrOfPreviousChunk)
                {
                    stripTrailingCrOfPreviousChunk = false;
                    if (end > 0 && combined[end - 1] == (byte)'\r')
                    {
                        end--;
                    }
                }

                for (int i = combined.Length - 1; i >= 0; i--)
                {
                    if (combined[i] != (byte)'\n')
                    {
                        continue;
                    }

                    int segmentStart = i + 1;
                    string line = DecodeLine(combined, segmentStart, end - segmentStart);
                    if (!onLine(line))
                    {
                        return false;
                    }

                    // 이 LF 바로 앞이 CR이면(Environment.NewLine == "\r\n") 그 CR은 "지금부터 더
                    // 왼쪽으로 찾을 이전 줄"의 종결자에 속한다 — 다음 세그먼트 추출 시 그 이전 줄의
                    // 내용에 CR이 섞여 들어가지 않도록 경계를 한 칸 당겨 둔다.
                    if (i > 0)
                    {
                        end = combined[i - 1] == (byte)'\r' ? i - 1 : i;
                    }
                    else
                    {
                        // LF가 이 청크의 첫 바이트라 짝인 CR을 여기서는 볼 수 없다 — 판단을 다음(더
                        // 이전) 청크로 미룬다(위 stripTrailingCrOfPreviousChunk 주석 참고).
                        end = 0;
                        stripTrailingCrOfPreviousChunk = true;
                    }
                }

                if (chunkStart == 0)
                {
                    // 파일 시작 — 남은 [0, end) 구간이 파일의 첫 줄(앞에 개행이 없다).
                    if (end > 0)
                    {
                        string firstLine = DecodeLine(combined, 0, end);
                        if (!onLine(firstLine))
                        {
                            // 하필 파일의 첫 줄에서 멈춰야 하는 경우(상한 가드가 정확히 여기서 걸린
                            // 경우 등)에도 "끝까지 훑었다"로 돌려주면, 호출부가 자정 경계로 판단해
                            // 어제 파일에서 한 줄을 더 먹는다 — 가드가 절대 상한이 아니게 된다
                            // (2026-09-14 CP1 리뷰에서 실측 재현·수정).
                            return false;
                        }
                    }

                    return true;
                }

                pendingTail = Slice(combined, 0, end);
                position = chunkStart;
            }

            return true;
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static string DecodeLine(byte[] buffer, int offset, int count) =>
        count <= 0 ? string.Empty : Encoding.UTF8.GetString(buffer, offset, count);

    private static byte[] Concat(byte[] a, byte[] b)
    {
        if (b.Length == 0)
        {
            return a;
        }

        if (a.Length == 0)
        {
            return b;
        }

        byte[] result = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        return result;
    }

    private static byte[] Slice(byte[] source, int start, int end)
    {
        int len = end - start;
        if (len <= 0)
        {
            return Array.Empty<byte>();
        }

        byte[] result = new byte[len];
        Buffer.BlockCopy(source, start, result, 0, len);
        return result;
    }
}
