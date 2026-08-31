using System;
using System.Text;

namespace KFTCOneCAP.KioskSim.Protocol
{
    /// <summary>
    /// 프레임 형식(업체 개발자가 이 소스에서 가장 먼저 찾아볼 자리 — Phase 19 실행계획서 P19-3):
    ///
    ///   [길이 4자리 ASCII 숫자][본문 바이트]
    ///
    /// - STX/ETX 등 별도 구분 바이트는 없다. 프레임은 "4바이트 길이 헤더 + 본문"이 전부다.
    /// - 길이 4자리는 <b>본문의 바이트 수</b>를 10진수로, 4자리보다 짧으면 앞을 '0'으로 채운
    ///   ASCII 문자열이다(예: 706바이트 본문 → "0706"). CP949/ASCII 모두 이 범위(숫자)에서는
    ///   1바이트=1문자로 동일하므로 어느 인코딩으로 읽어도 같다(이 코덱은 CP949로 통일해서 읽고 쓴다).
    /// - 이 값은 SPEC 표의 "#0 전문 길이"와 같은 값이다. 다만 "#0 전문 길이"는 본문 밖의
    ///   프레임 헤더이므로 <see cref="TelegramSchema"/>/<see cref="TelegramField"/>에는 필드로
    ///   등록돼 있지 않다(P17-2, P19-1 전제 1) — 이 코덱이 그 헤더 4바이트를 전담해서 다룬다.
    /// - 전문별 본문 길이는 고정이다: 501008=706바이트, 800000=500바이트, 902614=1500바이트.
    ///   즉 프레임 전체 길이는 각각 710/504/1504바이트가 된다.
    /// - 이 클래스는 "완성된 바이트 뭉치가 있을 때"의 프레임 구조 변환만 다룬다. TCP 소켓에서
    ///   나눠 들어오는 바이트를 누적해 완성된 프레임으로 모으는 일(부분 수신 처리)은 여기 범위가
    ///   아니다 — 그건 Net/OneCapClient(P19-4)의 책임이다.
    /// </summary>
    public static class TelegramCodec
    {
        /// <summary>길이 헤더의 고정 자릿수.</summary>
        public const int LengthHeaderSize = 4;

        /// <summary>
        /// CP949(Windows-949) 인코딩. 길이 헤더는 숫자만 다루므로 사실상 ASCII와 동일하게
        /// 동작하지만, 본문과 같은 인코딩으로 통일해 두는 것이 프레임 전체를 한 번에 바이트로
        /// 다룰 때 혼동이 없다. 본 앱 소스를 참조하지 않는다는 원칙(P19-2)에 따라 이 파일
        /// 안에서 독립적으로 정의한다.
        /// </summary>
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949);

        /// <summary>
        /// 본문 바이트를 받아 "[길이 4자리][본문]" 프레임 바이트로 감싼다.
        /// 본문 바이트 수가 4자리(0~9999)를 넘으면 표현할 수 없으므로 예외를 던진다
        /// (현재 3전문 중 가장 긴 902614도 1500바이트라 실무적으로는 걸릴 일이 없지만,
        /// 새 전문이 추가됐을 때 조용히 잘못된 프레임을 만드는 것을 막기 위한 방어다).
        /// </summary>
        public static byte[] Encode(byte[] body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (body.Length > 9999)
            {
                throw new InvalidOperationException(
                    $"본문 길이({body.Length}바이트)가 길이 헤더 4자리로 표현 가능한 최대값(9999)을 넘는다.");
            }

            string lengthHeader = body.Length.ToString("D4");
            byte[] headerBytes = Cp949.GetBytes(lengthHeader);

            var frame = new byte[headerBytes.Length + body.Length];
            Array.Copy(headerBytes, 0, frame, 0, headerBytes.Length);
            Array.Copy(body, 0, frame, headerBytes.Length, body.Length);
            return frame;
        }

        /// <summary>
        /// 완성된 프레임 바이트(길이 헤더 4바이트 + 본문 전체)에서 본문만 분리해 반환한다.
        /// 프레임이 헤더보다 짧거나, 헤더의 숫자 4자리가 실제 남은 바이트 수와 다르면 예외를
        /// 던진다.
        /// </summary>
        public static byte[] Decode(byte[] frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (frame.Length < LengthHeaderSize)
            {
                throw new InvalidOperationException(
                    $"프레임이 길이 헤더({LengthHeaderSize}바이트)보다 짧다: 실제 {frame.Length}바이트.");
            }

            int bodyLength = ReadLengthHeader(frame, 0);
            int expectedFrameLength = LengthHeaderSize + bodyLength;
            if (frame.Length != expectedFrameLength)
            {
                throw new InvalidOperationException(
                    $"프레임 길이 불일치: 길이 헤더 값={bodyLength}(본문 바이트 수), " +
                    $"기대 프레임 전체 길이={expectedFrameLength}, 실제 프레임 길이={frame.Length}.");
            }

            var body = new byte[bodyLength];
            Array.Copy(frame, LengthHeaderSize, body, 0, bodyLength);
            return body;
        }

        /// <summary>
        /// 길이 헤더 4바이트(offset부터)를 읽어 본문 바이트 수를 반환한다. TCP 클라이언트가
        /// "헤더까지는 왔는데 본문이 아직 다 안 왔다"를 판단할 때(P19-4) 이 값을 먼저 알아야
        /// 하므로 Decode와 분리해 공개해 둔다.
        /// 헤더가 4자리 숫자가 아니면 예외를 던진다.
        /// </summary>
        public static int ReadLengthHeader(byte[] buffer, int offset)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset + LengthHeaderSize > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset,
                    $"길이 헤더({LengthHeaderSize}바이트)를 읽기에 buffer 범위가 부족하다.");
            }

            string lengthHeader = Cp949.GetString(buffer, offset, LengthHeaderSize);

            // int.TryParse(string)은 기본 스타일(NumberStyles.Integer)로 앞뒤 공백과 부호(+/-)를
            // 허용한다 — 이 프로토콜은 "4자리 모두 ASCII 숫자"만 유효한 고정길이 헤더이므로 그보다
            // 엄격해야 한다(예: " 706"/"+706"이 int.TryParse로는 통과해버리는 문제가 있었다 —
            // 체크포인트 1 리뷰에서 발견). 4문자 전부가 '0'~'9' 범위인지 먼저 직접 검사한다.
            foreach (char c in lengthHeader)
            {
                if (c < '0' || c > '9')
                {
                    throw new InvalidOperationException(
                        $"길이 헤더가 4자리 숫자가 아니다: \"{lengthHeader}\".");
                }
            }

            int bodyLength = int.Parse(lengthHeader, System.Globalization.CultureInfo.InvariantCulture);
            return bodyLength;
        }
    }
}
