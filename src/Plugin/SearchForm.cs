using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NppMultiSearch.Core;

namespace NppMultiSearch.Plugin
{
    internal sealed class SearchForm : Form
    {
        private const int MaxShownHits = 100000;   // tope de lineas mostradas (el conteo sigue contando todas)
        private const int MaxTextChars = 2000;     // las lineas mas largas se recortan en pantalla

        private sealed class ModeItem
        {
            public string Text; public TermMode Mode;
            public override string ToString() { return Text; }
        }

        private sealed class TermRow
        {
            public Panel Panel; public ComboBox Mode; public TextBox Text; public Button Remove;
        }

        /// <summary>Estado compartido entre el hilo de busqueda y la ventana.</summary>
        private sealed class SearchRun
        {
            public readonly ConcurrentQueue<SearchHit> Queue = new ConcurrentQueue<SearchHit>();
            public volatile int Total;
            public volatile int Files;
            public volatile bool Truncated;
            public volatile bool Cancelled;
            public volatile bool Done;
            public volatile string Error;
            public volatile string CurrentFile;
            public long ElapsedMs;
            public GroupCounter Counter;
            public CancellationTokenSource Cts = new CancellationTokenSource();
        }

        private readonly float _scale;
        private readonly ToolTip _tip = new ToolTip();
        private readonly List<TermRow> _rows = new List<TermRow>();
        private readonly List<SearchHit> _results = new List<SearchHit>();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 100 };

        private RadioButton _rbDoc, _rbFolder;
        private TextBox _txtFolder, _txtFilters, _txtGroup;
        private Button _btnBrowse, _btnSearch, _btnStop, _btnAddTerm;
        private CheckBox _chkRecursive, _chkCase, _chkWord, _chkRegex;
        private Panel _pnlTerms;
        private TableLayoutPanel _pnlFolder;
        private ListView _lvResults, _lvCounts;
        private TabPage _tabResults, _tabCounts;
        private TabControl _tabs;
        private ToolStripStatusLabel _status;

        private SearchRun _run;
        private bool _allowClose;

        public SearchForm()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;

