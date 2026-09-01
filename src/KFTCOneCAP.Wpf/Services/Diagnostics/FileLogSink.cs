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
        string filePath = Path.Combine(LogPaths.LogDirectory, $"{record.Timestamp:yyyy-MM-dd}.log");
        byte[] bytes = Encoding.UTF8.GetBytes(line);

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
