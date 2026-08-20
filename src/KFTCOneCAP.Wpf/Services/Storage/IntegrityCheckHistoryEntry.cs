using System;

namespace KFTCOneCAP.Wpf.Services.Storage;

/// <summary>
/// Phase 11(docs/payment_relay/development_plan.md P11-3) — <see cref="IntegrityCheckStore.GetHistory"/>가
/// 반환하는 조회 결과 1건. 계층 규칙(ROADMAP.md "계층 구조" — Services는 WPF 타입을 알지 못한다)을
/// 지키기 위해 원본 DB 값을 그대로 담는 순수 DTO다. 화면 표시용 서식(시각 포맷, 칩 색상 등)이나
/// <c>Models.IntegrityCheckRow</c>로의 변환은 이 계층의 책임이 아니라 호출자(ViewModel, Phase 12)의
/// 책임이다 — Storage가 표시 규칙까지 알면 계층이 섞인다.
/// </summary>
public sealed class IntegrityCheckHistoryEntry
{
    public IntegrityCheckHistoryEntry(DateTime checkedAt, string comPort, bool isSuccess, string? responseCode,
        string? moduleId, string? readerAuthId, string posId)
    {
        CheckedAt = checkedAt;
        ComPort = comPort;
        IsSuccess = isSuccess;
        ResponseCode = responseCode;
        ModuleId = moduleId;
        ReaderAuthId = readerAuthId;
        PosId = posId;
    }

    /// <summary>체크를 수행한 로컬 시각(원본 값, 서식 없음).</summary>
    public DateTime CheckedAt { get; }

    public string ComPort { get; }

    /// <summary>최종 성공/실패 판정(0x71/0x72 DLL 연동 실패까지 포함) — <see cref="IntegrityCheckRecord.IsSuccess"/>와 동일한 의미.</summary>
    public bool IsSuccess { get; }

    /// <summary>0x72 응답의 업무 응답코드(ASCII, 예: "00"). 없으면 null.</summary>
    public string? ResponseCode { get; }

    public string? ModuleId { get; }

    public string? ReaderAuthId { get; }

    public string PosId { get; }
}
