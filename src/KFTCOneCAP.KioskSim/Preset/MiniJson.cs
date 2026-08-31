using System;
using System.Collections.Generic;
using System.Text;

namespace KFTCOneCAP.KioskSim.Preset
{
    /// <summary>
    /// <c>kiosksim.preset.json</c> 전용 최소 JSON 직렬화/역직렬화기.
    ///
    /// 외부 NuGet 패키지 0개 원칙(development_plan.md Phase 19 확정 결정 7)에 따라 손으로 짰다.
    /// 이 프로젝트가 다루는 JSON 구조는 정확히 "문자열 키를 가진 객체의 객체, 리프는 항상 문자열"
    /// 하나뿐이라 범용 JSON 파서를 끌어올 필요가 없다 — 그 구조 하나만 정확히 지원한다.
    /// </summary>
    internal static class MiniJson
    {
        /// <summary>
        /// { "a": { "b": "c" }, ... } 형태의 2단계 객체를 문자열 그대로 JSON으로 만든다.
        /// </summary>
        public static string WriteObjectOfObjectsOfStrings(Dictionary<string, Dictionary<int, string>> value)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            bool firstOuter = true;
            foreach (var outer in value)
            {
                if (!firstOuter) sb.Append(",\n");
                firstOuter = false;
                sb.Append("  ").Append(WriteString(outer.Key)).Append(": {\n");

                bool firstInner = true;
                // 필드 번호 오름차순으로 써서 사람이 파일을 열었을 때 읽기 편하게 한다.
                foreach (var inner in SortByKey(outer.Value))
                {
                    if (!firstInner) sb.Append(",\n");
                    firstInner = false;
                    sb.Append("    ").Append(WriteString(inner.Key.ToString())).Append(": ").Append(WriteString(inner.Value));
                }
                sb.Append("\n  }");
            }
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static List<KeyValuePair<int, string>> SortByKey(Dictionary<int, string> dict)
        {
            var list = new List<KeyValuePair<int, string>>(dict);
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            return list;
        }

        private static string WriteString(string s)
        {
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// 최상위가 객체이고, 그 값들도 전부 객체이며, 그 안의 리프는 전부 문자열이라고 가정하고
        /// 파싱한다. 이 가정을 벗어나면(배열, 숫자, 중첩 3단계 이상 등) 예외를 던진다 — 이 파일이
        /// 다루는 유일한 구조 밖의 내용은 "손상됨"으로 취급하는 것이 맞다(PresetStore.Load가 그
        /// 예외를 잡아 코드 기본값으로 폴백한다).
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> ParseObjectOfObjectsOfStrings(string json)
        {
            int pos = 0;
            var outer = ParseObject(json, ref pos, depth: 0);
            SkipWhitespace(json, ref pos);
            if (pos != json.Length)
                throw new FormatException($"JSON 끝난 뒤에 문자가 더 있다(offset {pos}).");
            return outer;
        }

        private static Dictionary<string, Dictionary<string, string>> ParseObject(string json, ref int pos, int depth)
        {
            SkipWhitespace(json, ref pos);
            Expect(json, ref pos, '{');
            var result = new Dictionary<string, Dictionary<string, string>>();
            SkipWhitespace(json, ref pos);
            if (Peek(json, pos) == '}')
            {
                pos++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(json, ref pos);
                string key = ParseString(json, ref pos);
                SkipWhitespace(json, ref pos);
                Expect(json, ref pos, ':');
                SkipWhitespace(json, ref pos);
                var inner = ParseInnerObject(json, ref pos);
                result[key] = inner;

                SkipWhitespace(json, ref pos);
                char c = Peek(json, pos);
                if (c == ',')
                {
                    pos++;
                    continue;
                }
                if (c == '}')
                {
                    pos++;
                    break;
                }
                throw new FormatException($"object 파싱 중 ',' 또는 '}}'를 기대했지만 '{c}'를 만났다(offset {pos}).");
            }

            return result;
        }

        private static Dictionary<string, string> ParseInnerObject(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            Expect(json, ref pos, '{');
            var result = new Dictionary<string, string>();
            SkipWhitespace(json, ref pos);
            if (Peek(json, pos) == '}')
            {
                pos++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(json, ref pos);
                string key = ParseString(json, ref pos);
                SkipWhitespace(json, ref pos);
                Expect(json, ref pos, ':');
                SkipWhitespace(json, ref pos);
                string value = ParseString(json, ref pos);
                result[key] = value;

                SkipWhitespace(json, ref pos);
                char c = Peek(json, pos);
                if (c == ',')
                {
                    pos++;
                    continue;
                }
                if (c == '}')
                {
                    pos++;
                    break;
                }
                throw new FormatException($"내부 object 파싱 중 ',' 또는 '}}'를 기대했지만 '{c}'를 만났다(offset {pos}).");
            }

            return result;
        }

        private static string ParseString(string json, ref int pos)
        {
            Expect(json, ref pos, '"');
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= json.Length)
                    throw new FormatException("문자열이 끝나지 않고 JSON이 종료됐다.");
                char c = json[pos++];
                if (c == '"')
                    break;
                if (c == '\\')
                {
                    if (pos >= json.Length)
                        throw new FormatException("이스케이프 시퀀스가 끝나지 않았다.");
                    char esc = json[pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (pos + 4 > json.Length)
                                throw new FormatException("\\u 이스케이프 뒤에 4자리가 부족하다.");
                            string hex = json.Substring(pos, 4);
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            pos += 4;
                            break;
                        default:
                            throw new FormatException($"알 수 없는 이스케이프 시퀀스: \\{esc}");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                pos++;
        }

        private static char Peek(string json, int pos)
        {
            if (pos >= json.Length)
                throw new FormatException("JSON이 예상보다 일찍 끝났다.");
            return json[pos];
        }

        private static void Expect(string json, ref int pos, char expected)
        {
            char actual = Peek(json, pos);
            if (actual != expected)
                throw new FormatException($"'{expected}'를 기대했지만 '{actual}'를 만났다(offset {pos}).");
            pos++;
        }
    }
}
