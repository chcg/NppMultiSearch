using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NppMultiSearch.Core;

namespace NppMultiSearch.Plugin
{
    /// <summary>Preferencias del usuario, en un archivo key=value dentro de la carpeta de config de plugins de Notepad++.</summary>
    internal sealed class Settings
    {
        public bool ScopeFolder;                 // false = documento actual, true = carpeta
        public string Folder = string.Empty;
        public string Filters = "*";
        public bool Recursive = true;
        public bool MatchCase;
        public bool WholeWord;
        public bool UseRegex;
        public string GroupRegex = string.Empty;
        public List<SearchTerm> Terms = new List<SearchTerm>();

        private const string FileName = "NppMultiSearch.ini";

        private static string FilePath()
        {
            string dir = Npp.PluginsConfigDir();
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FileName);
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                string path = FilePath();
                if (path != null && File.Exists(path))
                {
                    foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int eq = raw.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = raw.Substring(0, eq);
                        string val = raw.Substring(eq + 1);
                        switch (key)
                        {
                            case "scopeFolder": s.ScopeFolder = val == "1"; break;
                            case "folder": s.Folder = val; break;
                            case "filters": s.Filters = val; break;
                            case "recursive": s.Recursive = val != "0"; break;
                            case "matchCase": s.MatchCase = val == "1"; break;
                            case "wholeWord": s.WholeWord = val == "1"; break;
                            case "useRegex": s.UseRegex = val == "1"; break;
                            case "groupRegex": s.GroupRegex = val; break;
                            case "term":
                                int bar = val.IndexOf('|');
                                if (bar > 0 && Enum.TryParse(val.Substring(0, bar), out TermMode mode))
                                    s.Terms.Add(new SearchTerm(val.Substring(bar + 1), mode));
                                break;
                        }
                    }
                }
            }
            catch { /* preferencias corruptas: se usan los valores por defecto */ }

            if (s.Terms.Count == 0)
            {
                s.Terms.Add(new SearchTerm(string.Empty, TermMode.Must));
                s.Terms.Add(new SearchTerm(string.Empty, TermMode.Must));
                s.Terms.Add(new SearchTerm(string.Empty, TermMode.MustNot));
            }
            return s;
        }

        public void Save()
        {
            try
            {
                string path = FilePath();
                if (path == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var sb = new StringBuilder();
                sb.AppendLine("scopeFolder=" + (ScopeFolder ? "1" : "0"));
                sb.AppendLine("folder=" + Folder);
                sb.AppendLine("filters=" + Filters);
                sb.AppendLine("recursive=" + (Recursive ? "1" : "0"));
                sb.AppendLine("matchCase=" + (MatchCase ? "1" : "0"));
                sb.AppendLine("wholeWord=" + (WholeWord ? "1" : "0"));
                sb.AppendLine("useRegex=" + (UseRegex ? "1" : "0"));
                sb.AppendLine("groupRegex=" + GroupRegex);
                foreach (var t in Terms)
                    sb.AppendLine("term=" + t.Mode + "|" + t.Text);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { /* no poder guardar preferencias no debe molestar al usuario */ }
        }
    }
}
