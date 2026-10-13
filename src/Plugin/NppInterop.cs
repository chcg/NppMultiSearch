using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NppMultiSearch.Plugin
{
    // ----- Estructuras de PluginInterface.h (Notepad++) -----

    [StructLayout(LayoutKind.Sequential)]
    internal struct NppData
    {
        public IntPtr NppHandle;
        public IntPtr ScintillaMainHandle;
        public IntPtr ScintillaSecondHandle;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PluginCommand();

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShortcutKey
    {
        [MarshalAs(UnmanagedType.U1)] public bool IsCtrl;
        [MarshalAs(UnmanagedType.U1)] public bool IsAlt;
        [MarshalAs(UnmanagedType.U1)] public bool IsShift;
        public byte Key;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FuncItem
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ItemName;
        public IntPtr PFunc;
        public int CmdID;
        [MarshalAs(UnmanagedType.U1)] public bool Init2Check;
        public IntPtr PShKey;
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref int lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam,
            [MarshalAs(UnmanagedType.LPWStr)] string lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public const int GWLP_HWNDPARENT = -8;
    }

    /// <summary>Acceso a Notepad++ y Scintilla. Todas las llamadas son SendMessage, seguras desde cualquier hilo.</summary>
    internal static class Npp
    {
        // Mensajes de Notepad++ (Notepad_plus_msgs.h): NPPMSG = WM_USER + 1000 = 2024
        private const uint NPPM_GETCURRENTSCINTILLA = 2024 + 4;
        private const uint NPPM_GETPLUGINSCONFIGDIR = 2024 + 46;
        private const uint NPPM_DOOPEN = 2024 + 77;
        private const uint NPPM_GETFULLCURRENTPATH = 1024 + 3000 + 1; // RUNCOMMAND_USER + FULL_CURRENT_PATH

        public const int NPPN_SHUTDOWN = 1000 + 9;

        // Mensajes de Scintilla (Scintilla.h)
        private const uint SCI_GETLENGTH = 2006;
        private const uint SCI_GOTOLINE = 2024;
        private const uint SCI_GETLINEENDPOSITION = 2136;
        private const uint SCI_GETCODEPAGE = 2137;
        private const uint SCI_SETSEL = 2160;
        private const uint SCI_GETSELTEXT = 2161;
        private const uint SCI_POSITIONFROMLINE = 2167;
        private const uint SCI_GETTEXT = 2182;
        private const uint SCI_VISIBLEFROMDOCLINE = 2220;
        private const uint SCI_ENSUREVISIBLEENFORCEPOLICY = 2234;
        private const uint SCI_LINESONSCREEN = 2370;
        private const uint SCI_SETFIRSTVISIBLELINE = 2613;

        public static NppData Data;

        public static IntPtr MainWindow => Data.NppHandle;

        private static long Sci(IntPtr sci, uint msg, long wParam = 0, long lParam = 0)
        {
            return NativeMethods.SendMessage(sci, msg, new IntPtr(wParam), new IntPtr(lParam)).ToInt64();
        }

        public static IntPtr CurrentScintilla()
        {
            int which = 0;
            NativeMethods.SendMessage(Data.NppHandle, NPPM_GETCURRENTSCINTILLA, IntPtr.Zero, ref which);
            return which == 0 ? Data.ScintillaMainHandle : Data.ScintillaSecondHandle;
        }

        public static string CurrentFilePath()
        {
            var sb = new StringBuilder(1024);
            NativeMethods.SendMessage(Data.NppHandle, NPPM_GETFULLCURRENTPATH, new IntPtr(sb.Capacity), sb);
            return sb.ToString();
        }

        public static string PluginsConfigDir()
        {
            int len = NativeMethods.SendMessage(Data.NppHandle, NPPM_GETPLUGINSCONFIGDIR, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (len <= 0) return null;
            var sb = new StringBuilder(len + 1);
            NativeMethods.SendMessage(Data.NppHandle, NPPM_GETPLUGINSCONFIGDIR, new IntPtr(sb.Capacity), sb);
            return sb.ToString();
        }

        private static Encoding DocEncoding(IntPtr sci)
        {
            long cp = Sci(sci, SCI_GETCODEPAGE);
            if (cp == 65001) return new UTF8Encoding(false);
            try { return cp > 0 ? Encoding.GetEncoding((int)cp) : Encoding.Default; }
            catch { return Encoding.Default; }
        }

        private static string ReadScintillaText(IntPtr sci, uint msg, long length)
        {
            if (length <= 0) return string.Empty;
            IntPtr buf = Marshal.AllocHGlobal(new IntPtr(length + 1));
            try
            {
                NativeMethods.SendMessage(sci, msg, new IntPtr(length + 1), buf);
                var bytes = new byte[length];
                Marshal.Copy(buf, bytes, 0, (int)length);
                return DocEncoding(sci).GetString(bytes);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>Texto completo del documento activo, tal cual esta en el editor (guardado o no).</summary>
        public static string CurrentDocumentText()
        {
            IntPtr sci = CurrentScintilla();
            return ReadScintillaText(sci, SCI_GETTEXT, Sci(sci, SCI_GETLENGTH));
        }

        public static string SelectedText()
        {
            IntPtr sci = CurrentScintilla();
            // Con lParam = NULL devuelve el largo necesario (incluye el terminador).
            long len = Sci(sci, SCI_GETSELTEXT, 0, 0) - 1;
            if (len <= 0) return string.Empty;
            IntPtr buf = Marshal.AllocHGlobal(new IntPtr(len + 1));
            try
            {
                NativeMethods.SendMessage(sci, SCI_GETSELTEXT, IntPtr.Zero, buf);
                var bytes = new byte[len];
                Marshal.Copy(buf, bytes, 0, (int)len);
                return DocEncoding(sci).GetString(bytes);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>Abre el archivo (o activa su pestana) y deja la linea indicada seleccionada y centrada.</summary>
        public static bool OpenAtLine(string path, int line1Based)
        {
            if (!string.Equals(CurrentFilePath(), path, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) return false;
                NativeMethods.SendMessage(Data.NppHandle, NPPM_DOOPEN, IntPtr.Zero, path);
            }

            IntPtr sci = CurrentScintilla();
            long line = Math.Max(0, line1Based - 1);
            Sci(sci, SCI_ENSUREVISIBLEENFORCEPOLICY, line);
            Sci(sci, SCI_GOTOLINE, line);
            long start = Sci(sci, SCI_POSITIONFROMLINE, line);
            long end = Sci(sci, SCI_GETLINEENDPOSITION, line);
            // ancla al final y cursor al inicio: la linea queda seleccionada pero la vista no se desplaza a la derecha
            Sci(sci, SCI_SETSEL, end, start);

            long visible = Sci(sci, SCI_VISIBLEFROMDOCLINE, line);
            long onScreen = Sci(sci, SCI_LINESONSCREEN);
            Sci(sci, SCI_SETFIRSTVISIBLELINE, Math.Max(0, visible - onScreen / 2));
            return true;
        }
    }
}
