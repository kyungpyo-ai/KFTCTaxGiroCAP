using System;
using System.Collections.Generic;
using System.Linq;

namespace KFTCOneCAP.KioskSim.Protocol
{
    /// <summary>
    /// 전문 하나(501008 / 800000 / 902614)의 필드 전체를 담는 스키마.
    ///
    /// 생성자가 <b>자기 검증(self-validating)</b>을 수행한다: 필드들의 POSITION이 0부터
    /// 빈틈·겹침 없이 이어지고, 마지막 필드의 끝이 전문 총 길이와 정확히 일치해야 한다.
    /// 어긋나면 생성 시점(즉, 이 클래스를 참조하는 순간 static 초기화 시점)에 바로 예외가
    /// 발생한다 — 업체가 필드를 잘못 고쳐도 실행 즉시 알 수 있게 하기 위함이다
    /// (Phase 19 실행계획서 P19-2 완료 조건).
    /// </summary>
    public sealed class TelegramSchema
    {
        /// <summary>거래 구분 코드(예: "501008").</summary>
        public string TxType { get; }

        /// <summary>전문 본문 총 길이(바이트). 프레임 길이 헤더 4바이트는 포함하지 않는다.</summary>
        public int TotalLength { get; }

        /// <summary>SPEC 표 순서 그대로의 필드 목록(번호 오름차순과 동일).</summary>
        public IReadOnlyList<TelegramField> Fields { get; }

        public TelegramSchema(string txType, int totalLength, IReadOnlyList<TelegramField> fields)
        {
            if (string.IsNullOrWhiteSpace(txType))
                throw new ArgumentException("거래 구분 코드가 비어 있다.", nameof(txType));
            if (totalLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(totalLength), totalLength, $"{txType} 전문 총 길이는 1 이상이어야 한다.");
            if (fields == null || fields.Count == 0)
                throw new ArgumentException($"{txType} 전문에 필드가 하나도 없다.", nameof(fields));

            // POSITION 기준으로 정렬해 빈틈/겹침을 검사한다(선언 순서가 이미 POSITION 순이어야
            // 정상이지만, 정렬 후 검사해야 "순서를 실수로 바꿔 적었을 때"도 잡아낼 수 있다).
            var ordered = fields.OrderBy(f => f.Position).ToList();

            int expected = 0;
            foreach (var field in ordered)
            {
                if (field.Position != expected)
                {
                    throw new InvalidOperationException(
                        $"{txType} 전문 필드 POSITION 불일치: #{field.Number}({field.Name})의 POSITION={field.Position}, " +
                        $"기대값={expected}(직전 필드까지의 누적 길이). 빈틈 또는 겹침이 있다.");
                }
                expected = field.End;
            }

            if (expected != totalLength)
            {
                var last = ordered[ordered.Count - 1];
                throw new InvalidOperationException(
                    $"{txType} 전문 총 길이 불일치: 필드 누적 끝={expected}(마지막 필드 #{last.Number} {last.Name} 기준), " +
                    $"기대 총 길이={totalLength}.");
            }

            // 번호 중복도 함께 검사한다 — POSITION 검사만으로는 "같은 번호를 두 번 적은 실수"를
            // 잡을 수 없다.
            var duplicateNumbers = fields
                .GroupBy(f => f.Number)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicateNumbers.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{txType} 전문에 번호가 중복된 필드가 있다: #{string.Join(", #", duplicateNumbers)}.");
            }

            TxType = txType;
            TotalLength = totalLength;
            Fields = fields;
        }

        /// <summary>필드 번호로 조회한다. 없으면 예외.</summary>
        public TelegramField ByNumber(int number)
        {
            foreach (var field in Fields)
            {
                if (field.Number == number)
                    return field;
            }
            throw new KeyNotFoundException($"{TxType} 전문에 #{number} 필드가 없다.");
        }

