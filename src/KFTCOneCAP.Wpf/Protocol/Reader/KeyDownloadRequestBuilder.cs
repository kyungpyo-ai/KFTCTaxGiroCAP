using System;
using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 리더기 구간 키다운로드 요청 3종(`[63]`/`[64]`/`[65]`) Data 필드 조립기. `PRD.md` §3.4대로
    /// STX/길이/ETX/LRC는 여기서 만들지 않는다 — `Reader_SendCommand`(DLL)가 처리하므로 이 클래스는
    /// data payload byte[]만 반환한다(호출자가 그대로 `Reader_SendCommand`에 넘긴다).
    ///
    /// HASH/RND/SIGN/암호화 데이터/MAC은 전부 SPEC상 hex→ascii expanding된 X(문자) 필드다(§3.4 —
    /// HASH "RND의 SHA256 해시값, 2byte expanding(hex→ascii)") — `Protocol/KeyDownload/
    /// IsoKeyDownloadRequestBuilder`가 P-28/P-29 payload를 문자열로 다루는 것과 동일한 이유로, 이
    /// 조립기도 byte[]가 아니라 ASCII 문자열을 입력받는다. 길이가 SPEC과 다르면 호출자(P24-3/
    /// P24-5가 리더기 응답을 그대로 잘라 붙이는 조립 실수)이므로 예외로 알린다 — 이 값은 하드웨어
    /// 원본 응답이 아니라 호출자가 스스로 조립한 값이라, 응답 파서의 "예외를 던지지 않는다"
    /// 관례(Protocol/Reader/*ResponseParser)가 여기에는 적용되지 않는다
    /// (`Protocol/KeyDownload/IsoKeyDownloadRequestBuilder`와 동일한 원칙).
    /// </summary>
    internal static class KeyDownloadRequestBuilder
    {
        // ===================== [64] 키 다운로드 상호 인증 요청 =====================
        internal const int AuthHashLength = 64;
        internal const int AuthRndLength = 32;
        internal const int AuthSignLength = 512;

        /// <summary>[64] data 전체 길이(byte) — 64 + 32 + 512 = 608.</summary>
        internal const int AuthRequestLength = AuthHashLength + AuthRndLength + AuthSignLength;

        // ===================== [65] Using Key 전송 요청 =====================
        internal const int UsingKeyEncryptedDataLength = 128;
        internal const int UsingKeyMacLength = 16;

        /// <summary>[65] data 전체 길이(byte) — 128 + 16 = 144.</summary>
        internal const int UsingKeyRequestLength = UsingKeyEncryptedDataLength + UsingKeyMacLength;

        /// <summary>
        /// [63] 키 다운로드 시작 요청 — 요청 데이터 없음(§3.4).
        ///
        /// 경고(C-A, Phase 24 2차 Opus 리뷰, 치명적 회귀 사건): 이 메서드는 현재 아무도 호출하지
        /// 않는다 — 절대로 <see cref="Services.Reader.ReaderService.SendKeyDownloadStartCommandAsync"/>
        /// 에서 이 메서드를 다시 쓰지 마라. <c>Array.Empty&lt;byte&gt;()</c>를 net48/x86 P/Invoke로
        /// DLL(Reader_SendCommand)에 넘기면 non-null 포인터로 마샬링되는데, 실제 DLL
        /// (ReaderApi.cpp)의 인자 검증은 "data != nullptr &amp;&amp; dataLength &lt;= 0"이면
        /// READER_ERR_INVALID_ARGUMENT(-1001)를 반환한다 — 즉 이 메서드가 돌려주는 값을 그대로
        /// 넘기면 [63] 요청이 DLL 레벨에서 항상 실패한다(리더기 없는 COM 포트로 실증됨: null,0 ->
        /// READER_OK, Array.Empty&lt;byte&gt;(),0 -> -1001). ReaderService 쪽은 반드시 null, 0을
        /// 직접 <c>SendAndAwaitAsync</c>에 넘겨야 한다. 이 메서드는 "세 명령의 조립 창구를 하나로
        /// 통일한다"는 원래 의도로 남겨두되(지우다가 또 실수할 위험보다 안 쓰이는 메서드 하나가
        /// 남는 비용이 작다는 판단), 실제로는 죽은 코드다.
        /// </summary>
        internal static byte[] BuildStartRequest() => Array.Empty<byte>();

        /// <summary>[64] data 조립 — HASH(64) + RND(32) + SIGN(512), 그 순서 그대로 이어붙인다
        /// (§3.4 표 순서).</summary>
        internal static byte[] BuildAuthRequest(string hash, string rnd, string sign)
        {
            RequireLength(hash, AuthHashLength, nameof(hash));
            RequireLength(rnd, AuthRndLength, nameof(rnd));
            RequireLength(sign, AuthSignLength, nameof(sign));

            return Encoding.ASCII.GetBytes(hash + rnd + sign);
        }

        /// <summary>[65] data 조립 — 암호화 데이터(128) + MAC(16), 그 순서 그대로 이어붙인다
        /// (§3.4 표 순서. SPEC 표기가 LRC/ETX 순서만 예외적으로 뒤바뀌어 있으나 프레임 조립은
        /// DLL 몫이라 이 data 조립에는 영향이 없다 — §3.4 하단 주석).</summary>
        internal static byte[] BuildUsingKeyRequest(string encryptedData, string mac)
        {
            RequireLength(encryptedData, UsingKeyEncryptedDataLength, nameof(encryptedData));
            RequireLength(mac, UsingKeyMacLength, nameof(mac));

            return Encoding.ASCII.GetBytes(encryptedData + mac);
        }

        private static void RequireLength(string value, int expectedLength, string paramName)
        {
            if (value == null || value.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"길이가 정확히 {expectedLength}byte여야 합니다(실제: {(value == null ? "null" : value.Length.ToString())}).",
                    paramName);
            }
        }
    }
}
