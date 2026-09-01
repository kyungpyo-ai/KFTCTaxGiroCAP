using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-1, PRD.md §1.3-b) <see cref="LogRecord"/> → 파일에
/// 쓸 한 줄 문자열 변환. 아래 형식과 파싱 정규식은 반드시 짝이 맞아야 한다(PRD.md §1.3-b):
///
/// <code>
/// [yyyy-MM-dd HH:mm:ss.fff] [레벨] [카테고리] [코드] [거래ID] 메시지
/// ^\[([^\]]*)\] \[([^\]]*)\] \[([^\]]*)\] \[([^\]]*)\] \[([^\]]*)\] (.*)$
/// </code>
///
/// 규칙(PRD.md §1.3-b):
/// - 슬롯 5개는 항상 존재한다. 값이 없으면 <c>-</c>.
/// - 레벨/카테고리/코드/거래ID 네 슬롯은 고정폭 왼쪽 정렬(부족한 자리는 공백으로 채움)이다 — 사람이
///   여러 줄을 눈으로 훑을 때 <c>[</c> 위치가 세로로 정렬돼야 읽기 쉽다는 사용자 지적(2026-09-01)으로
///   레벨 슬롯에 이미 있던 예외를 나머지 세 슬롯까지 확장했다(값이 고정된 열거형/코드 체계라 "정렬용
///   패딩 금지" 규칙과 충돌하지 않는다 — 메시지처럼 자유 텍스트인 슬롯에는 패딩을 넣지 않는다):
///   <list type="bullet">
///   <item>레벨: 폭 5(<c>INFO </c>/<c>WARN </c>/<c>ERROR</c>).</item>
///   <item>카테고리: 폭 8(<see cref="LogCategoryText.ToText"/> 결과 중 최장인 <c>SETTINGS</c> 기준,
///     <see cref="LogCategory"/> 8종 전수). 빈 슬롯(<c>-</c>)도 이 폭에 맞춰 패딩한다.</item>
///   <item>코드: 폭 3(<c>PosResultCodeMapper</c>가 만드는 <c>E0x</c>/<c>R0x</c>/<c>R2x</c>/<c>D0x</c>
///     체계가 전부 3자리). 빈 슬롯(<c>-</c>)도 이 폭에 맞춰 패딩한다.</item>
///   <item>거래ID: 최소폭 12(<c>PaymentOrchestrator.LogTxId</c>가 SPEC <c>#9</c> 전문관리번호
///     AN(12)를 그대로 쓴다 — <c>PosCommonHeader</c> 필드 정의 참고). <b>최대폭이 아니라 최소폭이다</b>
///     — <c>LogTxId</c>의 fallback(<c>{전문구분}-NOID-{해시}</c>, #9가 빈 기형 요청일 때 합성)은
///     12자를 넘을 수 있는데, 이 경우 정보 손실을 막기 위해 자르지 않고 그 줄만 길어지게 둔다(짧은
///     값만 패딩).</item>
///   </list>
/// - 정렬용 패딩이 있는 네 슬롯(레벨/카테고리/코드/거래ID)은 파싱 시 <c>Trim()</c> 후 비교해야 한다
///   (레벨은 이미 그렇게 다뤄 왔다) — 파싱 정규식(<c>^\[([^\]]*)\] ...</c>) 자체는 폭과 무관하게
///   그대로 동작하므로 바꾸지 않는다.
/// - 슬롯 사이 구분자는 공백 1개.
/// - 메시지의 개행(<c>\r\n</c>/<c>\n</c>/<c>\r</c>)은 가시 문자 두 글자(백슬래시 + n)로 치환해 한
///   레코드가 반드시 한 줄이 되게 한다(예: <c>PosSocketServer.cs:248</c>의 예외 스택 트레이스처럼
///   여러 줄 메시지가 실제로 들어온다).
/// - 카테고리/코드/거래ID 슬롯 값에 <c>[</c>/<c>]</c>가 있으면 파싱이 깨지므로 각각 <c>(</c>/<c>)</c>로
///   치환한다(값의 출처가 모두 열거형·고정 코드·전문관리번호라 실제로는 일어나지 않지만 계약을 코드로
///   보장해 둔다). 개행도 마찬가지 이유로 메시지 슬롯과 동일하게 이스케이프한다(<c>PaymentOrchestrator
///   .LogTxId</c>를 거쳐 소켓 원시 바이트에서 온 거래ID는 전문 파싱이 문자셋을 검증하지 않아 개행이
///   섞일 수 있다 — "한 레코드 = 한 줄" 계약을 코드로 보장). 메시지는 마지막 슬롯이라 <c>[</c>/<c>]</c>를
///   그대로 둬도 안전하다(PRD.md §1.3-b).
/// </summary>
public static class LogLineRenderer
{
    private const string EmptySlot = "-";

