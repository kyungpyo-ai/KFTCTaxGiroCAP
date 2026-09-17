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

    /// <summary>
    /// Phase 27(docs/operations/development_plan.md P27-9-(d)) — 로그 한 건(<paramref name="record"/>)과
    /// 거래 종료 구분선을 **같은 락 안에서 원자적으로** 함께 기록한다. 처음엔 <c>Write</c> 한 번과
    /// 구분선 전용 메서드 한 번을 따로 호출했는데, 그 사이(먼저 락을 걸었다 풀고 다시 걸기까지의
    /// 짧은 틈)에 다른 연결의 스레드가 자기 줄을 끼워 넣는 게 실측(연결 2개가 겹치는 케이스)으로
    /// 확인돼 하나로 합쳤다. <b>공개 API가 아니다</b> — <c>FileLogger.WriteThenBoundary</c>(내부
    /// 전용)만 호출한다. 로컬 파일이 없는 원격 싱크는 <see cref="Write"/>만 수행하면 된다(구분선은
    /// 사람이 로컬 파일을 읽기 위한 편의 기능).
    ///
    /// 2026-09-17 사용자 지적(2차) — 여러 연결이 겹칠 때 구분선만 봐서는 어느 통신의 경계인지 알기
    /// 어렵다는 지적으로 <paramref name="boundaryLabel"/>(연결의 원격 엔드포인트, <c>remote</c>)을
    /// 추가해 구분선 문구 자체에 싣는다.
    /// </summary>
    void WriteThenBoundary(LogRecord record, string boundaryLabel);

    /// <summary>
    /// 2026-09-17 사용자 요청 — <see cref="WriteThenBoundary"/>(거래 종료)와 대칭으로 거래 시작
    /// 지점에도 구분선을 남긴다(구분선이 먼저, 로그 한 건이 그다음 — <paramref name="record"/> 앞에
    /// 붙는다). 여러 연결(거래)이 시간상 겹쳐 로그 줄이 뒤섞일 때, "이 줄부터 새 거래가 시작됐다"는
    /// 지점을 최소한 표시해 두면 완전히 풀어내진 못해도 사람이 눈으로 추적하기 쉬워진다(거래 하나가
    /// 끝나기 전엔 그 거래의 종료 구분선을 찍을 수 없으므로, 시작 구분선까지 있어야 겹친 두 거래
    /// 각각의 "시작"만큼은 항상 알아볼 수 있다).
    ///
    /// <see cref="WriteThenBoundary"/>와 마찬가지로 구분선과 로그를 **같은 락 안에서 원자적으로**
    /// 기록한다 — 처음엔 구분선 전용 메서드와 <c>Write</c>를 따로 호출했는데, 그 사이 틈에 다른
    /// 연결의 줄이 끼어드는 게 실측으로 확인돼(거래 종료 쪽과 같은 문제) 하나로 합쳤다.
    /// <b>공개 API가 아니다</b> — <c>FileLogger.WriteBoundaryThenWrite</c>(내부 전용)만 호출한다.
    /// 로컬 파일이 없는 원격 싱크는 <see cref="Write"/>만 수행하면 된다.
    ///
    /// 2026-09-17 사용자 지적(2차) — <see cref="WriteThenBoundary"/>와 같은 이유로
    /// <paramref name="boundaryLabel"/>을 추가해 구분선 문구 자체에 싣는다.
    /// </summary>
    void WriteBoundaryThenWrite(LogRecord record, string boundaryLabel);

    /// <summary>
    /// 2026-09-17 사용자 요청 — 앱 프로세스 1회 실행(기동)의 시작을 사람이 로그 파일을 눈으로 스크롤할
    /// 때 한눈에 알아볼 수 있도록 눈에 띄는 구분선을 남긴다. 거래 경계(<see cref="WriteThenBoundary"/>/
    /// <see cref="WriteBoundaryThenWrite"/>)와 의도적으로 다르게 생기게 한다 — 스코프가 다른
    /// 경계를 같은 모양으로 찍으면 "거래가 끝난 건지 프로세스가 새로 뜬 건지"를 사람이 구별할 수 없다.
    /// <see cref="LogRecord"/>/<c>LogLineRenderer</c>를 거치지 않는다 — 이 구분선도 파싱 대상
    /// 레코드가 아니다(위치 기반 파싱 정규식에 매치되지 않아 도구가 그냥 건너뛴다). <b>공개 API가
    /// 아니다</b> — <c>FileLogger.WriteStartupBanner</c>(내부 전용)만 호출한다. 로컬 파일이 없는 원격
    /// 싱크는 no-op으로 구현한다.
    /// </summary>
    void WriteStartupBanner();
}
