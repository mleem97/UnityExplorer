using System.Globalization;
using System.Text;

namespace UnityExplorer.MCP
{
    /// <summary>
    /// Minimal JSON helper for net35 compatibility. No external dependencies.
    /// </summary>
    internal static class JsonHelper
    {
        // ── Building JSON ───────────────────────────────────────────────

        internal class JsonBuilder
        {
            private readonly StringBuilder sb = new();
            private readonly Stack<bool> hasItemStack = new();
            private bool hasItem;

            public JsonBuilder StartObject()
            {
                Sep();
                sb.Append('{');
                hasItemStack.Push(hasItem);
                hasItem = false;
                return this;
            }

            public JsonBuilder EndObject()
            {
                sb.Append('}');
                hasItem = hasItemStack.Count > 0 ? hasItemStack.Pop() : false;
                hasItem = true; // the closed object/array counts as an item in the parent
                return this;
            }

            public JsonBuilder StartArray()
            {
                Sep();
                sb.Append('[');
                hasItemStack.Push(hasItem);
                hasItem = false;
                return this;
            }

            public JsonBuilder EndArray()
            {
                sb.Append(']');
                hasItem = hasItemStack.Count > 0 ? hasItemStack.Pop() : false;
                hasItem = true; // the closed object/array counts as an item in the parent
                return this;
            }

            private void Sep()
            {
                if (hasItem) sb.Append(',');
                hasItem = true;
            }

            public JsonBuilder Key(string key)
            {
                Sep();
                sb.Append('"');
                AppendEscaped(sb, key);
                sb.Append("\":");
                hasItem = false;
                return this;
            }

            public JsonBuilder Value(string val)
            {
                Sep();
                if (val == null) { sb.Append("null"); return this; }
                sb.Append('"');
                AppendEscaped(sb, val);
                sb.Append('"');
                return this;
            }

            public JsonBuilder Value(int val) { Sep(); sb.Append(val); return this; }
            public JsonBuilder Value(long val) { Sep(); sb.Append(val); return this; }
            public JsonBuilder Value(float val) { Sep(); sb.Append(val.ToString(CultureInfo.InvariantCulture)); return this; }
            public JsonBuilder Value(double val) { Sep(); sb.Append(val.ToString(CultureInfo.InvariantCulture)); return this; }
            public JsonBuilder Value(bool val) { Sep(); sb.Append(val ? "true" : "false"); return this; }
            public JsonBuilder Null() { Sep(); sb.Append("null"); return this; }

            public JsonBuilder Raw(string json)
            {
                Sep();
                sb.Append(json);
                return this;
            }

            public override string ToString() => sb.ToString();

            private static void AppendEscaped(StringBuilder sb, string s)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default: sb.Append(c); break;
                    }
                }
            }
        }

        // ── Parsing JSON ────────────────────────────────────────────────

        /// <summary>
        /// Parse a flat or nested JSON object into a dictionary.
        /// Values are string, double, bool, null, List&lt;object&gt;, or Dictionary&lt;string,object&gt;.
        /// </summary>
        internal static Dictionary<string, object> Parse(string json)
        {
            int index = 0;
            return ParseObject(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int i)
        {
            var dict = new Dictionary<string, object>();
            SkipWhitespace(json, ref i);
            Expect(json, ref i, '{');
            SkipWhitespace(json, ref i);

            if (i < json.Length && json[i] == '}') { i++; return dict; }

            while (true)
            {
                SkipWhitespace(json, ref i);
                string key = ParseString(json, ref i);
                SkipWhitespace(json, ref i);
                Expect(json, ref i, ':');
                SkipWhitespace(json, ref i);
                object val = ParseValue(json, ref i);
                dict[key] = val;
                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ',') { i++; continue; }
                break;
            }

            SkipWhitespace(json, ref i);
            Expect(json, ref i, '}');
            return dict;
        }

        private static List<object> ParseArray(string json, ref int i)
        {
            var list = new List<object>();
            Expect(json, ref i, '[');
            SkipWhitespace(json, ref i);
            if (i < json.Length && json[i] == ']') { i++; return list; }

            while (true)
            {
                SkipWhitespace(json, ref i);
                list.Add(ParseValue(json, ref i));
                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ',') { i++; continue; }
                break;
            }

            SkipWhitespace(json, ref i);
            Expect(json, ref i, ']');
            return list;
        }

        private static object ParseValue(string json, ref int i)
        {
            SkipWhitespace(json, ref i);
            if (i >= json.Length) return null;

            char c = json[i];
            if (c == '"') return ParseString(json, ref i);
            if (c == '{') return ParseObject(json, ref i);
            if (c == '[') return ParseArray(json, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }

            // Number
            int start = i;
            while (i < json.Length && "0123456789.eE+-".IndexOf(json[i]) >= 0) i++;
            string numStr = json.Substring(start, i - start);
            if (numStr.Contains(".") || numStr.Contains("e") || numStr.Contains("E"))
                return double.Parse(numStr, CultureInfo.InvariantCulture);
            return long.Parse(numStr, CultureInfo.InvariantCulture);
        }

        private static string ParseString(string json, ref int i)
        {
            Expect(json, ref i, '"');
            var sb = new StringBuilder();
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\')
                {
                    i++;
                    switch (json[i])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '/': sb.Append('/'); break;
                        default: sb.Append(json[i]); break;
                    }
                }
                else
                {
                    sb.Append(json[i]);
                }
                i++;
            }
            Expect(json, ref i, '"');
            return sb.ToString();
        }

        private static void SkipWhitespace(string json, ref int i)
        {
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        }

        private static void Expect(string json, ref int i, char expected)
        {
            if (i >= json.Length || json[i] != expected)
                throw new FormatException($"Expected '{expected}' at position {i}");
            i++;
        }

        // ── Convenience Accessors ───────────────────────────────────────

        internal static string GetString(Dictionary<string, object> dict, string key, string fallback = null)
        {
            if (dict.TryGetValue(key, out object val) && val is string s)
                return s;
            return fallback;
        }

        internal static int GetInt(Dictionary<string, object> dict, string key, int fallback = 0)
        {
            if (dict.TryGetValue(key, out object val))
            {
                if (val is long l) return (int)l;
                if (val is double d) return (int)d;
            }
            return fallback;
        }

        internal static bool GetBool(Dictionary<string, object> dict, string key, bool fallback = false)
        {
            if (dict.TryGetValue(key, out object val) && val is bool b)
                return b;
            return fallback;
        }

        internal static List<object> GetArray(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out object val) && val is List<object> list)
                return list;
            return null;
        }

        internal static Dictionary<string, object> GetObject(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out object val) && val is Dictionary<string, object> obj)
                return obj;
            return null;
        }
    }
}
