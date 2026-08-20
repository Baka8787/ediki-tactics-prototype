using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ediki.Core.Data
{
    public sealed class DataFormatException : Exception
    {
        public DataFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// One parsed line: a leading keyword plus key=value pairs.
    ///   unit id=momotaro name=Momotaro hp=300
    /// Blank lines and lines starting with '#' are comments.
    ///
    /// Chosen over JSON/ScriptableObject (OD-11) because Core must stay free of
    /// UnityEngine, tests need to parse inline strings, and Newtonsoft is not
    /// installed. Small enough that the whole reader is this one file.
    /// </summary>
    public sealed class DataLine
    {
        public readonly string Keyword;
        public readonly int LineNumber;
        private readonly Dictionary<string, string> _values;

        private DataLine(string keyword, int lineNumber, Dictionary<string, string> values)
        {
            Keyword = keyword;
            LineNumber = lineNumber;
            _values = values;
        }

        public bool Has(string key) => _values.ContainsKey(key);

        public string GetString(string key)
        {
            string v;
            if (!_values.TryGetValue(key, out v))
                throw new DataFormatException("Line " + LineNumber + ": '" + Keyword + "' is missing required key '" + key + "'.");
            return v;
        }

        public string GetString(string key, string fallback)
        {
            string v;
            return _values.TryGetValue(key, out v) ? v : fallback;
        }

        public int GetInt(string key)
        {
            string raw = GetString(key);
            int v;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                throw new DataFormatException("Line " + LineNumber + ": '" + key + "' expects an integer, got '" + raw + "'.");
            return v;
        }

        public int GetInt(string key, int fallback)
        {
            if (!Has(key)) return fallback;
            return GetInt(key);
        }

        public bool GetBool(string key, bool fallback)
        {
            if (!Has(key)) return fallback;
            string raw = GetString(key);
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new DataFormatException("Line " + LineNumber + ": '" + key + "' expects true/false, got '" + raw + "'.");
        }

        public char GetChar(string key)
        {
            string raw = GetString(key);
            if (raw.Length != 1)
                throw new DataFormatException("Line " + LineNumber + ": '" + key + "' expects a single character, got '" + raw + "'.");
            return raw[0];
        }

        /// <summary>
        /// Splits text into DataLines. Raw (non key=value) lines are returned via
        /// <paramref name="rawHandler"/> when the keyword matches a block marker —
        /// this is how the ASCII map body gets through.
        /// </summary>
        public static List<DataLine> ParseAll(string text, string blockStart, string blockEnd, List<string> blockLines)
        {
            List<DataLine> result = new List<DataLine>();
            if (text == null) return result;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool inBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int lineNumber = i + 1;

                if (inBlock)
                {
                    if (blockEnd != null && line.Trim() == blockEnd) { inBlock = false; continue; }
                    if (blockLines != null) blockLines.Add(line.TrimEnd());
                    continue;
                }

                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                if (blockStart != null && trimmed == blockStart) { inBlock = true; continue; }

                result.Add(ParseLine(trimmed, lineNumber));
            }

            if (inBlock && blockEnd != null)
                throw new DataFormatException("Block '" + blockStart + "' was never closed with '" + blockEnd + "'.");

            return result;
        }

        public static List<DataLine> ParseAll(string text) => ParseAll(text, null, null, null);

        private static DataLine ParseLine(string trimmed, int lineNumber)
        {
            string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string keyword = parts[0];
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0)
                    throw new DataFormatException("Line " + lineNumber + ": expected key=value, got '" + parts[i] + "'.");

                string key = parts[i].Substring(0, eq);
                string value = parts[i].Substring(eq + 1);
                if (values.ContainsKey(key))
                    throw new DataFormatException("Line " + lineNumber + ": duplicate key '" + key + "'.");
                values.Add(key, value);
            }

            return new DataLine(keyword, lineNumber, values);
        }
    }
}
