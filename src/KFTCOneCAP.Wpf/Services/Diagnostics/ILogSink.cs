using System;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-3, PRD.md §1.3-a) 로그 한 건을 받는 최소 인터페이스.
///
/// 현재 유일한 구현은 <see cref="FileLogSink"/>다. 장래 원격 싱크는 이 인터페이스로 병렬 추가한다
/// (파일 기록은 원격 전송 성공 여부와 무관하게 항상 수행돼야 하므로, 싱크는 서로 독립적으로 호출된다
/// — 호출 순서/실패 격리 책임은 이 인터페이스의 구현이 아니라 호출자(<see cref="FileLogger"/> 파이프라인)에
/// 있다).
/// </summary>
public interface ILogSink
{
    /// <summary>
    /// 로그 한 건을 기록한다. 구현은 <b>즉시 반환해야 한다</b> — <c>FileLogger.Dispatch</c>가 호출자
    /// 스레드(리더기 CALLBACK 스레드나 결제 오케스트레이터 스레드일 수 있다)에서 등록된 싱크들을
    /// 순차 동기 실행하므로, 여기서 블로킹하면 그 호출자 스레드 전체가 지연된다. I/O가 필요한 원격
    /// 싱크는 이 메서드 안에서 직접 I/O를 수행하지 말고 자체 큐에 넣은 뒤 백그라운드 스레드에서
    /// 처리해야 한다.
    /// </summary>
    void Write(LogRecord record);
}
