using System;
using System.IO;
using System.Text;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-3, PRD.md §1.3-a/§1.3-e) <see cref="ILogSink"/>의
/// 유일한 구현 — 기존 <c>FileLogger</c>가 직접 하던 파일 쓰기 로직을 그대로 옮긴 것이다.
///
/// - 기록 위치는 <c>C:\KFTC_PosAgent\KFTCTaxLog\</c>(Phase 22 P22-0)이고, 파일명은 <c>yyyy-MM-dd.log</c>다.
/// - 스레드 안전: Reader CALLBACK 스레드와 UI 스레드가 동시에 기록해도 줄이 섞이지 않도록 프로세스
///   전체에서 하나의 lock으로 직렬화한다(기존 <c>FileLogger</c>와 동일한 전략을 유지).
/// - 파일 열기 모드에 <b>공유 읽기</b>를 허용한다(PRD.md §1.3-e) — 장래 전송 기능이 기록 중인 파일을
///   동시에 읽을 수 있어야 한다. 사실 <see cref="File.AppendAllText(string, string, Encoding)"/>도 내부적으로
///   <c>FileShare.Read</c>를 사용해 열기 때문에 공유 읽기 자체는 원래도 가능했다 — 그럼에도 명시적
///   <see cref="FileStream"/>으로 바꾼 이유는 이 공유 모드가 우연이 아니라 <b>계약</b>임을 코드로 못박기
///   위함이다(향후 누군가 다른 오버로드로 바꾸면서 공유 모드를 실수로 깨뜨리는 것을 방지).
/// - 이 타입 자체는 예외를 삼키지 않는다 — 실패를 무시하는 책임은 파이프라인(<see cref="FileLogger"/>)에
///   있다(장래 다른 <see cref="ILogSink"/> 구현이 실패를 다르게 다루고 싶을 수 있어, 싱크 하나가
///   "무조건 조용히 삼킨다"는 정책을 자기 안에 박아두지 않는다).
/// </summary>
public sealed class FileLogSink : ILogSink
{
    private static readonly object SyncRoot = new();

    // 아래 여러 메서드 공용 — 텍스트가 두 곳에서 갈라지면(예: 오탈자 수정을 한쪽만 하는 실수) 사람이
    // 같은 의미의 구분선을 두 가지 모양으로 보게 된다.
    private const string TransactionEndBoundaryText = "---------------- 거래 종료 ----------------";
    private const string TransactionStartBoundaryText = "---------------- 거래 시작 ----------------";

    public void Write(LogRecord record)
    {
        byte[] bytes = RenderRecordBytes(record);
        lock (SyncRoot)
        {
            AppendLocked(record.Timestamp, bytes);
        }
    }

    /// <summary>
    /// 2026-09-17 사용자 지적으로 신설 — 로그 레코드 기록과 거래 종료 구분선을 **같은 락 안에서 한
    /// 번에** 기록한다(<see cref="ILogSink.WriteThenBoundary"/> 문서 참고 — 두 호출을 따로 하면 그
    /// 사이 틈에 다른 연결의 줄이 끼어들 수 있다는 게 실측으로 확인됨). <paramref name="boundaryLabel"/>은
    /// 구분선 문구에 그대로 실려(예: <c>[127.0.0.1:50251]</c>) 어느 통신의 경계인지 식별하게 한다.
    /// </summary>
    public void WriteThenBoundary(LogRecord record, string boundaryLabel)
    {
        byte[] recordBytes = RenderRecordBytes(record);
        byte[] boundaryBytes = Encoding.UTF8.GetBytes($"{Environment.NewLine}{TransactionEndBoundaryText} [{boundaryLabel}]{Environment.NewLine}");

        lock (SyncRoot)
        {
            AppendLocked(record.Timestamp, recordBytes, boundaryBytes);
        }
    }

    /// <summary>
    /// <see cref="Write"/>/<see cref="WriteThenBoundary"/> 공용 — 레코드 1건을 렌더링해 UTF-8 바이트로
    /// 만든다(파일 I/O는 하지 않는다, 락 밖에서 호출 가능). 부수 효과(<see cref="LogRetentionCleaner"/>
    /// 통지, "처리 종료" 뒤 빈 줄 추가)도 여기서 함께 처리한다.
    /// </summary>
    private static byte[] RenderRecordBytes(LogRecord record)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        // Phase 22(P22-5, PRD.md §1.2) "날짜가 바뀌어 새 로그 파일을 처음 만들 때" 훅. 매 기록마다
        // 호출되지만 실제 정리는 LogRetentionCleaner 내부에서 날짜가 바뀐 경우에만 한 번 트리거된다
        // — 여기서 블로킹 없이 즉시 반환하므로 기록 경로를 지연시키지 않는다.
        LogRetentionCleaner.NotifyLogWritten(record.Timestamp.Date);

        string line = LogLineRenderer.Render(record) + Environment.NewLine;

