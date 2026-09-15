using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 27(docs/operations/development_plan.md P27-9, fault_alert_catalog.md §2) 장애 알림 판정.
/// 카탈로그 §2.1(거래 경로)/§2.2(<c>S</c> 계열)/§2.3(<c>E42</c>/<c>E43</c>)의 <c>Y</c>/<c>N</c>/<c>T</c>
/// 판정표를 코드로 표현한다. 판정 결과가 <c>Y</c>/<c>T</c>(임계값 초과 시점)면 <see
/// cref="FileLogger.Alert(LogCategory, string, string?, string?)"/>로 <c>ALERT</c> 레벨 한 줄을 남긴다.
///
/// <b>서버 전송은 하지 않는다(Phase 28)</b> — 이 클래스는 판정과 로그 표시까지만 책임진다.
///
/// <b>호출 지점은 정확히 19곳</b>(development_plan.md P27-9-(a) 표는 "15곳"이라 적었으나, 그 표가
/// 1행으로 묶은 <c>S03</c>(3곳)·<c>S11</c>(3곳)이 실제로는 서로 다른 실패 경로라 코드에서는 각각
/// 별도 지점이다 — CP3 리뷰 §4에서 19곳으로 확정) — 이 메서드 자체를 다른 곳에서
/// 새로 호출하지 않는다(중복 판정 방지, <c>FileLogger.Write</c> 전역 후킹은 기각된 설계다).
///
/// <b>카운터는 고정 1시간 버킷</b>(<see cref="InternalFaultAlertConditions.WindowHours"/>)이고
/// <b>재시작 시 리셋을 허용</b>한다(영속화하지 않는다, §3). 코드별로 독립된
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> 항목을 써서 서로 다른 코드의 판정이 같은 락을
/// 다투지 않는다.
/// </summary>
internal static class FaultAlertJudge
{
    private sealed class BucketState
    {
        internal DateTime BucketStart;
        internal int Count;
        internal bool Alerted;
    }

    // 코드별 독립 버킷. 버킷 키(코드 문자열)마다 별도 BucketState를 갖는다 — R0x처럼 여러 개별 코드
    // (R00~R23)가 한 버킷을 공유해야 하는 경우는 호출부에서 공유 버킷 키("R0x")를 넘긴다.
    private static readonly ConcurrentDictionary<string, BucketState> Buckets = new();

    /// <summary>
    /// 관측된 코드 하나를 판정한다. <paramref name="category"/>는 호출부의 기본 카테고리이며,
    /// <c>R</c>/<c>D</c> 접두 코드는 원인 서브시스템(Reader/Van)으로 재판정한다(카탈로그 §1 "카테고리는
    /// 원인 서브시스템을 그대로 쓴다" — <c>PosSocketServer.SendResponse</c> 한 지점이 §2.1 전부를
    /// 커버하므로 코드 값에 따라 ALERT 카테고리를 다시 정해야 한다).
    ///
    /// 예외를 던지지 않는다(자체 <c>try/catch</c>로 완전히 방어) — 호출부의 <c>try/catch</c>는
    /// 이중 방어다.
    /// </summary>
    internal static void OnCodeObserved(string? code, LogCategory category, string? transactionId)
    {
        try
        {
            if (code is null || code.Length == 0)
            {
                return;
            }

            AlertDecision decision = Classify(code);
            if (decision.Verdict == AlertVerdict.None)
            {
                return;
            }

            LogCategory alertCategory = ResolveCategory(code, category);

            if (decision.Verdict == AlertVerdict.Immediate)
            {
                FileLogger.Alert(alertCategory, $"장애 알림 대상 — {decision.Reason}", code, transactionId);
                return;
            }

            // Threshold(T) — 코드별(또는 공유 버킷 키별) 고정 1시간 버킷 카운터.
            BucketState state = Buckets.GetOrAdd(decision.BucketKey!, _ => new BucketState());
            int count;
            bool crossedThisCall = false;

            lock (state)
            {
                DateTime bucketStart = CurrentBucketStart();
                if (state.BucketStart != bucketStart)
                {
                    state.BucketStart = bucketStart;
                    state.Count = 0;
                    state.Alerted = false;
                }

                state.Count++;
                count = state.Count;

                // 카탈로그 §3 "값의 근거"("N건이면 ~하다") — N번째 발생 시점에 이미 알림이 떠야
                // 한다는 뜻이다. "초과(>)"가 아니라 "도달(>=)"이 맞다(2026-09-14 코디네이터 지적으로
                // 정정 — 기존 count > threshold는 (임계값+1)번째에야 발동하는 off-by-one 버그였다).
                if (!state.Alerted && count >= decision.Threshold)
                {
                    state.Alerted = true;
                    crossedThisCall = true;
                }
            }

            if (crossedThisCall)
            {
                FileLogger.Alert(
                    alertCategory,
                    $"장애 알림 대상 — 임계값 도달(1시간 {count.ToString(CultureInfo.InvariantCulture)}건 >= {decision.Threshold.ToString(CultureInfo.InvariantCulture)}건)",
                    code,
                    transactionId);
            }
        }
        catch
        {
            // 판정 로직 자체의 실패가 결제 경로나 호출부에 영향을 주면 안 된다(P27-9-(e), 이중 방어).
        }
    }

