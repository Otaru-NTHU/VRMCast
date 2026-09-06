using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VRMCast.Core.Vrm
{
    /// <summary>
    /// Minimal, dependency-free JSON reader used only to inspect the glTF header of a VRM file
    /// (extension keys and a few metadata strings). Objects become Dictionary&lt;string, object&gt;,
    /// arrays become List&lt;object&gt;, numbers become double, and null becomes null.
    /// It is deliberately small; it is not a general-purpose JSON library.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var reader = new Reader(json);
            reader.SkipWhitespace();
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw reader.Error("Unexpected trailing characters");
            return value;
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) { _s = s; }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string message) => new FormatException($"{message} at offset {_i}");

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    var c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _i++;
                    else break;
                }
            }

            public object ReadValue()
            {
                if (AtEnd) throw Error("Unexpected end of JSON");
                var c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error($"Unexpected character '{c}'");
                }
            }

            private void Expect(string literal)
            {
                if (string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                    throw Error($"Expected '{literal}'");
                _i += literal.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++; // {
                SkipWhitespace();
                if (Peek() == '}') { _i++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Error("Expected object key");
                    var key = ReadString();
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("Expected ':'");
                    _i++;
                    SkipWhitespace();
                    result[key] = ReadValue();
                    SkipWhitespace();
                    var c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == '}') { _i++; return result; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                var result = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (Peek() == ']') { _i++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    result.Add(ReadValue());
                    SkipWhitespace();
                    var c = Peek();
                    if (c == ',') { _i++; continue; }
                    if (c == ']') { _i++; return result; }
                    throw Error("Expected ',' or ']'");
                }
            }

            private char Peek()
            {
                if (AtEnd) throw Error("Unexpected end of JSON");
                return _s[_i];
            }

            private string ReadString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("Unterminated escape");
                    var e = _s[_i++];
                    switch (e)
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
                            if (_i + 4 > _s.Length) throw Error("Invalid unicode escape");
                            var code = int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw Error($"Invalid escape '\\{e}'");
                    }
                }
            }

            private double ReadNumber()
            {
                var start = _i;
                while (!AtEnd)
                {
                    var c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') _i++;
                    else break;
                }
                var text = _s.Substring(start, _i - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw Error($"Invalid number '{text}'");
                return value;
            }
        }
    }
}
