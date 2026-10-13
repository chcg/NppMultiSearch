using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NppMultiSearch.Plugin
{
    internal static class PluginMain
    {
        public const string PluginName = "Multi-Search";

        private static IntPtr _namePtr = IntPtr.Zero;
        private static IntPtr _funcsPtr = IntPtr.Zero;
        private static readonly List<PluginCommand> KeepAlive = new List<PluginCommand>();

        private static Thread _uiThread;
        private static SearchForm _form;
        private static readonly ManualResetEvent FormReady = new ManualResetEvent(false);

        public static void SetInfo(NppData data)
        {
            Npp.Data = data;
        }

        public static IntPtr NamePtr()
        {
            if (_namePtr == IntPtr.Zero) _namePtr = Marshal.StringToHGlobalUni(PluginName);
            return _namePtr;
        }

        public static IntPtr FuncsArray(IntPtr nbF)
        {
            if (_funcsPtr == IntPtr.Zero)
            {
                // Atajo por defecto: Ctrl+Alt+M (se puede cambiar en Configuracion > Asignacion de atajos)
                IntPtr shortcut = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ShortcutKey)));
                Marshal.StructureToPtr(new ShortcutKey { IsCtrl = true, IsAlt = true, IsShift = false, Key = 0x4D }, shortcut, false);

                var commands = new[]
                {
                    new { Name = "Buscar con varios campos...", Action = (PluginCommand)ShowSearch, Shortcut = shortcut },
                    new { Name = "Acerca de...", Action = (PluginCommand)ShowAbout, Shortcut = IntPtr.Zero },
                };

                int size = Marshal.SizeOf(typeof(FuncItem));
                _funcsPtr = Marshal.AllocHGlobal(size * commands.Length);
                for (int i = 0; i < commands.Length; i++)
                {
                    KeepAlive.Add(commands[i].Action); // el delegado debe vivir mientras viva el plugin
                    var item = new FuncItem
                    {
                        ItemName = commands[i].Name,
                        PFunc = Marshal.GetFunctionPointerForDelegate(commands[i].Action),
                        CmdID = 0,
                        Init2Check = false,
                        PShKey = commands[i].Shortcut,
                    };
                    Marshal.StructureToPtr(item, IntPtr.Add(_funcsPtr, i * size), false);
                }
            }

            Marshal.WriteInt32(nbF, 2);
            return _funcsPtr;
        }

        public static void Notified(IntPtr notifyCode)
        {
            // SCNotification empieza con un NMHDR { HWND hwndFrom; UINT_PTR idFrom; UINT code; }
            int code = Marshal.ReadInt32(notifyCode, IntPtr.Size * 2);
            if (code == Npp.NPPN_SHUTDOWN) Shutdown();
        }

        // ---- Comandos de menu ----

        private static void ShowSearch()
        {
            try
            {
                // La seleccion se lee aqui, en el hilo de Notepad++, antes de pasar a la ventana.
                string selected = Npp.SelectedText();
                EnsureForm();
                var form = _form;
                form.BeginInvoke(new Action(() => form.ShowAndFocus(selected)));
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir Multi-Search:\n\n" + ex.Message, PluginName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ShowAbout()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            MessageBox.Show(
                "Multi-Search " + version.ToString(3) + "\n\n" +
                "Busca varias palabras que coincidan en una misma linea\n" +
                "(debe contener / no debe contener / alguno de),\n" +
                "en el documento actual o en una carpeta de trazas.\n\n" +
                "https://github.com/NachoMarengo",
                PluginName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- Ventana en su propio hilo con bucle de mensajes ----
        // Notepad++ no ejecuta el bucle de WinForms, y sin el no funcionan Tab, Enter ni los atajos de los controles.

        private static void EnsureForm()
        {
            if (_uiThread != null) { FormReady.WaitOne(); return; }

            _uiThread = new Thread(RunUi) { Name = "Multi-Search UI", IsBackground = true };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            FormReady.WaitOne();
        }

        private static void RunUi()
        {
            try { Application.EnableVisualStyles(); } catch { }
            try
            {
                _form = new SearchForm();
                IntPtr unused = _form.Handle; // fuerza la creacion del handle para poder usar BeginInvoke
            }
            finally { FormReady.Set(); }

            if (_form != null) Application.Run();
        }

        private static void Shutdown()
        {
            var form = _form;
            if (form == null || form.IsDisposed) return;
            try
            {
                form.BeginInvoke(new Action(() =>
                {
                    form.AllowClose();
                    form.Close();
                    Application.ExitThread();
                }));
            }
            catch { }
        }
    }
}
