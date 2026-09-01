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
/// 최종 마스킹 대상(1곳, 902614 전용):
/// <list type="bullet">
/// <item><c>#46</c>(암호화된 카드정보, POSITION 407, 길이 196) — 부분 마스킹(앞 6바이트만 남김, 사용자
///   확정). <b>단, 구간이 전부 space(카드리딩 전 스텁 등 아직 값이 채워지지 않은 상태)면 마스킹하지
///   않고 원문(공백) 그대로 남긴다</b>(2026-09-01 사용자 지적 — 값이 없는데도 마스킹 처리되어 혼란을
///   줬다).</item>
/// </list>
/// 나머지 필드(902614 <c>#43/#44/#45/#48/#50/#51/#53</c>, 800000 <c>#14</c>)는 원문 그대로 남긴다.
/// 501008은 원캡이 채우는 필드가 없어(카드 데이터 자체가 없는 전문) 이 유틸의 대상이 아니다.
///
/// <b>정상/기형 분기</b>(설계 확정): 본문 길이가 스키마가 선언한 <see cref="PosTelegramSchema.TotalLength"/>
/// 와 정확히 일치할 때만 위치 기반 마스킹을 적용한다 — <see cref="PosTelegramSchema"/> 생성자의 자체
/// 검증(POSITION 연속성)이 이미 보장하듯, 필드 POSITION은 "선언된 길이의 전문"에서만 신뢰할 수 있다.
/// 전문 종류를 식별할 수 없거나(<c>PosSchemaRegistry.TryResolve</c> 실패) 길이가 어긋나면(기형 전문)
/// 위치 기반 마스킹을 포기하고 원문을 그대로 돌려준다 — 이 경우에도 호출부가 최종적으로 거치는
/// <see cref="FileLogger.Info(LogCategory, string, string?, string?)"/> 파이프라인이 모든 메시지에
/// <see cref="LogMessageMasker.Mask"/>를 단일 지점에서 자동으로 한 번 더 적용하므로(13~19자리 숫자·
/// 트랙 데이터 패턴), 최소한의 방어는 항상 걸린다 — 이 클래스가 범용 마스킹을 직접 호출할 필요는 없다.
/// 이 폴백 경로는 <c>PaymentFlowTestScenarios</c>의 기형 전문 시나리오(902614, 길이를 일부러 어긋나게
/// 만든 본문)로 실제 실행 검증까지 마쳤다 — 위치 기반 마스킹을 시도하지 않고 원문을 그대로 돌려주는지,
/// 그 원문이 이후 <see cref="LogMessageMasker.Mask"/>를 거치는지를 확인한다. 902614가 애초에 길이
/// 검증을 통과하지 못하면(E40) 요청 자체가 실패 처리되어 VAN까지 가지 않으므로(<see
/// cref="PosRequestTelegram.Parse"/>), 이 폴백은 실제 운영에서는 로그 유틸이 직접 호출될 때만(가짜
/// 전문 주입 등) 드물게 닿는 경로다.
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
    /// <summary>SPEC #46 "암호화된 카드정보"(902614) — 부분 마스킹 대상(클래스 요약 참고).</summary>
    private const int EncryptedCardDataFieldNumber = 46;

    /// <summary>#46에서 가운데를 <c>*</c>로 채우기 전 앞에 남기는 바이트 수. #46은 어차피 암호문
    /// (사람이 읽을 값이 아니다)이라, "필드가 실제로 채워졌는지/형식이 대략 맞는지"를 눈으로 식별할 수
    /// 있는 최소한만 남긴다 — <see cref="LogMessageMasker"/>의 카드번호 마스킹("앞6+뒤4")과 같은
    /// 감각이되, 뒤쪽은 남길 실익이 없어(사용자 확정 2026-09-01) 앞 6바이트만 남긴다.
    /// </summary>
    private const int CardDataVisiblePrefixLength = 6;

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

        // POSITION 순으로 마스킹 구간을 모은다 — 이 전문 종류에 해당 필드 자체가 없으면(501008/800000)
        // 자연히 빈 목록이 되어 원문 그대로 남는다.
        var ranges = new List<(int Position, int Length, int VisiblePrefix)>();

        // 사용자 지적(2026-09-01) — #46이 아직 채워지지 않아 순수 공백(전체 space)인 경우까지
        // 무조건 마스킹하면(앞 6바이트 노출 + 나머지 '*') 공백이 사실상 "* 범벅"으로 표시돼 혼란을
        // 준다. 값이 실제로 채워졌을 때만("전부 space가 아닐 때만") 위치 기반 마스킹을 적용한다 —
        // 순수 공백이면 이 range 자체를 목록에서 제외해 원문(공백) 그대로 남긴다(PosField.Pad가
        // 빈 값을 전체 space로 채우는 것과 대칭되는 판단).
        if (TryGetField(schema, EncryptedCardDataFieldNumber, out PosField? cardField)
            && !IsAllSpaces(body, cardField!.Position, cardField.Length))
        {
            ranges.Add((cardField.Position, cardField.Length, CardDataVisiblePrefixLength));
        }

        // #51(암호화된 비밀번호 정보)은 2026-09-01 사용자 확정으로 마스킹하지 않는다(클래스 요약의
        // "2026-09-01 재확정" 절 참고) — SEED 암호화 전까지는 이 로그에 평문 PIN이 그대로 남는다.

        if (ranges.Count == 0)
            return DecodeWhole(body);

        ranges.Sort((a, b) => a.Position.CompareTo(b.Position));
        return BuildMaskedText(body, ranges);
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

    /// <summary>마스킹 구간 사이사이의 원문 구간과 마스킹 구간(앞 <c>VisiblePrefix</c>바이트만 원문,
    /// 나머지 <c>*</c>)을 순서대로 각각 독립적으로 CP949 디코딩해 이어 붙인다(클래스 요약 "바이트
    /// 단위로만 자른다" 참고). 구간은 서로 겹치지 않는다(SPEC 필드는 서로 겹치지 않으므로).</summary>
    private static string BuildMaskedText(byte[] body, List<(int Position, int Length, int VisiblePrefix)> ranges)
    {
        var sb = new System.Text.StringBuilder();
        int cursor = 0;

        foreach ((int position, int length, int visiblePrefix) in ranges)
        {
            if (position > cursor)
                sb.Append(PosMessageEncoding.Value.GetString(body, cursor, position - cursor));

            int visible = Math.Min(visiblePrefix, length);
            int maskedStars = length - visible;

            if (visible > 0)
                sb.Append(PosMessageEncoding.Value.GetString(body, position, visible));

            sb.Append('*', maskedStars);

            cursor = position + length;
        }

        if (cursor < body.Length)
            sb.Append(PosMessageEncoding.Value.GetString(body, cursor, body.Length - cursor));

        return sb.ToString();
    }
}
