using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KFTCOneCAP.KioskSim.Protocol;

namespace KFTCOneCAP.KioskSim.Preset
{
    /// <summary>
    /// 실행 파일 옆 <c>kiosksim.preset.json</c> 파일을 읽고 쓴다(Phase 19 실행계획서 P19-5,
    /// 확정된 설계 결정 3번 — 프리셋 방식).
    ///
    /// 저장 구조는 "전문 타입 → 필드 번호 → 값" 2단계 맵이다. <b>필드 POSITION이 아니라 번호를
    /// 키로 쓴다</b> — SPEC이 바뀌어 POSITION이 옮겨져도 프리셋 파일이 깨지지 않게 하기 위함이다
    /// (development_plan.md 원문 지시).
    ///
    /// 외부 NuGet 패키지 0개 원칙(development_plan.md 확정 결정 7)에 따라 JSON을 손으로 직렬화/
    /// 역직렬화한다 — 구조가 "문자열 키 2단계 객체, 리프는 항상 문자열"로 아주 단순해서 범용
    /// JSON 파서보다 이 파일 하나로 충분하다. 다만 겹따옴표/역슬래시/제어문자 이스케이프와 유니코드
    /// 이스케이프(\uXXXX) 정도는 최소한으로 지원해, 한글 값(예: 징수 기관명)이 깨지지 않게 했다.
    /// </summary>
    public static class PresetStore
    {
        public const string PresetFileName = "kiosksim.preset.json";

        /// <summary>실행 파일 옆 프리셋 파일 전체 경로.</summary>
        public static string PresetFilePath
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PresetFileName);

        /// <summary>
        /// <see cref="Load"/> 결과. 파일이 없거나 손상됐어도 예외로 죽지 않고, 그 사실을
        /// 호출부가 알 수 있게 <see cref="FileExisted"/>/<see cref="ParsedOk"/>/<see cref="Warning"/>로
        /// 알려준다(development_plan.md P19-5 지시: "파일이 손상됐으면 예외로 죽이지 말고 빈 값으로
        /// 폴백하되 그 사실을 호출부가 알 수 있게").
        /// </summary>
        public sealed class LoadResult
        {
            /// <summary>프리셋 파일 자체가 존재했는지.</summary>
            public bool FileExisted { get; }

            /// <summary>존재했다면 파싱에 성공했는지(존재하지 않았으면 true — "빈 프리셋"은 오류가 아니다).</summary>
            public bool ParsedOk { get; }

            /// <summary>파싱 실패 등 사람이 읽을 경고 메시지(문제 없으면 null).</summary>
            public string? Warning { get; }

            /// <summary>전문 타입 → (필드 번호 → 값). 파일이 없거나 파싱에 실패하면 빈 맵.</summary>
            public Dictionary<string, Dictionary<int, string>> Values { get; }

            internal LoadResult(bool fileExisted, bool parsedOk, string? warning, Dictionary<string, Dictionary<int, string>> values)
            {
                FileExisted = fileExisted;
                ParsedOk = parsedOk;
                Warning = warning;
                Values = values;
            }

            /// <summary>전문 타입/필드 번호로 저장된 값을 찾는다. 없으면 null.</summary>
            public string? TryGet(string txType, int fieldNumber)
            {
                if (Values.TryGetValue(txType, out var byNumber) && byNumber.TryGetValue(fieldNumber, out var value))
                    return value;
                return null;
            }
        }

        /// <summary>
        /// 프리셋 파일을 읽는다. 파일이 없으면 빈 결과(<see cref="LoadResult.FileExisted"/>=false)를
        /// 반환하고, 파싱에 실패하면 예외를 던지지 않고 빈 값으로 폴백한 결과(<see cref="LoadResult.ParsedOk"/>=false,
        /// <see cref="LoadResult.Warning"/>에 사유)를 반환한다.
        /// </summary>
        public static LoadResult Load()
        {
            string path = PresetFilePath;
            if (!File.Exists(path))
                return new LoadResult(fileExisted: false, parsedOk: true, warning: null, values: new Dictionary<string, Dictionary<int, string>>());

            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return new LoadResult(
                    fileExisted: true,
                    parsedOk: false,
                    warning: $"프리셋 파일을 읽을 수 없다({path}): {ex.Message}. 코드 기본값으로 대체한다.",
                    values: new Dictionary<string, Dictionary<int, string>>());
            }

