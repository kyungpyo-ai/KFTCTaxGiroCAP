using System.Text.RegularExpressions;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// Phase 22(docs/operations/development_plan.md P22-2, PRD.md §1.4) 로그 메시지에서 카드 데이터로
/// 보이는 패턴을 자동으로 지우는 **단일 지점**. 로거 진입점(P22-3 파이프라인)에서 모든 싱크보다
/// 앞서 한 번만 호출한다 — 싱크마다 따로 마스킹을 구현하지 않는다.
///
/// <b>이건 최후의 방어선이지 면허가 아니다</b>(PRD.md §1.4) — 호출부가 카드번호·트랙 데이터·PIN 블록을
/// 애초에 로그 메시지에 넣지 않는다는 원칙은 그대로 유효하다.
///
/// 마스킹 대상은 <see cref="LogRecord.Message"/> 뿐이다. 카테고리/코드/거래ID 슬롯은 열거형·고정
/// 코드·전문관리번호만 담을 수 있어 카드 데이터가 들어갈 여지가 없다(PRD.md §1.3-b, §1.4).
/// </summary>
public static class LogMessageMasker
{
    /// <summary>
    /// 카드번호로 보이는 13~19자리 연속 숫자. 앞뒤로 다른 숫자가 이어지면(20자리 이상 등) 카드번호가
    /// 아닌 다른 종류의 숫자열(전문 바이트 덤프 등)일 가능성이 커 대상에서 제외한다 — lookaround로
    /// 정확히 13~19자리인 구간만 잡는다. 정규식은 정적으로 한 번만 컴파일한다(모든 로그 한 줄마다
    /// 통과하는 경로라 성능에 민감 — development_plan.md P22-2).
    /// </summary>
    private static readonly Regex CardNumberPattern = new(@"(?<!\d)\d{13,19}(?!\d)", RegexOptions.Compiled);

    /// <summary>
    /// ISO 7813 트랙1 데이터 형태(<c>%B...^...^...?</c>). 시작 센티널(<c>%</c>+포맷코드 문자)부터 종료
    /// 센티널(<c>?</c>)까지를 통째로 지운다. 부분 마스킹(앞6+뒤4)이 아니라 전체 치환인 이유는 트랙
    /// 데이터는 카드번호 외에도 유효기간·서비스코드 등 카드 데이터 자체라 일부만 남겨도 여전히 카드
    /// 데이터이기 때문이다.
    ///
    /// 문자 클래스를 <see cref="CardNumberPattern"/>이 남기는 <c>*</c>를 포함하지 않는 negated
    /// class(<c>[^?\r\n]</c>)로 바꿔, 카드번호 마스킹이 먼저 실행돼 트랙 데이터 내부가 <c>*</c>로 바뀐
    /// 뒤에도 매칭이 깨지지 않는다 — 두 패턴의 적용 순서가 결과에 영향을 주지 않는다(개행은 "메시지
    /// 한 줄" 계약을 지키기 위해 경계에서 제외한다).
    /// </summary>
    private static readonly Regex Track1Pattern =
        new(@"%[A-Za-z][^?\r\n]{10,90}\?", RegexOptions.Compiled);

    /// <summary>
    /// ISO 7813 트랙2 데이터 형태(<c>;...=...?</c>). PAN 구간(<c>[0-9*]{12,19}</c>)이 카드번호 마스킹이
    /// 남긴 <c>*</c>도 포함하도록 해 <see cref="Track1Pattern"/>과 마찬가지로 순서 무관성을 확보한다.
    /// 시작 센티널(<c>;</c>+숫자열+<c>=</c>)을 요구해 "...실패; ...처리불가?" 같은 한국어 메시지를
    /// 오탐하지 않는다.
    /// </summary>
    private static readonly Regex Track2Pattern =
        new(@";[0-9*]{12,19}=[^?\r\n]{0,45}\?", RegexOptions.Compiled);

    private const string TrackDataRedacted = "[TRACK DATA MASKED]";

    /// <summary>메시지 문자열에서 카드번호/트랙 데이터로 보이는 패턴을 지운 결과를 돌려준다. 원본을
    /// 바꾸지 않는다(순수 함수). null 입력은 <see cref="string.Empty"/>를 돌려준다(호출부가 null 분기를
    /// 따로 처리하지 않아도 되도록 <see cref="LogLineRenderer"/>의 null 처리 방식과 통일).</summary>
    public static string Mask(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        // 두 트랙 패턴과 카드번호 패턴은 이제 서로 문자 클래스가 겹치지 않게(트랙 패턴이 '*'를 포함)
        // 설계돼 있어 적용 순서가 결과에 영향을 주지 않는다(순서 무관성은 LogMessageMaskerTests류
        // 콘솔 하네스로 트랙→카드, 카드→트랙 양쪽을 검증했다).
        string masked = Track1Pattern.Replace(message, TrackDataRedacted);
        masked = Track2Pattern.Replace(masked, TrackDataRedacted);
        masked = CardNumberPattern.Replace(masked, MaskCardNumber);
        return masked;
    }

    /// <summary>앞 6자리 + 뒤 4자리만 남기고 가운데를 <c>*</c>로 치환한다(PRD.md §1.4).</summary>
    private static string MaskCardNumber(Match match)
    {
        string digits = match.Value;
        int maskedLength = digits.Length - 10; // 앞 6 + 뒤 4를 제외한 나머지
        return digits.Substring(0, 6) + new string('*', maskedLength) + digits.Substring(digits.Length - 4);
    }
}