    /// <summary><see cref="LogCategory"/> 텍스트(<see cref="LogCategoryText.ToText"/>) 중 최장(<c>SETTINGS</c>)
    /// 기준 고정폭 — 클래스 요약 참고.</summary>
    private const int CategoryWidth = 8;

    /// <summary><c>PosResultCodeMapper</c>가 만드는 <c>E0x</c>/<c>R0x</c>/<c>R2x</c>/<c>D0x</c> 3자리
    /// 코드 체계 기준 고정폭 — 클래스 요약 참고.</summary>
    private const int CodeWidth = 3;

    /// <summary>SPEC <c>#9</c> 전문관리번호 AN(12) 기준 최소폭(최대폭 아님, fallback 값은 넘칠 수 있음) —
    /// 클래스 요약 참고.</summary>
    private const int TransactionIdMinWidth = 12;

    // \r\n을 하나의 개행으로 먼저 소비해야 "\n\n"처럼 이스케이프 마커가 중복 삽입되지 않는다. 세 패턴
    // 우선순위를 정규식 하나에 담아 미리 컴파일해 둔다 — 예외 스택 트레이스가 섞인 메시지가 로깅
    // 경로에서 자주 지나가는 곳이라 성능에 민감하다(P22-2 마스킹과 동일한 이유).
    private static readonly Regex NewlinePattern = new(@"\r\n|\r|\n", RegexOptions.Compiled);

    public static string Render(LogRecord record)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        var sb = new StringBuilder();
        sb.Append('[').Append(record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(']').Append(' ');
        sb.Append('[').Append(LevelText(record.Level)).Append(']').Append(' ');
        sb.Append('[').Append(PadSlot(SanitizeSlot(record.Category?.ToText()), CategoryWidth)).Append(']').Append(' ');
        sb.Append('[').Append(PadSlot(SanitizeSlot(record.Code), CodeWidth)).Append(']').Append(' ');
        sb.Append('[').Append(PadSlot(SanitizeSlot(record.TransactionId), TransactionIdMinWidth)).Append(']').Append(' ');
        sb.Append(EscapeNewlines(record.Message));

        return sb.ToString();
    }

    /// <summary>레벨 슬롯만 폭 5로 왼쪽 정렬한다(INFO/WARN → 4자 + 공백 1개, ERROR → 5자 그대로).</summary>
    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Info => "INFO ",
        LogLevel.Warn => "WARN ",
        LogLevel.Error => "ERROR",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "매핑되지 않은 LogLevel"),
    };

    /// <summary>값이 <paramref name="width"/>보다 짧으면 오른쪽에 공백을 채워 최소폭을 보장한다(왼쪽
    /// 정렬). 이미 폭 이상이면(거래ID fallback처럼) 자르지 않고 그대로 둔다 — "최소폭 보장"이지
    /// "최대폭 강제"가 아니다(클래스 요약 참고).</summary>
    private static string PadSlot(string value, int width) => value.PadRight(width);

    private static string SanitizeSlot(string? value)
    {
        // net48 BCL의 string.IsNullOrEmpty에는 NotNullWhen 어노테이션이 없어 null 가능성 분석이
        // 이어지지 않는다 — is null 패턴으로 직접 좁혀 CS8602 경고를 없앤다.
        if (value is null || value.Length == 0)
        {
            return EmptySlot;
        }

        string sanitized = value;
        if (sanitized.IndexOf('[') >= 0)
        {
            sanitized = sanitized.Replace('[', '(');
        }

        if (sanitized.IndexOf(']') >= 0)
        {
            sanitized = sanitized.Replace(']', ')');
        }

        return EscapeNewlines(sanitized);
    }

    private static string EscapeNewlines(string message) => NewlinePattern.Replace(message, "\\n");
}
