using System;
using System.Collections.Generic;
using System.Linq;
using KFTCOneCAP.Wpf.Protocol.Pos;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;

namespace KFTCOneCAP.Wpf.Services.Diagnostics;

/// <summary>
/// 사용자 요청(2026-09-01) — "실제 POS/VAN이 어떻게 전문을 주고받았는지 원문을 로그로 확인하고
/// 싶다"에 대응해, 전문 원문(요청/응답 둘 다)을 로그에 남기되 민감 필드만 <b>위치(POSITION) 기반</b>
/// 으로 정밀 마스킹한다.
///
/// <b>원캡↔VAN 구간(FNAISCRDVAN)은 POS↔원캡과 같은 전문 형식을 그대로 쓴다</b>(docs/payment_relay/
/// PRD.md §3.3/§4.10 확정 사항) — 그래서 이 유틸 하나를 POS 소켓 경계(<c>PosSocketServer</c>)와 VAN
/// 경계(<c>StubVanRelayService</c>/<c>VanService</c>) 양쪽에 그대로 적용한다.
///
/// <b>마스킹 대상 확정 경위</b>: `pos-onecap-spec-expert` SPEC 재확인 + 사용자가 필드별로 직접
/// 검토해 2026-09-01 "902614의 <c>#46 암호화된 카드정보</c> 하나만 마스킹하면 된다"고 확정했다
/// (800000 <c>#14</c> BIN, 902614 <c>#43/#44/#45/#48/#50/#51/#53</c>은 불필요로 결론).
///
/// <b>2026-09-01 재확정 — <c>#51</c>(암호화된 비밀번호 정보)은 마스킹하지 않는다</b>: 이 클래스는
/// 한때 사용자 확정 목록에 없던 <c>#51</c>을 방어적으로 다시 추가했었다(SEED 암호화 미구현으로
/// 지금은 평문 4자리 PIN이 그대로 실린다는 이유, <see cref="Payment.PinFieldEncoder.ToTelegramValue"/>
/// 참고). 그러나 사용자가 이 위험(평문 PIN이 로그에 그대로 남는다)을 고지받은 상태에서 "어차피 실제
/// 배포될 때는 암호화를 할 거라서 굳이 먼저 마스킹해놓을 필요는 없다"고 최종 결정해, <c>#51</c> 마스킹을
/// 도로 제거했다. <b>SEED 암호화가 실제로 구현되기 전까지는 이 전문 원문 로그에 실제 고객 PIN이 평문
/// 그대로 남는다</b> — 자세한 배경과 재검토 지시는
/// docs/operations/development_plan.md의 "P22-6부속" 절과 docs/operations/PRD.md §1.4 참고. PIN 암호화
/// 작업(SEED)이 착수될 때 이 클래스도 함께 재검토해야 한다.
///
/// 최종 마스킹 대상(4곳, 모두 902614 전용):
/// <list type="bullet">
/// <item><c>#46</c>(암호화된 카드정보, POSITION 407, 길이 196) — 부분 마스킹(앞 6바이트만 남기고 나머지
///   전부 <c>*</c>, 사용자 확정). <b>단, 구간이 전부 space(카드리딩 전 스텁 등 아직 값이 채워지지 않은
///   상태)면 마스킹하지 않고 원문(공백) 그대로 남긴다</b>(2026-09-01 사용자 지적 — 값이 없는데도 마스킹
///   처리되어 혼란을 줬다).</item>
/// <item><c>#14</c>(주민/사업자/법인등록번호, POSITION 70, 길이 13), <c>#36</c>(납부자 주민/사업자등록번호,
///   POSITION 296, 길이 13) — 2026-09-14 CP2(Opus) 리뷰 지적("범용 패턴 마스킹 제거 후 이 두 필드가
///   평문으로 남는다") + 사용자 결정으로 신규 추가. 카드번호가 아니라 고유식별정보지만, 삭제된
///   <c>LogMessageMasker</c>가 13~19자리 카드번호에 쓰던 방식("앞6+뒤4 노출, 가운데만 <c>*</c>")을 그대로
///   재사용하기로 사용자가 확정했다 — 13자리 기준 앞 6바이트(생년월일/사업자 앞자리)와 뒤 4바이트를
///   그대로 남기고 가운데 3바이트만 <c>*</c>로 채운다. <c>#46</c>과 마찬가지로 구간이 전부 space면
///   마스킹하지 않는다.</item>
/// <item><c>#38</c>(카드소유주 주민(사업자)등록번호, POSITION 319, 길이 13) — 2026-09-23 P30 체크포인트
///   지적(M-1)으로 추가. <c>#14</c>/<c>#36</c>과 완전히 같은 성격(주민/사업자등록번호)이라 동일한
///   앞6+뒤4 노출 방식을 그대로 재사용한다. SET 장소가 인터넷지로라 원래 kiosk는 채우지 않는 필드지만,
///   P30-1 스텁 확장 이후 스텁이 이 필드를 난수로 채우면서 평문 노출이 드러났다.</item>
/// </list>
/// 나머지 필드(902614 <c>#43/#44/#45/#48/#50/#51/#53</c>, 800000 <c>#14</c>)는 원문 그대로 남긴다.
/// 501008은 원캡이 채우는 필드가 없어(카드 데이터 자체가 없는 전문) 이 유틸의 대상이 아니다.
///
/// <b>정상/기형 분기</b>(설계 확정): 본문 길이가 스키마가 선언한 <see cref="PosTelegramSchema.TotalLength"/>
/// 와 정확히 일치할 때만 위치 기반 마스킹을 적용한다 — <see cref="PosTelegramSchema"/> 생성자의 자체
/// 검증(POSITION 연속성)이 이미 보장하듯, 필드 POSITION은 "선언된 길이의 전문"에서만 신뢰할 수 있다.
/// 전문 종류를 식별할 수 없거나(<c>PosSchemaRegistry.TryResolve</c> 실패) 길이가 어긋나면(기형 전문)
/// 위치 기반 마스킹을 포기하고 원문을 그대로 돌려준다.
///
/// <b>Phase 27(P27-6) 전수 확인</b> — <see cref="Redact"/>가 원문을 그대로 돌려주는 분기는 셋이다.
/// ①스키마 미상(<c>TryResolve</c> 실패), ②길이 불일치(기형 전문), ③마스킹 대상 필드 자체가 없거나
/// (501008/800000) 값이 아직 채워지지 않음(902614이지만 #46이 전부 space) — 이 중 ③이 정상 흐름의
/// 절대다수다. ①②는 실제 운영에서 다음과 같이 각각 막힌다.
/// <list type="bullet">
/// <item>요청 경로(<c>PosSocketServer</c> 요청 수신 로그, <c>VanService.RelayAsync</c>): <see
/// cref="PosRequestTelegram.Parse"/>가 스키마 미상이면 <c>E41</c>, 길이 불일치면 <c>E40</c>으로 그
/// 프레임을 즉시 실패 응답 처리한다 — <c>Redact</c>까지 도달하는 요청은 이미 스키마가 확정되고 길이도
/// 스키마와 일치하는 것만 남는다.</item>
/// <item>VAN 응답 경로(<see cref="Services.Van.VanService.RelayAsync"/>): 응답 본문을 <c>bodyLength =
/// populatedRequest.Schema.TotalLength</c> 크기로 직접 잘라 만들므로(요청과 같은 거래 구분 코드를
/// 그대로 쓴다) 스키마·길이 둘 다 항상 일치한다.</item>
/// <item>POS 응답 경로(<c>PosSocketServer.SendResponse</c>, <see cref="IPosOutboundResponse.
/// RedactionTransactionTypeCode"/>): 고정 스키마 응답(<c>PosResponseTelegram</c>)은 위 VAN 응답과
/// 같은 이유로 항상 일치한다. ②에 실제로 도달하는 유일한 경로는 <b>거래상태조회 응답</b>
/// (<see cref="PosInquiryResponseTelegram"/>, 고정부 80바이트 + 가변 꼬리)이다 — 이 응답은 요청
/// 스키마("999999", 70바이트)의 코드를 그대로 재사용하면서 실제 본문은 80바이트+꼬리라 길이가 항상
/// 어긋나 매번 원문 그대로 로그에 남는다. 다만 그 꼬리는 <c>PosResponseTelegram.
/// ClearCardReadingFields</c>(P26-1)가 이미 카드리딩·PIN 필드를 지운 뒤의 원거래 응답 바이트라
/// 안전하다.</item>
/// </list>
///
/// <b>바이트 단위로만 자른다</b>: SPEC 필드는 바이트 오프셋(<see cref="PosField.Position"/>)이지,
/// 문자(char) 오프셋이 아니다. CP949는 한글이 2바이트라 본문을 문자열로 통째로 디코딩한 뒤 그 위에서
/// <c>Substring</c>으로 자르면(문자 인덱스 ≠ 바이트 오프셋) 앞쪽에 한글 필드(예: 902614의 <c>#20</c>/
/// <c>#21</c>, AHN 타입)가 하나만 있어도 마스킹 구간이 어긋난다. 그래서 이 클래스는 항상 <c>byte[]</c>
/// 구간을 먼저 자른 뒤 각 구간을 독립적으로 CP949 디코딩한다 — 자르는 지점은 전부 SPEC 필드 경계라
/// (<see cref="PosTelegramSchema.Validate"/>가 이미 보장) 멀티바이트 문자를 중간에서 쪼갤 위험이 없다.
/// </summary>
internal static class TelegramLogRedactor
{
    /// <summary>마스킹 대상 4곳(<c>#14</c>/<c>#36</c>/<c>#38</c>/<c>#46</c>)이 전부 "902614 전용"이므로(클래스 요약)
    /// 다른 전문 종류는 필드 번호가 같아도 마스킹하지 않는다.
    ///
    /// <b>2026-09-15 Phase 27 최종 검증에서 발견·수정</b> — 필드 <b>번호</b>만 보고 마스킹하면 같은
    /// 번호가 전혀 다른 뜻으로 쓰이는 다른 전문까지 가려진다. 실측(501008 요청 1건 실전송): <c>#14</c>
    /// (전자납부번호, 19바이트) <c>1234567890123456789</c> → <c>123456*********6789</c>, <c>#36</c>
    /// (교통세, 15바이트) <c>000000000012345</c> → <c>000000*****2345</c>. 둘 다 민감정보가 아니라
    /// 진단에 꼭 필요한 값이라, P27-6("과도한 마스킹이 장애 분석을 방해한다")이 없애려던 문제가
    /// 위치 기반 마스킹 쪽에 그대로 남아 있었다. 800000의 <c>#14</c>(BIN, 8바이트)는 앞6+뒤4 clamp로
    /// <c>*</c>가 0개라 우연히 무해했을 뿐 같은 결함이었다.</summary>
    private const string CardApprovalTransactionTypeCode = "902614";

