using System.Text;

namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 카드 리딩 응답(0x3B)의 응답코드="00"(정상) 케이스 전체 필드. 필드 순서/길이 출처
    /// (reader-pinpad-spec-expert 확인, 2026-08-19): `암호화리더기설계서_20250122.pdf`
    /// §3.39 "거래정보"(footer p.89~91 / PDF p.94~96)의 `[3B] 거래정보 응답` 테이블(응답코드
    /// 다음 22개 필드). VAN 요청 매핑은 Phase 17로 보류한다(PRD §10) — 여기서는 구조화해서
    /// 보관만 한다.
    /// </summary>
    internal sealed class CardReadData
    {
        internal string TransactionType { get; }         // 거래구분 X(1)
        internal string KeyVersion { get; }               // 키 버전 X(2)
        internal string Tc { get; }                        // TC X(6)
        internal string ModuleId { get; }                  // 모듈 ID X(10)
        internal string FallbackCode { get; }               // Fallback 코드 X(1), '0'~'7'
        internal string Amount { get; }                     // 거래 금액 X(18)
        internal string CardNumber { get; }                 // 카드 번호 V(카드번호길이)
        internal string EncryptionMarker { get; }           // 암호화 구분자 X(3), "ENC"/"PON"
        internal string Wcc { get; }                        // WCC X(1)
        internal string EncryptedData { get; }               // 암호화 데이터 V(암호화데이터길이)
        internal string EmvEncodingMethod { get; }           // EMV 인코딩 방식 X(1), "B"/"E"
        internal string EmvEncodedData { get; }              // EMV 인코딩 데이터 V(EMV데이터길이)
        internal string ReaderAuthId { get; }                // 리더기 인증 식별 번호 X(16)
        internal string ReaderSerialEncryptionMarker { get; } // 리더기 고유번호 암호화 구분자 X(3), "NOE"/"ENC"
        internal string ReaderSerial { get; }                 // 리더기 고유번호 V(리더기고유번호길이)
        internal string ReaderEncryptionInfo { get; }         // 리더기 암호화 정보 X(20)
        internal string Tc3 { get; }                          // TC3 X(6)
        internal string PayOnCertifyCode { get; }             // payOn 인증코드 X(32)

        internal CardReadData(
            string transactionType, string keyVersion, string tc, string moduleId, string fallbackCode,
            string amount, string cardNumber, string encryptionMarker, string wcc, string encryptedData,
            string emvEncodingMethod, string emvEncodedData, string readerAuthId,
            string readerSerialEncryptionMarker, string readerSerial, string readerEncryptionInfo,
            string tc3, string payOnCertifyCode)
        {
            TransactionType = transactionType;
            KeyVersion = keyVersion;
            Tc = tc;
            ModuleId = moduleId;
            FallbackCode = fallbackCode;
            Amount = amount;
            CardNumber = cardNumber;
            EncryptionMarker = encryptionMarker;
            Wcc = wcc;
            EncryptedData = encryptedData;
            EmvEncodingMethod = emvEncodingMethod;
            EmvEncodedData = emvEncodedData;
            ReaderAuthId = readerAuthId;
            ReaderSerialEncryptionMarker = readerSerialEncryptionMarker;
            ReaderSerial = readerSerial;
            ReaderEncryptionInfo = readerEncryptionInfo;
            Tc3 = tc3;
            PayOnCertifyCode = payOnCertifyCode;
        }
    }

    /// <summary>
    /// 0x3B 파싱 결과. ParseFailed는 SPEC 형식에 못 미치는 데이터(길이 부족 등)일 때만 true —
    /// 예외 대신 결과 값으로 표현한다(Phase 10 P10-1 원칙).
    ///
    /// **응답코드 "07"(FallBack)/"12"(MS거래 불가) 처리 방침과 그 근거**: SPEC 원문 §2.1
    /// "공통 사항"의 일반 규칙("00" 아니면 응답코드 2byte만 송신)이 [3B]에도 적용되는지는
    /// SPEC에 명시적으로 서술돼 있지 않다 — [71]처럼 "이 명령은 예외"라는 문구가 [3B]에는
    /// 없다(reader-pinpad-spec-expert 확인 결과, 2026-08-19. 실기 검증 또는 제조사 재확인 필요
    /// 항목으로 남겨둠). 이 프로젝트는 응답코드가 "07"/"12"일 때 PRD §4.4/§4.5에 따라 재요청만
    /// 수행하고 카드 데이터를 쓰지 않으므로, 이 애매함을 지금 풀지 않아도 업무 로직에는 영향이
    /// 없다 — 이 파서는 "00"일 때만 CardData를 채우고, 그 외 응답코드는 남은 바이트가 있어도
    /// 파싱을 시도하지 않는다(있으면 무시, 없어도 실패 처리하지 않음 — 어느 쪽이든 안전).
    /// </summary>
    internal sealed class CardReadResponseResult
    {
        internal bool ParseFailed { get; }
        internal string ResponseCode { get; }
        internal CardReadData? CardData { get; }

        internal bool IsSuccess => !ParseFailed && ResponseCode == "00";

        /// <summary>PRD §4.4 FALLBACK 처리 대상.</summary>
        internal bool IsFallback => !ParseFailed && ResponseCode == "07";

        /// <summary>PRD §4.5 응답코드 12 처리(재요청) 대상.</summary>
        internal bool IsRetryCode12 => !ParseFailed && ResponseCode == "12";

        private CardReadResponseResult(bool parseFailed, string responseCode, CardReadData? cardData)
        {
            ParseFailed = parseFailed;
            ResponseCode = responseCode;
            CardData = cardData;
        }

        internal static CardReadResponseResult Failed() => new CardReadResponseResult(true, string.Empty, null);

        internal static CardReadResponseResult Of(string responseCode, CardReadData? cardData) =>
            new CardReadResponseResult(false, responseCode, cardData);
    }

    /// <summary>0x3B(카드 리딩 응답) 전문 파서. Services는 이 결과 객체만 받고 바이트를 직접
    /// 다루지 않는다(계층 규칙).</summary>
    internal static class CardReadResponseParser
    {
        internal static CardReadResponseResult Parse(byte[] data)
        {
            if (data == null || data.Length < 2)
                return CardReadResponseResult.Failed();

            string code = Encoding.ASCII.GetString(data, 0, 2);
            if (code != "00")
            {
                // "07"/"12"/그 외: 이 프로젝트는 카드 데이터를 쓰지 않으므로(위 클래스 주석 참고)
                // 남은 바이트를 해석하지 않는다 — 있어도 무시, 없어도 실패 처리하지 않는다.
                return CardReadResponseResult.Of(code, null);
            }

            var cursor = new SequentialAsciiFieldReader(data, 2);

            if (!cursor.TryReadFixed(1, out string transactionType)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(2, out string keyVersion)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(6, out string tc)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(10, out string moduleId)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(1, out string fallbackCode)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(18, out string amount)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadLengthThenPayload(2, out string cardNumber)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadFixed(3, out string encryptionMarker)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(1, out string wcc)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadLengthThenPayload(3, out string encryptedData)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadFixed(1, out string emvEncodingMethod)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadLengthThenPayload(4, out string emvEncodedData)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadFixed(16, out string readerAuthId)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(3, out string readerSerialEncryptionMarker)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadLengthThenPayload(3, out string readerSerial)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadFixed(20, out string readerEncryptionInfo)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(6, out string tc3)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(32, out string payOnCertifyCode)) return CardReadResponseResult.Failed();

            var cardData = new CardReadData(
                transactionType, keyVersion, tc, moduleId, fallbackCode, amount, cardNumber,
                encryptionMarker, wcc, encryptedData, emvEncodingMethod, emvEncodedData, readerAuthId,
                readerSerialEncryptionMarker, readerSerial, readerEncryptionInfo, tc3, payOnCertifyCode);

            return CardReadResponseResult.Of(code, cardData);
        }
    }

    /// <summary>
    /// 0x3B처럼 "고정폭 필드"와 "숫자 길이 필드 + 그 길이만큼의 가변 payload"가 순서대로 이어지는
    /// SPEC 응답을 순차적으로 읽는 헬퍼. 매 단계 바이트가 부족하면 예외 대신 false를 반환한다
    /// (Phase 10 P10-1 "파싱 실패를 결과 값으로" 원칙).
    /// </summary>
    internal sealed class SequentialAsciiFieldReader
    {
        private readonly byte[] _data;
        private int _offset;

        internal SequentialAsciiFieldReader(byte[] data, int startOffset)
        {
            _data = data;
            _offset = startOffset;
        }

        internal bool TryReadFixed(int length, out string text)
        {
            if (_offset + length > _data.Length)
            {
                text = string.Empty;
                return false;
            }

            text = Encoding.ASCII.GetString(_data, _offset, length);
            _offset += length;
            return true;
        }

        /// <summary>SPEC의 9(n) 숫자 길이 필드(ASCII 숫자, 왼쪽 '0' 패딩)를 읽어 그 값만큼의
        /// 가변 payload를 이어서 읽는다. 길이 필드 자체가 숫자로 해석되지 않으면 실패로 처리한다.</summary>
        internal bool TryReadLengthThenPayload(int lengthDigits, out string payload)
        {
            if (!TryReadFixed(lengthDigits, out string lengthText) || !int.TryParse(lengthText, out int length))
            {
                payload = string.Empty;
                return false;
            }

            return TryReadFixed(length, out payload);
        }
    }
}
