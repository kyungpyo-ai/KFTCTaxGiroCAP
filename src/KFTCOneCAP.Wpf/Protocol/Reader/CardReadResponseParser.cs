using System;
using KFTCOneCAP.Wpf.Security;

namespace KFTCOneCAP.Wpf.Protocol.Reader
{
    /// <summary>
    /// 카드 리딩 응답(0x3B)의 응답코드="00"(정상) 케이스 전체 필드. 필드 순서/길이 출처
    /// (reader-pinpad-spec-expert 확인, 2026-08-19): `암호화리더기설계서_20250122.pdf`
    /// §3.39 "거래정보"(footer p.89~91 / PDF p.94~96)의 `[3B] 거래정보 응답` 테이블(응답코드
    /// 다음 22개 필드). VAN 요청 매핑은 Phase 17로 보류한다(PRD §10) — 여기서는 구조화해서
    /// 보관만 한다.
    ///
    /// <b>타입(Phase 25 P25-3, PRD.md §4.3.2)</b>: 19개 필드 전부 <c>char[]</c>다 — 인증 시험 기준이
    /// 요구하는 "덮어쓸 수 있는 타입"으로 관리하기 위해서다(<c>string</c>은 불변이라 지울 수 없다).
    /// 필드별로 민감도를 판단하지 않고 전부 대상으로 잡았다(거래구분·키버전처럼 언뜻 안 민감해
    /// 보이는 필드도 포함 — 판단이 들어가면 누락 위험이 생긴다).
    ///
    /// <b><see cref="IDisposable"/></b>: <see cref="Dispose"/>가 19개 필드를 전부
    /// <see cref="SecureClear.Clear(char[])"/>로 지운다(3회 덮어쓰기). 이 거래가 더 이상 필요 없어지는
    /// 시점(거래 1건 종료, `PaymentOrchestrator.RunCardTransactionAsync`의 `finally`)에 호출된다
    /// (Phase 25 P25-6). 필드 개수와 클리어 대상 개수가 항상 일치하도록 이 메서드 하나에 모아 둔다 —
    /// 필드가 늘어도 여기만 고치면 누락되지 않는다.
    ///
    /// <b>예외 — <see cref="ReaderAuthId"/> 영속화</b>: Phase 22 P22-7이 이 값을
    /// <c>ObservedIdentityStore</c>(SQLite, `observed_identity` 테이블)에 진단 컨텍스트로 저장한다
    /// (PRD.md §1.6). 카드소유자 정보가 아니라 리더기 하드웨어 식별자라 저장 자체는 유지하되, 그
    /// 저장 호출 직전에만 <c>new string(ReaderAuthId)</c>로 변환한다(<c>PaymentOrchestrator</c>의
    /// 해당 호출부 주석 참고) — 이 클래스 수준에서는 여전히 다른 18개 필드와 동일하게 <c>char[]</c>로
    /// 관리하고 <see cref="Dispose"/> 대상에 포함한다.
    /// </summary>
    internal sealed class CardReadData : IDisposable
    {
        internal char[] TransactionType { get; }         // 거래구분 X(1)
        internal char[] KeyVersion { get; }               // 키 버전 X(2)
        internal char[] Tc { get; }                        // TC X(6)
        internal char[] ModuleId { get; }                  // 모듈 ID X(10)
        internal char[] FallbackCode { get; }               // Fallback 코드 X(1), '0'~'7'
        internal char[] Amount { get; }                     // 거래 금액 X(18)
        internal char[] CardNumber { get; }                 // 카드 번호 V(카드번호길이)
        internal char[] EncryptionMarker { get; }           // 암호화 구분자 X(3), "ENC"/"PON"
        internal char[] Wcc { get; }                        // WCC X(1)
        internal char[] EncryptedData { get; }               // 암호화 데이터 V(암호화데이터길이)

        /// <summary>리더기가 실제로 보낸 "암호화데이터 길이" 필드(3자리, 왼쪽 '0' 패딩) 원문.
        /// <see cref="EncryptedData"/>를 정확히 그 길이만큼 읽었으므로 그 길이의 3자리 표현과
        /// 항상 같다 — 2026-09-01 사용자 확정(PaymentOrchestrator.FillCardApprovalFields의 #46 필드 구성용,
        /// SPEC 문서 근거 아님. 상세는 그쪽 클래스 주석 참고).</summary>
        internal char[] EncryptedDataLengthText { get; }
        internal char[] EmvEncodingMethod { get; }           // EMV 인코딩 방식 X(1), "B"/"E"
        internal char[] EmvEncodedData { get; }              // EMV 인코딩 데이터 V(EMV데이터길이)
        internal char[] ReaderAuthId { get; }                // 리더기 인증 식별 번호 X(16)
        internal char[] ReaderSerialEncryptionMarker { get; } // 리더기 고유번호 암호화 구분자 X(3), "NOE"/"ENC"
        internal char[] ReaderSerial { get; }                 // 리더기 고유번호 V(리더기고유번호길이)
        internal char[] ReaderEncryptionInfo { get; }         // 리더기 암호화 정보 X(20)
        internal char[] Tc3 { get; }                          // TC3 X(6)
        internal char[] PayOnCertifyCode { get; }             // payOn 인증코드 X(32)

