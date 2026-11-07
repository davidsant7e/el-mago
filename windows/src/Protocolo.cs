// I · El Mago — protocolo y reglas puras (sin Windows Forms ni red), para poder probarlas.
// C# 5 (.NET Framework 4): sin interpolación de cadenas ni operador ?.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ElMago
{
    /// <summary>Constantes compartidas con la app Android (MagoProtocolo.kt).</summary>
    public static class Protocolo
    {
        public const int Version = 1;
        public const int PuertoTcp = 47800;
        public const int PuertoDescubrir = 47801;
        public const string Pregunta = "MAGO?v1";
        public const int MaxLinea = 64 * 1024;     // un mensaje no puede pasar de 64 KB
        public const int MaxTexto = 8000;           // caracteres por mensaje de texto
        public const int MaxRepetir = 200;          // teclas repetidas por mensaje

        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = MaxLinea };

        public static string ACadena(Dictionary<string, object> m) { return Json.Serialize(m); }

        /// <summary>Lee una línea JSON; devuelve null si no es un objeto válido.</summary>
        public static Dictionary<string, object> Leer(string linea)
        {
            if (string.IsNullOrEmpty(linea) || linea.Length > MaxLinea) return null;
            try { return Json.DeserializeObject(linea) as Dictionary<string, object>; }
            catch { return null; }
        }

        public static string Texto(Dictionary<string, object> m, string clave)
        {
            object v;
            if (m == null || !m.TryGetValue(clave, out v) || v == null) return null;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static int Entero(Dictionary<string, object> m, string clave, int def, int min, int max)
        {
            object v;
            if (m == null || !m.TryGetValue(clave, out v) || v == null) return def;
            int n;
            try { n = Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return def; }
            if (n < min) n = min;
            if (n > max) n = max;
            return n;
        }

        public static Dictionary<string, object> Msg(string tipo)
        {
            var d = new Dictionary<string, object>();
            d["t"] = tipo;
            return d;
        }

        // ---------- Seguridad ----------

        public static string Aleatorio(int bytes)
        {
            var b = new byte[bytes];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return Hex(b);
        }

        /// <summary>PIN de 6 cifras sin sesgo.</summary>
        public static string NuevoPin()
        {
            var b = new byte[4];
            using (var r = RandomNumberGenerator.Create())
            {
                while (true)
                {
                    r.GetBytes(b);
                    uint n = BitConverter.ToUInt32(b, 0);
                    if (n < 4294000000u) return (n % 1000000u).ToString("D6", CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>HMAC-SHA256(clave, reto) en hexadecimal minúsculo. Igual en Android.</summary>
        public static string Firma(string clave, string reto)
        {
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(clave)))
                return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(reto)));
        }

        public static bool IgualesSeguro(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int dif = 0;
            for (int i = 0; i < a.Length; i++) dif |= a[i] ^ b[i];
            return dif == 0;
        }

        public static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Limpia nombres que vienen del celular (para mostrarlos sin sorpresas).</summary>
        public static string NombreSeguro(string s, string def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (sb.Length >= 40) break;
                if (!char.IsControl(c)) sb.Append(c);
            }
            var r = sb.ToString().Trim();
            return r.Length == 0 ? def : r;
        }
    }

    /// <summary>Freno a los intentos de PIN: 5 errores seguidos bloquean el emparejamiento 60 s.</summary>
    public class FrenoPin
    {
        int fallos;
        DateTime bloqueadoHasta = DateTime.MinValue;
        public int MaxFallos = 5;
        public TimeSpan Bloqueo = TimeSpan.FromSeconds(60);

        public bool Bloqueado(DateTime ahora) { lock (this) return ahora < bloqueadoHasta; }

        public void Fallo(DateTime ahora)
        {
            lock (this)
            {
                fallos++;
                if (fallos >= MaxFallos) { bloqueadoHasta = ahora + Bloqueo; fallos = 0; }
            }
        }

        public void Exito() { lock (this) fallos = 0; }
    }

    /// <summary>Una acción de teclado ya validada, lista para el simulador.</summary>
    public class Accion
    {
        public enum Tipo { Texto, Tecla, Combo }
        public Tipo Clase;
        public string Texto;          // para Texto
        public ushort Vk;             // para Tecla
        public int Repetir = 1;
        public List<ushort> Mods = new List<ushort>(); // para Combo
        public override string ToString()
        {
            if (Clase == Tipo.Texto) return "Texto(" + Texto.Length + ")";
            if (Clase == Tipo.Tecla) return "Tecla(" + Vk + "x" + Repetir + ")";
            return "Combo(" + string.Join("+", Mods) + "+" + Vk + ")";
        }
    }

    /// <summary>Traduce los mensajes del celular a acciones de teclado.</summary>
    public static class Traductor
    {
        public const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11,
            VK_MENU = 0x12, VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23,
            VK_HOME = 0x24, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_DELETE = 0x2E,
            VK_LWIN = 0x5B, VK_F1 = 0x70;

        static readonly Dictionary<string, ushort> Teclas = Crear();

        static Dictionary<string, ushort> Crear()
        {
            var d = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            d["enter"] = VK_RETURN; d["backspace"] = VK_BACK; d["tab"] = VK_TAB; d["esc"] = VK_ESCAPE;
            d["delete"] = VK_DELETE; d["space"] = VK_SPACE; d["up"] = VK_UP; d["down"] = VK_DOWN;
            d["left"] = VK_LEFT; d["right"] = VK_RIGHT; d["home"] = VK_HOME; d["end"] = VK_END;
            d["pgup"] = VK_PRIOR; d["pgdn"] = VK_NEXT; d["win"] = VK_LWIN;
            for (int i = 1; i <= 12; i++) d["f" + i] = (ushort)(VK_F1 + i - 1);
            for (char c = 'a'; c <= 'z'; c++) d[c.ToString()] = (ushort)char.ToUpperInvariant(c);
            for (char c = '0'; c <= '9'; c++) d[c.ToString()] = (ushort)c;
            return d;
        }

        public static ushort? Tecla(string nombre)
        {
            ushort vk;
            if (nombre != null && Teclas.TryGetValue(nombre.Trim(), out vk)) return vk;
            return null;
        }

        static ushort? Modificador(string m)
        {
            switch ((m ?? "").Trim().ToLowerInvariant())
            {
                case "ctrl": return VK_CONTROL;
                case "alt": return VK_MENU;
                case "shift": return VK_SHIFT;
                case "win": return VK_LWIN;
                default: return null;
            }
        }

        /// <summary>
        /// Convierte un mensaje en acciones. Devuelve null si el tipo no es de teclado.
        /// Lanza ArgumentException si los datos son inválidos.
        /// </summary>
        public static List<Accion> Acciones(Dictionary<string, object> m)
        {
            string t = Protocolo.Texto(m, "t");
            var lista = new List<Accion>();
            switch (t)
            {
                case "texto":
                {
                    string s = Protocolo.Texto(m, "s") ?? "";
                    if (s.Length > Protocolo.MaxTexto) throw new ArgumentException("Texto muy largo");
                    if (s.Length > 0) lista.Add(new Accion { Clase = Accion.Tipo.Texto, Texto = s });
                    return lista;
                }
                case "tecla":
                {
                    var vk = Tecla(Protocolo.Texto(m, "k"));
                    if (vk == null) throw new ArgumentException("Tecla desconocida");
                    lista.Add(new Accion { Clase = Accion.Tipo.Tecla, Vk = vk.Value, Repetir = Protocolo.Entero(m, "n", 1, 1, Protocolo.MaxRepetir) });
                    return lista;
                }
                case "combo":
                {
                    string k = Protocolo.Texto(m, "k") ?? "";
                    var partes = k.Split('+');
                    if (partes.Length < 2 || partes.Length > 4) throw new ArgumentException("Combinación inválida");
                    var a = new Accion { Clase = Accion.Tipo.Combo };
                    for (int i = 0; i < partes.Length - 1; i++)
                    {
                        var mod = Modificador(partes[i]);
                        if (mod == null) throw new ArgumentException("Modificador inválido");
                        if (!a.Mods.Contains(mod.Value)) a.Mods.Add(mod.Value);
                    }
                    var tecla = Tecla(partes[partes.Length - 1]);
                    if (tecla == null) throw new ArgumentException("Tecla desconocida");
                    a.Vk = tecla.Value;
                    lista.Add(a);
                    return lista;
                }
                case "edicion":
                {
                    // Cambio dentro de lo escrito: el celular calcula la diferencia y aquí se aplica
                    // como ← izq, ⌫ borrar, texto nuevo y → der (el cursor vuelve al final).
                    int izq = Protocolo.Entero(m, "izq", 0, 0, Protocolo.MaxTexto);
                    int borrar = Protocolo.Entero(m, "borrar", 0, 0, Protocolo.MaxTexto);
                    string s = Protocolo.Texto(m, "s") ?? "";
                    if (s.Length > Protocolo.MaxTexto) throw new ArgumentException("Texto muy largo");
                    if (izq > 0) lista.Add(new Accion { Clase = Accion.Tipo.Tecla, Vk = VK_LEFT, Repetir = izq });
                    if (borrar > 0) lista.Add(new Accion { Clase = Accion.Tipo.Tecla, Vk = VK_BACK, Repetir = borrar });
                    if (s.Length > 0) lista.Add(new Accion { Clase = Accion.Tipo.Texto, Texto = s });
                    if (izq > 0) lista.Add(new Accion { Clase = Accion.Tipo.Tecla, Vk = VK_RIGHT, Repetir = izq });
                    return lista;
                }
                default:
                    return null;
            }
        }
    }
}
