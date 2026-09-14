namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 27(docs/operations/development_plan.md P27-9-(a), fault_alert_catalog.md §3) 장애 알림
/// 급증형(<c>T</c>) 판정의 조건값(임계값·윈도우) — <b>단일 지점</b>이다.
///
/// <b>이 파일이 조건값 리터럴이 코드베이스에 존재하는 유일한 지점이어야 한다</b>(완료 조건).
/// 호출부(<see cref="FaultAlertJudge"/> 포함)는 이 클래스의 상수만 참조하고, 숫자를 직접 흩지 않는다
/// — 장래 서버가 조건을 내려주게 될 때(§1) 이 지점만 갈아 끼우면 된다.
///
/// 1단계(지금)는 카탈로그 §3의 잠정값을 그대로 코드 상수로 옮긴 것이다. 값을 바꾸려면 이 파일을
/// 고치고 재배포한다(§1, "값을 바꾸려면 재배포가 필요하다").
/// </summary>
internal static class InternalFaultAlertConditions
{
    /// <summary>고정 버킷 크기(§3 "고정 1시간 버킷으로 확정한다", 2026-09-14). 모든 급증형 코드가
    /// 공유하는 윈도우 길이다 — 코드마다 창 길이가 다를 이유가 없어 별도 상수로 안 나눈다.</summary>
    internal const int WindowHours = 1;

    /// <summary><c>E05</c>(무결성 체크 전원 실패) 임계값 — 1시간 내 3건.</summary>
    internal const int E05Threshold = 3;

    /// <summary><c>R0x</c>(리더기 업무 응답코드 실패, <c>R00</c>~<c>R23</c>) 임계값 — 1시간 내 10건.</summary>
    internal const int R0xThreshold = 10;

    /// <summary><c>S09</c>(동시 연결 상한 초과 거부) 임계값 — 1시간 내 5건.</summary>
    internal const int S09Threshold = 5;

    /// <summary><c>E40</c>~<c>E43</c>(전문 오류, 코드별 독립 카운터) 공통 임계값 — 1시간 내 5건.</summary>
    internal const int E40ToE43Threshold = 5;
}