    private static DateTime CurrentBucketStart()
    {
        DateTime now = DateTime.Now;
        // WindowHours=1 고정 — 시간 단위로 자른 값(예: new DateTime(y,m,d,h,0,0)).
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind)
            .AddHours(-(now.Hour % InternalFaultAlertConditions.WindowHours));
    }

    /// <summary><c>R</c>/<c>D</c> 접두 코드는 원인 서브시스템(Reader/Van)으로 재판정하고, 그 외(<c>E</c>
    /// 접두·<c>S</c> 계열)는 호출부가 넘긴 카테고리를 그대로 쓴다 — <c>E</c> 계열은 <c>SendResponse</c>가
    /// <see cref="LogCategory.Payment"/>를, <c>E40</c>~<c>E43</c>은 호출부가 <see cref="LogCategory.Pos"/>를
    /// 이미 넘기고, <c>S</c> 계열은 P27-8이 이미 부여한 카테고리(Pos/Reader/App/Ui/Payment)를 그대로
    /// 넘기므로 재판정할 필요가 없다.</summary>
    private static LogCategory ResolveCategory(string code, LogCategory suppliedCategory)
    {
        if (code.Length > 0)
        {
            if (code[0] == 'R')
            {
                return LogCategory.Reader;
            }

            if (code[0] == 'D')
            {
                return LogCategory.Van;
            }
        }

        return suppliedCategory;
    }

    private enum AlertVerdict
    {
        None,
        Immediate,
        Threshold,
    }

    private readonly struct AlertDecision
    {
        internal AlertVerdict Verdict { get; }
        internal string? Reason { get; }
        internal string? BucketKey { get; }
        internal int Threshold { get; }

        private AlertDecision(AlertVerdict verdict, string? reason, string? bucketKey, int threshold)
        {
            Verdict = verdict;
            Reason = reason;
            BucketKey = bucketKey;
            Threshold = threshold;
        }

        internal static AlertDecision None() => new(AlertVerdict.None, null, null, 0);

        internal static AlertDecision Immediate(string reason) => new(AlertVerdict.Immediate, reason, null, 0);

        internal static AlertDecision ForThreshold(string bucketKey, int threshold) =>
            new(AlertVerdict.Threshold, null, bucketKey, threshold);
    }

    /// <summary>카탈로그 §2.1/§2.2/§2.3의 판정표. <c>R00</c>~<c>R23</c>은 문자열 파싱으로 범위를
    /// 확인한다(a-0 재배치 후 <c>R24</c>~<c>R32</c>는 이 범위 밖이라 개별 <c>Y</c> 행으로만 매칭된다).</summary>
    private static AlertDecision Classify(string code) => code switch
    {
        // §2.1 거래 경로 — N
        "E01" or "E02" or "E03" or "E04" or "E06" or "E07" => AlertDecision.None(),

        // §2.1 — T (개별 코드 전용 버킷)
        "E05" => AlertDecision.ForThreshold("E05", InternalFaultAlertConditions.E05Threshold),

        // §2.1 — Y
        "E99" => AlertDecision.Immediate("내부 예외"),

        // §2.1 — Y (a-0 재배치 후 리더기 DLL 연동 실패 코드)
        "R24" => AlertDecision.Immediate("리더기 포트 미개방"),
        "R25" => AlertDecision.Immediate("리더기 전송 실패"),
        "R26" => AlertDecision.Immediate("리더기 BUSY"),
        "R27" => AlertDecision.Immediate("리더기 포트 없음"),
        "R28" => AlertDecision.Immediate("리더기 포트 열기 실패"),
        "R29" => AlertDecision.Immediate("허용되지 않은 명령"),
        "R30" => AlertDecision.Immediate("리더기 통신 오류"),
        "R31" => AlertDecision.Immediate("그 외 리더기 DLL 실패"),
        "R32" => AlertDecision.Immediate("방어적 실패(승자 없음 등)"),

        // §2.1 — Y (VAN)
        "D01" => AlertDecision.Immediate("VAN DLL 로드 실패"),
        "D02" => AlertDecision.Immediate("VAN DLL 통신 실패"),

        // §2.1/§2.3 — T (코드별 독립 카운터)
        "E40" => AlertDecision.ForThreshold("E40", InternalFaultAlertConditions.E40ToE43Threshold),
        "E41" => AlertDecision.ForThreshold("E41", InternalFaultAlertConditions.E40ToE43Threshold),
        "E42" => AlertDecision.ForThreshold("E42", InternalFaultAlertConditions.E40ToE43Threshold),
        "E43" => AlertDecision.ForThreshold("E43", InternalFaultAlertConditions.E40ToE43Threshold),

        // §2.2 S 계열 — Y (S09 제외)
        InternalFaultCodes.ListenFailure => AlertDecision.Immediate("POS 소켓 리스닝 실패"),
        InternalFaultCodes.AcceptLoopDied => AlertDecision.Immediate("수락 루프 사망"),
        InternalFaultCodes.IntegrityStoreFailure => AlertDecision.Immediate("무결성 이력 저장·조회 실패"),
        InternalFaultCodes.LogRetentionFailure => AlertDecision.Immediate("로그 보관 정리 실패"),
        InternalFaultCodes.KeyboardHookFailure => AlertDecision.Immediate("전역 키보드 훅 설치 실패"),
        InternalFaultCodes.ResponseDeliveryFailure => AlertDecision.Immediate("응답 전달 실패"),
        InternalFaultCodes.ResponseSendFailure => AlertDecision.Immediate("응답 송신 실패"),
        InternalFaultCodes.ResponseSerializationFailure => AlertDecision.Immediate("응답 직렬화 실패"),
        InternalFaultCodes.ConnectionHandlingException => AlertDecision.Immediate("연결 처리 중 예외"),
        InternalFaultCodes.DllLoadSmokeFailure => AlertDecision.Immediate("DLL 로드 스모크 실패"),

        // §2.2 — T
        InternalFaultCodes.ConnectionLimitExceeded =>
            AlertDecision.ForThreshold(InternalFaultCodes.ConnectionLimitExceeded, InternalFaultAlertConditions.S09Threshold),

        _ => ClassifyRBusinessFailure(code),
    };

    /// <summary><c>R00</c>~<c>R23</c>(3자리, <c>R</c> + 00~23) 범위의 리더기 업무 응답코드 실패는
    /// 공유 버킷("R0x")으로 <c>T</c> 판정한다. 그 외 코드(승인/거절 등 표에 없는 값)는 <c>N</c>이다.</summary>
    private static AlertDecision ClassifyRBusinessFailure(string code)
    {
        if (code.Length == 3
            && code[0] == 'R'
            && int.TryParse(code.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            && n is >= 0 and <= 23)
        {
            return AlertDecision.ForThreshold("R0x", InternalFaultAlertConditions.R0xThreshold);
        }

        return AlertDecision.None();
    }
}
