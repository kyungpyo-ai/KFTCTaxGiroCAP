using System;

namespace KFTCOneCAP.KioskSim.Protocol
{
    /// <summary>
    /// SPEC(국세 베리어프리 키오스크용 전산설계서(POS-원캡)_20260826.pdf) 2장 "DATA 항목" 표의
    /// "표현" 열이다.
    ///
    /// SPEC 표에는 N / A / AN / AHN 뿐 아니라 ANS(902614 #35 연락전화번호, #53 EMV DATA)와
    /// AHNS(902614 #37 납부자 성명) 도 등장한다 — 발주처(Phase 19 실행계획서 P19-2)가 적어 둔
    /// "N/A/AN/AHN/ANS 4종"은 실제로는 5종이었고, 여기에 AHNS가 하나 더 있어 도합 6종이다.
    /// 표를 옮겨 적으며 발견한 사실이라 값을 빼지 않고 그대로 추가했다(문서와 실물이 다르면 실물을
    /// 우선한다는 이 저장소의 원칙, CLAUDE.md 참고).
    ///
    /// - N   : 숫자(Numeric)
    /// - A   : 영문자(Alpha)
    /// - AN  : 영문자 + 숫자(Alpha-Numeric)
    /// - AHN : 영문자 + 한글 + 숫자(Alpha-Hangul-Numeric)
    /// - ANS : 영문자 + 숫자 + 특수문자(Alpha-Numeric-Special)
    /// - AHNS: 영문자 + 한글 + 숫자 + 특수문자(Alpha-Hangul-Numeric-Special)
    /// </summary>
    public enum TelegramRepresentation
    {
        N,
        A,
        AN,
        AHN,
        ANS,
        AHNS,
    }

    /// <summary>
    /// 이 필드의 값을 실제로 채우는(SET하는) 주체. SPEC 표의 "SET 장소" 열을 옮긴 것이다.
    ///
    /// SPEC 원문은 전문마다 열 구성이 다르다(예: 800000은 "인터넷지로/VAN/kiosk/원캡" 4열,
    /// 902614는 "인터넷지로/kiosk/원캡" 3열, 501008은 "디지털예산/인터넷지로/kiosk" 3열).
    /// 여기서는 이걸 하나의 4종 열거형으로 정규화했다:
    ///   - Kiosk        : SPEC의 "kiosk" 열
    ///   - OneCap       : SPEC의 "원캡" 열
    ///   - InternetGiro : SPEC의 "인터넷지로" 열 + "디지털예산"(dBrain, 501008 전용) 열을 통합.
    ///                    501008의 "디지털예산" 열은 공통부 제네릭 표(2장 "가.공통부분")에서는
    ///                    "인터넷지로" 열로 뭉뚱그려 표기되어 있어(같은 POSITION의 #10 등을
    ///                    대조하면 확인된다), 이 저장소에서도 같은 값으로 취급한다.
    ///   - Van          : SPEC의 "VAN" 열(800000 전용)
    ///
    /// 한 필드에 두 열(예: kiosk + 인터넷지로)이 동시에 체크된 경우가 있다 — 요청 시에는 kiosk가
    /// 값을 채우고, 응답 시에는 같은 POSITION을 상대측이 다시 채워 넣는(echo/재기록) 패턴이다.
    /// 이런 경우 "요청을 만들 때 누가 채우는가"를 기준으로 Kiosk를 우선 선택했다(P19-5의
    /// "SET 장소가 kiosk인 필드만 편집 가능" 요구사항과 직접 연결되는 실무적 기준이기 때문).
    /// 두 열 다 kiosk가 아닌 경우(예: 902614 #53 EMV DATA는 인터넷지로+원캡 동시 체크)에는
    /// 값의 실제 출처(리더기 하드웨어에 가장 가까운 쪽)를 우선했다 — 각 필드의 XML 주석에 근거를
    /// 남겼다.
    ///
    /// 아무 열도 체크되지 않은 필드(예비/미사용 FIELD 등)는 Kiosk로 분류했다 — SPEC 공통부 표
    /// 하단의 "※ 키오스크에서 전문 요청시 O 체크 없는 필드는 space로 채워서 총 길이로 전문 생성"
    /// 규칙에 따라, 체크가 없는 필드도 결국 요청을 만드는 kiosk가 (의미 없는 값이라도) space로
    /// 채워 넣어야 하기 때문이다(2026-08-28 사용자 재확인 — "아무도 안 채우는 필드는 그냥
    /// 키오스크단에서 공백으로 채워서 보내면 된다").
    ///
    /// <b>본 앱(KFTCOneCAP.Wpf)의 <c>PosFieldOwner</c>는 이 경우를 <c>None</c>(제4의 값)으로 따로
    /// 구분한다</b> — 이 프로젝트의 <see cref="TelegramSetLocation"/>에는 대응하는 값이 없고,
    /// 의도적으로 두지 않는다. 시뮬레이터는 "누가 값을 채워 보내는가"라는 실무적 질문 하나만
    /// 답하면 되고(그 답은 위 확정에 따라 항상 kiosk다), 본 앱의 `None`은 "SPEC 표에 체크가
    /// 없다"는 문서적 사실을 그대로 보존하려는 다른 목적이라 굳이 맞출 필요가 없다(P19-2 교차
    /// 대조 기록, development_plan.md 참고 — 이 차이는 결함이 아니라 두 프로젝트가 같은 사실을
    /// 다른 축으로 표현한 것으로 판정됨).
    /// </summary>
    public enum TelegramSetLocation
    {
        Kiosk,
        OneCap,
        InternetGiro,
        Van,
    }