            Text = "Multi-Search - varias coincidencias en la misma línea";
            Font = new Font("Segoe UI", 9f * _scale);
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(980), Px(680));
            MinimumSize = new Size(Px(720), Px(480));
            KeyPreview = true;

            BuildUi();
            ApplySettings(Settings.Load());

            _timer.Tick += (s, e) => OnTick();
            FormClosing += OnFormClosing;
            KeyDown += OnFormKeyDown;
        }

        private int Px(int v) { return (int)Math.Round(v * _scale); }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabResults = new TabPage("Resultados");
            _tabCounts = new TabPage("Conteo por valor");
            _tabs.TabPages.Add(_tabResults);
            _tabs.TabPages.Add(_tabCounts);

            _lvResults = NewListView(virtualMode: true);
            _lvResults.Columns.Add("Archivo", Px(220));
            _lvResults.Columns.Add("Línea", Px(70), HorizontalAlignment.Right);
            _lvResults.Columns.Add("Texto", Px(900));
            _lvResults.RetrieveVirtualItem += OnRetrieveVirtualItem;
            _lvResults.DoubleClick += (s, e) => OpenSelectedResult();
            _lvResults.KeyDown += OnResultsKeyDown;
            _lvResults.ContextMenuStrip = BuildResultsMenu();
            _tabResults.Controls.Add(_lvResults);

            _lvCounts = NewListView(virtualMode: false);
            _lvCounts.Columns.Add("Valor", Px(260));
            _lvCounts.Columns.Add("Veces", Px(80), HorizontalAlignment.Right);
            _lvCounts.Columns.Add("Primera aparición", Px(300));
            _lvCounts.Columns.Add("Última aparición", Px(300));
            _lvCounts.DoubleClick += OnCountsDoubleClick;
            _tabCounts.Controls.Add(_lvCounts);

            var statusStrip = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = true };
            _status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            statusStrip.Items.Add(_status);

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, Padding = new Padding(Px(8), Px(8), Px(8), Px(4)),
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            top.Controls.Add(BuildScopeRow());
            top.Controls.Add(BuildFolderRow());
            top.Controls.Add(BuildTermsArea());
            top.Controls.Add(BuildOptionsRow());
            top.Controls.Add(BuildGroupRow());
            top.Controls.Add(BuildButtonsRow());

            // El control con Dock=Fill se agrega primero: WinForms le reparte al final el espacio que sobra.
            Controls.Add(_tabs);
            Controls.Add(statusStrip);
            Controls.Add(top);
        }

        private ListView NewListView(bool virtualMode)
        {
            return new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
                HideSelection = false, MultiSelect = true, VirtualMode = virtualMode, ShowItemToolTips = true,
            };
        }

        private Control BuildScopeRow()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            p.Controls.Add(new Label { Text = "Buscar en:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(6), Px(8), 0) });
            _rbDoc = new RadioButton { Text = "Documento actual", AutoSize = true, Checked = true };
            _rbFolder = new RadioButton { Text = "Carpeta", AutoSize = true };
            _rbFolder.CheckedChanged += (s, e) => _pnlFolder.Enabled = _rbFolder.Checked;
            p.Controls.Add(_rbDoc);
            p.Controls.Add(_rbFolder);
            return p;
        }

        private Control BuildFolderRow()
        {
            _pnlFolder = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 6, Enabled = false };
            _pnlFolder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _pnlFolder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _pnlFolder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _pnlFolder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _pnlFolder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(130)));
            _pnlFolder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _txtFolder = new TextBox { Dock = DockStyle.Fill };
            _btnBrowse = new Button { Text = "...", Width = Px(34), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom };
            _btnBrowse.Click += OnBrowse;
            _txtFilters = new TextBox { Dock = DockStyle.Fill };
            _chkRecursive = new CheckBox { Text = "Subcarpetas", AutoSize = true, Checked = true };
            _tip.SetToolTip(_txtFilters, "Filtros separados por ; (ej.: *.log;*.txt)");

            _pnlFolder.Controls.Add(new Label { Text = "Carpeta:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(6), Px(4), 0) }, 0, 0);
            _pnlFolder.Controls.Add(_txtFolder, 1, 0);
            _pnlFolder.Controls.Add(_btnBrowse, 2, 0);
            _pnlFolder.Controls.Add(new Label { Text = "Filtros:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(Px(10), Px(6), Px(4), 0) }, 3, 0);
            _pnlFolder.Controls.Add(_txtFilters, 4, 0);
            _pnlFolder.Controls.Add(_chkRecursive, 5, 0);
            WireEnterToSearch(_txtFolder);
            WireEnterToSearch(_txtFilters);
            return _pnlFolder;
        }

        private Control BuildTermsArea()
        {
            _pnlTerms = new Panel { Dock = DockStyle.Fill, Height = Px(30) };
            _pnlTerms.Resize += (s, e) => LayoutTerms();
            return _pnlTerms;
        }

        private Control BuildOptionsRow()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            _chkCase = new CheckBox { Text = "Coincidir mayúsculas y minúsculas", AutoSize = true };
            _chkWord = new CheckBox { Text = "Solo palabras completas", AutoSize = true };
            _chkRegex = new CheckBox { Text = "Expresión regular", AutoSize = true };
            p.Controls.Add(_chkCase);
            p.Controls.Add(_chkWord);
            p.Controls.Add(_chkRegex);
            return p;
        }

        private Control BuildGroupRow()
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _txtGroup = new TextBox { Dock = DockStyle.Fill };
            _tip.SetToolTip(_txtGroup,
                "Opcional. Regex con un grupo ( ) que extrae un valor de cada línea encontrada.\r\n" +
                "Ej.: MOVIL[=: ]*(\\w+)  ->  cuenta cuántas veces aparece cada móvil (pestana 'Conteo por valor').");
            p.Controls.Add(new Label { Text = "Contar por (regex con un grupo):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(6), Px(6), 0) }, 0, 0);
            p.Controls.Add(_txtGroup, 1, 0);
            WireEnterToSearch(_txtGroup);
            return p;
        }

        private Button NewButton(string text, int minWidth)
        {
            return new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(Px(minWidth), Px(28)), Padding = new Padding(Px(6), 0, Px(6), 0),
            };
        }
        private Control BuildButtonsRow()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, Px(4), 0, 0) };
            _btnSearch = NewButton("Buscar", 100);
            _btnSearch.Click += (s, e) => StartSearch();
            _btnStop = NewButton("Detener", 100); _btnStop.Enabled = false;
            _btnStop.Click += (s, e) => StopSearch();
            _btnAddTerm = NewButton("+ Agregar campo", 130);
            _btnAddTerm.Click += (s, e) => { var r = AddTermRow(TermMode.Must, string.Empty); r.Text.Focus(); };
            var btnClear = NewButton("Limpiar campos", 120);
            btnClear.Click += (s, e) => { foreach (var r in _rows) r.Text.Text = string.Empty; _rows[0].Text.Focus(); };
            p.Controls.Add(_btnSearch);
            p.Controls.Add(_btnStop);
            p.Controls.Add(_btnAddTerm);
            p.Controls.Add(btnClear);
            return p;
        }

        private ContextMenuStrip BuildResultsMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Ir a la línea", null, (s, e) => OpenSelectedResult());
            m.Items.Add("Copiar líneas seleccionadas", null, (s, e) => CopyResults(selectedOnly: true));
            m.Items.Add("Copiar todas las líneas", null, (s, e) => CopyResults(selectedOnly: false));
            return m;
        }

        // ------------------------------------------------------------ filas de terminos

        private TermRow AddTermRow(TermMode mode, string text)
        {
            var row = new TermRow();
            row.Panel = new Panel { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            row.Mode = new ComboBox { Dock = DockStyle.Left, Width = Px(150), DropDownStyle = ComboBoxStyle.DropDownList };
            row.Mode.Items.Add(new ModeItem { Text = "Debe contener", Mode = TermMode.Must });
            row.Mode.Items.Add(new ModeItem { Text = "No debe contener", Mode = TermMode.MustNot });
            row.Mode.Items.Add(new ModeItem { Text = "Alguno de", Mode = TermMode.Any });
            row.Mode.SelectedIndex = mode == TermMode.Must ? 0 : mode == TermMode.MustNot ? 1 : 2;
            row.Text = new TextBox { Dock = DockStyle.Fill, Text = text ?? string.Empty };
            row.Remove = new Button { Dock = DockStyle.Right, Width = Px(30), Text = "x" };
            _tip.SetToolTip(row.Remove, "Quitar este campo");
            row.Remove.Click += (s, e) => RemoveTermRow(row);
            WireEnterToSearch(row.Text);

            // Fill primero y luego los bordes (ver comentario en BuildUi).
            row.Panel.Controls.Add(row.Text);
            row.Panel.Controls.Add(row.Remove);
            row.Panel.Controls.Add(row.Mode);

            _rows.Add(row);
            _pnlTerms.Controls.Add(row.Panel);
            LayoutTerms();
            return row;
        }

        private void RemoveTermRow(TermRow row)
        {
            if (_rows.Count <= 1) { row.Text.Text = string.Empty; return; } // siempre queda al menos un campo
            _rows.Remove(row);
            _pnlTerms.Controls.Remove(row.Panel);
            row.Panel.Dispose();
            LayoutTerms();
        }

        private void LayoutTerms()
        {
            int rowH = Px(30);
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].Panel.SetBounds(0, i * rowH, _pnlTerms.ClientSize.Width, rowH - Px(3));
            _pnlTerms.Height = Math.Max(rowH, _rows.Count * rowH);
        }

        private void WireEnterToSearch(TextBox box)
        {
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartSearch(); }
            };
        }

        // --------------------------------------------------------- preferencias

        private void ApplySettings(Settings s)
        {
            _rbFolder.Checked = s.ScopeFolder;
            _rbDoc.Checked = !s.ScopeFolder;
            _pnlFolder.Enabled = s.ScopeFolder;
            _txtFolder.Text = s.Folder;
            _txtFilters.Text = string.IsNullOrWhiteSpace(s.Filters) ? "*" : s.Filters;
            _chkRecursive.Checked = s.Recursive;
            _chkCase.Checked = s.MatchCase;
            _chkWord.Checked = s.WholeWord;
            _chkRegex.Checked = s.UseRegex;
            _txtGroup.Text = s.GroupRegex;
            foreach (var t in s.Terms) AddTermRow(t.Mode, t.Text);
        }

        private Settings CollectSettings()
        {
            var s = new Settings
            {
                ScopeFolder = _rbFolder.Checked, Folder = _txtFolder.Text, Filters = _txtFilters.Text,
                Recursive = _chkRecursive.Checked, MatchCase = _chkCase.Checked, WholeWord = _chkWord.Checked,
                UseRegex = _chkRegex.Checked, GroupRegex = _txtGroup.Text,
            };
            foreach (var r in _rows) s.Terms.Add(new SearchTerm(r.Text.Text, ((ModeItem)r.Mode.SelectedItem).Mode));
            return s;
        }

        // ---------------------------------------------- mostrar / ocultar la ventana

        /// <summary>Se llama desde el hilo de la ventana (via BeginInvoke).</summary>
        public void ShowAndFocus(string selectedText)
        {
            if (!IsHandleCreated) { var h = Handle; }
            // Dueño = ventana de Notepad++: la ventana queda siempre por encima de él, sin ocupar un lugar en la barra de tareas.
            NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWLP_HWNDPARENT, Npp.MainWindow);

            if (!string.IsNullOrWhiteSpace(selectedText) && selectedText.Length <= 200 && selectedText.IndexOfAny(new[] { '\r', '\n' }) < 0)
                _rows[0].Text.Text = selectedText.Trim();

            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            _rows[0].Text.Focus();
            _rows[0].Text.SelectAll();
        }

        public void AllowClose() { _allowClose = true; }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            CollectSettings().Save();
            if (_allowClose) { StopSearch(); return; }
            e.Cancel = true; // el cierre solo oculta la ventana: conserva campos y resultados
            Hide();
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape) return;
            e.Handled = true;
            if (_run != null && !_run.Done) StopSearch(); else Hide();
        }

        // ------------------------------------------------------------- busqueda

        private void StartSearch()
        {
            if (_run != null && !_run.Done) return;

            var terms = _rows.Select(r => new SearchTerm(r.Text.Text.Trim(), ((ModeItem)r.Mode.SelectedItem).Mode)).ToList();
            var opts = new QueryOptions { MatchCase = _chkCase.Checked, WholeWord = _chkWord.Checked, UseRegex = _chkRegex.Checked };

            LineQuery query;
            try { query = new LineQuery(terms, opts); }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, "Alguna expresión regular no es válida:\n\n" + ex.Message, "Multi-Search", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (query.IsEmpty)
            {
                SetStatus("Escribe al menos un campo 'Debe contener' o 'Alguno de'.");
                return;
            }

            GroupCounter counter = null;
            if (!string.IsNullOrWhiteSpace(_txtGroup.Text))
            {
                try { counter = new GroupCounter(_txtGroup.Text, _chkCase.Checked); }
                catch (ArgumentException ex)
                {
                    MessageBox.Show(this, "La expresión de 'Contar por' no es válida:\n\n" + ex.Message, "Multi-Search", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            bool inFolder = _rbFolder.Checked;
            FolderSearchOptions folder = null;
            string docText = null, docPath = null;
            try
            {
                if (inFolder)
                {
                    if (!Directory.Exists(_txtFolder.Text)) { SetStatus("La carpeta no existe: " + _txtFolder.Text); return; }
                    folder = new FolderSearchOptions { Folder = _txtFolder.Text, Filters = _txtFilters.Text, Recursive = _chkRecursive.Checked };
                }
                else
                {
                    docPath = Npp.CurrentFilePath();
                    docText = Npp.CurrentDocumentText();
                }
            }
            catch (Exception ex) { SetStatus("No se pudo leer el documento: " + ex.Message); return; }

            _results.Clear();
            _lvResults.VirtualListSize = 0;
            _lvCounts.Items.Clear();
            _tabs.SelectedTab = _tabResults;

            var run = new SearchRun { Counter = counter };
            _run = run;
            SetRunning(true);
            SetStatus("Buscando...");
            CollectSettings().Save();

            Task.Run(() => Worker(run, query, folder, docPath, docText));
            _timer.Start();
        }

        private void StopSearch()
        {
            var run = _run;
            if (run != null && !run.Done) { run.Cancelled = true; run.Cts.Cancel(); }
        }

        private static void Worker(SearchRun run, LineQuery query, FolderSearchOptions folder, string docPath, string docText)
        {
            var sw = Stopwatch.StartNew();
            var filesWithHits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int queued = 0;
            try
            {
                IEnumerable<SearchHit> hits = folder != null
                    ? FileSearcher.SearchFolder(folder, query, run.Cts.Token, f => run.CurrentFile = f)
                    : FileSearcher.SearchText(docPath, docText, query, run.Cts.Token);

                foreach (var hit in hits)
                {
                    run.Cts.Token.ThrowIfCancellationRequested();
                    run.Total++;
                    if (filesWithHits.Add(hit.FilePath)) run.Files = filesWithHits.Count;
                    if (run.Counter != null) run.Counter.Add(hit);

                    if (queued < MaxShownHits)
                    {
                        string text = hit.Text.Length > MaxTextChars ? hit.Text.Substring(0, MaxTextChars) + "..." : hit.Text;
                        run.Queue.Enqueue(new SearchHit(hit.FilePath, hit.LineNumber, text));
                        queued++;
                    }
                    else
                    {
                        run.Truncated = true;
                        if (run.Counter == null) break; // sin conteo no hace falta seguir leyendo
                    }
                }
            }
            catch (OperationCanceledException) { run.Cancelled = true; }
            catch (Exception ex) { run.Error = ex.Message; }
            finally
            {
                sw.Stop();
                run.ElapsedMs = sw.ElapsedMilliseconds;
                run.Done = true;
            }
        }

        private void OnTick()
        {
            var run = _run;
            if (run == null) { _timer.Stop(); return; }

            // Se vuelca la cola en bloques para que la ventana siga fluida con miles de resultados.
            int moved = 0;
            while (moved < 5000 && run.Queue.TryDequeue(out var hit)) { _results.Add(hit); moved++; }
            if (moved > 0) _lvResults.VirtualListSize = _results.Count;

            if (!run.Done)
            {
                string file = run.CurrentFile;
                SetStatus("Buscando...  " + run.Total.ToString("N0") + " coincidencias" +
                          (file != null ? "   -   " + Path.GetFileName(file) : string.Empty));
                return;
            }
            if (!run.Queue.IsEmpty) return; // falta terminar de volcar

            _timer.Stop();
            SetRunning(false);
            ShowCounts(run);

            string end = run.Error != null ? "Error: " + run.Error
                : (run.Cancelled ? "Detenido. " : string.Empty) +
                  run.Total.ToString("N0") + " coincidencias en " + run.Files + " archivo(s)  (" + run.ElapsedMs + " ms)";
            if (run.Truncated) end += "  -  se muestran solo las primeras " + MaxShownHits.ToString("N0");
            SetStatus(end);
        }

        private void ShowCounts(SearchRun run)
        {
            if (run.Counter == null) return;
            var rows = run.Counter.Rows();
            _lvCounts.BeginUpdate();
            try
            {
                _lvCounts.Items.Clear();
                foreach (var r in rows)
                {
                    var item = new ListViewItem(new[]
                    {
                        r.Key, r.Count.ToString("N0"),
                        Path.GetFileName(r.FirstFile) + "(" + r.FirstLine + ")",
                        Path.GetFileName(r.LastFile) + "(" + r.LastLine + ")",
                    }) { Tag = r };
                    _lvCounts.Items.Add(item);
                }
            }
            finally { _lvCounts.EndUpdate(); }

            if (rows.Count > 0) _tabs.SelectedTab = _tabCounts;
            if (run.Counter.LinesWithoutValue > 0)
                _tabCounts.Text = "Conteo por valor (" + rows.Count + ")  -  " + run.Counter.LinesWithoutValue + " línea(s) sin valor";
            else
                _tabCounts.Text = "Conteo por valor (" + rows.Count + ")";
        }

        private void SetRunning(bool running)
        {
            _btnSearch.Enabled = !running;
            _btnStop.Enabled = running;
        }

        private void SetStatus(string text) { _status.Text = text; }

        // -------------------------------------------------------------- resultados

        private void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            var h = _results[e.ItemIndex];
            e.Item = new ListViewItem(new[] { Path.GetFileName(h.FilePath), h.LineNumber.ToString(), h.Text }) { ToolTipText = h.FilePath };
        }

        private void OpenSelectedResult()
        {
            if (_lvResults.SelectedIndices.Count == 0) return;
            int i = _lvResults.SelectedIndices[0];
            if (i >= 0 && i < _results.Count) GoTo(_results[i].FilePath, _results[i].LineNumber);
        }

        private void OnCountsDoubleClick(object sender, EventArgs e)
        {
            if (_lvCounts.SelectedItems.Count == 0) return;
            var r = _lvCounts.SelectedItems[0].Tag as GroupRow;
            if (r != null) GoTo(r.FirstFile, r.FirstLine);
        }

        private void GoTo(string path, int line)
        {
            try
            {
                if (!Npp.OpenAtLine(path, line))
                    SetStatus("No se encontró el archivo: " + path);
            }
            catch (Exception ex) { SetStatus("No se pudo abrir: " + ex.Message); }
        }

        private void OnResultsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; OpenSelectedResult(); }
            else if (e.Control && e.KeyCode == Keys.C) { e.Handled = true; CopyResults(selectedOnly: true); }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                e.Handled = true;
                for (int i = 0; i < _lvResults.VirtualListSize; i++) _lvResults.SelectedIndices.Add(i);
            }
        }

        private void CopyResults(bool selectedOnly)
        {
            var sb = new StringBuilder();
            if (selectedOnly)
            {
                foreach (int i in _lvResults.SelectedIndices)
                    if (i < _results.Count) sb.AppendLine(_results[i].FilePath + "(" + _results[i].LineNumber + "): " + _results[i].Text);
            }
            else
            {
                foreach (var h in _results) sb.AppendLine(h.FilePath + "(" + h.LineNumber + "): " + h.Text);
            }
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); SetStatus("Copiado al portapapeles."); }
            catch (Exception ex) { SetStatus("No se pudo copiar: " + ex.Message); }
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog { Description = "Carpeta donde buscar", SelectedPath = _txtFolder.Text })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) _txtFolder.Text = dlg.SelectedPath;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _tip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
