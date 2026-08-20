namespace KFTCOneCAP.Wpf.Services.Storage;

/// <summary>
/// Phase 11(P11-4, 2026-08-20 사용자 확정 정책) — DB 저장 API는 실패를 예외로 던지지 않고 이
/// 값으로 반환한다. 무결성 체크 자체의 성공/실패(<see cref="IntegrityCheckRecord.IsSuccess"/>)와
/// "그 결과를 DB에 저장하는 것"의 성공/실패는 서로 다른 축이다 — 무결성 체크가 성공했는데 저장만
/// 실패한 경우, 호출자(Phase 15 결제 Flow)는 로그만 남기고 결제를 계속 진행해야 하므로 이 구분이
/// 반드시 필요하다.
/// </summary>
public sealed class IntegrityCheckSaveResult
{
    private IntegrityCheckSaveResult(bool success, string? errorMessage)
    {
        Success = success;
        ErrorMessage = errorMessage;
    }

    /// <summary>DB 저장 자체가 성공했는지 여부. 무결성 체크의 업무 결과와는 무관하다.</summary>
    public bool Success { get; }

    /// <summary>저장 실패 시 원인(로그에도 이미 기록됨). 성공이면 null.</summary>
    public string? ErrorMessage { get; }

    public static IntegrityCheckSaveResult Ok() => new(true, null);

    public static IntegrityCheckSaveResult Failed(string errorMessage) => new(false, errorMessage);
}
