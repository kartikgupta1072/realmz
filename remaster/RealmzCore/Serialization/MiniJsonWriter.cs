using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RealmzCore.Serialization
{
    /// <summary>
    /// Dependency-free JSON serializer matching MiniJson types for netstandard2.1.
    /// </summary>
    public static class MiniJsonWriter
    {
        public static string Serialize(object? value, bool indented = false)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, indented, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object? value, bool indented, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is string s)
            {
                WriteString(sb, s);
                return;
            }

            if (value is char c)
            {
                WriteString(sb, c.ToString());
                return;
            }

            if (value is sbyte || value is byte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            if (value is float f)
            {
                if (float.IsNaN(f) || float.IsInfinity(f))
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                }
                return;
            }

            if (value is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                }
                return;
            }

            if (value is decimal dec)
            {
                sb.Append(dec.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is IDictionary dict)
            {
                WriteDictionary(sb, dict, indented, depth);
                return;
            }

            if (value is IReadOnlyDictionary<string, object?> roDict)
            {
                WriteReadOnlyDictionary(sb, roDict, indented, depth);
                return;
            }

            if (value is IEnumerable enumerable)
            {
                WriteArray(sb, enumerable, indented, depth);
                return;
            }

            WriteString(sb, value.ToString() ?? string.Empty);
        }

        private static void WriteDictionary(StringBuilder sb, IDictionary dict, bool indented, int depth)
        {
            if (dict.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                if (indented)
                {
                    sb.Append('\n');
                    AppendIndent(sb, depth + 1);
                }

                WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty);
                sb.Append(indented ? ": " : ":");
                WriteValue(sb, entry.Value, indented, depth + 1);
            }

            if (indented)
            {
                sb.Append('\n');
                AppendIndent(sb, depth);
            }

            sb.Append('}');
        }

        private static void WriteReadOnlyDictionary(
            StringBuilder sb,
            IReadOnlyDictionary<string, object?> dict,
            bool indented,
            int depth)
        {
            if (dict.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object?> entry in dict)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                if (indented)
                {
                    sb.Append('\n');
                    AppendIndent(sb, depth + 1);
                }

                WriteString(sb, entry.Key);
                sb.Append(indented ? ": " : ":");
                WriteValue(sb, entry.Value, indented, depth + 1);
            }

            if (indented)
            {
                sb.Append('\n');
                AppendIndent(sb, depth);
            }

            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable enumerable, bool indented, int depth)
        {
            List<object?> items = new List<object?>();
            foreach (object? item in enumerable)
            {
                items.Add(item);
            }

            if (items.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                if (indented)
                {
                    sb.Append('\n');
                    AppendIndent(sb, depth + 1);
                }

                WriteValue(sb, items[i], indented, depth + 1);
            }

            if (indented)
            {
                sb.Append('\n');
                AppendIndent(sb, depth);
            }

            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string text)
        {
            sb.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            sb.Append('"');
        }

        private static void AppendIndent(StringBuilder sb, int depth)
        {
            for (int i = 0; i < depth; i++)
            {
                sb.Append("  ");
            }
        }
    }
}