    /// <summary>SPEC #46 "암호화된 카드정보"(902614) — 부분 마스킹 대상(클래스 요약 참고).</summary>
    private const int EncryptedCardDataFieldNumber = 46;

    /// <summary>#46에서 앞에 남기는 바이트 수(뒤쪽은 전부 <c>*</c>). #46은 어차피 암호문(사람이 읽을
    /// 값이 아니다)이라, "필드가 실제로 채워졌는지/형식이 대략 맞는지"를 눈으로 식별할 수 있는 최소한만
    /// 남긴다 — 뒤쪽은 남길 실익이 없어(사용자 확정 2026-09-01) 앞 6바이트만 남기고 나머지는 전부
    /// 마스킹한다(<see cref="CardDataVisibleSuffixLength"/> = 0).
    /// </summary>
    private const int CardDataVisiblePrefixLength = 6;

    /// <summary>#46은 뒤쪽을 남길 실익이 없어 뒤쪽 노출 바이트 수는 0(전부 <c>*</c>).</summary>
    private const int CardDataVisibleSuffixLength = 0;

    /// <summary>SPEC #14 "주민(사업자,법인)등록번호"(902614) — 부분 마스킹 대상(클래스 요약 참고).</summary>
    private const int PayerRegistrationNumberFieldNumber14 = 14;

