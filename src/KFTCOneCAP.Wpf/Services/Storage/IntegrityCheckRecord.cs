using System;

namespace KFTCOneCAP.Wpf.Services.Storage;

/// <summary>
/// Phase 11(docs/payment_relay/development_plan.md P11-2) — 무결성 체크 이력 1건을 저장할 때
/// 호출자(Phase 12 화면 배선/Phase 15 결제 Flow)가 채워 넘기는 입력 모델. PRD §7 저장 항목
/// (체크 일시/COM Port/결과/응답코드/모듈 ID/리더기 인증 식별번호/POS 식별번호) 중 POS 식별번호는
/// <see cref="IntegrityCheckStore.PosId"/> 상수로 이 계층이 직접 채우므로 여기에 포함하지 않는다.
///
/// <see cref="IsSuccess"/>(결과)와 <see cref="ResponseCode"/>(응답코드)를 별도 필드로 둔 이유:
/// 무결성 체크는 0x61→0x71(상태체크)→0x62→0x72(무결성) 2단계로 진행되고(PRD §6.4), 0x71 단계에서
/// DLL 연동 실패로 끝나면 업무 응답코드 자체가 없을 수 있다(<see cref="ResponseCode"/>가 null/빈
/// 문자열). "결과"는 이런 경우까지 포함한 최종 성공/실패 판정이고, "응답코드"는 실제 리더기가
/// 응답한 원본 코드(있는 경우만)다 — 둘을 하나로 합치면 이 구분이 사라진다.
/// </summary>
public sealed class IntegrityCheckRecord
{
    public IntegrityCheckRecord(DateTime checkedAt, string comPort, bool isSuccess, string? responseCode,
        string? moduleId, string? readerAuthId)
    {
        CheckedAt = checkedAt;
        ComPort = comPort;
        IsSuccess = isSuccess;
        ResponseCode = responseCode;
        ModuleId = moduleId;
        ReaderAuthId = readerAuthId;
    }

    /// <summary>체크를 수행한 로컬 시각.</summary>
    public DateTime CheckedAt { get; }

    /// <summary>체크에 사용한 COM 포트 표시 문자열(예: "COM 01").</summary>
    public string ComPort { get; }

    /// <summary>최종 성공/실패 판정 — 0x71/0x72 DLL 연동 실패까지 포함한 업무 결과.</summary>
    public bool IsSuccess { get; }

    /// <summary>0x72 응답의 업무 응답코드(ASCII, 예: "00"). 응답 자체를 못 받았으면 null.</summary>
    public string? ResponseCode { get; }

    /// <summary>0x71 응답에서 파싱한 모듈 ID(PRD §4.2, §6.2). 없으면 null.</summary>
    public string? ModuleId { get; }

    /// <summary>0x71 응답에서 파싱한 리더기 인증 식별번호. 없으면 null.</summary>
    public string? ReaderAuthId { get; }
}