        // 사용자 요청(2026-09-01) — 거래 구분선. PaymentOrchestrator.ProcessAsync가 거래 1건의 수명
        // 끝에서 남기는 중앙화된 "거래 확정" 로그(P22-6, PAYMENT 카테고리) 뒤에 빈 줄 하나를 추가로
        // 써서, 사람이 파일을 눈으로 볼 때 거래 단위 경계를 알아볼 수 있게 한다.
        //
        // 이 지점을 고른 이유: "거래ID가 바뀔 때마다 구분선"은 거래ID가 아예 없는(레거시 151곳 다수)
        // 줄이 많아 오히려 애매해진다 — 반면 "거래 확정" 로그는 P22-6에서 이미 거래 수명의 끝을
        // 나타내는 유일한 지점으로 확정돼 있으므로(클래스 요약, "모든 분기가 PosResponseTelegram
        // 한 개로 수렴하는 이 지점에서 한 번만 남긴다") 애매함이 없다.
        //
        // 빈 줄은 파싱 정규식(PRD.md §1.3-b, `^\[([^\]]*)\] ...`)에 매치되지 않으므로 장래 서버/분석
        // 도구가 그냥 건너뛰면 된다 — 기계 파싱에 영향을 주지 않는다.
        // Phase 24 후속(2026-09-02 사용자 요청) — 리더기 설정 화면 액션 경계선. 초기화/상태체크/
        // 무결성체크/키다운로드는 메시지 내용이 성공/실패마다 달라 "이게 마지막 줄이다"를 메시지
        // 패턴으로 특정할 수 없다(위 Payment 조건과 달리 고정 문구가 없다) — 그래서
        // ReaderSetupViewModel.LogActionBoundary가 각 동작 끝에 내용과 무관한 고정 문구
        // ("처리 종료")를 UI 카테고리로 한 줄 남기고, 여기서는 그 고정 문구만 보고 판단한다.
        //
        // Phase 27(P27-9-(d)) — "거래 확정" 메시지 매칭 조건은 여기서 제거됐다. 경계는 이제
        // PosSocketServer.HandleConnection의 연결 종료 지점에서 FileLogger.WriteThenBoundary가
        // "연결 종료" 로그와 함께 원자적으로 찍는다(위 클래스의 WriteThenBoundary 참고). Ui
        // 카테고리의 "처리 종료" 조건은 그것과 무관해 그대로 둔다.
        bool appendBlankLineAfter =
            record.Category == LogCategory.Ui
                && record.Message.EndsWith("처리 종료", StringComparison.Ordinal);

        return Encoding.UTF8.GetBytes(appendBlankLineAfter ? line + Environment.NewLine : line);
    }

    /// <summary>
    /// <see cref="SyncRoot"/> 락을 이미 쥔 상태에서만 호출한다 — 오늘 날짜 파일을 한 번만 열어 넘겨받은
    /// 바이트 조각들을 순서대로 이어 쓴다(여러 조각을 한 <see cref="FileStream"/> 수명 안에서 쓰면 그
    /// 사이에 다른 스레드가 끼어들 수 없다 — <see cref="WriteThenBoundary"/>가 원자성을 보장하는 근거).
    /// </summary>
    private static void AppendLocked(DateTime timestamp, params byte[][] chunks)
    {
        string filePath = Path.Combine(LogPaths.LogDirectory, $"{timestamp:yyyy-MM-dd}.log");
        Directory.CreateDirectory(LogPaths.LogDirectory);
        using var stream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        foreach (byte[] chunk in chunks)
        {
            stream.Write(chunk, 0, chunk.Length);
        }
    }

    /// <summary>
    /// 2026-09-17 사용자 지적으로 신설(<see cref="ILogSink.WriteBoundaryThenWrite"/> 문서 참고) —
    /// <see cref="WriteThenBoundary"/>(거래 종료)와 대칭으로 거래 시작 지점에도 구분선을 남기되,
    /// 구분선과 로그 한 건을 같은 락 안에서 원자적으로 함께 기록한다(순서: 구분선 → 로그).
    /// <paramref name="boundaryLabel"/>은 <see cref="WriteThenBoundary"/>와 동일하게 구분선 문구에 실린다.
    /// </summary>
    public void WriteBoundaryThenWrite(LogRecord record, string boundaryLabel)
    {
        byte[] boundaryBytes = Encoding.UTF8.GetBytes($"{Environment.NewLine}{TransactionStartBoundaryText} [{boundaryLabel}]{Environment.NewLine}");
        byte[] recordBytes = RenderRecordBytes(record);

        lock (SyncRoot)
        {
            AppendLocked(record.Timestamp, boundaryBytes, recordBytes);
        }
    }

    /// <summary>
    /// 2026-09-17 사용자 요청 — 앱 프로세스가 새로 뜰 때마다 눈에 띄는 구분선을 남긴다.
    /// <see cref="WriteThenBoundary"/>/<see cref="WriteBoundaryThenWrite"/>(거래 경계)와
    /// 겉모습을 다르게 해(<c>=</c> 기호, 더 김) 사람이 스크롤하며 훑을 때 "거래 경계"와 "새 프로세스가
    /// 시작된 지점"을 한눈에 구별할 수 있게 한다.
    /// </summary>
    public void WriteStartupBanner() =>
        AppendRaw($"{Environment.NewLine}==================== 새 프로세스 시작 ===================={Environment.NewLine}{Environment.NewLine}");

    /// <summary>
    /// <see cref="WriteStartupBanner"/> 전용 — 오늘 날짜 파일에 <see cref="LogRecord"/>/
    /// <c>LogLineRenderer</c>를 거치지 않고 원문 그대로 추가한다(구분선은 애초에 파싱 대상 레코드가
    /// 아니다). <see cref="Write"/>와 같은 <see cref="SyncRoot"/> 락 아래에서 실행해 두 메서드가
    /// 동시에 파일에 쓰지 않도록 한다.
    /// </summary>
    private static void AppendRaw(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        lock (SyncRoot)
        {
            AppendLocked(DateTime.Now, bytes);
        }
    }
}
