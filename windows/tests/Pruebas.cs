// Pruebas de El Mago (Windows) sin tocar el teclado real: mcs + mono.
//   mcs -r:System.Web.Extensions.dll -r:System.Security.dll -out:p.exe ../src/Protocolo.cs ../src/Config.cs ../src/Servidor.cs ../src/Teclado.cs Pruebas.cs && mono p.exe
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ElMago;

static class Pruebas
{
    const string VectorHmac = "f6ced6f4883ffc0981a6b9945819f680102b43097ad8ef7a0df9bde06fb3d2e4";
    static int ok, mal;
    static void Si(bool c, string nombre) { if (c) ok++; else { mal++; Console.WriteLine("FALLA: " + nombre); } }

    static int Main()
    {
        // Firma: vector fijo, el mismo que prueba la app Android (MagoProtocoloTest.kt)
        Si(Protocolo.Firma("123456", "abc") == VectorHmac, "firma igual a la de Android");
        Console.WriteLine("Vector HMAC(\"123456\",\"abc\") = " + Protocolo.Firma("123456", "abc"));
        Si(Protocolo.NuevoPin().Length == 6, "PIN de 6");
        Si(Protocolo.IgualesSeguro("ab", "ab") && !Protocolo.IgualesSeguro("ab", "ac") && !Protocolo.IgualesSeguro("ab", null), "comparación");
        Si(Protocolo.NombreSeguro("Moto\u0007rola\n", "x") == "Motorola", "nombre limpio");

        // Traductor
        var a = Traductor.Acciones(Protocolo.Leer("{\"t\":\"texto\",\"s\":\"Hola ñ 😀\"}"));
        Si(a.Count == 1 && a[0].Texto == "Hola ñ 😀", "texto con ñ y emoji");
        a = Traductor.Acciones(Protocolo.Leer("{\"t\":\"tecla\",\"k\":\"backspace\",\"n\":3}"));
        Si(a[0].Vk == Traductor.VK_BACK && a[0].Repetir == 3, "tecla repetida");
        a = Traductor.Acciones(Protocolo.Leer("{\"t\":\"tecla\",\"k\":\"left\",\"n\":99999}"));
        Si(a[0].Repetir == Protocolo.MaxRepetir, "tope de repeticiones");
        a = Traductor.Acciones(Protocolo.Leer("{\"t\":\"combo\",\"k\":\"ctrl+shift+z\"}"));
        Si(a[0].Mods.Count == 2 && a[0].Vk == 'Z', "combo");
        bool lanzo = false;
        try { Traductor.Acciones(Protocolo.Leer("{\"t\":\"combo\",\"k\":\"hack+z\"}")); } catch (ArgumentException) { lanzo = true; }
        Si(lanzo, "combo inválido");
        a = Traductor.Acciones(Protocolo.Leer("{\"t\":\"edicion\",\"izq\":2,\"borrar\":1,\"s\":\"é\"}"));
        Si(a.Count == 4 && a[0].Vk == Traductor.VK_LEFT && a[1].Vk == Traductor.VK_BACK && a[2].Texto == "é" && a[3].Vk == Traductor.VK_RIGHT, "edición en medio");
        Si(Traductor.Acciones(Protocolo.Leer("{\"t\":\"otra\"}")) == null, "tipo desconocido");
        Si(Protocolo.Leer("no es json") == null && Protocolo.Leer("[1,2]") == null, "json malo");

        // Freno de PIN
        var f = new FrenoPin(); var t0 = new DateTime(2026, 1, 1);
        for (int i = 0; i < 5; i++) f.Fallo(t0);
        Si(f.Bloqueado(t0.AddSeconds(30)) && !f.Bloqueado(t0.AddSeconds(61)), "freno de PIN");

        // Servidor real por TCP con teclado simulado
        Config.Proteger = s => "P:" + s; Config.Desproteger = s => s.Substring(2);
        string ruta = Path.Combine(Path.GetTempPath(), "mago-prueba-" + Guid.NewGuid().ToString("N"), "config.json");
        var cfg = Config.Cargar(ruta);
        Si(File.Exists(ruta) && cfg.Pin.Length == 6, "config creada");
        var srv = new Servidor(cfg);
        var escrito = new List<string>();
        srv.Escribir = acc => { lock (escrito) escrito.Add(acc.ToString() + (acc.Texto != null ? ":" + acc.Texto : "")); };
        srv.Ventana = () => "Bloc de notas";
        srv.Registro += s => Console.WriteLine("  [srv] " + s);
        int puerto = 48800 + new Random().Next(500);
        srv.Iniciar(puerto, puerto + 1);

        // PIN malo
        var r = Sesion(puerto, "par", "telefono-1234", "000000", null);
        Si(r[0] == "rechazo", "PIN malo rechazado");
        // Emparejar
        string llave = null;
        r = Sesion(puerto, "par", "telefono-1234", cfg.Pin, c =>
        {
            Enviar(c, "{\"t\":\"texto\",\"s\":\"Hola\"}");
            Enviar(c, "{\"t\":\"ping\"}");
        });
        Si(r[0] == "ok", "emparejado");
        llave = r[1];
        Si(llave != null && llave.Length == 64, "llave de 64 hex");
        Si(r[2].Contains("Bloc de notas"), "pong con ventana");
        Thread.Sleep(100);
        lock (escrito) Si(escrito.Count == 1 && escrito[0] == "Texto(4):Hola", "texto escrito");
        // Reconectar con la llave
        r = Sesion(puerto, "auth", "telefono-1234", llave, c =>
        {
            Enviar(c, "{\"t\":\"tecla\",\"k\":\"enter\"}");
            Enviar(c, "{\"t\":\"ping\"}");
        });
        Si(r[0] == "ok", "auth con llave");
        Thread.Sleep(100);
        lock (escrito) Si(escrito.Count == 2, "tecla escrita");
        // Llave mala
        r = Sesion(puerto, "auth", "telefono-1234", "otra", null);
        Si(r[0] == "rechazo", "llave mala rechazada");
        // Pausa
        cfg.Pausado = true;
        r = Sesion(puerto, "auth", "telefono-1234", llave, c => { Enviar(c, "{\"t\":\"texto\",\"s\":\"x\"}"); });
        Si(r.Count > 1 && r[1] == "error", "pausa avisa");
        cfg.Pausado = false;
        // Olvidado
        cfg.Olvidar("telefono-1234");
        r = Sesion(puerto, "auth", "telefono-1234", llave, null);
        Si(r[0] == "rechazo", "olvidado ya no entra");
        // Descubrimiento UDP
        using (var u = new UdpClient())
        {
            u.Client.ReceiveTimeout = 2000;
            var b = Encoding.UTF8.GetBytes(Protocolo.Pregunta);
            u.Send(b, b.Length, "127.0.0.1", puerto + 1);
            var ep = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            var resp = Protocolo.Leer(Encoding.UTF8.GetString(u.Receive(ref ep)));
            Si(Protocolo.Texto(resp, "t") == "pc" && Protocolo.Entero(resp, "puerto", 0, 0, 70000) == puerto, "descubrimiento UDP");
        }
        // Línea gigante: el servidor corta sin caerse
        try
        {
            using (var c = new TcpClient("127.0.0.1", puerto))
            {
                var s = c.GetStream();
                var big = Encoding.UTF8.GetBytes(new string('a', Protocolo.MaxLinea + 10));
                s.Write(big, 0, big.Length);
            }
        }
        catch { }
        r = Sesion(puerto, "par", "telefono-5678", cfg.Pin, null);
        Si(r[0] == "ok", "sigue vivo tras abuso");

        srv.Detener();
        Console.WriteLine(ok + " OK, " + mal + " fallas");
        return mal == 0 ? 0 : 1;
    }

