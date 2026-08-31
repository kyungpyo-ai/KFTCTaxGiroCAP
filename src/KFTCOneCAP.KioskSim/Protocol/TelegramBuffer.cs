using System;
using System.Text;

namespace KFTCOneCAP.KioskSim.Protocol
{
    /// <summary>
    /// 전문 본문(고정 길이 바이트 배열)을 필드 단위로 읽고 쓰는 버퍼.
    ///
    /// Phase 19 실행계획서(docs/payment_relay/development_plan.md) P19-3: 규칙은 SPEC 원문 그대로다.
    /// - 전체를 space(0x20)로 초기화한 뒤 시작한다(SPEC 공통부 표 하단 각주: "체크 없는 필드는
    ///   space로 채워서 총 길이로 전문 생성").
    /// - 표현이 <see cref="TelegramRepresentation.N"/>(숫자)인 필드는 우측 정렬 + 앞을 '0'으로 채운다.
    /// - 그 외(A/AN/AHN/ANS/AHNS, 즉 문자 계열)는 좌측 정렬 + 뒤를 space로 채운다.
    /// - 값의 CP949 바이트 길이가 필드 허용 길이를 넘으면 잘라내지 않고 즉시 예외를 던진다 —
    ///   조용히 잘리면 나중에 "어떤 값이 어디서 잘렸는지" 원인 추적이 불가능해지기 때문이다.
    /// </summary>
    public sealed class TelegramBuffer
    {
        /// <summary>
        /// CP949(한글 EUC 계열, Windows-949) 인코딩. .NET Framework/코어 공통으로 코드페이지
        /// 번호(949)로 얻는다 — 본 앱 PosMessageEncoding.cs와 같은 방식이지만 이 프로젝트는
        /// 본 앱 소스를 참조하지 않는다는 원칙(P19-2)에 따라 이 파일 안에서 독립적으로 정의한다.
        /// </summary>
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949);

        private readonly byte[] _body;

        /// <summary>이 버퍼가 따르는 전문 스키마.</summary>
        public TelegramSchema Schema { get; }

        /// <summary>본문 총 길이(바이트). Schema.TotalLength와 항상 같다.</summary>
        public int Length => _body.Length;

        /// <summary>
        /// 스키마가 정한 총 길이만큼 새 버퍼를 만들고 전체를 space(0x20)로 초기화한다.
        /// </summary>
        public TelegramBuffer(TelegramSchema schema)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _body = new byte[schema.TotalLength];
            for (int i = 0; i < _body.Length; i++)
                _body[i] = 0x20;
        }

        /// <summary>
        /// 이미 받은(또는 이미 만들어진) 본문 바이트를 그대로 감싸 읽기용 버퍼를 만든다.
        /// Phase 19 실행계획서 P19-6: 응답 화면이 원캡 응답 본문을 이 스키마로 분해해 보여줄 때 쓴다
        /// (요청은 <see cref="TelegramBuffer(TelegramSchema)"/> + <see cref="Write"/>로 새로 만들지만,
        /// 응답은 이미 완성된 바이트를 "읽기만" 하면 되므로 별도 생성자를 둔다). 본문 길이가
        /// 스키마 총 길이와 다르면 즉시 예외를 던진다 — 응답이 다른 전문이거나 길이가 깨진 것을
        /// 조용히 넘기지 않기 위함이다.
        /// </summary>
        public TelegramBuffer(TelegramSchema schema, byte[] body)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (body.Length != schema.TotalLength)
            {
                throw new InvalidOperationException(
                    $"{schema.TxType} 전문 본문 길이 불일치: 기대 {schema.TotalLength}바이트, 실제 {body.Length}바이트.");
            }

            _body = new byte[body.Length];
            Array.Copy(body, _body, body.Length);
        }

        /// <summary>
        /// 필드 번호에 해당하는 값을 채운다. 표현에 따라 정렬 규칙이 다르다(클래스 주석 참고).
        /// 필드 번호가 스키마에 없거나, 값의 CP949 바이트 길이가 필드 허용 길이를 넘으면 예외를
        /// 던진다(잘라내지 않는다).
        /// </summary>
        public void Write(int fieldNumber, string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var field = Schema.ByNumber(fieldNumber); // 스키마에 없으면 TelegramSchema.ByNumber가 KeyNotFoundException을 던진다.

            byte[] valueBytes = Cp949.GetBytes(value);
            if (valueBytes.Length > field.Length)
            {
                throw new InvalidOperationException(
                    $"{Schema.TxType} 전문 #{field.Number}({field.Name}) 길이 초과: " +
                    $"허용 길이={field.Length}바이트, 실제 값 길이={valueBytes.Length}바이트(CP949 기준), " +
                    $"값=\"{value}\". 값을 줄여서 다시 시도하라(자동으로 잘라내지 않는다).");
            }

            // 필드가 차지하는 구간을 먼저 space로 되돌려(재작성 대비) 정렬 규칙대로 다시 채운다.
            int start = field.Position;
            for (int i = 0; i < field.Length; i++)
                _body[start + i] = 0x20;

            if (field.Representation == TelegramRepresentation.N)
            {
                // 숫자: 우측 정렬 + 앞을 '0'으로 채움.
                int padding = field.Length - valueBytes.Length;
                for (int i = 0; i < padding; i++)
                    _body[start + i] = (byte)'0';
                Array.Copy(valueBytes, 0, _body, start + padding, valueBytes.Length);
            }
            else
            {
                // 문자 계열(A/AN/AHN/ANS/AHNS): 좌측 정렬 + 뒤는 space(위에서 이미 space로 채워둠).
                Array.Copy(valueBytes, 0, _body, start, valueBytes.Length);
            }
        }

        /// <summary>
        /// 필드 번호에 해당하는 구간을 CP949로 디코딩해 반환한다.
        ///
        /// 문자 계열(A/AN/AHN/ANS/AHNS)은 Write 시 뒤쪽을 space로 채우는 좌측 정렬 규칙이므로
        /// 우측 공백을 TrimEnd한다 — 그래야 "값 + 채움 공백"이 아니라 실제 값만 돌려주는 것이
        /// 자연스럽다(예: 이름 필드에 사람이 넣은 값 뒤에 붙은 여백은 의미가 없다).
        /// 숫자(N)는 앞을 '0'으로 채우는 규칙이라 그 '0'이 값의 일부인지(금액 등 자릿수) 채움인지
        /// 필드마다 의미가 다를 수 있어(예: "00012300"을 그대로 보여줘야 SPEC POSITION 대조가 쉬움)
        /// 임의로 TrimStart('0')하지 않고 원문 그대로(공백 포함 가능성 없음, 항상 숫자로 채워짐)
        /// 반환한다 — 숫자값 해석(정수 변환 등)은 호출부의 책임으로 남긴다.
        /// </summary>
        public string Read(int fieldNumber)
        {
            var field = Schema.ByNumber(fieldNumber);
            string raw = Cp949.GetString(_body, field.Position, field.Length);
            return field.Representation == TelegramRepresentation.N ? raw : raw.TrimEnd(' ');
        }

        /// <summary>본문 전체 바이트 배열을 복사본으로 반환한다(내부 배열을 직접 노출하지 않는다).</summary>
        public byte[] ToBytes()
        {
            var copy = new byte[_body.Length];
            Array.Copy(_body, copy, _body.Length);
            return copy;
        }
    }
}