    /// <summary>SPEC #36 "납부자 주민(사업자)등록번호"(902614) — 부분 마스킹 대상(클래스 요약 참고).</summary>
    private const int PayerRegistrationNumberFieldNumber36 = 36;

    /// <summary>SPEC #38 "카드소유주 주민(사업자)등록번호"(902614) — 부분 마스킹 대상. P30 체크포인트
    /// 지적(M-1, 2026-09-23) — #14/#36과 완전히 같은 성격(주민/사업자등록번호)인데 마스킹 목록에서
    /// 빠져 있었다. 이전엔 이 필드가 SET 장소가 인터넷지로라 항상 공백이라 안 드러났지만, P30-1
    /// 스텁 확장 이후 값이 실려 평문으로 로그에 남게 됐다.</summary>
    private const int PayerRegistrationNumberFieldNumber38 = 38;

    /// <summary>#14/#36(13자리 등록번호)에서 앞뒤로 남기는 바이트 수 — 삭제된 <c>LogMessageMasker</c>가
    /// 13~19자리 카드번호에 쓰던 "앞6+뒤4 노출, 가운데만 <c>*</c>" 방식을 그대로 재사용한다(2026-09-14
    /// 사용자 확정). 13자리 기준 가운데 3바이트(7~9번째)만 <c>*</c>로 채워진다.</summary>
    private const int RegistrationNumberVisiblePrefixLength = 6;

