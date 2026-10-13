using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NppMultiSearch.Core;

namespace NppMultiSearch.Cli
{
    internal static class Program
    {
        // Uso: SearchCli <carpeta> <filtros> [+debe] [-nodebe] [?alguno] [--case] [--word] [--regex] [--max N]
        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Uso: SearchCli <carpeta> <filtros> +debe -nodebe ?alguno [--case] [--word] [--regex] [--max N]");
                return 1;
            }

            var terms = new List<SearchTerm>();
            var opts = new QueryOptions();
            int max = 20;

            for (int i = 2; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--case") opts.MatchCase = true;
                else if (a == "--word") opts.WholeWord = true;
                else if (a == "--regex") opts.UseRegex = true;
                else if (a == "--max") max = int.Parse(args[++i]);
                else if (a.StartsWith("+")) terms.Add(new SearchTerm(a.Substring(1), TermMode.Must));
                else if (a.StartsWith("-")) terms.Add(new SearchTerm(a.Substring(1), TermMode.MustNot));
                else if (a.StartsWith("?")) terms.Add(new SearchTerm(a.Substring(1), TermMode.Any));
                else terms.Add(new SearchTerm(a, TermMode.Must));
            }

            var query = new LineQuery(terms, opts);
            var folder = new FolderSearchOptions { Folder = args[0], Filters = args[1] };

            var sw = Stopwatch.StartNew();
            int total = 0, shown = 0;
            var perFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var hit in FileSearcher.SearchFolder(folder, query))
            {
                total++;
                perFile[hit.FilePath] = perFile.TryGetValue(hit.FilePath, out int c) ? c + 1 : 1;
                if (shown < max)
                {
                    shown++;
                    string text = hit.Text.Length > 170 ? hit.Text.Substring(0, 170) + "..." : hit.Text;
                    Console.WriteLine($"{System.IO.Path.GetFileName(hit.FilePath)}({hit.LineNumber}): {text}");
                }
            }
            sw.Stop();

            Console.WriteLine();
            Console.WriteLine($"{total} coincidencias en {perFile.Count} archivo(s), {sw.ElapsedMilliseconds} ms");
            foreach (var kv in perFile.OrderByDescending(k => k.Value).Take(10))
                Console.WriteLine($"  {kv.Value,8}  {kv.Key}");
            return 0;
        }
    }
}
