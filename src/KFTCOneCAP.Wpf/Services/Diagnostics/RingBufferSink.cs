namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-4, PRD.md §1.3-d) <see cref="ILogSink"/> 구현.
///
/// 저장소(<see cref="LogRingBuffer"/>)와 파이프라인 어댑터(이 타입)를 분리했다 — 조회 API는 장래
/// 장애 보고 기능처럼 <see cref="FileLogger"/> 파이프라인을 모르는 코드에서도 써야 하므로
/// <see cref="LogRingBuffer"/>의 정적 메서드로 직접 노출하고, 이 타입은 "기록만" 한다.
///
/// <see cref="ILogSink.Write"/> 계약대로 <see cref="LogRingBuffer.Add"/>는 짧은 lock 하나로 끝나
/// 즉시 반환한다(블로킹 금지).
/// </summary>
public sealed class RingBufferSink : ILogSink
{
    public void Write(LogRecord record)
    {
        LogRingBuffer.Add(record);
    }
}
