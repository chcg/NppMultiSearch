using System;
using System.Runtime.InteropServices;

namespace NppMultiSearch.Plugin
{
    /// <summary>
    /// Las 6 funciones que Notepad++ exige a todo plugin. Nada de aqui debe lanzar excepciones hacia el
    /// codigo nativo: un error no controlado en un plugin cierra Notepad++ entero.
    /// </summary>
    internal static class Exports
    {
        [DllExport("isUnicode", CallingConvention = CallingConvention.Cdecl)]
        public static bool IsUnicode() => true;

        [DllExport("setInfo", CallingConvention = CallingConvention.Cdecl)]
        public static void SetInfo(NppData notepadPlusData)
        {
            try { PluginMain.SetInfo(notepadPlusData); } catch { }
        }

        [DllExport("getName", CallingConvention = CallingConvention.Cdecl)]
        public static IntPtr GetName()
        {
            try { return PluginMain.NamePtr(); } catch { return IntPtr.Zero; }
        }

        [DllExport("getFuncsArray", CallingConvention = CallingConvention.Cdecl)]
        public static IntPtr GetFuncsArray(IntPtr nbF)
        {
            try { return PluginMain.FuncsArray(nbF); } catch { return IntPtr.Zero; }
        }

        [DllExport("beNotified", CallingConvention = CallingConvention.Cdecl)]
        public static void BeNotified(IntPtr notifyCode)
        {
            try { PluginMain.Notified(notifyCode); } catch { }
        }

        [DllExport("messageProc", CallingConvention = CallingConvention.Cdecl)]
        public static IntPtr MessageProc(uint message, IntPtr wParam, IntPtr lParam) => new IntPtr(1);
    }
}
