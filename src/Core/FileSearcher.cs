using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace NppMultiSearch.Core
{
    public sealed class SearchHit
    {
        public string FilePath { get; }
        public int LineNumber { get; }   // base 1, como Notepad++
        public string Text { get; }

        public SearchHit(string filePath, int lineNumber, string text)
        {
            FilePath = filePath;
            LineNumber = lineNumber;
            Text = text;
        }
    }

    public sealed class FolderSearchOptions
    {
        public string Folder { get; set; }
        /// <summary>Filtros tipo Notepad++: "*.log;*.txt". Vacio = "*".</summary>
        public string Filters { get; set; } = "*";
        public bool Recursive { get; set; } = true;
    }

    public static class FileSearcher
    {
        /// <summary>Busca en todos los archivos de una carpeta. Lee en streaming (no carga el archivo entero).</summary>
        public static IEnumerable<SearchHit> SearchFolder(
            FolderSearchOptions folder, LineQuery query, CancellationToken ct = default(CancellationToken),
            Action<string> onFile = null)
        {
            if (folder == null) throw new ArgumentNullException(nameof(folder));
            if (!Directory.Exists(folder.Folder))
                throw new DirectoryNotFoundException(folder.Folder);

            var option = folder.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in SplitFilters(folder.Filters))
            {
                foreach (var file in EnumerateFilesSafe(folder.Folder, pattern, option))
                {
                    if (!seen.Add(file)) continue; // un archivo que cae en dos filtros se busca una vez
                    ct.ThrowIfCancellationRequested();
                    onFile?.Invoke(file);
                    foreach (var hit in SearchFile(file, query, ct))
                        yield return hit;
                }
            }
        }

        public static IEnumerable<SearchHit> SearchFile(
            string path, LineQuery query, CancellationToken ct = default(CancellationToken))
        {
            StreamReader reader;
            try
            {
                // FileShare.ReadWrite: el servidor puede estar escribiendo el log mientras lo analizamos.
                var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                // UTF-8 con deteccion de BOM; los bytes invalidos no abortan la lectura.
                reader = new StreamReader(fs, new UTF8Encoding(false), true);
            }
            catch (IOException) { yield break; }
            catch (UnauthorizedAccessException) { yield break; }

            using (reader)
            {
                foreach (var hit in SearchReader(path, reader, query, ct))
                    yield return hit;
            }
        }

        /// <summary>Busca en un texto que ya esta en memoria (p. ej. el documento abierto en el editor, guardado o no).</summary>
        public static IEnumerable<SearchHit> SearchText(
            string displayPath, string text, LineQuery query, CancellationToken ct = default(CancellationToken))
        {
            using (var reader = new StringReader(text ?? string.Empty))
            {
                foreach (var hit in SearchReader(displayPath, reader, query, ct))
                    yield return hit;
            }
        }

        private static IEnumerable<SearchHit> SearchReader(
            string path, TextReader reader, LineQuery query, CancellationToken ct)
        {
            string line;
            int n = 0;
            while ((line = reader.ReadLine()) != null)
            {
                n++;
                if ((n & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
                if (query.IsMatch(line))
                    yield return new SearchHit(path, n, line);
            }
        }

        private static IEnumerable<string> SplitFilters(string filters)
        {
            if (string.IsNullOrWhiteSpace(filters)) { yield return "*"; yield break; }
            foreach (var f in filters.Split(new[] { ';', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                yield return f;
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, SearchOption option)
        {
            // Se recorre a mano para que una carpeta sin permisos no corte toda la busqueda.
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                string[] files = Array.Empty<string>();
                try { files = Directory.GetFiles(dir, pattern); }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
                foreach (var f in files) yield return f;

                if (option == SearchOption.AllDirectories)
                {
                    try { foreach (var d in Directory.GetDirectories(dir)) pending.Push(d); }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            }
        }
    }
}
