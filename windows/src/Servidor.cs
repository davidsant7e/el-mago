// I · El Mago — escucha al celular en la red Wi-Fi de la casa.
//   TCP 47800: una línea JSON por mensaje (UTF-8).
//   UDP 47801: el celular pregunta "MAGO?v1" por difusión y el PC responde con su nombre.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ElMago
{
    public class Servidor
    {
        readonly Config cfg;
        readonly FrenoPin freno = new FrenoPin();
        readonly object teclado = new object();
        TcpListener tcp;
        UdpClient udp;
        volatile bool activo;
        readonly List<TcpClient> clientes = new List<TcpClient>();

        /// <summary>Quién está conectado ahora (para la ventana y el ícono).</summary>
        public event Action<string> Registro;
        public event Action CambioConexiones;

        /// <summary>Ejecuta una acción de teclado. En pruebas se reemplaza para no tocar el teclado real.</summary>
        public Action<Accion> Escribir = Teclado.Ejecutar;
        public Func<string> Ventana = Teclado.VentanaActiva;

        public readonly List<string> Conectados = new List<string>();

        public Servidor(Config c) { cfg = c; }

        /// <summary>Últimas líneas del registro (la ventana las muestra aunque se abra después).</summary>
        public readonly List<string> Historial = new List<string>();

        void Log(string s)
        {
            string linea = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
            lock (Historial) { Historial.Add(linea); if (Historial.Count > 80) Historial.RemoveAt(0); }
            var r = Registro; if (r != null) r(linea);
        }

        public void Iniciar(int puertoTcp, int puertoUdp)
        {
            activo = true;
            tcp = new TcpListener(IPAddress.Any, puertoTcp);
            tcp.Start();
            new Thread(Aceptar) { IsBackground = true, Name = "Mago-TCP" }.Start();
            try
            {
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, puertoUdp));
                new Thread(Descubrir) { IsBackground = true, Name = "Mago-UDP" }.Start();
            }
            catch (Exception e) { Log("Sin búsqueda automática (UDP " + puertoUdp + "): " + e.Message + ". En el celular escribe la IP a mano."); }
            Log("Escuchando en el puerto " + puertoTcp + ".");
        }

        public void Detener()
        {
            activo = false;
            try { tcp.Stop(); } catch { }
            try { if (udp != null) udp.Close(); } catch { }
            lock (clientes) { foreach (var c in clientes) try { c.Close(); } catch { } clientes.Clear(); }
        }

        void Descubrir()
        {
            while (activo)
            {
                try
                {
                    var origen = new IPEndPoint(IPAddress.Any, 0);
                    byte[] datos = udp.Receive(ref origen);
                    if (Encoding.UTF8.GetString(datos).Trim() != Protocolo.Pregunta) continue;
                    var m = Protocolo.Msg("pc");
                    m["nombre"] = Environment.MachineName;
                    m["id"] = cfg.IdPc;
                    m["puerto"] = ((IPEndPoint)tcp.LocalEndpoint).Port;
                    m["v"] = Protocolo.Version;
                    byte[] r = Encoding.UTF8.GetBytes(Protocolo.ACadena(m));
                    udp.Send(r, r.Length, origen);
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (!activo) return; }
                catch (Exception) { }
            }
        }

        void Aceptar()
        {
            while (activo)
            {
                TcpClient c;
                try { c = tcp.AcceptTcpClient(); }
                catch { if (!activo) return; continue; }
                lock (clientes)
                {
                    if (clientes.Count >= 4) { try { c.Close(); } catch { } continue; }
                    clientes.Add(c);
                }
                new Thread(() => Atender(c)) { IsBackground = true, Name = "Mago-cliente" }.Start();
            }
        }

        /// <summary>Lee una línea con tope de tamaño (para que nadie llene la memoria).</summary>
        static string LeerLinea(Stream s, Decoder dec, StringBuilder buf, byte[] uno, char[] car)
        {
            buf.Length = 0;
            while (true)
            {
                int n = s.Read(uno, 0, 1);
                if (n <= 0) return buf.Length > 0 ? buf.ToString() : null;
                int k = dec.GetChars(uno, 0, 1, car, 0);
                for (int i = 0; i < k; i++)
                {
                    if (car[i] == '\n') return buf.ToString();
                    buf.Append(car[i]);
                }
                if (buf.Length > Protocolo.MaxLinea) throw new IOException("Mensaje demasiado largo");
            }
        }

        void Atender(TcpClient c)
        {
            string quien = null;
            string ip = "?";
            try
            {
                ip = ((IPEndPoint)c.Client.RemoteEndPoint).Address.ToString();
                c.NoDelay = true;
                var red = c.GetStream();
                var lector = new BufferedStream(red, 4096);
                var dec = new UTF8Encoding(false).GetDecoder();
                var buf = new StringBuilder();
                byte[] uno = new byte[1];
                char[] car = new char[2];
                var esc = new StreamWriter(red, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                Action<Dictionary<string, object>> enviar = m => { lock (esc) esc.WriteLine(Protocolo.ACadena(m)); };

                // 1) Reto: el celular debe firmarlo con su llave (o con el PIN si se empareja).
                c.ReceiveTimeout = 15000;
                string reto = Protocolo.Aleatorio(16);
                var hola = Protocolo.Msg("reto");
                hola["n"] = reto; hola["pc"] = Environment.MachineName; hola["id"] = cfg.IdPc; hola["v"] = Protocolo.Version;
                enviar(hola);

                var auth = Protocolo.Leer(LeerLinea(lector, dec, buf, uno, car));
                string tipo = Protocolo.Texto(auth, "t");
                string id = Protocolo.NombreSeguro(Protocolo.Texto(auth, "id"), "");
                string nombre = Protocolo.NombreSeguro(Protocolo.Texto(auth, "nombre"), "Celular");
                string firma = Protocolo.Texto(auth, "h");
                if (id.Length < 8) { Rechazar(enviar, "Mensaje inválido"); return; }

                if (tipo == "auth")
                {
                    string llave = cfg.LlaveDe(id);
                    if (llave == null) { Rechazar(enviar, "no-emparejado"); Log(nombre + " (" + ip + ") ya no está emparejado."); return; }
                    if (!Protocolo.IgualesSeguro(Protocolo.Firma(llave, reto), firma)) { Rechazar(enviar, "llave-invalida"); Log(nombre + ": llave inválida."); return; }
                    cfg.Visto(id, nombre);
                    enviar(Protocolo.Msg("ok"));
                }
                else if (tipo == "par")
                {
                    var ahora = DateTime.Now;
                    if (freno.Bloqueado(ahora)) { Rechazar(enviar, "Demasiados intentos. Espera un minuto."); return; }
                    if (!Protocolo.IgualesSeguro(Protocolo.Firma(cfg.Pin, reto), firma))
                    {
                        freno.Fallo(ahora);
                        Rechazar(enviar, "PIN incorrecto");
                        Log("PIN incorrecto desde " + ip + " (" + nombre + ").");
                        return;
                    }
                    freno.Exito();
                    string llave = cfg.Emparejar(id, nombre);
                    var ok = Protocolo.Msg("ok");
                    ok["llave"] = llave;
                    enviar(ok);
                    Log("Emparejado: " + nombre + " (" + ip + ").");
                }
                else { Rechazar(enviar, "Mensaje inválido"); return; }

                quien = nombre;
                lock (Conectados) Conectados.Add(nombre);
                Avisar();
                Log("Conectado: " + nombre + " (" + ip + ").");

                // 2) Mensajes. El celular hace ping cada pocos segundos; 45 s sin nada = se fue.
                c.ReceiveTimeout = 45000;
                while (activo)
                {
                    string linea = LeerLinea(lector, dec, buf, uno, car);
                    if (linea == null) break;
                    var m = Protocolo.Leer(linea);
                    string t = Protocolo.Texto(m, "t");
                    if (t == null) continue;
                    if (t == "ping")
                    {
                        var p = Protocolo.Msg("pong");
                        p["ventana"] = Ventana();
                        p["pausado"] = cfg.Pausado;
                        enviar(p);
                        continue;
                    }
                    if (t == "adios") break;
                    if (cfg.Buscar(id) == null) { Rechazar(enviar, "no-emparejado"); break; } // lo olvidaron desde el PC
                    List<Accion> acciones;
                    try { acciones = Traductor.Acciones(m); }
                    catch (ArgumentException e) { Error(enviar, e.Message); continue; }
                    if (acciones == null) continue; // tipo desconocido: se ignora (versiones futuras)
                    if (cfg.Pausado) { Error(enviar, "El Mago está en pausa en el PC."); continue; }
                    try { lock (teclado) foreach (var a in acciones) Escribir(a); }
                    catch (Exception e) { Error(enviar, e.Message); }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception e) { Log("Error con " + ip + ": " + e.Message); }
            finally
            {
                try { c.Close(); } catch { }
                lock (clientes) clientes.Remove(c);
                if (quien != null)
                {
                    lock (Conectados) Conectados.Remove(quien);
                    Avisar();
                    Log("Desconectado: " + quien + ".");
                }
            }
        }

        static void Rechazar(Action<Dictionary<string, object>> enviar, string motivo)
        {
            var e = Protocolo.Msg("rechazo"); e["m"] = motivo;
            try { enviar(e); } catch { }
        }

        static void Error(Action<Dictionary<string, object>> enviar, string motivo)
        {
            var e = Protocolo.Msg("error"); e["m"] = motivo;
            try { enviar(e); } catch { }
        }

        void Avisar() { var a = CambioConexiones; if (a != null) a(); }
    }
}
