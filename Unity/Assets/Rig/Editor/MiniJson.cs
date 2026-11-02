using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SdfRig.Editor
{
    /// <summary>
    /// A tiny JSON reader (no Newtonsoft in this project). Objects are Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;,
    /// numbers double, plus string, bool and null.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            var p = new Parser(json);
            var value = p.Value();
            p.SkipWhite();
            if (!p.End)
                throw new FormatException($"JSON: unexpected text at {p.Pos}");
            return value;
        }

        public static object Get(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v : null;

        public static List<object> List(object o) => o as List<object>;

        public static float Float(object o, float fallback = 0) => o is double d ? (float)d : fallback;

        public static int Int(object o, int fallback = 0) => o is double d ? (int)d : fallback;

        public static string String(object o, string fallback = null) => o as string ?? fallback;

        public static bool Bool(object o, bool fallback = false) => o is bool b ? b : fallback;

        /// <summary>A JSON array of numbers as floats; null if it is not an array.</summary>
        public static float[] Floats(object o)
        {
            if (!(o is List<object> list))
                return null;
            var r = new float[list.Count];
            for (var i = 0; i < r.Length; i++)
                r[i] = Float(list[i]);
            return r;
        }

        private sealed class Parser
        {
            private readonly string s;
            public int Pos;

            public Parser(string text) => s = text;

            public bool End => Pos >= s.Length;

            public void SkipWhite()
            {
                while (Pos < s.Length && char.IsWhiteSpace(s[Pos]))
                    Pos++;
            }

            public object Value()
            {
                SkipWhite();
                if (End)
                    throw new FormatException("JSON: unexpected end");
                var c = s[Pos];
                switch (c)
                {
                    case '{': return ObjectValue();
                    case '[': return ArrayValue();
                    case '"': return StringValue();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return NumberValue();
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(s, Pos, word, 0, word.Length) != 0)
                    throw new FormatException($"JSON: expected {word} at {Pos}");
                Pos += word.Length;
            }

            private object ObjectValue()
            {
                var d = new Dictionary<string, object>();
                Pos++;
                SkipWhite();
                if (Peek() == '}') { Pos++; return d; }
                while (true)
                {
                    SkipWhite();
                    var key = StringValue();
                    SkipWhite();
                    if (Peek() != ':')
                        throw new FormatException($"JSON: expected : at {Pos}");
                    Pos++;
                    d[key] = Value();
                    SkipWhite();
                    var c = Peek();
                    Pos++;
                    if (c == '}') return d;
                    if (c != ',')
                        throw new FormatException($"JSON: expected , or }} at {Pos - 1}");
                }
            }

            private object ArrayValue()
            {
                var list = new List<object>();
                Pos++;
                SkipWhite();
                if (Peek() == ']') { Pos++; return list; }
                while (true)
                {
                    list.Add(Value());
                    SkipWhite();
                    var c = Peek();
                    Pos++;
                    if (c == ']') return list;
                    if (c != ',')
                        throw new FormatException($"JSON: expected , or ] at {Pos - 1}");
                }
            }

            private char Peek() => Pos < s.Length ? s[Pos] : '\0';

            private string StringValue()
            {
                if (Peek() != '"')
                    throw new FormatException($"JSON: expected string at {Pos}");
                Pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (End)
                        throw new FormatException("JSON: unterminated string");
                    var c = s[Pos++];
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    var e = s[Pos++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)int.Parse(s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            Pos += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            private object NumberValue()
            {
                var start = Pos;
                while (Pos < s.Length && "+-0123456789.eE".IndexOf(s[Pos]) >= 0)
                    Pos++;
                if (start == Pos)
                    throw new FormatException($"JSON: unexpected character '{s[Pos]}' at {Pos}");
                return double.Parse(s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
