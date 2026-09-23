using System.Collections.Generic;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// 값 변환 종류(PRD.md §13.7, Phase 30 P30-3가 실제 변환 로직을 구현한다). 이 열거형은 <see
/// cref="TelegramFieldChainMap"/>이 "무엇을 해야 하는지"만 표시하고, "어떻게 하는지"는 P30-3의 변환기가
/// 담당한다 — 매핑 테이블과 변환 로직을 분리해 SPEC이 바뀔 때 이 파일만 고치면 되게 한다.
/// </summary>
public enum FieldChainConversion
{
    /// <summary>출처 값을 그대로 옮긴다(표현·길이가 같음).</summary>
    Direct,

    /// <summary>바이트 한도를 넘는 값을 자른다 — CP949 한글 반글자 방지 절삭(§13.7).</summary>
    Truncate,

    /// <summary>더 짧은 필드(N형)의 값을 더 긴 필드로 옮긴다 — <see cref="PosField.Pad"/>의 0-패딩이
    /// 그대로 처리하므로 별도 변환 로직이 없다(§13.7, §10.1).</summary>
    Widen,

    /// <summary>자릿수는 같고 표현(<see cref="PosFieldType"/>)만 다르다 — 값을 그대로 옮긴다(§13.7).</summary>
    RepresentationChange,

    /// <summary><see cref="TelegramFieldChainMap.ChainEntry.SourceFieldNumbers"/> 여러 필드의 정수합.</summary>
    Sum,

    /// <summary>출처 전문과 무관한 고정값(<see cref="TelegramFieldChainMap.ChainEntry.FixedValue"/>).</summary>
    Fixed,
}

/// <summary>
/// 전문 간 필드 연쇄 매핑 정본(PRD.md §3.3.2, Phase 30 P30-2). 앞 전문(<c>501008</c>/<c>800000</c>) 응답값이
/// 뒤 전문(<c>800000</c>/<c>902614</c>) 요청 필드로 들어가는 규칙을 선언적 테이블 하나에 모은다.
///
/// <b>이 테이블 하나가 유일한 출처다</b> — 화면(ViewModel)이나 P30-3 값 변환기는 이 테이블의 필드번호만
/// 참조하고, 연쇄용 필드번호 리터럴을 직접 흩뿌리지 않는다. SPEC이 또 바뀌면(2026-09-18 → 09-21 →
/// 09-22로 세 번 바뀐 전례가 있다) 이 파일 하나만 고치면 된다.
///
/// <b>근거 수준이 항목마다 다르다.</b> SPEC 원문에 명문 근거가 있는 항목은 <c>#29</c>(2026-09-22
/// 재배포본에서 p.16 자기참조 오탈자가 정정돼 근거가 생겼다) 하나뿐이고, 나머지는 전부 필드명 일치
/// 또는 사용자 확정만 근거다 — SPEC은 이 저장소 개발팀이 직접 작성·관리하는 문서이며(발주처 질의
/// 대상이 아니다), 미기재 항목의 조사 기록과 확정 상태는 <c>docs/payment_relay/spec_open_questions.md</c>
/// §4(Q1~Q10)가 정본이다. 각 항목의 <see cref="ChainEntry.Evidence"/>가 그 Q번호를 가리킨다.
///
/// <b>연쇄 대상에서 제외한 필드</b>(PRD.md §3.3.1-c, §3.3.2): <c>902614 #38</c>(인터넷지로 담당, kiosk가
/// 채우지 않음), <c>902614 #42</c>(전문 간 연쇄가 아니라 가맹점 설정값 — 이 테이블의 범위 밖),
/// <c>800000 #11</c>/<c>#12</c>(2026-09-22 SPEC 개정으로 공백 확정, spec_open_questions.md Q9).
/// </summary>
public static class TelegramFieldChainMap
{
    /// <summary>SPEC #4 거래 구분 코드 — 501008/800000/902614 세 스키마의
    /// <see cref="PosTelegramSchema.TransactionTypeCode"/>와 반드시 일치해야 한다.</summary>
    public const string Notice501008 = "501008";
    public const string CardInfo800000 = "800000";
    public const string CardApproval902614 = "902614";