    /// <summary>
    /// 고정길이 전문의 필드 하나를 표현하는 불변 값 타입.
    ///
    /// Phase 19 실행계획서(docs/payment_relay/development_plan.md) P19-2: 이 타입과
    /// <see cref="TelegramSchema"/>는 본 앱(KFTCOneCAP.Wpf)의 Protocol/Pos/Schemas를 절대
    /// 참조하지 않고, SPEC PDF를 처음부터 다시 읽어 옮겨 적은 "독립 전사본"이다. 두 소스가
    /// 서로 다른 사람이 각자 옮겨 적은 것이어야 어느 한쪽의 오타가 "검증 통과"로 둔갑하지 않는다.
    /// </summary>
    public sealed class TelegramField
    {
        /// <summary>SPEC 표의 "번호"(#).</summary>
        public int Number { get; }

        /// <summary>SPEC 표의 "DATA 항목"(필드명, 한글).</summary>
        public string Name { get; }

        /// <summary>SPEC 표의 "표현".</summary>
        public TelegramRepresentation Representation { get; }

        /// <summary>SPEC 표의 "길이"(바이트 수. 한글 필드도 바이트 기준이다).</summary>
        public int Length { get; }

        /// <summary>SPEC 표의 "POSITION"(본문 내 0-based 오프셋. "#0 전문 길이"는 본문 밖 헤더라 여기 포함되지 않는다).</summary>
        public int Position { get; }

        /// <summary>SPEC 표의 "SET 장소"를 정규화한 값. 위 <see cref="TelegramSetLocation"/> 문서 참고.</summary>
        public TelegramSetLocation SetLocation { get; }

        /// <summary>
        /// SET 장소 판정 근거나 SPEC 원문과 다르게 분류한 이유(있는 경우)를 담는 자유 서술.
        /// 사람이 읽는 주석 용도이며 코드 로직에서는 쓰지 않는다.
        /// </summary>
        public string? Note { get; }

        /// <summary>
        /// <see cref="SetLocation"/>이 <see cref="TelegramSetLocation.Kiosk"/>이더라도, 이 필드에
        /// 유효한 값을 넣을 일이 실제로는 없어(FILLER/예비 정보 FIELD처럼 정의된 용도가 없거나,
        /// SPEC 표에는 체크가 없어도 업무적으로 항상 공백이 맞다고 확인된 경우) 편집 UI에서
        /// 잠가 두는 것이 맞는 필드는 <c>true</c>다(2026-08-28 사용자 확정 — P19-5 전송 화면에서
        /// 이런 필드까지 편집 가능하게 열어두면 업체가 무엇을 넣어야 하는지 헷갈리고, 잘못된 값을
        /// 실수로 채워 보낼 위험만 커진다). 기본값 <c>false</c>.
        ///
        /// 이 값이 <c>true</c>인 8개 필드: 501008 <c>#5</c>/<c>#13</c>, 800000 <c>#10</c>/<c>#13</c>,
        /// 902614 <c>#5</c>/<c>#17</c>/<c>#47</c>/<c>#54</c>(development_plan.md P19-5 후속 수정 기록
        /// 참고). <c>#38</c>(902614 카드소유주 주민등록번호)처럼 SPEC 표에 체크는 없지만 실질적인
        /// 값이 들어갈 수 있는 선택 입력 필드는 여기 포함하지 않고 계속 편집 가능하게 둔다 —
        /// "SPEC 표에 체크가 없다"는 사실 하나만으로 이 값을 정하지 않는다(판단 근거가 필요하다).
        /// </summary>
        public bool AlwaysBlank { get; }

        /// <summary>이 필드가 차지하는 본문 범위의 끝(배타적 상한). Position + Length.</summary>
        public int End => Position + Length;

        public TelegramField(
            int number,
            string name,
            TelegramRepresentation representation,
            int length,
            int position,
            TelegramSetLocation setLocation,
            string? note = null,
            bool alwaysBlank = false)
        {
            if (number <= 0)
                throw new ArgumentOutOfRangeException(nameof(number), number, "필드 번호는 1 이상이어야 한다.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("필드 이름이 비어 있다.", nameof(name));
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), length, $"필드 #{number}({name}) 길이는 1 이상이어야 한다.");
            if (position < 0)
                throw new ArgumentOutOfRangeException(nameof(position), position, $"필드 #{number}({name}) POSITION은 0 이상이어야 한다.");

            Number = number;
            Name = name;
            Representation = representation;
            Length = length;
            Position = position;
            SetLocation = setLocation;
            Note = note;
            AlwaysBlank = alwaysBlank;
        }

        public override string ToString()
            => $"#{Number} {Name} ({Representation},{Length}) pos={Position}..{End - 1} SET={SetLocation}";
    }
}
