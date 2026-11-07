// Prueba de punta a punta sin tocar tus ventanas: abre una caja de texto propia, hace de "celular"
// (se empareja con el PIN por 127.0.0.1), manda mensajes por El Mago y compara letra por letra.
// Uso: PruebaCaja.exe <pin> <archivo-resultado>
using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

static class PruebaCaja
{
    static string leido;
    const string Bloque = "abcdefghijklmnñopqrstuvwxyzÁÉÍÓÚÜ¿?¡!@#$%&/()=0123456789";

    static string Firma(string clave, string reto)
    {
        using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(clave)))
        {
            var sb = new StringBuilder();
            foreach (var b in h.ComputeHash(Encoding.UTF8.GetBytes(reto))) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    static string Campo(string json, string k)
    {
        int i = json.IndexOf("\"" + k + "\":\"");
        if (i < 0) return null;
        i += k.Length + 4;
        int f = json.IndexOf('"', i);
        return json.Substring(i, f - i);
    }

    static string J(string s)
    {
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    /// <summary>Trae la caja al frente (Windows solo lo permite tras una tecla; Alt suelto no escribe nada).</summary>
    static bool AlFrente(Form f, TextBox caja)
    {
        for (int i = 0; i < 20; i++)
        {
            bool listo = false;
            f.Invoke((Action)(() =>
            {
                if (GetForegroundWindow() != f.Handle)
                {
                    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
                    SetForegroundWindow(f.Handle); f.Activate();
                }
                caja.Focus();
                listo = GetForegroundWindow() == f.Handle && caja.Focused;
            }));
            if (listo) return true;
            Thread.Sleep(300);
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)] struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MI { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MI mi; [FieldOffset(0)] public KI ki; }
    [StructLayout(LayoutKind.Sequential)] struct IN { public uint type; public U u; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, IN[] i, int size);

    /// <summary>Control: esta misma prueba teclea una letra con SendInput (sin El Mago).</summary>
    static string Control(char c)
    {
        var a = new IN[2];
        a[0].type = 1; a[0].u.ki.scan = c; a[0].u.ki.flags = 4;
        a[1].type = 1; a[1].u.ki.scan = c; a[1].u.ki.flags = 4 | 2;
        uint n = SendInput(2, a, Marshal.SizeOf(typeof(IN)));
        return "SendInput devolvió " + n + " (error " + Marshal.GetLastWin32Error() + "), tamaño INPUT " + Marshal.SizeOf(typeof(IN));
    }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint acc, bool inh, int pid);
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr p, uint acc, out IntPtr tok);
    [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr tok, int clase, IntPtr buf, int tam, out int ret);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint i);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(int pid, out int ses);

    static string Integridad(int pid)
    {
        IntPtr p = OpenProcess(0x1000, false, pid), tok;
        if (p == IntPtr.Zero || !OpenProcessToken(p, 8, out tok)) return "?";
        int n; IntPtr b = Marshal.AllocHGlobal(256);
        if (!GetTokenInformation(tok, 25, b, 256, out n)) return "?";
        IntPtr sid = Marshal.ReadIntPtr(b);
        int cnt = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
        int rid = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(cnt - 1)));
        int ses; ProcessIdToSessionId(pid, out ses);
        string nivel = rid >= 0x4000 ? "sistema" : rid >= 0x3000 ? "alta (admin)" : rid >= 0x2100 ? "media+ (UIAccess)" : rid >= 0x2000 ? "media" : rid >= 0x1000 ? "baja" : "no confiable";
        return nivel + " (0x" + rid.ToString("x") + "), sesión " + ses;
    }

    [STAThread]
    static int Main(string[] args)
    {
        string pin = args[0], salida = args[1];
        var registro = new StringBuilder();
        var f = new Form { Text = "Prueba de El Mago", Width = 720, Height = 420, TopMost = true, StartPosition = FormStartPosition.CenterScreen };
        var caja = new TextBox { Multiline = true, AcceptsTab = true, AcceptsReturn = true, Dock = DockStyle.Fill, Font = new System.Drawing.Font("Segoe UI", 11f) };
        f.Controls.Add(caja);

        string esperado = "Hola desde El Mago: tildes áéíóú, eñe ñ, emoji 😀\r\nqué tal\r\nLínea con salto\r\ny tabulador:\tfin\r\n";
        string largo = "";
        for (int i = 0; i < 5; i++) largo += Bloque;
        esperado += largo.Substring(0, largo.Length - 3);

        f.Shown += (o, e) =>
        {
            f.Activate(); caja.Focus();
            new Thread(() =>
            {
                try
                {
                    Thread.Sleep(800);
                    registro.AppendLine("Caja al frente: " + (AlFrente(f, caja) ? "sí" : "NO"));
                    registro.AppendLine("Integridad de esta prueba: " + Integridad(System.Diagnostics.Process.GetCurrentProcess().Id));
                    foreach (var pm in System.Diagnostics.Process.GetProcessesByName("ElMago"))
                        registro.AppendLine("Integridad de ElMago.exe (pid " + pm.Id + "): " + Integridad(pm.Id) + ", ruta " + pm.MainModule.FileName);
                    registro.AppendLine("Control: " + Control('X'));
                    Thread.Sleep(500);
                    string antes = null;
                    f.Invoke((Action)(() => { antes = caja.Text; caja.Text = ""; }));
                    registro.AppendLine("Control: la caja recibió «" + antes + "» " + (antes == "X" ? "(OK: Windows deja teclear)" : "(NO llegó: algo bloquea el teclado simulado)"));
                    using (var c = new TcpClient("127.0.0.1", 47800))
                    {
                        var s = c.GetStream();
                        var lr = new StreamReader(s, new UTF8Encoding(false));
                        var w = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                        string reto = Campo(lr.ReadLine(), "n");
                        w.WriteLine("{\"t\":\"par\",\"id\":\"prueba-local-csharp\",\"nombre\":\"Prueba local\",\"h\":\"" + Firma(pin, reto) + "\"}");
                        string r = lr.ReadLine();
                        registro.AppendLine("Emparejar: " + (r.Contains("\"ok\"") ? "ok" : r));
                        var t0 = DateTime.Now;
                        w.WriteLine("{\"t\":\"texto\",\"s\":" + J("Hola desde El Mago: tildes áéíóú, eñe ñ, emoji 😀") + "}");
                        w.WriteLine("{\"t\":\"tecla\",\"k\":\"enter\"}");
                        w.WriteLine("{\"t\":\"texto\",\"s\":\"ke tal\"}");
                        w.WriteLine("{\"t\":\"edicion\",\"izq\":4,\"borrar\":2,\"s\":\"qué\"}");
                        w.WriteLine("{\"t\":\"tecla\",\"k\":\"enter\"}");
                        w.WriteLine("{\"t\":\"texto\",\"s\":" + J("Línea con salto\ny tabulador:\tfin") + "}");
                        w.WriteLine("{\"t\":\"tecla\",\"k\":\"enter\"}");
                        w.WriteLine("{\"t\":\"texto\",\"s\":" + J(largo) + "}");
                        w.WriteLine("{\"t\":\"tecla\",\"k\":\"backspace\",\"n\":3}");
                        w.WriteLine("{\"t\":\"ping\"}");
                        string pong = lr.ReadLine(); // llega después de escribir todo (el servidor atiende en orden)
                        registro.AppendLine("Ventana activa según El Mago: " + Campo(pong, "ventana"));
                        registro.AppendLine("Tiempo escribiendo: " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms");
                        w.WriteLine("{\"t\":\"adios\"}");
                    }
                }
                catch (Exception ex) { registro.AppendLine("ERROR cliente: " + ex.Message); }
                Thread.Sleep(700);
                // Se lee la caja con la ventana todavía abierta (después de cerrar puede quedar vacía).
                f.Invoke((Action)(() => { leido = caja.Text; }));
                f.BeginInvoke((Action)(() => f.Close()));
            }) { IsBackground = true }.Start();
        };
        var limite = new System.Windows.Forms.Timer { Interval = 90000 };
        limite.Tick += (o, e) => f.Close();
        limite.Start();
        Application.Run(f);

        string obtenido = leido ?? "";
        bool ok = obtenido == esperado;
        var rep = new StringBuilder();
        rep.AppendLine("Prueba El Mago " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        rep.AppendLine("Resultado: " + (ok ? "OK - idéntico" : "DIFERENTE"));
        rep.Append(registro);
        rep.AppendLine("Esperado (" + esperado.Length + "):"); rep.AppendLine(esperado);
        rep.AppendLine("Obtenido (" + obtenido.Length + "):"); rep.AppendLine(obtenido);
        if (!ok)
        {
            int i = 0;
            while (i < Math.Min(esperado.Length, obtenido.Length) && esperado[i] == obtenido[i]) i++;
            rep.AppendLine("Primera diferencia en la posición " + i);
        }
        File.WriteAllText(salida, rep.ToString(), new UTF8Encoding(true));
        return ok ? 0 : 1;
    }
}
