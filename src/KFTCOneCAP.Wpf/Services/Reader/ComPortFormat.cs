using System.Linq;

namespace KFTCOneCAP.Wpf.Services.Reader
{
    /// <summary>
    /// Phase 12(docs/payment_relay/development_plan.md P12-2) — 같은 COM 포트를 가리키는 여러 표현
    /// (콤보/레지스트리 표시 문자열 "COM 05", <c>SerialPort.GetPortNames()</c> 반환값 "COM5",
    /// <c>Reader_OpenPort</c>의 정수 <c>portNumber</c>) 사이의 변환을 이 클래스 한 곳에만 둔다.
    ///
    /// 형식이 흩어지면 조용한 버그가 생긴다 — 특히 DB(<see cref="Storage.IntegrityCheckStore"/>)의
    /// <c>ComPort</c> 컬럼은 문자열 완전 일치로 조회되므로(<c>HasSuccessToday</c>), 저장/조회 양쪽이
    /// 항상 같은 형식(<see cref="ToDisplay"/> 결과, 예: "COM 05")을 써야 한다. "(사용불가)" 접미가
    /// 붙은 값(저장된 포트가 현재 열거되지 않을 때, P12-2)은 정수 변환/DB 저장 전에 반드시
    /// <see cref="StripUnavailableSuffix"/>로 걷어낸다 — 접미가 붙은 문자열을 그대로 흘리지 않는다.
    /// </summary>
    internal static class ComPortFormat
    {
        internal const string Unused = "미사용";

        private const string UnavailableSuffix = "(사용불가)";

        /// <summary>콤보/레지스트리 표시 형식으로 통일한다 — "COM %02d"(공백 포함 2자리, 예: "COM 05").</summary>
        internal static string ToDisplay(int portNumber) => $"COM {portNumber:D2}";

        /// <summary>저장된 포트가 현재 열거 목록에 없을 때(P12-2) 선택 상태를 유지하기 위해 붙이는
        /// 표시 전용 접미. 조용히 "미사용"으로 되돌리지 않기 위함이다.</summary>
        internal static string ToUnavailableDisplay(int portNumber) => ToDisplay(portNumber) + UnavailableSuffix;

        /// <summary>"COM 05(사용불가)" 형태에서 접미를 걷어내 저장/전송에 쓸 수 있는 깨끗한 표시
        /// 문자열("COM 05")로 되돌린다. 접미가 없으면 그대로 반환한다.</summary>
        internal static string StripUnavailableSuffix(string display)
        {
            if (string.IsNullOrEmpty(display))
                return display;

            return display.EndsWith(UnavailableSuffix)
                ? display.Substring(0, display.Length - UnavailableSuffix.Length)
                : display;
        }

        /// <summary>
        /// "COM 05"/"COM 05(사용불가)" 같은 표시 문자열에서 숫자만 뽑아 <c>Reader_OpenPort</c>가
        /// 요구하는 정수 portNumber로 변환한다. "미사용"이거나 숫자를 찾을 수 없으면 -1을 반환한다.
        /// 이 프로젝트에서 표시 문자열 → 정수 변환은 이 메서드 하나뿐이다(P12-2 완료 조건).
        /// </summary>
        internal static int ToPortNumber(string display)
        {
            if (string.IsNullOrEmpty(display))
                return -1;

            string clean = StripUnavailableSuffix(display);
            if (clean == Unused)
                return -1;

            var digits = new string(clean.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int value) ? value : -1;
        }

        /// <summary>
        /// <c>SerialPort.GetPortNames()</c>가 돌려주는 시스템 포트 이름("COM5", "COM11")에서 숫자만
        /// 뽑는다. 표시 형식("COM 05")과는 자리수/공백이 달라 별도 파서가 필요하다.
        /// </summary>
        internal static int ParseSystemPortName(string systemPortName)
        {
            if (string.IsNullOrEmpty(systemPortName))
                return -1;

            var digits = new string(systemPortName.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int value) ? value : -1;
        }
    }
}
