using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InFalsus.Gameplay.Falsum.Core
{
    public sealed class FalsumJsonObject : Dictionary<string, object>
    {
        public FalsumJsonObject()
            : base(StringComparer.Ordinal)
        {
        }
    }

    public static class FalsumJson
    {
        public static object Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            var parser = new Parser(text);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                throw parser.Error("Unexpected trailing JSON content.");
            }
            return value;
        }

        public static string Serialize(object value, bool pretty)
        {
            var builder = new StringBuilder(1024);
            WriteValue(builder, value, pretty, 0);
            if (pretty)
            {
                builder.Append('\n');
            }
            return builder.ToString();
        }

        public static FalsumJsonObject AsObject(object value)
        {
            var result = value as FalsumJsonObject;
            if (result == null)
            {
                throw new FormatException("Expected a JSON object.");
            }
            return result;
        }

        public static List<object> AsArray(object value)
        {
            var result = value as List<object>;
            if (result == null)
            {
                throw new FormatException("Expected a JSON array.");
            }
            return result;
        }

        public static string GetString(
            FalsumJsonObject source,
            string key,
            string fallback)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }
            var text = value as string;
            if (text == null)
            {
                throw new FormatException(
                    "JSON field '" + key + "' must be a string.");
            }
            return text;
        }

        public static int GetInt(
            FalsumJsonObject source,
            string key,
            int fallback)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }
            return ConvertToInt32(value, key);
        }

        public static double GetNumber(
            FalsumJsonObject source,
            string key,
            double fallback)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }
            if (value is double)
            {
                var number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    throw new FormatException(
                        "JSON field '" + key +
                        "' must be a finite number.");
                }
                return number;
            }
            if (value is int)
            {
                return (int)value;
            }
            if (value is long)
            {
                return (long)value;
            }
            throw new FormatException(
                "JSON field '" + key + "' must be a number.");
        }

        public static bool GetBool(
            FalsumJsonObject source,
            string key,
            bool fallback)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }
            if (!(value is bool))
            {
                throw new FormatException(
                    "JSON field '" + key + "' must be a boolean.");
            }
            return (bool)value;
        }

        public static FalsumJsonObject GetObject(
            FalsumJsonObject source,
            string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }
            return AsObject(value);
        }

        public static List<object> GetArray(
            FalsumJsonObject source,
            string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }
            return AsArray(value);
        }

        public static void Set(
            FalsumJsonObject source,
            string key,
            object value)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            source[key] = value;
        }

        private static int ConvertToInt32(
            object value,
            string key)
        {
            if (value is int)
            {
                return (int)value;
            }
            if (value is long)
            {
                var longValue = (long)value;
                if (longValue < int.MinValue ||
                    longValue > int.MaxValue)
                {
                    throw new FormatException(
                        "JSON field '" + key +
                        "' is outside the Int32 range.");
                }
                return (int)longValue;
            }
            if (!(value is double))
            {
                throw new FormatException(
                    "JSON field '" + key + "' must be an integer.");
            }
            var number = (double)value;
            if (double.IsNaN(number) ||
                double.IsInfinity(number) ||
                number < int.MinValue ||
                number > int.MaxValue ||
                Math.Truncate(number) != number)
            {
                throw new FormatException(
                    "JSON field '" + key +
                    "' must be a finite Int32 integer.");
            }
            return (int)number;
        }

        public static object DeepClone(object value)
        {
            if (value == null)
            {
                return null;
            }
            var obj = value as FalsumJsonObject;
            if (obj != null)
            {
                var clone = new FalsumJsonObject();
                foreach (var pair in obj)
                {
                    clone[pair.Key] = DeepClone(pair.Value);
                }
                return clone;
            }
            var array = value as List<object>;
            if (array != null)
            {
                var clone = new List<object>(array.Count);
                for (var i = 0; i < array.Count; i++)
                {
                    clone.Add(DeepClone(array[i]));
                }
                return clone;
            }
            return value;
        }

        private static void WriteValue(
            StringBuilder builder,
            object value,
            bool pretty,
            int depth)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            var obj = value as FalsumJsonObject;
            if (obj != null)
            {
                WriteObject(builder, obj, pretty, depth);
                return;
            }
            var array = value as IList;
            if (array != null && !(value is string))
            {
                WriteArray(builder, array, pretty, depth);
                return;
            }
            if (value is string)
            {
                WriteString(builder, (string)value);
                return;
            }
            if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
                return;
            }
            if (IsNumber(value))
            {
                WriteNumber(builder, Convert.ToDouble(
                    value,
                    CultureInfo.InvariantCulture));
                return;
            }

            WriteString(
                builder,
                Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteObject(
            StringBuilder builder,
            FalsumJsonObject obj,
            bool pretty,
            int depth)
        {
            builder.Append('{');
            var first = true;
            foreach (var pair in obj)
            {
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                if (pretty)
                {
                    builder.Append('\n');
                    AppendIndent(builder, depth + 1);
                }
                WriteString(builder, pair.Key);
                builder.Append(pretty ? ": " : ":");
                WriteValue(builder, pair.Value, pretty, depth + 1);
            }
            if (!first && pretty)
            {
                builder.Append('\n');
                AppendIndent(builder, depth);
            }
            builder.Append('}');
        }

        private static void WriteArray(
            StringBuilder builder,
            IList array,
            bool pretty,
            int depth)
        {
            builder.Append('[');
            for (var i = 0; i < array.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                if (pretty)
                {
                    builder.Append('\n');
                    AppendIndent(builder, depth + 1);
                }
                WriteValue(builder, array[i], pretty, depth + 1);
            }
            if (array.Count > 0 && pretty)
            {
                builder.Append('\n');
                AppendIndent(builder, depth);
            }
            builder.Append(']');
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString(
                                "x4",
                                CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private static void WriteNumber(StringBuilder builder, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                builder.Append("null");
                return;
            }
            if (Math.Abs(value) < 0.0000000001)
            {
                builder.Append('0');
                return;
            }
            if (value == Math.Truncate(value) &&
                Math.Abs(value) <= 9007199254740991.0)
            {
                builder.Append(value.ToString("0", CultureInfo.InvariantCulture));
                return;
            }
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static bool IsNumber(object value)
        {
            return value is byte || value is sbyte ||
                   value is short || value is ushort ||
                   value is int || value is uint ||
                   value is long || value is ulong ||
                   value is float || value is double ||
                   value is decimal;
        }

        private static void AppendIndent(StringBuilder builder, int depth)
        {
            builder.Append(' ', depth * 2);
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;

            public Parser(string text)
            {
                _text = text;
            }

            public bool AtEnd
            {
                get { return _index >= _text.Length; }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    throw Error("Unexpected end of JSON.");
                }

                var c = Peek();
                if (c == '{')
                {
                    return ParseObject();
                }
                if (c == '[')
                {
                    return ParseArray();
                }
                if (c == '"')
                {
                    return ParseString();
                }
                if (c == 't')
                {
                    ConsumeLiteral("true");
                    return true;
                }
                if (c == 'f')
                {
                    ConsumeLiteral("false");
                    return false;
                }
                if (c == 'n')
                {
                    ConsumeLiteral("null");
                    return null;
                }
                return ParseNumber();
            }

            public void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    var c = _text[_index];
                    if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                    {
                        break;
                    }
                    _index++;
                }
            }

            public FormatException Error(string message)
            {
                return new FormatException(
                    message + " At character " +
                    _index.ToString(CultureInfo.InvariantCulture) + ".");
            }

            private FalsumJsonObject ParseObject()
            {
                Expect('{');
                var result = new FalsumJsonObject();
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                    {
                        throw Error("Expected an object key string.");
                    }
                    var key = ParseString();
                    if (result.ContainsKey(key))
                    {
                        throw Error(
                            "Duplicate JSON object key: " + key + ".");
                    }
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }
                    Expect(',');
                }
            }

            private List<object> ParseArray()
            {
                Expect('[');
                var result = new List<object>();
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }
                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (!AtEnd)
                {
                    var c = _text[_index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (AtEnd)
                    {
                        throw Error("Unterminated JSON string escape.");
                    }

                    var escaped = _text[_index++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escaped);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            if (_index + 4 > _text.Length)
                            {
                                throw Error("Incomplete unicode escape.");
                            }
                            var hex = _text.Substring(_index, 4);
                            ushort code;
                            if (!ushort.TryParse(
                                    hex,
                                    NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture,
                                    out code))
                            {
                                throw Error("Invalid unicode escape.");
                            }
                            builder.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            throw Error("Unknown JSON escape.");
                    }
                }
                throw Error("Unterminated JSON string.");
            }

            private double ParseNumber()
            {
                var start = _index;
                if (Peek() == '-')
                {
                    _index++;
                }
                while (!AtEnd && char.IsDigit(Peek()))
                {
                    _index++;
                }
                if (!AtEnd && Peek() == '.')
                {
                    _index++;
                    while (!AtEnd && char.IsDigit(Peek()))
                    {
                        _index++;
                    }
                }
                if (!AtEnd && (Peek() == 'e' || Peek() == 'E'))
                {
                    _index++;
                    if (!AtEnd && (Peek() == '+' || Peek() == '-'))
                    {
                        _index++;
                    }
                    while (!AtEnd && char.IsDigit(Peek()))
                    {
                        _index++;
                    }
                }
                if (_index == start)
                {
                    throw Error("Expected a JSON value.");
                }

                var token = _text.Substring(start, _index - start);
                double result;
                if (!double.TryParse(
                        token,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out result))
                {
                    throw Error("Invalid JSON number.");
                }
                if (double.IsNaN(result) || double.IsInfinity(result))
                {
                    throw Error("JSON number must be finite.");
                }
                return result;
            }

            private void ConsumeLiteral(string literal)
            {
                if (_index + literal.Length > _text.Length ||
                    string.CompareOrdinal(
                        _text,
                        _index,
                        literal,
                        0,
                        literal.Length) != 0)
                {
                    throw Error("Expected JSON literal " + literal + ".");
                }
                _index += literal.Length;
            }

            private void Expect(char expected)
            {
                SkipWhitespace();
                if (AtEnd || _text[_index] != expected)
                {
                    throw Error("Expected '" + expected + "'.");
                }
                _index++;
            }

            private bool TryConsume(char expected)
            {
                SkipWhitespace();
                if (AtEnd || _text[_index] != expected)
                {
                    return false;
                }
                _index++;
                return true;
            }

            private char Peek()
            {
                return AtEnd ? '\0' : _text[_index];
            }
        }
    }
}
