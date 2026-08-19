namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 8(docs/payment_relay/development_plan.md P8-3) 최소 파일 로깅의 레벨 구분.
/// 로깅 프레임워크(NLog 등)를 도입하지 않기로 했으므로(P8-3 근거) 딱 필요한 수준만 둔다.
/// </summary>
public enum LogLevel
{
    Info,
    Warn,
    Error,
}
