using System.Runtime.InteropServices;

namespace KFTCOneCAP.Wpf.Interop;

/// <summary>
/// 네이티브 경계 — <c>KFTC_GIRO.dll</c>의 Export 함수 <c>FNAISCRDVAN</c> P/Invoke 선언과 버퍼 상수만.
/// 업무 로직 없음(docs/payment_relay/ROADMAP.md "계층 구조" — Interop은 P/Invoke 선언만 담당,
/// <see cref="NativeLibrary"/>가 세운 규칙 그대로).
///
/// <b>계약 출처</b>: <c>KFTC_GIRO.dll</c>은 별도 SPEC 문서·샘플 소스가 없다 — <c>docs/payment_relay/PRD.md</c>
/// §2.3이 현재 확보된 유일한 계약 정보다(development_plan.md Phase 20 실행계획서).
///
/// <b>문자열 인자를 <see cref="string"/>이 아니라 <see cref="byte"/>[]로 선언한 이유</b>: <c>char*</c>를
/// <c>string</c> + <c>CharSet.Ansi</c>로 마샬링하면 마샬러가 **프로세스의 ANSI 코드페이지**로 변환한다.
/// 한국어 Windows에서는 우연히 949(CP949)와 일치하지만, 로캘이 다른 PC에서는 조용히 깨진다(한글 필드가
/// <c>?</c>로 치환). 전문 본문은 이미 <c>PosMessageEncoding.Value</c>(CP949 고정)로 인코딩된 바이트이므로,
/// 그걸 <c>string</c>으로 되돌렸다가 마샬러가 다시 인코딩하게 두는 것은 불필요한 왕복이자 손실 지점이다.
/// <c>byte[]</c>는 마샬러가 손대지 않고 고정(pinned)해서 포인터만 넘기므로 코드페이지 변환이 개입할
/// 여지가 없다. <c>CharSet</c>을 지정하지 않는 것도 같은 이유.
///
/// <b>32bit 전용</b>: <c>KFTC_GIRO.dll</c>은 32bit(x86) DLL이다 — <c>KFTCOneCAP.Wpf.csproj</c>의
/// <c>PlatformTarget</c>이 <c>x86</c>으로 설정돼 있어야 한다(이미 설정됨, Phase 8).
/// </summary>
internal static class KftcGiroNative
{
    /// <summary><c>outData</c> 버퍼 크기(바이트). ROADMAP 확정값 — SPEC 최대 전문 길이(902614, 1500바이트)의
    /// 2.7배 여유.</summary>
    internal const int OutDataBufferSize = 4096;

    /// <summary><c>out_szRetCode</c> 버퍼 크기(바이트). <b>검증되지 않은 가정</b> — PRD §2.3에 "DLL 처리
    /// 결과코드가 반환된다"고만 적혀 있고 길이 언급이 없다. 결과코드가 256바이트를 넘을 개연성은 사실상
    /// 없다고 보고 넉넉히 잡은 값이며, 실서버 연동 후 실측으로 검증해야 한다.</summary>
    internal const int RetCodeBufferSize = 256;

    /// <summary><c>FNAISCRDVAN</c> 호출 타임아웃(초). PRD §2.3 사용 예(<c>FNAISCRDVAN("OT", input, data,
    /// ret_code, 60)</c>)를 그대로 따른 값.</summary>
    internal const int DefaultTimeoutSeconds = 60;

    /// <summary><c>in_szMode</c> — 외부망 테스트 서버. PRD §4.10 / 2026-08-18 확정. 운영("R")/내부망
    /// 테스트("IT")는 아직 쓰지 않는다.</summary>
    internal const string ModeExternalTest = "OT";

    /// <summary>
    /// VAN 서버에 결제 전문을 중계한다.
    /// </summary>
    /// <param name="in_szMode">"R"(운영)/"IT"(내부망테스트)/"OT"(외부망테스트)의 ASCII 바이트 + NUL 종단.</param>
    /// <param name="inData">CP949로 인코딩된 요청 전문 본문 + NUL 종단.</param>
    /// <param name="outData">응답 전문을 받을 버퍼(<see cref="OutDataBufferSize"/>바이트로 호출자가 할당).</param>
    /// <param name="out_szRetCode">DLL 처리 결과코드를 받을 버퍼(<see cref="RetCodeBufferSize"/>바이트로
    /// 호출자가 할당).</param>
    /// <param name="int_iTimeout">타임아웃(초).</param>
    /// <returns><c>0</c>: DLL 통신 성공(실제 승인/거절은 <paramref name="outData"/>를 봐야 앎).
    /// <c>-1</c>: DLL 통신 실패.</returns>
    [DllImport("KFTC_GIRO.dll", CallingConvention = CallingConvention.StdCall)]
    internal static extern int FNAISCRDVAN(
        byte[] in_szMode, byte[] inData, byte[] outData, byte[] out_szRetCode, int int_iTimeout);
}
