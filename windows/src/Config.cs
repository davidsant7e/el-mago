// I · El Mago — ajustes y celulares emparejados (%APPDATA%\ElMago\config.json).
// Las llaves de los celulares se guardan cifradas con DPAPI (solo tu usuario de Windows las lee).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ElMago
{
    public class Dispositivo
    {
        public string Id { get; set; }
        public string Nombre { get; set; }
        public string Llave { get; set; }     // cifrada (base64) en disco
        public DateTime Ultima { get; set; }
    }

    public class Config
    {
        public string Pin { get; set; }
        public string IdPc { get; set; }
        public bool Pausado { get; set; }
        public List<Dispositivo> Dispositivos { get; set; }

        public Config() { Dispositivos = new List<Dispositivo>(); }

        [ScriptIgnore] public string Ruta;

        /// <summary>Cifrado de las llaves; en pruebas se cambia por uno de mentira.</summary>
        public static Func<string, string> Proteger = s =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));
        public static Func<string, string> Desproteger = s =>
            Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser));

        public static string RutaPorDefecto()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ElMago", "config.json");
        }

        public static Config Cargar(string ruta)
        {
            Config c = null;
            try
            {
                if (File.Exists(ruta)) c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(ruta, Encoding.UTF8));
            }
            catch
            {
                try { File.Copy(ruta, ruta + ".dañado", true); } catch { }
                c = null;
            }
            if (c == null) c = new Config();
            if (c.Dispositivos == null) c.Dispositivos = new List<Dispositivo>();
            c.Ruta = ruta;
            bool cambio = false;
            if (string.IsNullOrEmpty(c.Pin) || c.Pin.Length != 6 || !c.Pin.All(char.IsDigit)) { c.Pin = Protocolo.NuevoPin(); cambio = true; }
            if (string.IsNullOrEmpty(c.IdPc)) { c.IdPc = Protocolo.Aleatorio(8); cambio = true; }
            if (cambio) c.Guardar();
            return c;
        }

        public void Guardar()
        {
            lock (this)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
                string tmp = Ruta + ".tmp";
                File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
                if (File.Exists(Ruta)) File.Replace(tmp, Ruta, null); else File.Move(tmp, Ruta);
            }
        }

        public Dispositivo Buscar(string id)
        {
            lock (this) return Dispositivos.FirstOrDefault(d => d.Id == id);
        }

        /// <summary>Llave en claro del celular, o null si no está emparejado o no se puede leer.</summary>
        public string LlaveDe(string id)
        {
            var d = Buscar(id);
            if (d == null || string.IsNullOrEmpty(d.Llave)) return null;
            try { return Desproteger(d.Llave); } catch { return null; }
        }

        /// <summary>Empareja (o vuelve a emparejar) un celular y devuelve su llave nueva.</summary>
        public string Emparejar(string id, string nombre)
        {
            string llave = Protocolo.Aleatorio(32);
            lock (this)
            {
                Dispositivos.RemoveAll(d => d.Id == id);
                Dispositivos.Add(new Dispositivo { Id = id, Nombre = nombre, Llave = Proteger(llave), Ultima = DateTime.Now });
            }
            Guardar();
            return llave;
        }

        public void Olvidar(string id)
        {
            lock (this) Dispositivos.RemoveAll(d => d.Id == id);
            Guardar();
        }

        public void Visto(string id, string nombre)
        {
            var d = Buscar(id);
            if (d == null) return;
            lock (this) { d.Ultima = DateTime.Now; if (!string.IsNullOrEmpty(nombre)) d.Nombre = nombre; }
            Guardar();
        }

        public void CambiarPin()
        {
            Pin = Protocolo.NuevoPin();
            Guardar();
        }
    }
}
