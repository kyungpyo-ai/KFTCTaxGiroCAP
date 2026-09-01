using System;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 8(docs/payment_relay/development_plan.md P8-3) 최소 파일 로깅의 공개 진입점.
///
/// Phase 22(docs/operations/development_plan.md P22-3, PRD.md §1.3-a)에서 내부 구현을
/// <see cref="ILogSink"/> 파이프라인 위임으로 바꿨다 — <b>공개 정적 메서드 시그니처(<see cref="Info"/>
/// / <see cref="Warn"/> / <see cref="Error"/>)는 그대로다.</b> 151곳의 호출부를 고치지 않는 것이
/// 이 리팩터링의 목적이라, DI 컨테이너는 도입하지 않는다.
///
/// 파이프라인 순서(PRD.md §1.4 "모든 싱크보다 앞, 단일 지점"):
/// 1) <see cref="LogMessageMasker.Mask"/>로 메시지를 마스킹한다.
/// 2) 마스킹된 메시지로 <see cref="LogRecord"/>를 만든다(카테고리/코드/거래ID는 기존 151곳의 호출이
///    채우지 않으므로 <c>null</c> — P22-6에서 새 오버로드로 확장한다).
/// 3) 등록된 각 <see cref="ILogSink"/>에 순서대로 전달한다. 싱크 하나(렌더링 포함, 예:
///    <see cref="LogLineRenderer.Render"/>가 던지는 <see cref="ArgumentOutOfRangeException"/> 등)가
///    예외를 던져도 다른 싱크와 호출자에게 전파되지 않는다 — 로깅 실패가 앱 동작에 영향을 주면 안
///    된다는 기존 계약을 유지한다.
///
/// 싱크 목록은 앱 기동 시 한 번 <see cref="ConfigureSinks"/>로 구성한다(<c>App.xaml.cs</c>). 장래
/// 원격 싱크는 그 호출에 인자를 추가하는 것만으로 붙는다. <see cref="ConfigureSinks"/>를 호출하지
/// 않은 상태(콘솔 하네스 등 <c>OnStartup</c>을 거치지 않는 진입점)에서도 파일 로깅과 링버퍼 기록이
/// 그대로 동작하도록 기본값은 <see cref="FileLogSink"/>와 <see cref="RingBufferSink"/> 두 개로
/// 초기화돼 있다.
/// </summary>
public static class FileLogger
{
    // volatile: ConfigureSinks(기동 시 한 번, App.xaml.cs OnStartup)의 배열 참조 교체가 Dispatch를 호출하는
    // 다른 스레드(리더기 CALLBACK/결제 오케스트레이터 등)에도 즉시 보이도록 한다. 배열 참조 교체 자체는
    // 원래도 원자적이라 별도 lock으로 "교체 동작"을 보호할 필요는 없었다 — 이전 lock은 동시 ConfigureSinks
    // 호출끼리의 직렬화만 보장했을 뿐(실제로는 그 호출이 기동 시 1회뿐이라 의미가 없었다) 가시성은 보장하지
    // 않았으므로 volatile로 대체한다.
    private static volatile ILogSink[] _sinks = { new FileLogSink(), new RingBufferSink() };

    /// <summary>
    /// 로그 싱크 목록을 (교체) 구성한다. 앱 기동 시 한 번만 호출한다(<c>App.xaml.cs</c>,
    /// <c>OnStartup</c> 최상단). 이후 <see cref="Info"/>/<see cref="Warn"/>/<see cref="Error"/> 호출은
    /// 이 목록으로 디스패치된다.
    ///
    /// <para>이 클래스에서 예외를 던지는 <b>유일한</b> 공개 메서드다 — 부트스트랩 구성 오류(빈 싱크 목록
    /// 전달 등)를 즉시 드러내기 위한 의도적 fail-fast다. <see cref="Info"/>/<see cref="Warn"/>/
    /// <see cref="Error"/>는 로깅 실패가 앱 동작에 영향을 주면 안 된다는 계약에 따라 절대 던지지 않는다.</para>
    /// </summary>
    public static void ConfigureSinks(params ILogSink[] sinks)
    {
        if (sinks is null || sinks.Length == 0)
        {
            throw new ArgumentException("최소 1개의 ILogSink가 필요합니다.", nameof(sinks));
        }

        // 방어적 복사: 호출자가 이후 자신이 들고 있는 배열을 변경해도 싱크 목록이 바뀌지 않도록 한다.
        _sinks = (ILogSink[])sinks.Clone();
    }

    public static void Info(string message) => Write(LogLevel.Info, category: null, code: null, transactionId: null, message);