        private bool _disposed;

        internal CardReadData(
            char[] transactionType, char[] keyVersion, char[] tc, char[] moduleId, char[] fallbackCode,
            char[] amount, char[] cardNumber, char[] encryptionMarker, char[] wcc, char[] encryptedData,
            char[] encryptedDataLengthText,
            char[] emvEncodingMethod, char[] emvEncodedData, char[] readerAuthId,
            char[] readerSerialEncryptionMarker, char[] readerSerial, char[] readerEncryptionInfo,
            char[] tc3, char[] payOnCertifyCode)
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
            EncryptedDataLengthText = encryptedDataLengthText;
            EmvEncodingMethod = emvEncodingMethod;
            EmvEncodedData = emvEncodedData;
            ReaderAuthId = readerAuthId;
            ReaderSerialEncryptionMarker = readerSerialEncryptionMarker;
            ReaderSerial = readerSerial;
            ReaderEncryptionInfo = readerEncryptionInfo;
            Tc3 = tc3;
            PayOnCertifyCode = payOnCertifyCode;
        }

        /// <summary>19개 필드를 전부 3회 덮어쓴다(<see cref="SecureClear"/>). 여러 번 불러도 무해하다
        /// (<see cref="SecureClear.Clear(char[])"/>가 빈 배열도 무해하게 처리하지만, 이 메서드는 아예
        /// 두 번째 호출부터 아무 것도 하지 않도록 <see cref="_disposed"/>로 막는다 — 이미 지운 배열을
        /// 또 지우는 무의미한 작업을 피한다).</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            SecureClear.Clear(TransactionType);
            SecureClear.Clear(KeyVersion);
            SecureClear.Clear(Tc);
            SecureClear.Clear(ModuleId);
            SecureClear.Clear(FallbackCode);
            SecureClear.Clear(Amount);
            SecureClear.Clear(CardNumber);
            SecureClear.Clear(EncryptionMarker);
            SecureClear.Clear(Wcc);
            SecureClear.Clear(EncryptedData);
            SecureClear.Clear(EncryptedDataLengthText);
            SecureClear.Clear(EmvEncodingMethod);
            SecureClear.Clear(EmvEncodedData);
            SecureClear.Clear(ReaderAuthId);
            SecureClear.Clear(ReaderSerialEncryptionMarker);
            SecureClear.Clear(ReaderSerial);
            SecureClear.Clear(ReaderEncryptionInfo);
            SecureClear.Clear(Tc3);
            SecureClear.Clear(PayOnCertifyCode);
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
        /// <summary>ASCII 숫자 2문자(응답코드)는 카드정보가 아니라 그대로 <c>string</c>으로 다룬다 —
        /// <see cref="CardReadData"/> 필드가 아니므로 Phase 25 타입 변경 대상이 아니다.</summary>
        internal static CardReadResponseResult Parse(byte[] data)
        {
            if (data == null || data.Length < 2)
                return CardReadResponseResult.Failed();

            string code = System.Text.Encoding.ASCII.GetString(data, 0, 2);
            if (code != "00")
            {
                // "07"/"12"/그 외: 이 프로젝트는 카드 데이터를 쓰지 않으므로(위 클래스 주석 참고)
                // 남은 바이트를 해석하지 않는다 — 있어도 무시, 없어도 실패 처리하지 않는다.
                return CardReadResponseResult.Of(code, null);
            }

            var cursor = new SequentialAsciiFieldReader(data, 2);

            if (!cursor.TryReadFixed(1, out char[] transactionType)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(2, out char[] keyVersion)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(6, out char[] tc)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(10, out char[] moduleId)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(1, out char[] fallbackCode)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(18, out char[] amount)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadLengthThenPayload(2, out char[] cardNumber)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadFixed(3, out char[] encryptionMarker)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(1, out char[] wcc)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadLengthThenPayload(3, out char[] encryptedData)) return CardReadResponseResult.Failed();
            // 2026-09-01 사용자 확정(PaymentOrchestrator.FillCardApprovalFields #46 주석 참고) — 방금
            // 정확히 이 길이만큼 읽었으므로 재구성해도 안전하다(원래 3자리 길이 필드 원문과 항상 같다).
            char[] encryptedDataLengthText = FormatLength3(encryptedData.Length);

            if (!cursor.TryReadFixed(1, out char[] emvEncodingMethod)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadLengthThenPayload(4, out char[] emvEncodedData)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadFixed(16, out char[] readerAuthId)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(3, out char[] readerSerialEncryptionMarker)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadLengthThenPayload(3, out char[] readerSerial)) return CardReadResponseResult.Failed();

            if (!cursor.TryReadFixed(20, out char[] readerEncryptionInfo)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(6, out char[] tc3)) return CardReadResponseResult.Failed();
            if (!cursor.TryReadFixed(32, out char[] payOnCertifyCode)) return CardReadResponseResult.Failed();

            var cardData = new CardReadData(
                transactionType, keyVersion, tc, moduleId, fallbackCode, amount, cardNumber,
                encryptionMarker, wcc, encryptedData, encryptedDataLengthText, emvEncodingMethod, emvEncodedData,
                readerAuthId, readerSerialEncryptionMarker, readerSerial, readerEncryptionInfo, tc3, payOnCertifyCode);

            return CardReadResponseResult.Of(code, cardData);
        }

