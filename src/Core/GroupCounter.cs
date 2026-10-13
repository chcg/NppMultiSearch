using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NppMultiSearch.Core
{
    public sealed class GroupRow
    {
        public string Key { get; internal set; }
        public int Count { get; internal set; }
        public string FirstFile { get; internal set; }
        public int FirstLine { get; internal set; }
        public string LastFile { get; internal set; }
        public int LastLine { get; internal set; }
    }

    /// <summary>
    /// Cuenta cuantas veces aparece cada valor extraido de las lineas encontradas.
    /// Ej.: regex "MOVIL[=: ]*(\w+)" -> "MOVIL 12 = 47 veces, MOVIL 8 = 3 veces".
    /// Usa el grupo 1 si la regex lo tiene; si no, la coincidencia completa.
    /// </summary>
    public sealed class GroupCounter
    {
        private readonly Regex _regex;
        private readonly Dictionary<string, GroupRow> _rows = new Dictionary<string, GroupRow>(StringComparer.Ordinal);

        /// <summary>Lanza ArgumentException si la regex no es valida.</summary>
        public GroupCounter(string pattern, bool matchCase)
        {
            var opts = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (!matchCase) opts |= RegexOptions.IgnoreCase;
            _regex = new Regex(pattern, opts, TimeSpan.FromSeconds(2));
        }

        public int LinesWithoutValue { get; private set; }

        public void Add(SearchHit hit)
        {
            Match m;
            try { m = _regex.Match(hit.Text); }
            catch (RegexMatchTimeoutException) { LinesWithoutValue++; return; }

            if (!m.Success) { LinesWithoutValue++; return; }

            string key = (m.Groups.Count > 1 && m.Groups[1].Success) ? m.Groups[1].Value : m.Value;

            if (!_rows.TryGetValue(key, out var row))
            {
                row = new GroupRow { Key = key, FirstFile = hit.FilePath, FirstLine = hit.LineNumber };
                _rows.Add(key, row);
            }
            row.Count++;
            row.LastFile = hit.FilePath;
            row.LastLine = hit.LineNumber;
        }

        public IReadOnlyList<GroupRow> Rows()
        {
            return _rows.Values.OrderByDescending(r => r.Count).ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
