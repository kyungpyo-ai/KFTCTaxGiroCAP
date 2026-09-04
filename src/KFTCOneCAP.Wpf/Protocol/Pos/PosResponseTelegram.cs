using System;
using System.Globalization;
using System.Linq;
using KFTCOneCAP.Wpf.Protocol.Pos.Schemas;
using KFTCOneCAP.Wpf.Security;

namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// OneCAP→POS 응답 전문(docs/payment_relay/development_plan.md P17-3). 임시 전문 시절의
/// <c>PosPaymentResponse</c>를 대체한다.
///
/// <b>두 가지 생성 경로</b>(설계 근거는 클래스 요약이 아니라 development_plan.md P17-3 본문 참고 — 매우
/// 중요한 구분이라 여기서도 다시 요약한다): SPEC 흐름도(p.7/12/13)에서 응답이 KFTCVAN→OneCAP→POS까지
/// 같은 라벨(예: ④0210)로 이어진다는 것은, 응답이 각 경계마다 새로 만드는 전문이 아니라 <b>같은 바이트를
/// 그대로 통과시키는 중계</b>라는 뜻이다. 실제 응답 필드 대부분은 kiosk가 아니라 디지털예산/인터넷지로/
/// VAN이 채우므로 OneCAP이 요청만으로 만들어낼 수 있는 값이 아니다.
/// <list type="bullet">
/// <item><see cref="Relay"/> — VAN까지 도달해 실제 응답을 받은 성공 경로. VAN이 준 바이트를 그대로
///   감싸되, <see cref="ClearCardReadingFields"/>로 원캡이 채워 보냈던 카드리딩·PIN 필드(§4.13)만
///   되돌리고 그 외 필드는 재작성하지 않는다.</item>
/// <item><see cref="Failure(PosRequestTelegram, string)"/> / <see cref="Failure(PosTelegramSchema, string)"/>
///   — OneCAP이 VAN에 도달하기 전 자체 실패(취소/Timeout/리더기 실패/전문 오류)한 경로. VAN 응답이
///   없으므로 합성한다. 요청 텔레그램(Clone) 또는 스키마(요청이 무효했던 경우 CreateEmpty)를 바탕으로
///   <c>#3</c>/<c>#6</c>/<c>#7</c>/<c>#8</c>만 덮어쓴다 — 서버가 채우는 필드는 kiosk도 원 요청에 채우지
///   않아 이미 공백이므로 clone해도 값이 어색해지지 않는다(SET 장소가 디지털예산/인터넷지로/VAN 단독인
///   필드는 kiosk 열에 표시가 없다는 전제가 성립하기 때문).</item>
/// </list>
/// </summary>
public sealed class PosResponseTelegram
{
    private const string ResponseTransactionTypeSuffix = "0210";
    private const string SendFlagFromOneCap = "G";

    private PosResponseTelegram(PosTelegram telegram)
    {
        Telegram = telegram;
    }

    internal PosTelegram Telegram { get; }

    /// <summary>해당 필드를 CP949로 디코딩하고 패딩을 제거해 읽는다(P22-6 로깅용 — 결과코드 <c>#7</c>/
    /// 전문관리번호 <c>#9</c> 등, <see cref="PosRequestTelegram.Read"/>와 동일한 목적).</summary>
    public string Read(int fieldNumber) => Telegram.Read(fieldNumber);

    /// <summary>
    /// VAN이 돌려준 응답 바이트를 감싼다. 승인/거절 판단 등 값의 <b>해석</b>은 여전히 하지 않지만(§4.10
    /// relay 원칙), <see cref="ClearCardReadingFields"/>만은 예외로 적용한다 — 원캡이 요청 방향으로 써
    /// 넣었던 자리를 되돌리는 것뿐이라 relay 원칙과 상충하지 않는다(PRD.md §4.13, 2026-09-04 확정).
    /// </summary>
    public static PosResponseTelegram Relay(PosTelegramSchema schema, byte[] vanResponseBody)
    {
        PosTelegram telegram = PosTelegram.FromBytes(schema, vanResponseBody);
        ClearCardReadingFields(telegram);
        return new PosResponseTelegram(telegram);
    }

    /// <summary>
    /// 유효했던 요청을 clone해 실패 응답을 합성한다(실패 경로, 요청 자체는 정상 파싱됨 — 취소/Timeout/
    /// 리더기 실패 등 OneCAP 자체 판단으로 VAN에 도달하지 못한 경우).
    /// </summary>
    public static PosResponseTelegram Failure(PosRequestTelegram request, string resultCode) =>
        BuildFailure(request.Telegram.Clone(), resultCode);

    /// <summary>
    /// 요청 자체가 무효(길이 불일치 등)라 clone할 수 없을 때, 스키마만으로 빈 응답을 합성한다(E40 전용).
    /// </summary>
    public static PosResponseTelegram Failure(PosTelegramSchema schema, string resultCode) =>
        BuildFailure(PosTelegram.CreateEmpty(schema), resultCode);