        /// <summary>길이(0~999)를 3자리 zero-padded <c>char[]</c>로 만든다. <c>int.ToString("D3")</c>를
        /// 쓰지 않는다 — 이 값 자체는 민감하지 않지만, 그 자리에서 만든 임시 <c>string</c>을 남기지
        /// 않는다는 원칙을 이 파서 전체에 일관되게 적용한다(Phase 25 P25-3).</summary>
        private static char[] FormatLength3(int length)
        {
            if (length < 0 || length > 999)
                throw new ArgumentOutOfRangeException(nameof(length), length, "3자리로 표현할 수 없는 길이");

            return new[] { (char)('0' + length / 100), (char)('0' + length / 10 % 10), (char)('0' + length % 10) };
        }
    }

    /// <summary>
    /// 0x3B처럼 "고정폭 필드"와 "숫자 길이 필드 + 그 길이만큼의 가변 payload"가 순서대로 이어지는
    /// SPEC 응답을 순차적으로 읽는 헬퍼. 매 단계 바이트가 부족하면 예외 대신 false를 반환한다
    /// (Phase 10 P10-1 "파싱 실패를 결과 값으로" 원칙).
    ///
    /// <b>Phase 25 P25-3</b> — 모든 필드를 <c>char[]</c>로 직접 만든다. 바이트를 ASCII 문자로 옮기는
    /// 과정에서 <c>Encoding.ASCII.GetString</c>을 쓰지 않는다 — 그 순간 지울 수 없는 <c>string</c>이
    /// 생기기 때문이다(ASCII 0~127은 대응하는 유니코드 코드포인트와 1:1이라 바이트→char 캐스팅만으로
    /// 안전하게 변환된다).
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

        internal bool TryReadFixed(int length, out char[] chars)
        {
            if (_offset + length > _data.Length)
            {
                chars = Array.Empty<char>();
                return false;
            }

            chars = new char[length];
            for (int i = 0; i < length; i++)
                chars[i] = (char)_data[_offset + i];
            _offset += length;
            return true;
        }

        /// <summary>SPEC의 9(n) 숫자 길이 필드(ASCII 숫자, 왼쪽 '0' 패딩)를 읽어 그 값만큼의
        /// 가변 payload를 이어서 읽는다. 길이 필드 자체가 숫자로 해석되지 않으면 실패로 처리한다.
        /// 길이 필드 자체는 민감정보가 아니므로(자릿수일 뿐) 클리어 대상이 아니다.</summary>
        internal bool TryReadLengthThenPayload(int lengthDigits, out char[] payload)
        {
            if (!TryReadFixed(lengthDigits, out char[] lengthChars) || !TryParseDigits(lengthChars, out int length))
            {
                payload = Array.Empty<char>();
                return false;
            }

            return TryReadFixed(length, out payload);
        }

        private static bool TryParseDigits(char[] digits, out int value)
        {
            value = 0;
            foreach (char c in digits)
            {
                if (c < '0' || c > '9')
                {
                    value = 0;
                    return false;
                }

                value = value * 10 + (c - '0');
            }

            return true;
        }
    }
}
