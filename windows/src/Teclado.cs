// I · El Mago — escribe en la ventana activa con SendInput (como un teclado de verdad).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ElMago
{
    public static class Teclado
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public INPUTUNION u; }

        // La unión debe tener el tamaño del miembro más grande (MOUSEINPUT) para que
        // sizeof(INPUT) sea 40 en 64 bits; si no, SendInput rechaza todo.
        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        static readonly HashSet<ushort> Extendidas = new HashSet<ushort> {
            Traductor.VK_LEFT, Traductor.VK_RIGHT, Traductor.VK_UP, Traductor.VK_DOWN, Traductor.VK_HOME,
            Traductor.VK_END, Traductor.VK_PRIOR, Traductor.VK_NEXT, Traductor.VK_DELETE, Traductor.VK_LWIN };

        static INPUT Vk(ushort vk, bool arriba)
        {
            var i = new INPUT { type = INPUT_KEYBOARD };
            i.u.ki.wVk = vk;
            i.u.ki.dwFlags = (arriba ? KEYEVENTF_KEYUP : 0) | (Extendidas.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
            return i;
        }

        static INPUT Uni(char c, bool arriba)
        {
            var i = new INPUT { type = INPUT_KEYBOARD };
            i.u.ki.wScan = c;
            i.u.ki.dwFlags = KEYEVENTF_UNICODE | (arriba ? KEYEVENTF_KEYUP : 0);
            return i;
        }

        /// <summary>
        /// Pausa entre teclas (ms). El Bloc de notas de Windows 11 y otras apps con TSF pierden o
        /// repiten letras si llegan de golpe; con 1 tecla cada pocos ms escriben bien.
        /// </summary>
        public static int PausaLetra = 6, PausaTecla = 3;

        static void Enviar(List<INPUT> l, bool unicode)
        {
            int tam = Marshal.SizeOf(typeof(INPUT));
            // De a una tecla (bajar + subir) y una pausa corta: más lento, pero no se pierde nada.
            for (int i = 0; i < l.Count; i += 2)
            {
                var par = l.GetRange(i, Math.Min(2, l.Count - i)).ToArray();
                uint n = SendInput((uint)par.Length, par, tam);
                if (n != par.Length)
                    throw new InvalidOperationException("Windows bloqueó el teclado (¿ventana de administrador o pantalla bloqueada?)");
                System.Threading.Thread.Sleep(unicode ? PausaLetra : PausaTecla);
            }
        }

        public static void Ejecutar(Accion a)
        {
            var l = new List<INPUT>();
            switch (a.Clase)
            {
                case Accion.Tipo.Texto:
                    foreach (char c in a.Texto)
                    {
                        if (c == '\r') continue;
                        if (c == '\n') { l.Add(Vk(Traductor.VK_RETURN, false)); l.Add(Vk(Traductor.VK_RETURN, true)); continue; }
                        if (c == '\t') { l.Add(Vk(Traductor.VK_TAB, false)); l.Add(Vk(Traductor.VK_TAB, true)); continue; }
                        l.Add(Uni(c, false)); l.Add(Uni(c, true)); // los emojis van como dos mitades UTF-16; Windows las une
                    }
                    break;
                case Accion.Tipo.Tecla:
                    for (int i = 0; i < a.Repetir; i++) { l.Add(Vk(a.Vk, false)); l.Add(Vk(a.Vk, true)); }
                    break;
                case Accion.Tipo.Combo:
                {
                    foreach (var m in a.Mods) l.Add(Vk(m, false));
                    l.Add(Vk(a.Vk, false)); l.Add(Vk(a.Vk, true));
                    for (int i = a.Mods.Count - 1; i >= 0; i--) l.Add(Vk(a.Mods[i], true));
                    var arr = l.ToArray(); // la combinación va junta, en un solo envío
                    if (SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT))) != arr.Length)
                        throw new InvalidOperationException("Windows bloqueó el teclado (¿ventana de administrador o pantalla bloqueada?)");
                    System.Threading.Thread.Sleep(PausaTecla * 4);
                    return;
                }
            }
            // Windows redondea cada pausa a ~16 ms: unas 60 letras por segundo, de sobra para escribir a mano.
            if (l.Count > 0) Enviar(l, a.Clase == Accion.Tipo.Texto);
        }

        /// <summary>Nombre de la ventana donde se está escribiendo (para mostrarlo en el celular).</summary>
        public static string VentanaActiva()
        {
            try
            {
                var h = GetForegroundWindow();
                if (h == IntPtr.Zero) return "";
                var sb = new StringBuilder(256);
                GetWindowText(h, sb, sb.Capacity);
                string titulo = sb.ToString();
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                string proceso = "";
                try { proceso = Process.GetProcessById((int)pid).ProcessName; } catch { }
                if (titulo.Length > 60) titulo = titulo.Substring(0, 57) + "...";
                if (titulo.Length == 0) return proceso;
                return proceso.Length == 0 ? titulo : titulo + " (" + proceso + ")";
            }
            catch { return ""; }
        }
    }
}