    public static void Warn(string message) => Write(LogLevel.Warn, category: null, code: null, transactionId: null, message);

    public static void Error(string message) => Write(LogLevel.Error, category: null, code: null, transactionId: null, message);

    /// <summary>
    /// Phase 22(docs/operations/development_plan.md P22-5) — 카테고리를 싣는 오버로드.
    /// 기존 151곳의 호출부를 건드리지 않기 위해 위 3개(<see cref="Info(string)"/> 등)는 그대로 두고,
    /// 카테고리만 필요한 호출이 이 3개(<see cref="Info(LogCategory, string)"/>/
    /// <see cref="Warn(LogCategory, string)"/>/<see cref="Error(LogCategory, string)"/>)를 쓴다. 코드·
    /// 거래ID까지 함께 싣는 4-인자 오버로드는 아래 P22-6에서 추가했다.
    ///
    /// <b>2/4-인자 오버로드로 나눈 이유(CS0121 회피)</b>: 코드·거래ID를 <c>string? code = null,
    /// string? transactionId = null</c> 같은 선택적 인자로 4-인자 시그니처 하나에 합치면
    /// <c>Info(LogCategory.App, "msg")</c> 호출이 이 오버로드와도, 2-인자 오버로드와도 똑같이
    /// 일치해 모호성 컴파일 오류가 난다 — 그래서 인자 개수가 다른 두 오버로드로 명시적으로
    /// 나눴다(P22-6 development_plan.md 경고 반영).
    /// </summary>
    public static void Info(LogCategory category, string message) => Write(LogLevel.Info, category, code: null, transactionId: null, message);

    public static void Warn(LogCategory category, string message) => Write(LogLevel.Warn, category, code: null, transactionId: null, message);

    public static void Error(LogCategory category, string message) => Write(LogLevel.Error, category, code: null, transactionId: null, message);

    /// <summary>
    /// Phase 22(docs/operations/development_plan.md P22-6, PRD.md §1.5 경계 표) — 카테고리·코드(SPEC
    /// 3자리 결과 코드, <see cref="PosResultCodeMapper"/>가 만든 값)·거래ID(<see
    /// cref="Services.Payment.PaymentOrchestrator"/>가 만드는 전문관리번호, <c>LogTxId</c>)까지 싣는
    /// 오버로드. 결제 1건의 흐름을 따라가며 필요한 경계(POS/READER/VAN/PAYMENT)에서만 쓴다 — 기존
    /// 151곳을 일괄 개조하지 않는다.
    /// </summary>
    public static void Info(LogCategory category, string message, string? code, string? transactionId) => Write(LogLevel.Info, category, code, transactionId, message);

    public static void Warn(LogCategory category, string message, string? code, string? transactionId) => Write(LogLevel.Warn, category, code, transactionId, message);

    public static void Error(LogCategory category, string message, string? code, string? transactionId) => Write(LogLevel.Error, category, code, transactionId, message);

    private static void Write(LogLevel level, LogCategory? category, string? code, string? transactionId, string message)
    {
        try
        {
            string masked = LogMessageMasker.Mask(message);
            var record = new LogRecord(DateTime.Now, level, category, code, transactionId, message: masked);
            Dispatch(record);
        }
        catch
        {
            // 마스킹/레코드 생성 단계의 실패까지 포함해, 로깅 실패가 앱 동작에 영향을 주면 안 된다
            // (디스크 가득참·권한 문제 등은 조용히 무시한다는 기존 계약을 유지).
        }
    }

    private static void Dispatch(LogRecord record)
    {
        // 스냅샷 참조 하나만 읽는다 — _sinks가 volatile이라 ConfigureSinks의 배열 참조 교체(swap)가 이
        // 시점의 읽기에 즉시 보이며, 이 지역 변수로 스냅샷을 떠 두면 순회 도중 다른 스레드가 교체해도
        // 이번 Dispatch 호출은 시작 시점의 목록으로 일관되게 순회한다.
        ILogSink[] sinks = _sinks;
        foreach (ILogSink sink in sinks)
        {
            try
            {
                // 렌더링(LogLineRenderer.Render)은 각 싱크 구현(FileLogSink) 내부에서 호출되므로 이
                // try/catch가 렌더링 실패(ArgumentNullException/ArgumentOutOfRangeException 등)까지
                // 함께 감싼다 — 싱크 하나의 실패가 다른 싱크나 호출자에게 전파되지 않는다(장래 원격
                // 싱크 대비 특히 중요).
                sink.Write(record);
            }
            catch
            {
                // 싱크 실패를 조용히 무시한다.
            }
        }
    }
}