    static void Enviar(StreamWriter w, string s) { w.WriteLine(s); }

    /// <summary>Se conecta como el celular. Devuelve: [t de respuesta auth, llave?, respuestas siguientes...]</summary>
    static List<string> Sesion(int puerto, string tipo, string id, string clave, Action<StreamWriter> cuerpo)
    {
        var res = new List<string>();
        using (var c = new TcpClient("127.0.0.1", puerto))
        {
            c.ReceiveTimeout = 2000;
            var s = c.GetStream();
            var lr = new StreamReader(s, new UTF8Encoding(false));
            var w = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var reto = Protocolo.Leer(lr.ReadLine());
            string n = Protocolo.Texto(reto, "n");
            w.WriteLine("{\"t\":\"" + tipo + "\",\"id\":\"" + id + "\",\"nombre\":\"Motorola\",\"h\":\"" + Protocolo.Firma(clave, n) + "\"}");
            var resp = Protocolo.Leer(lr.ReadLine());
            res.Add(Protocolo.Texto(resp, "t"));
            if (tipo == "par") res.Add(Protocolo.Texto(resp, "llave"));
            if (res[0] != "ok" || cuerpo == null) return res;
            cuerpo(w);
            try
            {
                string l;
                while ((l = lr.ReadLine()) != null)
                {
                    var m = Protocolo.Leer(l);
                    res.Add(Protocolo.Texto(m, "t") == "pong" ? "pong:" + Protocolo.Texto(m, "ventana") : Protocolo.Texto(m, "t"));
                    if (res.Count >= (tipo == "par" ? 3 : 2)) break;
                }
            }
            catch (IOException) { }
        }
        return res;
    }
}