    private const int RegistrationNumberVisibleSuffixLength = 4;

    /// <summary>
    /// 2026-09-15 사용자 지적 — POS 소켓 경계(<see cref="Services.Pos.PosSocketServer"/>)의 로그는
    /// "POS가 실제로 보낸/받은 프레임 그대로"를 표방하는데, <c>#0</c>(전문 길이, 4자리 ASCII)은
    /// 소켓에 실제로 나가는 바이트의 일부이면서도 <see cref="Redact(string, byte[])"/>에는 빠져 있었다
    /// — <c>#0</c>은 본문(BODY) 밖 프레임 헤더라 스키마에 필드로 등록되지 않기 때문이다
    /// (<c>PosCommonHeader</c> 참고). 그렇다고 <c>#0</c>을 본문에 섞어 넣으면(POSITION 체계를 바꾸면)
    /// 이 클래스가 의존하는 "필드 POSITION은 본문 오프셋"이라는 전제 전체가 깨지고, 그 위에 놓인
    /// 파서·프레이머·4개 스키마·<c>KFTCOneCAP.KioskSim</c>의 독립 전사본까지 전부 다시 맞춰야 한다
    /// (Phase 17부터 고정된 계약) — 로그 표시 하나를 위해 감수할 위험이 아니다.
    ///
    /// 그래서 <c>#0</c>은 <see cref="Redact(string, byte[])"/>의 위치 기반 마스킹에는 전혀 관여시키지
    /// 않고(그 메서드는 순수 본문만 계속 다룬다), 이 메서드가 마스킹이 끝난 결과 앞에 <c>#0</c> 값을
    /// 다시 조립해 붙이기만 한다. 파싱에 성공해 여기까지 온 본문은 이미 스키마 총 길이와 일치함이
    /// 보장되므로(<see cref="Protocol.Pos.PosRequestTelegram.Parse"/>/<see
    /// cref="Protocol.Pos.PosResponseTelegram"/>이 응답 프레임을 이 길이로 직접 만든다), <c>body.Length</c>를
    /// <c>PosMessageFramer.BuildFrame</c>과 같은 <c>"D4"</c> 포맷으로 재구성하면 실제 전송된 <c>#0</c>
    /// 값과 항상 일치한다 — 근사값이 아니라 정확한 재현이다.
    /// </summary>
    internal static string RedactFrameForLog(string transactionTypeCode, byte[] body) =>
        body.Length.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + Redact(transactionTypeCode, body);

