using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RealmzCore.Serialization
{
    /// <summary>
    /// Dependency-free recursive-descent JSON parser for loading extracted scenario JSON
    /// inside Unity netstandard2.1 without external NuGet packages.
    /// </summary>
    public static class MiniJson
    {
        private const int MaxDepth = 256;

        public static object? Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            Parser parser = new Parser(json);
            object? result = parser.ParseValue(0);
            parser.SkipWhitespace();
            if (!parser.IsAtEnd)
            {
                throw parser.Error("Unexpected trailing characters after JSON root value.");
            }

            return result;
        }

        public static IReadOnlyDictionary<string, object?> AsObject(object? value)
        {
            if (value is IReadOnlyDictionary<string, object?> roDict)
            {
                return roDict;
            }

            if (value is Dictionary<string, object?> dict)
            {
                return dict;
            }

            throw new FormatException("Expected JSON object.");
        }

        public static IReadOnlyList<object?> AsArray(object? value)
        {
            if (value is IReadOnlyList<object?> list)
            {
                return list;
            }

            throw new FormatException("Expected JSON array.");
        }

        public static bool TryGetValue(IReadOnlyDictionary<string, object?> obj, string key, out object? value)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            return obj.TryGetValue(key, out value);
        }

        public static string GetString(IReadOnlyDictionary<string, object?> obj, string key, string defaultValue = "")
        {
            if (TryGetValue(obj, key, out object? raw) && raw != null)
            {
                if (raw is string s)
                {
                    return s;
                }

                throw new FormatException($"JSON field '{key}' is not a string.");
            }

            return defaultValue;
        }

        public static int GetInt(IReadOnlyDictionary<string, object?> obj, string key, int defaultValue = 0)
        {
            if (TryGetValue(obj, key, out object? raw) && raw != null)
            {
                return ToInt32(raw, key);
            }

            return defaultValue;
        }

        public static long GetLong(IReadOnlyDictionary<string, object?> obj, string key, long defaultValue = 0)
        {
            if (TryGetValue(obj, key, out object? raw) && raw != null)
            {
                return ToInt64(raw, key);
            }

            return defaultValue;
        }

        public static double GetDouble(IReadOnlyDictionary<string, object?> obj, string key, double defaultValue = 0.0)
        {
            if (TryGetValue(obj, key, out object? raw) && raw != null)
            {
                return ToDouble(raw, key);
            }

            return defaultValue;
        }

        public static bool GetBool(IReadOnlyDictionary<string, object?> obj, string key, bool defaultValue = false)
        {
            if (TryGetValue(obj, key, out object? raw) && raw != null)
            {
                if (raw is bool b)
                {
                    return b;
                }

                if (raw is long l)
                {
                    return l != 0;
                }

                throw new FormatException($"JSON field '{key}' is not a boolean.");
            }

            return defaultValue;
        }

        public static int ToInt32(object? value, string context = "value")
        {
            if (value is long l)
            {
                return checked((int)l);
            }

            if (value is double d)
            {
                return checked((int)d);
            }

            if (value is int i)
            {
                return i;
            }

            throw new FormatException($"Expected integer for {context}.");
        }

        public static long ToInt64(object? value, string context = "value")
        {
            if (value is long l)
            {
                return l;
            }

            if (value is double d)
            {
                return checked((long)d);
            }

            if (value is int i)
            {
                return i;
            }

            throw new FormatException($"Expected integer for {context}.");
        }

        public static double ToDouble(object? value, string context = "value")
        {
            if (value is double d)
            {
                return d;
            }

            if (value is long l)
            {
                return l;
            }

            if (value is int i)
            {
                return i;
            }

            throw new FormatException($"Expected number for {context}.");
        }

        private sealed class Parser
        {
            private readonly string _json;
            private int _pos;

            public Parser(string json)
            {
                _json = json;
                _pos = 0;
            }

            public bool IsAtEnd => _pos >= _json.Length;

            public void SkipWhitespace()
            {
                while (_pos < _json.Length)
                {
                    char c = _json[_pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public object? ParseValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Error($"JSON nesting depth exceeded {MaxDepth}.");
                }

                SkipWhitespace();
                if (IsAtEnd)
                {
                    throw Error("Unexpected end of JSON input.");
                }

                char c = _json[_pos];
                switch (c)
                {
                    case '{':
                        return ParseObject(depth + 1);
                    case '[':
                        return ParseArray(depth + 1);
                    case '"':
                        return ParseString();
                    case 't':
                        ConsumeLiteral("true");
                        return true;
                    case 'f':
                        ConsumeLiteral("false");
                        return false;
                    case 'n':
                        ConsumeLiteral("null");
                        return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ParseNumber();
                        }

                        throw Error($"Unexpected character '{c}'.");
                }
            }

            private Dictionary<string, object?> ParseObject(int depth)
            {
                Expect('{');
                Dictionary<string, object?> obj = new Dictionary<string, object?>(StringComparer.Ordinal);
                SkipWhitespace();

                if (!IsAtEnd && _json[_pos] == '}')
                {
                    _pos++;
                    return obj;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (IsAtEnd || _json[_pos] != '"')
                    {
                        throw Error("Expected string key in JSON object.");
                    }

                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    object? value = ParseValue(depth);
                    obj[key] = value;

                    SkipWhitespace();
                    if (IsAtEnd)
                    {
                        throw Error("Unterminated JSON object.");
                    }

                    char next = _json[_pos++];
                    if (next == '}')
                    {
                        break;
                    }

                    if (next != ',')
                    {
                        throw Error($"Expected ',' or '}}' in JSON object, found '{next}'.");
                    }
                }

                return obj;
            }

            private List<object?> ParseArray(int depth)
            {
                Expect('[');
                List<object?> list = new List<object?>();
                SkipWhitespace();

                if (!IsAtEnd && _json[_pos] == ']')
                {
                    _pos++;
                    return list;
                }

                while (true)
                {
                    object? value = ParseValue(depth);
                    list.Add(value);

                    SkipWhitespace();
                    if (IsAtEnd)
                    {
                        throw Error("Unterminated JSON array.");
                    }

                    char next = _json[_pos++];
                    if (next == ']')
                    {
                        break;
                    }

                    if (next != ',')
                    {
                        throw Error($"Expected ',' or ']' in JSON array, found '{next}'.");
                    }
                }

                return list;
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder sb = new StringBuilder();

                while (_pos < _json.Length)
                {
                    char c = _json[_pos++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c < 0x20)
                    {
                        throw Error("Unescaped control character in JSON string.");
                    }

                    if (c == '\\')
                    {
                        if (IsAtEnd)
                        {
                            throw Error("Unterminated escape sequence in JSON string.");
                        }

                        char esc = _json[_pos++];
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                sb.Append(ParseUnicodeEscape());
                                break;
                            default:
                                throw Error($"Invalid escape sequence '\\{esc}' in JSON string.");
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                throw Error("Unterminated JSON string.");
            }

            private char ParseUnicodeEscape()
            {
                if (_pos + 4 > _json.Length)
                {
                    throw Error("Incomplete unicode escape sequence.");
                }

                int code = 0;
                for (int i = 0; i < 4; i++)
                {
                    char hex = _json[_pos++];
                    int digit;
                    if (hex >= '0' && hex <= '9')
                    {
                        digit = hex - '0';
                    }
                    else if (hex >= 'a' && hex <= 'f')
                    {
                        digit = 10 + (hex - 'a');
                    }
                    else if (hex >= 'A' && hex <= 'F')
                    {
                        digit = 10 + (hex - 'A');
                    }
                    else
                    {
                        throw Error($"Invalid hex digit '{hex}' in unicode escape.");
                    }

                    code = (code << 4) | digit;
                }

                return (char)code;
            }

            private object ParseNumber()
            {
                int start = _pos;
                bool isFloat = false;

                if (_json[_pos] == '-')
                {
                    _pos++;
                }

                if (IsAtEnd || !char.IsDigit(_json[_pos]))
                {
                    throw Error("Invalid number format.");
                }

                if (_json[_pos] == '0')
                {
                    _pos++;
                    if (!IsAtEnd && char.IsDigit(_json[_pos]))
                    {
                        throw Error("Leading zeroes are not allowed in JSON numbers.");
                    }
                }
                else
                {
                    while (!IsAtEnd && char.IsDigit(_json[_pos]))
                    {
                        _pos++;
                    }
                }

                if (!IsAtEnd && _json[_pos] == '.')
                {
                    isFloat = true;
                    _pos++;
                    if (IsAtEnd || !char.IsDigit(_json[_pos]))
                    {
                        throw Error("Expected digit after decimal point.");
                    }

                    while (!IsAtEnd && char.IsDigit(_json[_pos]))
                    {
                        _pos++;
                    }
                }

                if (!IsAtEnd && (_json[_pos] == 'e' || _json[_pos] == 'E'))
                {
                    isFloat = true;
                    _pos++;
                    if (!IsAtEnd && (_json[_pos] == '+' || _json[_pos] == '-'))
                    {
                        _pos++;
                    }

                    if (IsAtEnd || !char.IsDigit(_json[_pos]))
                    {
                        throw Error("Expected digit in exponent.");
                    }

                    while (!IsAtEnd && char.IsDigit(_json[_pos]))
                    {
                        _pos++;
                    }
                }

                string token = _json.Substring(start, _pos - start);
                if (!isFloat && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long intVal))
                {
                    return intVal;
                }

                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                {
                    return dblVal;
                }

                throw Error($"Unable to parse number '{token}'.");
            }

            private void ConsumeLiteral(string literal)
            {
                if (_pos + literal.Length > _json.Length ||
                    string.CompareOrdinal(_json, _pos, literal, 0, literal.Length) != 0)
                {
                    throw Error($"Expected '{literal}'.");
                }

                _pos += literal.Length;
            }

            private void Expect(char expected)
            {
                if (IsAtEnd || _json[_pos] != expected)
                {
                    throw Error($"Expected '{expected}'.");
                }

                _pos++;
            }

            public FormatException Error(string message)
            {
                return new FormatException($"{message} (offset {_pos})");
            }
        }
    }
}