    /// <summary>
    /// 연쇄 매핑 한 항목. <see cref="SourceTelegram"/>이 <see langword="null"/>이면
    /// <see cref="Conversion"/>은 반드시 <see cref="FieldChainConversion.Fixed"/>이고 <see cref="FixedValue"/>를
    /// 쓴다. <see cref="SourceTelegram"/>이 <see cref="TargetTelegram"/>과 같으면(현재 <c>902614 #29</c> 1건)
    /// 같은 전문 안의 다른 연쇄 필드(<c>#27</c>/<c>#28</c>)가 먼저 채워진 뒤에 계산해야 하는 2단계
    /// 항목이라는 뜻이다(§13.7, P30-4가 적용 순서를 지킨다).
    /// </summary>
    public sealed class ChainEntry
    {
        public ChainEntry(
            string targetTelegram,
            int targetFieldNumber,
            string? sourceTelegram,
            IReadOnlyList<int> sourceFieldNumbers,
            FieldChainConversion conversion,
            string evidence,
            string? fixedValue = null)
        {
            TargetTelegram = targetTelegram;
            TargetFieldNumber = targetFieldNumber;
            SourceTelegram = sourceTelegram;
            SourceFieldNumbers = sourceFieldNumbers;
            Conversion = conversion;
            Evidence = evidence;
            FixedValue = fixedValue;
        }

        /// <summary>연쇄로 채워지는 요청 전문(<see cref="CardInfo800000"/> 또는 <see cref="CardApproval902614"/>).</summary>
        public string TargetTelegram { get; }

        public int TargetFieldNumber { get; }

        /// <summary>값이 오는 응답 전문. <see cref="FieldChainConversion.Fixed"/>면 <see langword="null"/>.</summary>
        public string? SourceTelegram { get; }

        /// <summary>출처 필드 번호(들). <see cref="FieldChainConversion.Sum"/>이면 2개 이상, 그 외에는
        /// 정확히 1개, <see cref="FieldChainConversion.Fixed"/>면 빈 목록.</summary>
        public IReadOnlyList<int> SourceFieldNumbers { get; }

        public FieldChainConversion Conversion { get; }

        /// <summary>SPEC 명문 근거인지 사용자 확정인지, 그리고 spec_open_questions.md §4의 어느 Q번호에
        /// 대응하는지 사람이 읽을 수 있게 적은 주석.</summary>
        public string Evidence { get; }

        /// <summary><see cref="FieldChainConversion.Fixed"/> 전용 — 이 고정 문자열을 그대로 쓴다.</summary>
        public string? FixedValue { get; }
    }

