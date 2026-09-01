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
/// - 레벨 슬롯만 폭 5(<c>INFO </c>/<c>WARN </c>/<c>ERROR</c>)로 맞춘다 — PRD.md §1.3-b 예시가 실제로
///   이렇게 정렬돼 있다(고정된 3값짜리 열거형이라 "정렬용 패딩 금지" 규칙과 충돌하지 않는다). 카테고리
///   /코드/거래ID처럼 길이가 들쭉날쭉한 슬롯에는 패딩을 넣지 않는다.
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
        sb.Append('[').Append(SanitizeSlot(record.Category?.ToText())).Append(']').Append(' ');
        sb.Append('[').Append(SanitizeSlot(record.Code)).Append(']').Append(' ');
        sb.Append('[').Append(SanitizeSlot(record.TransactionId)).Append(']').Append(' ');
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