            try
            {
                var parsed = MiniJson.ParseObjectOfObjectsOfStrings(text);
                var result = new Dictionary<string, Dictionary<int, string>>();
                foreach (var txTypeEntry in parsed)
                {
                    var byNumber = new Dictionary<int, string>();
                    foreach (var fieldEntry in txTypeEntry.Value)
                    {
                        if (int.TryParse(fieldEntry.Key, out int fieldNumber))
                            byNumber[fieldNumber] = fieldEntry.Value;
                        // 필드 번호로 파싱되지 않는 키(사람이 파일을 손으로 잘못 고친 경우 등)는
                        // 조용히 무시한다 — 파일 전체를 버리지 않고 나머지는 최대한 살린다.
                    }
                    result[txTypeEntry.Key] = byNumber;
                }
                return new LoadResult(fileExisted: true, parsedOk: true, warning: null, values: result);
            }
            catch (Exception ex)
            {
                return new LoadResult(
                    fileExisted: true,
                    parsedOk: false,
                    warning: $"프리셋 파일 파싱에 실패했다({path}): {ex.Message}. 코드 기본값으로 대체한다.",
                    values: new Dictionary<string, Dictionary<int, string>>());
            }
        }

        /// <summary>
        /// 현재 필드 값들을 프리셋 파일에 덮어쓴다. "현재 값을 프리셋으로 저장" 버튼 전용
        /// (자동 저장 없음 — 확정된 설계 결정 3번).
        /// </summary>
        public static void Save(Dictionary<string, Dictionary<int, string>> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            string json = MiniJson.WriteObjectOfObjectsOfStrings(values);
            File.WriteAllText(PresetFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        // --------------------------------------------------------------
        // 코드 기본값 — 프리셋 파일이 없거나 그 필드가 파일에 없을 때 쓰는 값.
        // tools/spec_client.ps1(Phase 17/18 실장비 왕복 검증 완료)이 실제로 SET하는 값을
        // 필드 번호 기준으로 그대로 옮겼다. spec_client.ps1이 값을 SET하지 않고 건너뛴 필드
        // (Set-Field의 "빈 값이면 건너뛴다" 규칙과 동일)는 여기서도 빈 문자열로 둔다 — 빈
        // 문자열은 화면에서 "이 필드는 안 쓴다"는 의도로 해석되어 TelegramBuffer.Write를
        // 호출하지 않고 space로 남긴다(스키마 초기화 규칙과 spec_client의 실제 검증 동작이
        // 정확히 일치).
        // --------------------------------------------------------------

        private static readonly Random DefaultRandom = new Random();

        /// <summary>
        /// txType/fieldNumber에 대한 코드 기본값을 돌려준다. #8(전송 일시)·#9(전문 관리 번호)·
        /// #31/#32(902614 납기/납부 일자)처럼 "매 전송마다 달라져야 자연스러운" 값은 호출 시점의
        /// 현재 시각/난수로 즉석에서 만든다(spec_client.ps1도 매 실행마다 이렇게 만든다).
        /// 나머지는 고정 문자열이다.
        /// </summary>
        public static string GetCodeDefault(string txType, int fieldNumber)
        {
            // 공통부 중 값이 매 전송마다 바뀌는 필드 — 3전문 공통.
            if (fieldNumber == 8)
                return DateTime.Now.ToString("yyMMddHHmmss");
            if (fieldNumber == 9)
                return "0EC0" + DefaultRandom.Next(10_000_000, 99_999_999).ToString();

            switch (txType)
            {
                case "501008":
                    switch (fieldNumber)
                    {
                        case 1: return "IGN";
                        case 2: return "095";
                        case 3: return "0200";
                        case 4: return "501008";
                        case 6: return "G";
                        case 11: return "01";
                        case 12: return "1234567";
                        case 14: return "1234567890123456789";
                        default: return string.Empty;
                    }

                case "800000":
                    switch (fieldNumber)
                    {
                        case 1: return "IGN";
                        case 2: return "095";
                        case 3: return "0200";
                        case 4: return "800000";
                        case 6: return "G";
                        case 11: return "01";
                        case 12: return "1234567";
                        case 15: return "1000";
                        case 16: return "10";
                        default: return string.Empty;
                    }

                case "902614":
                    switch (fieldNumber)
                    {
                        case 1: return "IGN";
                        case 2: return "095";
                        case 3: return "0200";
                        case 4: return "902614";
                        case 6: return "G";
                        case 11: return "01";
                        case 12: return "1234567";
                        case 14: return "8001011234567";
                        case 15: return "1234567890123456789";
                        case 16: return "001";
                        case 18: return "2601510";
                        case 19: return "123456";
                        case 20: return "강남세무서";
                        case 21: return "부가가치세";
                        case 23: return "2026";
                        case 24: return "1000";
                        case 27: return "1000";
                        case 28: return "0";
                        case 29: return "1000";
                        case 30: return "1";
                        case 31: return DateTime.Now.ToString("yyyyMMdd");
                        case 32: return DateTime.Now.ToString("yyyyMMdd");
                        case 33: return "01";
                        case 34: return "00";
                        case 36: return "8001011234567";
                        case 37: return "홍길동";
                        case 39: return "O";
                        case 41: return "Q";
                        case 42: return "1234567890BF0001";
                        case 49: return "0";
                        default: return string.Empty;
                    }

                default:
                    return string.Empty;
            }
        }

        /// <summary>프리셋 파일 값이 있으면 그 값, 없으면 코드 기본값(우선순위: 프리셋 &gt; 코드 기본값).</summary>
        public static string Resolve(LoadResult loaded, string txType, int fieldNumber)
        {
            string? fromFile = loaded?.TryGet(txType, fieldNumber);
            return fromFile ?? GetCodeDefault(txType, fieldNumber);
        }

        /// <summary>
        /// 스키마의 kiosk 편집 가능 필드 전체에 대해 <see cref="Resolve"/>를 적용한 초기 맵을 만든다.
        /// MainForm이 시작 시 세 전문 모두를 채워 두는 데 쓴다.
        /// </summary>
        public static Dictionary<int, string> BuildInitialValues(LoadResult loaded, TelegramSchema schema)
        {
            var result = new Dictionary<int, string>();
            foreach (var field in schema.Fields)
            {
                // AlwaysBlank 필드(그리드에서 편집이 잠긴 필드, 2026-08-28 확정)는 애초에 값을
                // 가질 일이 없으므로 여기서도 제외한다 — 예전에는 이 필드까지 포함해 놓고
                // (기본값이 항상 빈 문자열이라 실질적 동작 차이는 없었지만) "저장/보관은 되는데
                // 화면에는 절대 안 보이는" 죽은 값이 프리셋 파일에 계속 쌓일 수 있었다
                // (2026-08-31 검증에서 발견 — 낮음).
                if (field.SetLocation != TelegramSetLocation.Kiosk || field.AlwaysBlank)
                    continue;
                result[field.Number] = Resolve(loaded, schema.TxType, field.Number);
            }
            return result;
        }
    }
}