    /// <summary>
    /// 연쇄 매핑 전체 목록(23건 — 501008→902614 16건 + 800000→902614 2건 + 902614 내부 합산 1건(#29) +
    /// 501008→902614 합산 1건(#27) + 501008→800000 2건 + 902614 고정값 1건(#34) = PRD.md §3.3.2 표의
    /// 모든 필드를 빠짐없이 옮긴 것, §38/§42/800000 #11·#12는 의도적 제외(클래스 주석 참고)).
    /// </summary>
    public static readonly IReadOnlyList<ChainEntry> Entries = new[]
    {
        // ── 501008 응답 → 902614 요청 (PRD §3.3.2, 이름 일치 그룹 — SPEC 명문 없음, 이름 동일성만 근거) ──
        new ChainEntry(CardApproval902614, 14, Notice501008, new[] { 17 }, FieldChainConversion.Direct,
            "spec_open_questions.md Q7 — #14/#36 둘 다 501008 #17(납세의무자번호)에서 옴, SPEC 명문 없음(사용자 확정)"),
        new ChainEntry(CardApproval902614, 15, Notice501008, new[] { 14 }, FieldChainConversion.Direct,
            "이름 일치(전자납부번호, AN19=AN19), SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 16, Notice501008, new[] { 15 }, FieldChainConversion.RepresentationChange,
            "이름 일치(납부 순번), 501008 N3 → 902614 AN3, SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 18, Notice501008, new[] { 20 }, FieldChainConversion.RepresentationChange,
            "이름 일치(징수 과목 코드), 501008 AN7 → 902614 N7, SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 19, Notice501008, new[] { 22 }, FieldChainConversion.Direct,
            "이름 일치(징수관 계좌번호, AN6=AN6), SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 20, Notice501008, new[] { 19 }, FieldChainConversion.Truncate,
            "이름 일치(징수 기관명), 501008 AHN40 → 902614 AHN20 — 반글자 방지 절삭(P30-3), SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 21, Notice501008, new[] { 21 }, FieldChainConversion.Truncate,
            "이름 일치(징수 과목명), 501008 AHN40 → 902614 AHN20 — 반글자 방지 절삭(P30-3), SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 22, Notice501008, new[] { 23 }, FieldChainConversion.Direct,
            "이름 일치(소계정, N1=N1), SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 23, Notice501008, new[] { 24 }, FieldChainConversion.Direct,
            "이름 일치(징수 결의 회계 년도, N4=N4), SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 24, Notice501008, new[] { 30 }, FieldChainConversion.Direct,
            "이름 유사(본세, N15=N15) — 902614 #24 '납부세액(본세)' ← 501008 #30 '본세', SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 25, Notice501008, new[] { 32 }, FieldChainConversion.Direct,
            "이름 유사(교육세, N15=N15) — 902614 #25 '납부세액(교육세)' ← 501008 #32 '교육세'" +
            "(501008 #31/#32 순서가 902614 #25/#26과 반대이니 번호가 아니라 이름으로 짝지었다), SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 26, Notice501008, new[] { 31 }, FieldChainConversion.Direct,
            "이름 유사(농어촌특별세, N15=N15) — 902614 #26 '납부세액(농어촌특별세)' ← 501008 #31 '농어촌 특별세', SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 30, Notice501008, new[] { 44 }, FieldChainConversion.Direct,
            "이름 일치(납기 내후 구분, AN1=AN1), SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 31, Notice501008, new[] { 26 }, FieldChainConversion.Direct,
            "spec_open_questions.md Q6 — 501008 #26(납기일-납기내)/#28(납기일-납기후) 중 #26 고정 선택, SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 36, Notice501008, new[] { 17 }, FieldChainConversion.Direct,
            "spec_open_questions.md Q7 — #14와 동일 출처(501008 #17 납세의무자번호), SPEC 명문 없음"),
        new ChainEntry(CardApproval902614, 37, Notice501008, new[] { 18 }, FieldChainConversion.Truncate,
            "이름 일치(납세 의무자 명 → 납부자 성명), 501008 AHN40 → 902614 AHNS10 — 절삭 + 표현 변환(P30-3), SPEC 명문 없음"),

        // ── 501008 응답 → 902614 요청, 합산 ──
        new ChainEntry(CardApproval902614, 27, Notice501008, new[] { 30, 31, 32 }, FieldChainConversion.Sum,
            "spec_open_questions.md Q5 — 501008 #30(본세)+#31(농어촌특별세)+#32(교육세) 단순합, SPEC 명문 없음"),

        // ── 800000 응답 → 902614 요청 ──
        new ChainEntry(CardApproval902614, 28, CardInfo800000, new[] { 24 }, FieldChainConversion.Widen,
            "spec_open_questions.md Q3 — 800000 #24(납부대행 수수료 금액) N12 → 902614 #28(수수료) N15, 0패딩은 PosField.Pad가 처리"),
        new ChainEntry(CardApproval902614, 33, CardInfo800000, new[] { 17 }, FieldChainConversion.RepresentationChange,
            "spec_open_questions.md Q1 — 800000 #17(카드사 코드) AN2 → 902614 #33 N2"),

        // ── 902614 내부 파생(2단계 — #27/#28이 먼저 채워진 뒤 계산) ──
        new ChainEntry(CardApproval902614, 29, CardApproval902614, new[] { 27, 28 }, FieldChainConversion.Sum,
            "spec_open_questions.md Q4 — SPEC 명문 있음: p.16 자기참조 오탈자 '(#27)+(#29)'가 2026-09-22 재배포본에서 " +
            "'(#27)+(#28)'로 정정돼 #29=#27+#28과 일치한다"),

        // ── 902614 고정값 ──
        new ChainEntry(CardApproval902614, 34, null, System.Array.Empty<int>(), FieldChainConversion.Fixed,
            "spec_open_questions.md Q2 — 이번 범위는 일시불 고정(할부 선택 UI는 범위 밖, PRD §13.9)",
            fixedValue: "00"),

        // ── 501008 응답 → 800000 요청 ──
        new ChainEntry(CardInfo800000, 15, Notice501008, new[] { 30, 31, 32 }, FieldChainConversion.Sum,
            "902614 #27과 동일 합산 규칙(501008 #30+#31+#32), SPEC 명문 없음"),
        new ChainEntry(CardInfo800000, 16, Notice501008, new[] { 55 }, FieldChainConversion.Direct,
            "이름 일치(납세자 유형, AN2=AN2), SPEC 명문 없음"),
    };
}