    /// <summary>
    /// 원캡이 요청 방향으로 채워 보냈던 카드리딩·PIN 필드(PRD.md §4.13, 2026-09-04 확정, Phase 26
    /// P26-1) — <c>902614</c> 응답에서만 등장한다(501008/800000에는 필드 자체가 없다).
    ///
    /// <list type="bullet">
    /// <item><c>#45</c> 복호화 정보</item>
    /// <item><c>#46</c> 암호화된 카드정보</item>
    /// <item><c>#51</c> 암호화된 비밀번호 정보 — 2026-08-27 Phase 18 최종 검증 H-1에서 이미 지우고
    ///   있던 필드. 이번에 나머지 3개와 같은 처리로 일반화했다(개별 상수·개별 분기를 두지 않는다).</item>
    /// <item><c>#53</c> EMV DATA — 원캡·인터넷지로 공유 필드(<c>PosFieldOwner.InternetGiro | OneCap</c>)
    ///   이지만, §7.1 저장이 이 값을 디스크에 남긴다는 이유로 포함한다(Phase 25가 메모리에서 지우는
    ///   데이터를 디스크에 남기지 않는다 — PRD.md §4.13 참고). 인터넷지로가 응답 방향으로 값을 실어
    ///   보내는지는 미확인 — 열린 항목(ROADMAP.md Phase 26 "남은 미확정 사항" 6번).</item>
    /// </list>
    ///
    /// <c>#43</c>(보안단말기 인증번호)/<c>#44</c>(FALLBACK CODE)/<c>#48</c>(거래 입력 유형)/<c>#50</c>
    /// (승인 인증방식)은 카드 데이터가 아닌 제어값이라 대상에서 뺐다(2026-09-04 사용자 확정).
    /// <c>800000</c>의 <c>#14</c> BIN도 대상이 아니다 — BIN을 돌려주는 것이 그 전문의 목적 자체다.
    /// </summary>
    private static readonly int[] CardReadingFieldNumbers = { 45, 46, 51, 53 };

    /// <summary>
    /// 카드리딩 필드 삭제 대상 전문. <c>501008</c>도 스키마에 #45/#46/#51/#53 번호를 갖지만
    /// DigitalBudget 소유의 전혀 다른 업무 필드라(납부 금액 수정 허용 유무 등) 번호만 보고 지우면
    /// 정상 응답 데이터를 침범한다(P26-1 검증 중 발견, Scenario20 케이스 4) — 반드시 전문 종류로도
    /// 걸러야 한다.
    /// </summary>
    private const string CardReadingFieldsTransactionTypeCode = "902614";

    /// <summary>
    /// <paramref name="telegram"/>이 <see cref="CardReadingFieldsTransactionTypeCode"/> 전문이고 그
    /// 스키마에 <see cref="CardReadingFieldNumbers"/>가 있으면 space로 지운다(길이 유지, <c>0x00</c>
    /// 금지 — 유효한 전문은 space/<c>'0'</c>로만 패딩된다는 원칙과 <c>VanService</c>의 H-1 NUL 검사를
    /// 깨뜨리지 않기 위함). 빈 문자열을 쓰면 <see cref="PosField.Pad"/>가 타입과 무관하게 전체 space로
    /// 채우는 기존 동작(P17 체크포인트1 M-1)을 그대로 쓴다.
    /// </summary>
    private static void ClearCardReadingFields(PosTelegram telegram)
    {
        if (telegram.Schema.TransactionTypeCode != CardReadingFieldsTransactionTypeCode)
        {
            return;
        }

        foreach (int fieldNumber in CardReadingFieldNumbers)
        {
            if (telegram.Schema.Fields.Any(f => f.Number == fieldNumber))
            {
                telegram.Write(fieldNumber, string.Empty);
            }
        }
    }

    private static PosResponseTelegram BuildFailure(PosTelegram telegram, string resultCode)
    {
        telegram.Write(3, ResponseTransactionTypeSuffix);
        telegram.Write(6, SendFlagFromOneCap);
        telegram.Write(7, resultCode);
        telegram.Write(8, DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture));

        ClearCardReadingFields(telegram);

        return new PosResponseTelegram(telegram);
    }

    /// <summary>
    /// <c>[길이 4자리][본문]</c> 프레임 바이트를 만든다. 길이 필드 자릿수(4)·형식은
    /// <see cref="PosMessageFramer"/>가 기대하는 것과 반드시 일치해야 한다(P14-1 프레이밍 규칙 계승).
    /// </summary>
    public byte[] ToFrame()
    {
        byte[] bodyBytes = Telegram.ToBody();
        try
        {
            if (bodyBytes.Length > 9999)
                throw new PosProtocolException($"응답 본문이 길이 필드(4자리) 범위를 초과함: {bodyBytes.Length}바이트");

            byte[] lengthBytes = PosMessageEncoding.Value.GetBytes(bodyBytes.Length.ToString("D4", CultureInfo.InvariantCulture));

            byte[] frame = new byte[lengthBytes.Length + bodyBytes.Length];
            Buffer.BlockCopy(lengthBytes, 0, frame, 0, lengthBytes.Length);
            Buffer.BlockCopy(bodyBytes, 0, frame, lengthBytes.Length, bodyBytes.Length);
            return frame;
        }
        finally
        {
            // Phase 25 P25-5(PRD.md §4.2 #9) — Telegram._body(#7)의 복사본일 뿐, 이 메서드가 반환하는
            // frame(별도 배열)에 이미 옮겨 적혔으므로 이 로컬 사본은 여기서 지운다.
            SecureClear.Clear(bodyBytes);
        }
    }
}
