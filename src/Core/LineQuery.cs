using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NppMultiSearch.Core
{
    /// <summary>Como participa un termino en la coincidencia de una linea.</summary>
    public enum TermMode
    {
        /// <summary>La linea DEBE contener el termino (AND).</summary>
        Must,
        /// <summary>La linea NO debe contener el termino (NOT).</summary>
        MustNot,
        /// <summary>La linea debe contener AL MENOS UNO de los terminos Any (OR).</summary>
        Any
    }

    public sealed class SearchTerm
    {
        public string Text { get; }
        public TermMode Mode { get; }

        public SearchTerm(string text, TermMode mode)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Mode = mode;
        }
    }

    public sealed class QueryOptions
    {
        public bool MatchCase { get; set; }
        public bool WholeWord { get; set; }
        public bool UseRegex { get; set; }
    }

    /// <summary>
    /// Consulta de varios terminos evaluada sobre UNA sola linea, sin importar el orden.
    /// Equivale a la regex ^(?=.*A)(?=.*B)(?!.*C).*$ pero sin tener que escribirla.
    /// </summary>
    public sealed class LineQuery
    {
        private readonly List<Matcher> _must = new List<Matcher>();
        private readonly List<Matcher> _mustNot = new List<Matcher>();
        private readonly List<Matcher> _any = new List<Matcher>();

        public LineQuery(IEnumerable<SearchTerm> terms, QueryOptions options)
        {
            if (terms == null) throw new ArgumentNullException(nameof(terms));
            options = options ?? new QueryOptions();

            foreach (var t in terms)
            {
                if (string.IsNullOrEmpty(t.Text)) continue; // los campos vacios se ignoran
                var m = new Matcher(t.Text, options);
                switch (t.Mode)
                {
                    case TermMode.Must: _must.Add(m); break;
                    case TermMode.MustNot: _mustNot.Add(m); break;
                    default: _any.Add(m); break;
                }
            }
        }

        /// <summary>Una consulta sin terminos utiles no coincide con nada (evita volcar todo el log).</summary>
        public bool IsEmpty => _must.Count == 0 && _any.Count == 0;

        public bool IsMatch(string line)
        {
            if (IsEmpty) return false;

            foreach (var m in _must)
                if (!m.IsMatch(line)) return false;

            foreach (var m in _mustNot)
                if (m.IsMatch(line)) return false;

            if (_any.Count > 0)
            {
                bool anyHit = false;
                foreach (var m in _any)
                {
                    if (m.IsMatch(line)) { anyHit = true; break; }
                }
                if (!anyHit) return false;
            }
            return true;
        }

        private sealed class Matcher
        {
            private readonly Regex _regex;
            private readonly string _literal;
            private readonly StringComparison _comparison;

            public Matcher(string text, QueryOptions o)
            {
                _comparison = o.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                // Camino rapido: texto literal sin palabra completa -> IndexOf, sin regex.
                if (!o.UseRegex && !o.WholeWord)
                {
                    _literal = text;
                    return;
                }

                string pattern = o.UseRegex ? text : Regex.Escape(text);
                if (o.WholeWord) pattern = @"(?<!\w)(?:" + pattern + @")(?!\w)";

                var opts = RegexOptions.CultureInvariant | RegexOptions.Compiled;
                if (!o.MatchCase) opts |= RegexOptions.IgnoreCase;
                _regex = new Regex(pattern, opts, TimeSpan.FromSeconds(2));
            }

            public bool IsMatch(string line)
            {
                if (_literal != null) return line.IndexOf(_literal, _comparison) >= 0;
                try { return _regex.IsMatch(line); }
                catch (RegexMatchTimeoutException) { return false; }
            }
        }
    }
}