    /// <summary>
    /// 전문 본문(길이 헤더 제외, <see cref="PosTelegram.ToBody"/> 결과)을 로그에 남길 수 있는 형태로
    /// 변환한다. 실패하지 않는다 — 스키마를 식별할 수 없거나 길이가 어긋나도 예외를 던지지 않고 원문
    /// 문자열을 그대로 돌려준다(클래스 요약의 "기형 전문" 폴백).
    /// </summary>
    internal static string Redact(string transactionTypeCode, byte[] body)
    {
        if (!PosSchemaRegistry.TryResolve(transactionTypeCode, out PosTelegramSchema? schema) || schema is null)
            return DecodeWhole(body); // 알 수 없는 전문 종류 — 위치 기반 마스킹 불가.

        if (body.Length != schema.TotalLength)
            return DecodeWhole(body); // 기형 전문 — POSITION을 신뢰할 수 없어 폴백.

        // 마스킹 대상 4곳이 전부 902614 전용이다(CardApprovalTransactionTypeCode 주석의 실측 참고) —
        // 501008/800000/999999는 필드 번호가 겹쳐도 손대지 않고 원문 그대로 남긴다.
        if (!string.Equals(schema.TransactionTypeCode, CardApprovalTransactionTypeCode, StringComparison.Ordinal))
            return DecodeWhole(body);

        // POSITION 순으로 마스킹 구간을 모은다 — 이 전문 종류에 해당 필드 자체가 없으면(501008/800000)
        // 자연히 빈 목록이 되어 원문 그대로 남는다.
        var ranges = new List<(int Position, int Length, int VisiblePrefix, int VisibleSuffix)>();

        // 사용자 지적(2026-09-01) — #46이 아직 채워지지 않아 순수 공백(전체 space)인 경우까지
        // 무조건 마스킹하면(앞 6바이트 노출 + 나머지 '*') 공백이 사실상 "* 범벅"으로 표시돼 혼란을
        // 준다. 값이 실제로 채워졌을 때만("전부 space가 아닐 때만") 위치 기반 마스킹을 적용한다 —
        // 순수 공백이면 이 range 자체를 목록에서 제외해 원문(공백) 그대로 남긴다(PosField.Pad가
        // 빈 값을 전체 space로 채우는 것과 대칭되는 판단).
        if (TryGetField(schema, EncryptedCardDataFieldNumber, out PosField? cardField)
            && !IsAllSpaces(body, cardField!.Position, cardField.Length))
        {
            ranges.Add((cardField.Position, cardField.Length, CardDataVisiblePrefixLength, CardDataVisibleSuffixLength));
        }

        // #51(암호화된 비밀번호 정보)은 2026-09-01 사용자 확정으로 마스킹하지 않는다(클래스 요약의
        // "2026-09-01 재확정" 절 참고) — SEED 암호화 전까지는 이 로그에 평문 PIN이 그대로 남는다.

        // #14/#36/#38(13자리 등록번호) — #14/#36은 2026-09-14 CP2 리뷰 지적 + 사용자 결정, #38은
        // 2026-09-23 P30 체크포인트 지적(M-1)으로 추가(클래스 요약 참고). #46과 동일하게 "전부 space면
        // 마스킹하지 않는다" 예외를 적용한다.
        AddRegistrationNumberRangeIfPresent(schema, body, PayerRegistrationNumberFieldNumber14, ranges);
        AddRegistrationNumberRangeIfPresent(schema, body, PayerRegistrationNumberFieldNumber36, ranges);
        AddRegistrationNumberRangeIfPresent(schema, body, PayerRegistrationNumberFieldNumber38, ranges);

        if (ranges.Count == 0)
            return DecodeWhole(body);

        ranges.Sort((a, b) => a.Position.CompareTo(b.Position));
        return BuildMaskedText(body, ranges);
    }