        /// <summary>SET 장소가 kiosk가 아닌 필드만 뽑는다(P19-2 완료 조건 3번, P19-5의 "회색 비활성" 판정에도 재사용).</summary>
        public IEnumerable<TelegramField> NonKioskFields()
            => Fields.Where(f => f.SetLocation != TelegramSetLocation.Kiosk).OrderBy(f => f.Number);
    }

    /// <summary>
    /// 3전문(501008/800000/902614)의 필드 테이블 정의.
    ///
    /// Phase 19 실행계획서 P19-2: 본 앱(KFTCOneCAP.Wpf)의 Protocol/Pos/Schemas는 이 파일을
    /// 작성하는 동안 한 번도 열어보지 않았다 — SPEC 원문
    /// (docs/payment_relay/spec/국세 베리어프리 키오스크용 전산설계서(POS-원캡)_20260826.pdf)을
    /// 처음부터 다시 읽고 손으로 옮겨 적었다. 대조는 이 파일을 완성한 뒤 별도로 수행했고, 그
    /// 결과(및 이 과정에서 실제로 잡아낸 오타 1건)는 development_plan.md의 P19-2 항목에
    /// 기록되어 있다.
    ///
    /// 공통부(#1~#13, POSITION 0~70)는 3전문 모두 POSITION·길이·표현은 완전히 동일하지만,
    /// <b>SET 장소(누가 채우는가)는 전문마다 다르다</b> — SPEC이 이 표를 전문마다 통째로 다시
    /// 그려 두었고, 실제로 체크된 열이 전문마다 다르다(예: #13 FILLER는 501008/800000은 kiosk
    /// 열이 체크되어 있지만 902614는 체크되어 있지 않다 — 인터넷지로 열만 체크). 그래서 아래 3개
    /// Build*Fields 메서드는 공통부를 공유하지 않고 각자 채운다 — SPEC 원문과 똑같은 구조다.
    /// </summary>
    public static class TelegramSchemas
    {
        // ------------------------------------------------------------------
        // 나. 국고 상세 고지내역 조회 전문 (501008) — SPEC 2-나(p.7~8), 총 길이 706
        //
        // 조회정보부(#14~#56)는 실 납부자번호(#16, 조회 키)와 전자납부번호(#14, 조회 대상 식별자)만
        // kiosk가 채우고, 나머지는 전부 디지털예산(dBrain)이 응답으로 채워 돌려주는 조회 결과다.
        // ------------------------------------------------------------------
        private static List<TelegramField> BuildNotice501008Fields()
        {
            var fields = new List<TelegramField>
            {
                // 공통부(#1~13, p.7 표: 디지털예산/인터넷지로/kiosk 3열).
                new TelegramField(1, "업무 구분", TelegramRepresentation.A, 3, 0, TelegramSetLocation.Kiosk,
                    "고정값 \"IGN\"."),
                new TelegramField(2, "요청기관 코드", TelegramRepresentation.N, 3, 3, TelegramSetLocation.Kiosk,
                    "고정값 \"095\"."),
                new TelegramField(3, "전문 종별 코드", TelegramRepresentation.N, 4, 6, TelegramSetLocation.Kiosk,
                    "요청 시 \"0200\". SPEC 표는 디지털예산/인터넷지로/kiosk 3열 모두 체크 — 응답 시 " +
                    "\"0210\"으로 재기록되지만, 요청을 만드는 kiosk를 대표값으로 선택."),
                new TelegramField(4, "거래 구분 코드", TelegramRepresentation.N, 6, 10, TelegramSetLocation.Kiosk,
                    "\"501008\" 고정."),
                new TelegramField(5, "상태 코드", TelegramRepresentation.N, 3, 16, TelegramSetLocation.Kiosk,
                    "SPEC 표(p.7)는 이 전문에서 디지털예산/인터넷지로/kiosk 3열 모두 체크되어 있다 " +
                    "(800000의 #5는 kiosk 열이 체크되어 있지 않아 다르다 — 전문마다 SET 장소가 다르다는 " +
                    "것을 보여주는 실제 사례. 902614는 kiosk 열이 체크되어 있으나 동일하게 공백으로 " +
                    "충분하다 — 이 필드 자체가 요청 시 채울 필요가 없는 필드이기 때문, 바로 아래 참고). " +
                    "**요청 시에는 채우지 않아도 된다 — 공백으로 보내면 충분하다(2026-08-28 사용자 확인). " +
                    "전송 화면에서도 편집을 막아 둔다(값을 넣을 수 없게 잠금).**", alwaysBlank: true),
                new TelegramField(6, "송·수신 FLAG", TelegramRepresentation.AN, 1, 19, TelegramSetLocation.Kiosk,
                    "SPEC 표 3열 모두 체크. 요청 시 \"G\". tools/spec_client.ps1도 공통부에서 무조건 " +
                    "\"G\"를 SET하고 실장비 왕복에 성공했다."),
                new TelegramField(7, "응답 코드", TelegramRepresentation.AN, 3, 20, TelegramSetLocation.InternetGiro,
                    "요청 전문에서는 SPACE(SPEC #7 설명). SPEC 표는 디지털예산+인터넷지로 열만 체크, " +
                    "kiosk 열은 체크되어 있지 않다."),
                new TelegramField(8, "전송 일시", TelegramRepresentation.N, 12, 23, TelegramSetLocation.Kiosk,
                    "YYMMDDhhmmss. SPEC 표 3열 모두 체크 — 요청을 보내는 kiosk가 현재 시각을 채운다."),
                new TelegramField(9, "요청기관 전문 관리 번호", TelegramRepresentation.AN, 12, 35, TelegramSetLocation.Kiosk,
                    "구분코드(AN,3,\"0EC\")+\"0\"(Reserved)+일련번호(8). 800000/902614 표는 같은 자리를 " +
                    "\"은행/센터 전문 관리 번호\"라는 이름으로 쓰지만 POSITION·길이·SET(kiosk)은 동일하다."),
                new TelegramField(10, "이용기관/센터 전문 관리 번호", TelegramRepresentation.AN, 12, 47, TelegramSetLocation.InternetGiro,
                    "SPEC 표는 디지털예산 열만 체크 — 센터/dBrain이 부여하는 응답 전용 일련번호."),
                new TelegramField(11, "지로 이용기관 분류코드", TelegramRepresentation.N, 2, 59, TelegramSetLocation.Kiosk,
                    "고정값 \"01\". SPEC 표는 디지털예산+kiosk 열 체크."),
                new TelegramField(12, "지로 이용기관 지로번호", TelegramRepresentation.N, 7, 61, TelegramSetLocation.Kiosk,
                    "SPEC 표는 디지털예산+kiosk 열 체크."),
                new TelegramField(13, "FILLER", TelegramRepresentation.N, 2, 68, TelegramSetLocation.Kiosk,
                    "SPEC 표 3열 모두 체크. 요청 시 kiosk가 space로 채운다. 전송 화면에서는 편집을 " +
                    "막아 둔다(FILLER라 정의된 값이 없다, 2026-08-28 확정).", alwaysBlank: true),

                // 조회정보부(#14~56, p.7~8).
                new TelegramField(14, "전자납부번호", TelegramRepresentation.AN, 19, 70, TelegramSetLocation.Kiosk,
                    "조회 대상 전자납부번호. SPEC 표(p.7)는 kiosk 열만 체크되어 있다 — 이 전문의 1차 " +
                    "조회 키다(#16 실 납부자번호는 국세청 연대 납부 등 특수 케이스의 보조 키)."),
                new TelegramField(15, "납부 순번", TelegramRepresentation.N, 3, 89, TelegramSetLocation.InternetGiro,
                    "dBrain이 '001'부터 순차 채번(SPEC 설명). 응답 전용."),
                new TelegramField(16, "실 납부자번호(고객관리번호)", TelegramRepresentation.AN, 13, 92, TelegramSetLocation.Kiosk,
                    "국세청 연대 납부인 경우 필수(SPEC 설명). SPEC 표는 디지털예산+kiosk 열 체크."),
                new TelegramField(17, "납세 의무자 번호", TelegramRepresentation.AN, 13, 105, TelegramSetLocation.InternetGiro),
                new TelegramField(18, "납세 의무자 명", TelegramRepresentation.AHN, 40, 118, TelegramSetLocation.InternetGiro),
                new TelegramField(19, "징수 기관명", TelegramRepresentation.AHN, 40, 158, TelegramSetLocation.InternetGiro),
                new TelegramField(20, "징수 과목 코드(세목 코드)", TelegramRepresentation.AN, 7, 198, TelegramSetLocation.InternetGiro),
                new TelegramField(21, "징수 과목명", TelegramRepresentation.AHN, 40, 205, TelegramSetLocation.InternetGiro),
                new TelegramField(22, "징수관 계좌번호", TelegramRepresentation.AN, 6, 245, TelegramSetLocation.InternetGiro),
                new TelegramField(23, "소계정", TelegramRepresentation.N, 1, 251, TelegramSetLocation.InternetGiro),
                new TelegramField(24, "징수 결의 회계 년도", TelegramRepresentation.N, 4, 252, TelegramSetLocation.InternetGiro),
                new TelegramField(25, "납기내 금액", TelegramRepresentation.N, 15, 256, TelegramSetLocation.InternetGiro),
                new TelegramField(26, "납기일 (납기내)", TelegramRepresentation.N, 8, 271, TelegramSetLocation.InternetGiro),
                new TelegramField(27, "납기후 금액", TelegramRepresentation.N, 15, 279, TelegramSetLocation.InternetGiro),
                new TelegramField(28, "납기일 (납기후)", TelegramRepresentation.N, 8, 294, TelegramSetLocation.InternetGiro),
                new TelegramField(29, "고지서 유형", TelegramRepresentation.AN, 1, 302, TelegramSetLocation.InternetGiro,
                    "1: 본세+가산금, 2: 본세+농특세+교육세+가산금, 3: 그 외 전세목 포함(SPEC 설명 표)."),
                new TelegramField(30, "본세", TelegramRepresentation.N, 15, 303, TelegramSetLocation.InternetGiro),
                new TelegramField(31, "농어촌 특별세", TelegramRepresentation.N, 15, 318, TelegramSetLocation.InternetGiro),
                new TelegramField(32, "교육세", TelegramRepresentation.N, 15, 333, TelegramSetLocation.InternetGiro),
                new TelegramField(33, "특별 소비세", TelegramRepresentation.N, 15, 348, TelegramSetLocation.InternetGiro),
                new TelegramField(34, "주세", TelegramRepresentation.N, 15, 363, TelegramSetLocation.InternetGiro),
                new TelegramField(35, "부가가치세", TelegramRepresentation.N, 15, 378, TelegramSetLocation.InternetGiro),
                new TelegramField(36, "교통세", TelegramRepresentation.N, 15, 393, TelegramSetLocation.InternetGiro),
                new TelegramField(37, "방위세", TelegramRepresentation.N, 15, 408, TelegramSetLocation.InternetGiro),
                new TelegramField(38, "예비 정보 FIELD", TelegramRepresentation.N, 15, 423, TelegramSetLocation.InternetGiro),
                new TelegramField(39, "가산금", TelegramRepresentation.N, 15, 438, TelegramSetLocation.InternetGiro),
                new TelegramField(40, "회계명", TelegramRepresentation.AHN, 40, 453, TelegramSetLocation.InternetGiro),
                new TelegramField(41, "소관명", TelegramRepresentation.AHN, 40, 493, TelegramSetLocation.InternetGiro,
                    "수납 소관 기관명. 예) 경찰청(SPEC 설명)."),
                new TelegramField(42, "납부자 주소", TelegramRepresentation.AHN, 80, 533, TelegramSetLocation.InternetGiro,
                    "특허청은 \"출원인 명칭(40)+사건 번호(15)+서류명(25)\"으로 구성(SPEC 설명)."),
                new TelegramField(43, "수입 신고 번호", TelegramRepresentation.AN, 15, 613, TelegramSetLocation.InternetGiro),
                new TelegramField(44, "납기 내후 구분", TelegramRepresentation.AN, 1, 628, TelegramSetLocation.InternetGiro),
                new TelegramField(45, "납부 금액 수정 허용 유무", TelegramRepresentation.AN, 1, 629, TelegramSetLocation.InternetGiro,
                    "Y: 수정 허용(제한 없음), P: 부분 납부 허용(고지 금액 이내), N: 수정 금지(SPEC 설명)."),
                new TelegramField(46, "연대 납부 대상 유무", TelegramRepresentation.AN, 1, 630, TelegramSetLocation.InternetGiro,
                    "Y: 연대 납부 대상, N: 대상 아님(SPEC 설명)."),
                new TelegramField(47, "수납은행 점별 코드", TelegramRepresentation.N, 7, 631, TelegramSetLocation.InternetGiro),
                new TelegramField(48, "납부 일시", TelegramRepresentation.N, 14, 638, TelegramSetLocation.InternetGiro),
                new TelegramField(49, "고지 일자", TelegramRepresentation.N, 8, 652, TelegramSetLocation.InternetGiro),
                new TelegramField(50, "대리 납부 허용 유무", TelegramRepresentation.N, 1, 660, TelegramSetLocation.InternetGiro,
                    "0: 대리 납부 불허, 1: 대리 납부 허용. 국세 간편계좌납부는 이 값과 무관하게 항상 허용(SPEC 설명)."),
                new TelegramField(51, "기 납부 금액", TelegramRepresentation.N, 15, 661, TelegramSetLocation.InternetGiro,
                    "분납 시 발생하는 기 납부 금액(SPEC 설명)."),
                new TelegramField(52, "잔여 납부할 금액", TelegramRepresentation.N, 15, 676, TelegramSetLocation.InternetGiro,
                    "분납 시 발생하는 잔여 납부할 금액(SPEC 설명)."),
                new TelegramField(53, "분야(기능) 코드", TelegramRepresentation.AN, 3, 691, TelegramSetLocation.InternetGiro),
                new TelegramField(54, "신용카드 납부 가능 여부", TelegramRepresentation.AN, 1, 694, TelegramSetLocation.InternetGiro,
                    "Y: 가능, N: 제한(SPEC 설명)."),
                new TelegramField(55, "납세자 유형", TelegramRepresentation.AN, 2, 695, TelegramSetLocation.InternetGiro,
                    "'10' 일괄인하(Default) / '20','30' 영세사업자 추가인하 / '40' 대형사업자 인하제외(SPEC 표)."),
                new TelegramField(56, "예비 정보 FIELD", TelegramRepresentation.AN, 9, 697, TelegramSetLocation.InternetGiro),
            };
            return fields;
        }

        // ------------------------------------------------------------------
        // 다. 카드 정보 조회 전문 (800000) — SPEC 2-다(p.12), 총 길이 500
        //
        // 정보부(#14~#27) 중 #14~16만 요청 입력(kiosk가 채워야 조회가 성립)이고, #17~27은 조회
        // 결과(인터넷지로 응답 전용)다. #14~16 중에서는 #14 BIN만 kiosk가 아니라 원캡이 채운다 —
        // 리더기로 카드를 태그해야 얻을 수 있는 값이기 때문(SPEC 표에서 #14 행은 "원캡" 열만
        // 체크되어 있다).
        // ------------------------------------------------------------------
        private static List<TelegramField> BuildCardInfo800000Fields()
        {
            var fields = new List<TelegramField>
            {
                // 공통부(#1~13, p.12 표: 인터넷지로/VAN/kiosk/원캡 4열).
                new TelegramField(1, "업무 구분", TelegramRepresentation.A, 3, 0, TelegramSetLocation.Kiosk,
                    "고정값 \"IGN\"."),
                new TelegramField(2, "요청기관 코드", TelegramRepresentation.N, 3, 3, TelegramSetLocation.Kiosk,
                    "고정값 \"095\"."),
                new TelegramField(3, "전문 종별 코드", TelegramRepresentation.N, 4, 6, TelegramSetLocation.Kiosk,
                    "요청 시 \"0200\". SPEC 표는 VAN+kiosk 열 체크(인터넷지로 열은 체크 안 됨 — 501008과 " +
                    "다른 점)."),
                new TelegramField(4, "거래 구분 코드", TelegramRepresentation.N, 6, 10, TelegramSetLocation.Kiosk,
                    "\"800000\" 고정."),
                new TelegramField(5, "상태 코드", TelegramRepresentation.N, 3, 16, TelegramSetLocation.InternetGiro,
                    "SPEC 표는 인터넷지로+VAN 열만 체크, kiosk 열은 체크되어 있지 않다(501008/902614와 다름 " +
                    "— 다만 501008/902614도 이 필드는 요청 시 채울 필요가 없어(공백으로 충분, 2026-08-28 " +
                    "사용자 확인) 실질적인 요청 값 관점에서는 3전문 모두 결과가 같다)."),
                new TelegramField(6, "송·수신 FLAG", TelegramRepresentation.AN, 1, 19, TelegramSetLocation.Kiosk,
                    "**정정(2026-08-28)**: SPEC 표(p.12)는 이 행에서 인터넷지로+**kiosk** 열이 체크되어 " +
                    "있다(VAN 열이 아니다 — 초기 전사와 `pos-onecap-spec-expert` 재확인 둘 다 kiosk 열을 " +
                    "놓치고 InternetGiro로 잘못 분류했었다. 처음엔 사용자 지적을 받고도 저해상도로 다시 " +
                    "보다가 VAN 열로 착각해 재차 정정할 뻔했으나, 사용자가 하이라이트로 표시해 준 캡처로 " +
                    "kiosk 열이 맞음을 최종 확인했다). 요청 시 \"G\"(spec_client.ps1도 3전문 공통으로 채워 " +
                    "실장비 왕복 성공)."),
                new TelegramField(7, "응답 코드", TelegramRepresentation.AN, 3, 20, TelegramSetLocation.InternetGiro,
                    "요청 시 SPACE. SPEC 표는 인터넷지로 열만 체크."),
                new TelegramField(8, "전송 일시", TelegramRepresentation.N, 12, 23, TelegramSetLocation.Kiosk,
                    "**정정(2026-08-28)**: SPEC 표(p.12)는 이 행에서도 인터넷지로+**kiosk** 열이 체크되어 " +
                    "있다(#5 상태 코드는 인터넷지로+VAN이라 #6/#8과 다르다 — 한 표 안에서도 행마다 두 번째 " +
                    "체크 열이 VAN/kiosk로 갈린다는 게 이번에 확인된 사실). 초기 전사와 " +
                    "`pos-onecap-spec-expert` 재확인 둘 다 이 kiosk 열을 놓쳤다. 요청 시 " +
                    "YYMMDDhhmmss(spec_client.ps1도 3전문 공통으로 채워 실장비 왕복 성공)."),
                new TelegramField(9, "은행/센터 전문 관리 번호", TelegramRepresentation.AN, 12, 35, TelegramSetLocation.Kiosk,
                    "SPEC 표는 kiosk 열만 체크."),
                new TelegramField(10, "이용기관/센터 전문 관리 번호", TelegramRepresentation.AN, 12, 47, TelegramSetLocation.Kiosk,
                    "SPEC 표에 SET 장소 체크가 전혀 없다(공란) — 공통부 규칙(체크 없는 필드는 kiosk가 " +
                    "space로 채움)에 따라 Kiosk로 분류했다. 전송 화면에서는 편집을 막아 둔다(현재 정의된 " +
                    "실제 값이 없다, 2026-08-28 확정 — 필요해지면 다시 편집 가능하게 바꾼다).", alwaysBlank: true),
                new TelegramField(11, "이용기관 발행기관 분류코드", TelegramRepresentation.N, 2, 59, TelegramSetLocation.Kiosk,
                    "SPEC 표는 kiosk 열만 체크."),
                new TelegramField(12, "이용기관 지로 번호", TelegramRepresentation.N, 7, 61, TelegramSetLocation.Kiosk,
                    "SPEC 표는 kiosk 열만 체크."),
                new TelegramField(13, "FILLER (응답 코드 구분)", TelegramRepresentation.N, 2, 68, TelegramSetLocation.Kiosk,
                    "SPEC 표에 SET 장소 체크가 전혀 없다(공란) — #10과 같은 이유로 Kiosk로 분류. 전송 " +
                    "화면에서는 편집을 막아 둔다(FILLER라 정의된 값이 없다, 2026-08-28 확정).", alwaysBlank: true),

                // 정보부(#14~27, p.12).
                new TelegramField(14, "BIN", TelegramRepresentation.AN, 8, 70, TelegramSetLocation.OneCap,
                    "카드 리딩 결과에서 나오는 카드번호 앞자리(BIN). SPEC 표에서 \"원캡\" 열만 체크되어 있다 — " +
                    "kiosk가 채우지 않는 유일한 요청-입력 필드(#14~16 중)."),
                new TelegramField(15, "납부세액", TelegramRepresentation.N, 15, 78, TelegramSetLocation.Kiosk),
                new TelegramField(16, "납세자 유형", TelegramRepresentation.AN, 2, 93, TelegramSetLocation.Kiosk,
                    "501008 #55와 같은 코드 체계('10'/'20'/'30'/'40')."),
                new TelegramField(17, "카드사 코드", TelegramRepresentation.AN, 2, 95, TelegramSetLocation.InternetGiro,
                    "조회 결과(응답 전용). BIN에 해당하는 카드사를 인터넷지로가 응답."),
                new TelegramField(18, "카드사명", TelegramRepresentation.AHN, 30, 97, TelegramSetLocation.InternetGiro),
                new TelegramField(19, "체크카드 여부", TelegramRepresentation.AN, 1, 127, TelegramSetLocation.InternetGiro),
                new TelegramField(20, "납부가능 시간 여부", TelegramRepresentation.AN, 1, 128, TelegramSetLocation.InternetGiro),
                new TelegramField(21, "포인트 납부 가능 여부", TelegramRepresentation.AN, 1, 129, TelegramSetLocation.InternetGiro),
                new TelegramField(22, "카드 할부개월 LIST", TelegramRepresentation.AN, 60, 130, TelegramSetLocation.InternetGiro,
                    "할부개월수 2Byte 단위 코드를 연속 구성. 예: 01020304 (space padding), 총 60Byte로 최대 30개(SPEC 각주)."),
                new TelegramField(23, "포인트 할부개월 LIST", TelegramRepresentation.AN, 60, 190, TelegramSetLocation.InternetGiro,
                    "형식은 #22와 동일."),
                new TelegramField(24, "납부대행 수수료 금액", TelegramRepresentation.N, 12, 250, TelegramSetLocation.InternetGiro),
                new TelegramField(25, "합계금액", TelegramRepresentation.N, 12, 262, TelegramSetLocation.InternetGiro),
                // #26 신규 추가(SPEC 20260831 개정) — 뒤따르는 #27/#28은 이 삽입으로 번호·POSITION이
                // 한 칸씩 밀렸다(20260826판에서는 #26/#27이었음). 본 앱(Protocol/Pos/Schemas/
                // CardInfoInquirySchema.cs)과 독립적으로 SPEC PDF를 다시 옮겨 적었다(P19-2 원칙 유지).
                new TelegramField(26, "납부대행 수수료율", TelegramRepresentation.N, 4, 274, TelegramSetLocation.InternetGiro,
                    "SPEC 20260831 개정판 신규 필드 — 인터넷지로 열만 체크(응답 전용, kiosk/원캡/VAN 전부 공란)."),
                new TelegramField(27, "API 세부 응답코드", TelegramRepresentation.AN, 6, 278, TelegramSetLocation.InternetGiro),
                new TelegramField(28, "예비 정보 FIELD", TelegramRepresentation.AN, 216, 284, TelegramSetLocation.InternetGiro,
                    "SPEC 표(p.12)는 인터넷지로 열이 체크되어 있다(직접 확인) — 응답 전용. #26 신규 삽입으로 " +
                    "길이가 220→216으로 축소(총 길이 500 유지)."),
            };
            return fields;
        }

        // ------------------------------------------------------------------
        // 라. 국고 신용카드 승인 요청 전문 (보안단말기 거래용, 902614) — SPEC 2-라(p.13~14), 총 길이 1500
        //
        // 납부정보부(#14~#54) 중 카드 하드웨어 관련 필드(#43/44/45/46/48/50/53)는 원캡이 채운다
        // (SPEC 표에서 "원캡" 열이 체크됨). #51(암호화된 비밀번호 정보)은 SPEC 표 자체는 kiosk
        // 열을 체크해 두었지만, Phase 18 실장비 검증에서 PIN은 kiosk가 평문으로 다루지 않고
        // 리더기 핀패드→원캡이 암호화해 채우는 것으로 확정되었다 — 이 저장소는 그 결정을 따라
        // SPEC 표의 체크와 다르게 OneCap으로 분류한다(아래 필드 주석에 근거 상세, SPEC과 다르게
        // 분류한 유일한 필드).
        // #52(선불카드 잔액)는 인터넷지로 응답 전용(승인 후 돌아오는 잔액 정보)이라 kiosk도
        // 원캡도 아니다 — "kiosk가 아닌 필드" 목록에는 들어가지만 카드 하드웨어 관련 8개 필드와는
        // 성격이 다르므로 구분해서 기록한다(development_plan.md P19-2 항목 참고).
        // ------------------------------------------------------------------
        private static List<TelegramField> BuildCardApproval902614Fields()
        {
            var fields = new List<TelegramField>
            {
                // 공통부(#1~13, p.13 표: 인터넷지로/kiosk/원캡 3열, VAN 열 없음).
                new TelegramField(1, "업무 구분", TelegramRepresentation.A, 3, 0, TelegramSetLocation.Kiosk,
                    "고정값 \"IGN\"."),
                new TelegramField(2, "요청기관 코드", TelegramRepresentation.N, 3, 3, TelegramSetLocation.Kiosk,
                    "고정값 \"095\"."),
                new TelegramField(3, "전문 종별 코드", TelegramRepresentation.N, 4, 6, TelegramSetLocation.Kiosk,
                    "요청 시 \"0200\". SPEC 표는 인터넷지로+kiosk 열 체크."),
                new TelegramField(4, "거래 구분 코드", TelegramRepresentation.N, 6, 10, TelegramSetLocation.Kiosk,
                    "\"902614\" 고정."),
                new TelegramField(5, "상태 코드", TelegramRepresentation.N, 3, 16, TelegramSetLocation.Kiosk,
                    "SPEC 표는 인터넷지로+kiosk 열 체크(800000과 달리 kiosk 열도 체크되어 있다 — 직접 대조로 " +
                    "확인). **다만 요청 시에는 채우지 않아도 되는 필드다 — 공백으로 보내면 충분하다" +
                    "(2026-08-28 사용자 확인). 전송 화면에서도 편집을 막아 둔다(값을 넣을 수 없게 잠금).**",
                    alwaysBlank: true),
                new TelegramField(6, "송·수신 FLAG", TelegramRepresentation.AN, 1, 19, TelegramSetLocation.Kiosk,
                    "SPEC 표는 인터넷지로+kiosk 열 체크. 요청 시 \"G\"(tools/spec_client.ps1에서 실장비 " +
                    "왕복 확인된 값)."),
                new TelegramField(7, "응답 코드", TelegramRepresentation.AN, 3, 20, TelegramSetLocation.InternetGiro,
                    "요청 시 SPACE. SPEC 표는 인터넷지로 열만 체크."),
                new TelegramField(8, "전송 일시", TelegramRepresentation.N, 12, 23, TelegramSetLocation.Kiosk,
                    "SPEC 표는 인터넷지로+kiosk 열 체크."),
                new TelegramField(9, "은행/센터 전문 관리 번호", TelegramRepresentation.AN, 12, 35, TelegramSetLocation.Kiosk,
                    "SPEC 표는 kiosk 열만 체크. 구분코드(AN,3,\"0EC\")+\"0\"(Reserved)+일련번호(8)."),
                new TelegramField(10, "이용기관/센터 전문 관리 번호", TelegramRepresentation.AN, 12, 47, TelegramSetLocation.InternetGiro,
                    "SPEC 표는 인터넷지로 열만 체크 — 응답 전용."),
                new TelegramField(11, "이용기관 발행기관 분류코드", TelegramRepresentation.N, 2, 59, TelegramSetLocation.Kiosk,
                    "고정값 \"01\". SPEC 표는 인터넷지로+kiosk 열 체크."),
                new TelegramField(12, "이용기관 지로 번호", TelegramRepresentation.N, 7, 61, TelegramSetLocation.Kiosk,
                    "SPEC 표는 인터넷지로+kiosk 열 체크."),
                new TelegramField(13, "FILLER (응답 코드 구분)", TelegramRepresentation.N, 2, 68, TelegramSetLocation.InternetGiro,
                    "SPEC 표는 인터넷지로 열만 체크, kiosk 열은 체크되어 있지 않다(501008/800000과 다름)."),

                // 납부정보부(#14~54, p.13~14).
                new TelegramField(14, "주민(사업자,법인)등록번호", TelegramRepresentation.AN, 13, 70, TelegramSetLocation.Kiosk),
                new TelegramField(15, "전자납부번호", TelegramRepresentation.AN, 19, 83, TelegramSetLocation.Kiosk),
                new TelegramField(16, "납부 순번", TelegramRepresentation.AN, 3, 102, TelegramSetLocation.Kiosk),
                new TelegramField(17, "예비 정보 FIELD", TelegramRepresentation.AN, 8, 105, TelegramSetLocation.Kiosk,
                    "SPEC 표에 SET 장소 체크가 없다 — 미사용 예비 필드. 공통부 규칙(체크 없는 필드는 kiosk가 " +
                    "space로 채움)에 따라 Kiosk로 분류했다. 전송 화면에서는 편집을 막아 둔다(FILLER류라 " +
                    "정의된 값이 없다, 2026-08-28 확정).", alwaysBlank: true),
                new TelegramField(18, "징수 과목 코드(세목 코드)", TelegramRepresentation.N, 7, 113, TelegramSetLocation.Kiosk),
                new TelegramField(19, "징수관 계좌번호", TelegramRepresentation.AN, 6, 120, TelegramSetLocation.Kiosk),
                new TelegramField(20, "징수 기관명", TelegramRepresentation.AHN, 20, 126, TelegramSetLocation.Kiosk),
                new TelegramField(21, "징수 과목명", TelegramRepresentation.AHN, 20, 146, TelegramSetLocation.Kiosk),
                new TelegramField(22, "소계정", TelegramRepresentation.N, 1, 166, TelegramSetLocation.Kiosk),
                new TelegramField(23, "징수 결의 회계 년도", TelegramRepresentation.N, 4, 167, TelegramSetLocation.Kiosk),
                new TelegramField(24, "납부세액(본세)", TelegramRepresentation.N, 15, 171, TelegramSetLocation.Kiosk,
                    "SPEC 표는 \"납부 세액\" 열 아래 \"본세\"로 표기(구성 필드 24~26)."),
                new TelegramField(25, "납부세액(교육세)", TelegramRepresentation.N, 15, 186, TelegramSetLocation.Kiosk),
                new TelegramField(26, "납부세액(농어촌특별세)", TelegramRepresentation.N, 15, 201, TelegramSetLocation.Kiosk),
                new TelegramField(27, "납부 세액", TelegramRepresentation.N, 15, 216, TelegramSetLocation.Kiosk,
                    "본세+교육세+농특세 등을 합산한 총 납부세액(#24~26과는 별개 필드)."),
                new TelegramField(28, "수수료", TelegramRepresentation.N, 15, 231, TelegramSetLocation.Kiosk),
                new TelegramField(29, "총 납부 금액", TelegramRepresentation.N, 15, 246, TelegramSetLocation.Kiosk),
                new TelegramField(30, "납기 내후 구분", TelegramRepresentation.AN, 1, 261, TelegramSetLocation.Kiosk),
                new TelegramField(31, "납기 일자", TelegramRepresentation.N, 8, 262, TelegramSetLocation.Kiosk),
                new TelegramField(32, "납부 일자", TelegramRepresentation.N, 8, 270, TelegramSetLocation.Kiosk),
                new TelegramField(33, "카드사 코드", TelegramRepresentation.N, 2, 278, TelegramSetLocation.Kiosk),
                new TelegramField(34, "할부 개월 수", TelegramRepresentation.N, 2, 280, TelegramSetLocation.Kiosk),
                new TelegramField(35, "연락 전화 번호", TelegramRepresentation.ANS, 14, 282, TelegramSetLocation.Kiosk),
                new TelegramField(36, "납부자 주민(사업자)등록번호", TelegramRepresentation.AN, 13, 296, TelegramSetLocation.Kiosk),
                new TelegramField(37, "납부자 성명", TelegramRepresentation.AHNS, 10, 309, TelegramSetLocation.Kiosk),
                new TelegramField(38, "카드소유주 주민(사업자)등록번호", TelegramRepresentation.AN, 13, 319, TelegramSetLocation.Kiosk,
                    "SPEC 표에 SET 장소 체크가 없다 — 카드 소유주가 납부자와 다른 경우에 대비한 선택 입력으로 " +
                    "보이며, 공통부 규칙에 따라 Kiosk로 분류(체크 없으면 kiosk가 space 또는 값 채움)."),
                new TelegramField(39, "납부 이용 시스템", TelegramRepresentation.AN, 1, 332, TelegramSetLocation.Kiosk,
                    "고정값 \"O\"(SPEC 표에는 별도 설명 없음, spec_client.ps1에서 확인된 값)."),
                new TelegramField(40, "기 납부 이용 시스템", TelegramRepresentation.AN, 1, 333, TelegramSetLocation.InternetGiro,
                    "SPEC 표에서 \"인터넷지로\" 열만 체크됨 — 과거(기존) 납부 채널을 응답으로 알려주는 필드로 보인다."),
                new TelegramField(41, "납부 형태 구분", TelegramRepresentation.AN, 1, 334, TelegramSetLocation.Kiosk,
                    "베리어프리 조회납부는 \"Q\"(spec_client.ps1에서 확인된 값)."),
                new TelegramField(42, "키오스크 고유번호", TelegramRepresentation.AN, 20, 335, TelegramSetLocation.Kiosk),
                new TelegramField(43, "보안단말기 인증번호", TelegramRepresentation.ANS, 32, 355, TelegramSetLocation.OneCap,
                    "리더기(보안단말기)가 만들어내는 인증번호. 원캡이 ReaderSerial.dll로 리딩해 채운다."),
                new TelegramField(44, "FALLBACK CODE", TelegramRepresentation.N, 2, 387, TelegramSetLocation.OneCap),
                new TelegramField(45, "복호화 정보", TelegramRepresentation.AN, 18, 389, TelegramSetLocation.OneCap),
                new TelegramField(46, "암호화된 카드정보", TelegramRepresentation.AN, 196, 407, TelegramSetLocation.OneCap),
                new TelegramField(47, "예비 정보 FIELD", TelegramRepresentation.AN, 6, 603, TelegramSetLocation.Kiosk,
                    "SPEC 표에 SET 장소 체크가 없다 — 미사용 예비 필드. 전송 화면에서는 편집을 막아 " +
                    "둔다(2026-08-28 확정).", alwaysBlank: true),
                new TelegramField(48, "거래 입력 유형 (IC 전문 전용)", TelegramRepresentation.AN, 1, 609, TelegramSetLocation.OneCap,
                    "과거 정리에서 kiosk로 잘못 읽었던 전례가 있는 필드(development_plan.md 기록) — SPEC 표를 다시 " +
                    "확인한 결과 \"원캡\" 열만 체크되어 있다. IC 카드 리딩 결과에 따라 원캡이 채운다."),
                new TelegramField(49, "납부카드 구분", TelegramRepresentation.AN, 1, 610, TelegramSetLocation.Kiosk,
                    "0: 개인카드(spec_client.ps1에서 확인된 값)."),
                new TelegramField(50, "신용카드 승인 인증방식", TelegramRepresentation.AN, 1, 611, TelegramSetLocation.OneCap),
                new TelegramField(51, "암호화된 비밀번호 정보", TelegramRepresentation.ANS, 100, 612, TelegramSetLocation.OneCap,
                    "**SPEC 표 원문은 이 필드를 \"kiosk\" 열로 체크해 두었으나, Phase 18에서 PIN은 kiosk가 " +
                    "평문으로 취급하지 않고 알림창 화면 키패드(터치+물리 키보드, 리더기 핀패드가 아니다 — " +
                    "Pinpad_SendCommand 계열은 Phase 18 범위 밖으로 명시적으로 제외됨)로 입력받아 원캡이 " +
                    "암호화해 채우는 것으로 확정됐다(development_plan.md Phase 18 실행계획서, PIN 보안 " +
                    "요구사항). 이 스키마는 SPEC 체크보다 그 실측/보안 결정을 우선해 OneCap으로 분류한다 " +
                    "— SPEC과 다르게 분류한 유일한 필드다.**"),
                new TelegramField(52, "선불카드 잔액", TelegramRepresentation.AN, 12, 712, TelegramSetLocation.InternetGiro,
                    "승인 후 응답으로 돌아오는 선불카드 잔여 잔액 — 요청 입력이 아니라 응답 전용 필드다. 카드 " +
                    "하드웨어 리딩 필드(#43~50)와는 성격이 다르다(SPEC 표에서 \"인터넷지로\" 열만 체크)."),
                new TelegramField(53, "EMV DATA", TelegramRepresentation.ANS, 604, 724, TelegramSetLocation.OneCap,
                    "SPEC 표는 \"인터넷지로\"와 \"원캡\" 두 열이 동시에 체크되어 있다 — EMV 원본 데이터는 IC 카드 " +
                    "리더기가 만들어 원캡이 채워 넣고(요청), 인터넷지로/VAN은 승인 처리를 위해 그 값을 그대로 " +
                    "중계·소비하는 것으로 해석해 OneCap을 대표값으로 선택했다(Phase 18에서 kiosk가 아님이 확정된 " +
                    "8개 필드 중 하나)."),
                new TelegramField(54, "예비 정보 FIELD", TelegramRepresentation.AN, 172, 1328, TelegramSetLocation.Kiosk,
                    "SPEC 표에 SET 장소 체크가 없다 — 미사용 예비 필드. 전송 화면에서는 편집을 막아 " +
                    "둔다(2026-08-28 확정).", alwaysBlank: true),
            };
            return fields;
        }

        /// <summary>국고 상세 고지내역 조회 전문(501008). 총 길이 706바이트.</summary>
        public static readonly TelegramSchema Notice501008 =
            new TelegramSchema("501008", 706, BuildNotice501008Fields());

        /// <summary>카드 정보 조회 전문(800000). 총 길이 500바이트.</summary>
        public static readonly TelegramSchema CardInfo800000 =
            new TelegramSchema("800000", 500, BuildCardInfo800000Fields());

        /// <summary>국고 신용카드 승인 요청 전문(보안단말기 거래용, 902614). 총 길이 1500바이트.</summary>
        public static readonly TelegramSchema CardApproval902614 =
            new TelegramSchema("902614", 1500, BuildCardApproval902614Fields());

        /// <summary>거래 구분 코드 문자열로 스키마를 찾는다. 3종 외에는 예외.</summary>
        public static TelegramSchema ByTxType(string txType)
        {
            switch (txType)
            {
                case "501008": return Notice501008;
                case "800000": return CardInfo800000;
                case "902614": return CardApproval902614;
                default:
                    throw new KeyNotFoundException($"알 수 없는 거래 구분 코드: \"{txType}\". 501008/800000/902614만 지원한다.");
            }
        }
    }
}
