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

    public void Write(LogRecord record)
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
        bool appendBlankLineAfter =
            (record.Category == LogCategory.Payment
                && record.Message.StartsWith("[PaymentOrchestrator] 거래 확정", StringComparison.Ordinal))
            || (record.Category == LogCategory.Ui
                && record.Message.EndsWith("처리 종료", StringComparison.Ordinal));

        string filePath = Path.Combine(LogPaths.LogDirectory, $"{record.Timestamp:yyyy-MM-dd}.log");
        byte[] bytes = Encoding.UTF8.GetBytes(appendBlankLineAfter ? line + Environment.NewLine : line);

        lock (SyncRoot)
        {
            Directory.CreateDirectory(LogPaths.LogDirectory);
            using var stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