    /// <summary>#14/#36 공통 처리 — 필드가 존재하고(스키마에 따라 없을 수 있음) 값이 전부 space가
    /// 아닐 때만 마스킹 구간에 추가한다(<see cref="EncryptedCardDataFieldNumber"/> 처리와 동일한
    /// 패턴).</summary>
    private static void AddRegistrationNumberRangeIfPresent(
        PosTelegramSchema schema,
        byte[] body,
        int fieldNumber,
        List<(int Position, int Length, int VisiblePrefix, int VisibleSuffix)> ranges)
    {
        if (TryGetField(schema, fieldNumber, out PosField? field)
            && !IsAllSpaces(body, field!.Position, field.Length))
        {
            ranges.Add((field.Position, field.Length, RegistrationNumberVisiblePrefixLength, RegistrationNumberVisibleSuffixLength));
        }
    }

    private static bool TryGetField(PosTelegramSchema schema, int fieldNumber, out PosField? field)
    {
        field = schema.Fields.FirstOrDefault(f => f.Number == fieldNumber);
        return field is not null;
    }

    private static string DecodeWhole(byte[] body) => PosMessageEncoding.Value.GetString(body);

    /// <summary>SPEC 필드 구간(<c>[position, position+length)</c>)이 전부 space(0x20)인지 확인한다.
    /// CP949는 ASCII 호환이라 space는 항상 1바이트 0x20이므로 문자열로 디코딩하지 않고 바이트 그대로
    /// 비교해도 안전하다(클래스 요약 "바이트 단위로만 자른다"와 동일한 이유).</summary>
    private static bool IsAllSpaces(byte[] body, int position, int length)
    {
        for (int i = position; i < position + length; i++)
        {
            if (body[i] != (byte)' ')
                return false;
        }

        return true;
    }

    /// <summary>마스킹 구간 사이사이의 원문 구간과 마스킹 구간(앞 <c>VisiblePrefix</c>바이트 + 뒤
    /// <c>VisibleSuffix</c>바이트는 원문, 그 사이 가운데만 <c>*</c>)을 순서대로 각각 독립적으로 CP949
    /// 디코딩해 이어 붙인다(클래스 요약 "바이트 단위로만 자른다" 참고). 구간은 서로 겹치지 않는다
    /// (SPEC 필드는 서로 겹치지 않으므로). <c>VisibleSuffix</c>가 0이면(#46) 기존과 동일하게 뒤쪽이
    /// 전부 <c>*</c>가 된다.</summary>
    private static string BuildMaskedText(byte[] body, List<(int Position, int Length, int VisiblePrefix, int VisibleSuffix)> ranges)
    {
        var sb = new System.Text.StringBuilder();
        int cursor = 0;

        foreach ((int position, int length, int visiblePrefix, int visibleSuffix) in ranges)
        {
            if (position > cursor)
                sb.Append(PosMessageEncoding.Value.GetString(body, cursor, position - cursor));

            // 앞뒤 노출 바이트 수가 필드 길이를 넘지 않도록 clamp한다(짧은 값 등 방어적 처리).
            int visiblePrefixClamped = Math.Min(visiblePrefix, length);
            int visibleSuffixClamped = Math.Min(visibleSuffix, length - visiblePrefixClamped);
            int maskedStars = length - visiblePrefixClamped - visibleSuffixClamped;

            if (visiblePrefixClamped > 0)
                sb.Append(PosMessageEncoding.Value.GetString(body, position, visiblePrefixClamped));

            sb.Append('*', maskedStars);

            if (visibleSuffixClamped > 0)
                sb.Append(PosMessageEncoding.Value.GetString(body, position + length - visibleSuffixClamped, visibleSuffixClamped));

            cursor = position + length;
        }

        if (cursor < body.Length)
            sb.Append(PosMessageEncoding.Value.GetString(body, cursor, body.Length - cursor));

        return sb.ToString();
    }
}
