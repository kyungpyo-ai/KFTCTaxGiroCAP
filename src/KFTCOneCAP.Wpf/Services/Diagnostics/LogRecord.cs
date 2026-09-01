using System;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-1, PRD.md §1.3) 구조화 로그 한 줄의 불변 표현.
///
/// 링버퍼(P22-4)와 파일 싱크(P22-3)가 공유하는 공통 타입이다 — <b>렌더링된 문자열이 아니라 구조화된
/// 값을 담는다</b>(PRD.md §1.3-d, "장래 원격 싱크가 JSON으로 보낼 때 텍스트를 정규식으로 되파싱하는
/// 일이 없어야 한다"). 파일에 쓸 한 줄 문자열이 필요하면 <see cref="LogLineRenderer"/>를 쓴다.
///
/// <see cref="Category"/>/<see cref="Code"/>/<see cref="TransactionId"/>는 값이 없을 수 있다 — 기존
/// <c>FileLogger.Info(string)</c> 등 151곳의 호출은 이 세 값을 채우지 않으므로(PRD.md §1.3-b, "카테고리는
/// Phase 22 이후 새로 쓰는 로그부터 채우고, 기존 호출을 일괄 개조하지 않는다") null을 허용해야 한다.
/// 렌더링 시 null은 <c>-</c>로 표시된다(<see cref="LogLineRenderer"/>).
///
/// WPF 타입에 의존하지 않는다(Services는 View를 몰라야 한다는 계층 규칙, CLAUDE.md).
/// </summary>
public sealed class LogRecord
{
    public LogRecord(
        DateTime timestamp,
        LogLevel level,
        LogCategory? category,
        string? code,
        string? transactionId,
        string message)
    {
        Timestamp = timestamp;
        Level = level;
        Category = category;
        Code = code;
        TransactionId = transactionId;
        Message = message ?? string.Empty;
    }

    /// <summary>로컬 시각(<c>DateTime.Now</c> 기준, PRD.md §1.3-b).</summary>
    public DateTime Timestamp { get; }

    /// <summary>INFO/WARN/ERROR 3단.</summary>
    public LogLevel Level { get; }

    /// <summary>미지정이면 <c>null</c> — 렌더링 시 <c>-</c>.</summary>
    public LogCategory? Category { get; }

    /// <summary>3자리 결과 코드(<c>PosResultCodeMapper</c> 체계) 또는 <c>null</c> — 렌더링 시 <c>-</c>.</summary>
    public string? Code { get; }

    /// <summary>전문관리번호(<c>PaymentOrchestrator.LogTxId</c>가 만든 값) 또는 <c>null</c> — 렌더링 시 <c>-</c>.</summary>
    public string? TransactionId { get; }

    /// <summary>사람이 읽는 메시지. 개행이 포함될 수 있다(예: 예외 스택 트레이스) — 렌더링 시
    /// 이스케이프된다(<see cref="LogLineRenderer"/>).</summary>
    public string Message { get; }
}
