namespace KFTCOneCAP.Wpf.Protocol.Pos;

/// <summary>
/// POS 소켓으로 내보내는 응답 하나의 최소 계약(P26-3/P26-4). <see cref="PosResponseTelegram"/>(고정
/// 스키마 3전문)과 <see cref="PosInquiryResponseTelegram"/>(고정부 80바이트 + 가변 꼬리, Phase 26 신규)
/// 둘 다 이 인터페이스로 <c>TransactionQueue</c>/<c>PosSocketServer</c>를 통과한다 — 두 클래스는 응답을
/// 만드는 방법만 다를 뿐, "프레임으로 만들고, 로그용 원문을 내주고, 송신 후 지운다"는 계약은 같다.
///
/// <b>계층 규칙</b>: <c>TransactionQueue</c>/<c>PosSocketServer</c>(<c>Services/</c>)는 이 인터페이스만
/// 알고, 구현체가 <see cref="Protocol.Pos.PosTelegram"/>을 몇 개 들고 있는지·응답이 고정인지 가변인지는
/// 알 필요가 없다.
/// </summary>
public interface IPosOutboundResponse
{
    /// <summary>SPEC 공통부 필드(<c>#7</c>/<c>#9</c> 등)를 읽는다 — 두 구현 모두 이 필드들은 항상
    /// 고정부(POSITION 0~70) 안에 있으므로 고정/가변 여부와 무관하게 안전하다.</summary>
    string Read(int fieldNumber);

    /// <summary><c>[길이 4자리][본문]</c> 송신 프레임 바이트를 만든다.</summary>
    byte[] ToFrame();

    /// <summary>로그/마스킹용 전체 본문(길이 헤더 제외) 복사본. 호출자가 사용 후
    /// <c>SecureClear.Clear(byte[])</c>로 지울 책임을 진다(<see cref="PosTelegram.ToBody"/>와 동일한
    /// 계약).</summary>
    byte[] BodyForLog();

    /// <summary><c>TelegramLogRedactor.Redact</c>가 위치 기반 마스킹 규칙을 찾을 때 쓰는 거래 구분
    /// 코드 — 반드시 <see cref="PosTelegram.Schema"/>의 그것과 같은 값일 필요는 없다(가변 응답은
    /// 고정부 자신의 코드를 쓴다).</summary>
    string RedactionTransactionTypeCode { get; }

    /// <summary>송신이 끝난 뒤(성공/실패 무관) 원본 버퍼를 지운다(Phase 25 P25-6 원칙의 계승).</summary>
    void ClearBody();
}
