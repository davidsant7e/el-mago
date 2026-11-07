// I · El Mago — ventana, ícono junto al reloj y arranque.
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace ElMago
{
    public class Ventana : Form
    {
        static readonly Color Noche = Color.FromArgb(0x1f, 0x2a, 0x5c);
        static readonly Color NocheClara = Color.FromArgb(0x2b, 0x38, 0x73);
        static readonly Color Durazno = Color.FromArgb(0xf4, 0xa5, 0x82);
        static readonly Color Crema = Color.FromArgb(0xfb, 0xe9, 0xdf);

        readonly Config cfg;
        readonly Servidor srv;
        readonly NotifyIcon bandeja = new NotifyIcon();
        readonly Label lblEstado = new Label(), lblPin = new Label(), lblRed = new Label();
        readonly ListBox lstDisp = new ListBox();
        readonly TextBox txtLog = new TextBox();
        readonly CheckBox chkPausa = new CheckBox();
        readonly ToolStripMenuItem menuPausa = new ToolStripMenuItem("Pausar");
        bool salir, avisado;

        public Ventana(Config c, Servidor s, Icon icono)
        {
            cfg = c; srv = s;
            Text = "El Mago — teclado desde el celular";
            Icon = icono;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 600);
            BackColor = Noche;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10f);

            int y = 16;
            Agregar(Etiqueta("I · EL MAGO", 18f, Durazno, true), 20, ref y, 34);
            Agregar(Etiqueta("Escribe en este PC desde tu celular, por el Wi-Fi de la casa.", 9.5f, Crema, false), 20, ref y, 30);

            Agregar(Etiqueta("ESTE PC", 8.5f, Durazno, true), 20, ref y, 20);
            lblRed.ForeColor = Color.White; lblRed.Font = new Font("Segoe UI", 10f); lblRed.AutoSize = false;
            Agregar(lblRed, 20, ref y, 62);

            Agregar(Etiqueta("PIN PARA EMPAREJAR UN CELULAR", 8.5f, Durazno, true), 20, ref y, 20);
            lblPin.Font = new Font("Consolas", 26f, FontStyle.Bold); lblPin.ForeColor = Durazno; lblPin.AutoSize = false;
            lblPin.SetBounds(20, y, 230, 46); Controls.Add(lblPin);
            var btnPin = Boton("Nuevo PIN", false);
            btnPin.SetBounds(270, y + 6, 150, 34);
            btnPin.Click += (o, e) => { cfg.CambiarPin(); Refrescar(); };
            Controls.Add(btnPin);
            y += 54;

            Agregar(Etiqueta("ESTADO", 8.5f, Durazno, true), 20, ref y, 20);
            lblEstado.Font = new Font("Segoe UI", 11f, FontStyle.Bold); lblEstado.AutoSize = false;
            Agregar(lblEstado, 20, ref y, 28);

            chkPausa.Text = "Pausar: no escribir nada que llegue del celular";
            chkPausa.ForeColor = Crema; chkPausa.AutoSize = false;
            chkPausa.CheckedChanged += (o, e) => CambiarPausa(chkPausa.Checked);
            Agregar(chkPausa, 20, ref y, 30);

            Agregar(Etiqueta("CELULARES EMPAREJADOS", 8.5f, Durazno, true), 20, ref y, 20);
            lstDisp.BackColor = NocheClara; lstDisp.ForeColor = Color.White; lstDisp.BorderStyle = BorderStyle.None;
            lstDisp.SetBounds(20, y, 270, 70); Controls.Add(lstDisp);
            btnOlvidar = Boton("Olvidar", false);
            relojOlvidar.Tick += (o, e) => CancelarOlvidar();
            lstDisp.SelectedIndexChanged += (o, e) => { if (porOlvidar != null) CancelarOlvidar(); };
            btnOlvidar.SetBounds(300, y, 120, 34);
            btnOlvidar.Click += (o, e) => Olvidar();
            Controls.Add(btnOlvidar);
            y += 80;

            Agregar(Etiqueta("REGISTRO", 8.5f, Durazno, true), 20, ref y, 20);
            txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = NocheClara; txtLog.ForeColor = Crema; txtLog.BorderStyle = BorderStyle.None;
            txtLog.Font = new Font("Consolas", 8.5f);
            txtLog.SetBounds(20, y, 400, ClientSize.Height - y - 16); Controls.Add(txtLog);

            // Ícono junto al reloj
            bandeja.Icon = icono;
            bandeja.Text = "El Mago";
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir", null, (o, e) => Mostrar());
            menuPausa.Click += (o, e) => CambiarPausa(!cfg.Pausado);
            menu.Items.Add(menuPausa);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (o, e) => { salir = true; Close(); });
            bandeja.ContextMenuStrip = menu;
            bandeja.DoubleClick += (o, e) => Mostrar();
            bandeja.Visible = true;

            lock (srv.Historial) txtLog.Lines = srv.Historial.ToArray();
            srv.Registro += linea => EnUi(() => AgregarLog(linea));
            srv.CambioConexiones += () => EnUi(() => { Refrescar(); AvisarConexion(); });
            NetworkChange.NetworkAddressChanged += (o, e) => EnUi(Refrescar);

            var reloj = new System.Windows.Forms.Timer { Interval = 30000 };
            reloj.Tick += (o, e) => Refrescar();
            reloj.Start();
            Refrescar();
        }

        void EnUi(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        Label Etiqueta(string t, float tam, Color color, bool negrita)
        {
            return new Label { Text = t, ForeColor = color, AutoSize = false, Font = new Font("Segoe UI", tam, negrita ? FontStyle.Bold : FontStyle.Regular) };
        }

        Button Boton(string t, bool principal)
        {
            var b = new Button { Text = t, FlatStyle = FlatStyle.Flat, ForeColor = principal ? Noche : Durazno, BackColor = principal ? Durazno : Noche, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Durazno;
            return b;
        }

        void Agregar(Control c, int x, ref int y, int alto)
        {
            c.SetBounds(x, y, ClientSize.Width - 2 * x, alto);
            Controls.Add(c);
            y += alto;
        }

        void AgregarLog(string s)
        {
            var lineas = txtLog.Lines.ToList();
            lineas.Add(s);
            if (lineas.Count > 80) lineas.RemoveRange(0, lineas.Count - 80);
            txtLog.Lines = lineas.ToArray();
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        public static string[] Direcciones()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254"))
                        .Select(a => a.Address + (n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "  (Wi-Fi)" : "  (" + n.Name + ")")))
                    .ToArray();
            }
            catch { return new string[0]; }
        }

        void Refrescar()
        {
            var ips = Direcciones();
            lblRed.Text = Environment.MachineName + "\n" + (ips.Length == 0 ? "Sin red: conecta el PC al Wi-Fi." : string.Join("\n", ips.Take(2)));
            lblPin.Text = cfg.Pin.Substring(0, 3) + " " + cfg.Pin.Substring(3);
            string[] con;
            lock (srv.Conectados) con = srv.Conectados.ToArray();
            if (cfg.Pausado) { lblEstado.Text = "En pausa"; lblEstado.ForeColor = Durazno; }
            else if (con.Length > 0) { lblEstado.Text = "● Conectado: " + string.Join(", ", con); lblEstado.ForeColor = Color.FromArgb(0x9d, 0xe0, 0xad); }
            else { lblEstado.Text = "○ Esperando al celular"; lblEstado.ForeColor = Crema; }
            chkPausa.Checked = cfg.Pausado;
            menuPausa.Checked = cfg.Pausado;
            bandeja.Text = "El Mago — " + (cfg.Pausado ? "en pausa" : con.Length > 0 ? "conectado" : "esperando");
            var sel = lstDisp.SelectedIndex;
            lstDisp.Items.Clear();
            foreach (var d in cfg.Dispositivos.ToList().OrderByDescending(d => d.Ultima.ToUniversalTime()))
                lstDisp.Items.Add(new ItemDisp { D = d });
            if (sel >= 0 && sel < lstDisp.Items.Count) lstDisp.SelectedIndex = sel;
        }

        class ItemDisp
        {
            public Dispositivo D;
            public override string ToString() { return D.Nombre + "   · " + D.Ultima.ToLocalTime().ToString("dd/MM HH:mm"); }
        }

        void AvisarConexion()
        {
            int n;
            lock (srv.Conectados) n = srv.Conectados.Count;
            if (n > 0 && !avisado && !Visible)
            {
                avisado = true;
                bandeja.ShowBalloonTip(3000, "El Mago", "Celular conectado: lo que escribas allí aparece aquí.", ToolTipIcon.Info);
            }
            if (n == 0) avisado = false;
        }

        void CambiarPausa(bool p)
        {
            if (cfg.Pausado == p) return;
            cfg.Pausado = p;
            cfg.Guardar();
            Refrescar();
        }

        Button btnOlvidar;
        Dispositivo porOlvidar;
        readonly System.Windows.Forms.Timer relojOlvidar = new System.Windows.Forms.Timer { Interval = 4000 };

        /// <summary>Primer clic: pide confirmar en el mismo botón (4 s). Segundo clic: olvida.</summary>
        void Olvidar()
        {
            var it = lstDisp.SelectedItem as ItemDisp;
            if (it == null) { AgregarLog(DateTime.Now.ToString("HH:mm:ss") + "  Elige un celular de la lista para olvidarlo."); return; }
            if (porOlvidar == null || porOlvidar.Id != it.D.Id)
            {
                porOlvidar = it.D;
                btnOlvidar.Text = "¿Seguro?";
                btnOlvidar.BackColor = Durazno; btnOlvidar.ForeColor = Noche;
                relojOlvidar.Stop(); relojOlvidar.Start();
                return;
            }
            cfg.Olvidar(porOlvidar.Id);
            AgregarLog(DateTime.Now.ToString("HH:mm:ss") + "  Olvidado: " + porOlvidar.Nombre + ". Tendrá que emparejarse otra vez con el PIN.");
            CancelarOlvidar();
            Refrescar();
        }

        void CancelarOlvidar()
        {
            relojOlvidar.Stop();
            porOlvidar = null;
            btnOlvidar.Text = "Olvidar";
            btnOlvidar.BackColor = Noche; btnOlvidar.ForeColor = Durazno;
        }

        void Mostrar()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!salir && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;   // la X la esconde; se sale desde el ícono del reloj
                Hide();
                return;
            }
            bandeja.Visible = false;
            srv.Detener();
            base.OnFormClosing(e);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool nuevo;
            using (var mutex = new Mutex(true, "Local\\ElMago-davidsant7e", out nuevo))
            {
                if (!nuevo) { MessageBox.Show("El Mago ya está abierto (ícono junto al reloj).", "El Mago"); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var cfg = Config.Cargar(Config.RutaPorDefecto());
                var srv = new Servidor(cfg);
                try { srv.Iniciar(Protocolo.PuertoTcp, Protocolo.PuertoDescubrir); }
                catch (SocketException e)
                {
                    MessageBox.Show("No se pudo abrir el puerto " + Protocolo.PuertoTcp + ": " + e.Message +
                        "\n\n¿Hay otro El Mago abierto con otro usuario?", "El Mago");
                    return;
                }

                Icon icono = SystemIcons.Application;
                try
                {
                    string ico = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "mago.ico");
                    if (File.Exists(ico)) icono = new Icon(ico);
                }
                catch { }

                var v = new Ventana(cfg, srv, icono);
                bool oculto = args.Any(a => a == "--oculto");
                if (oculto) { v.WindowState = FormWindowState.Minimized; v.ShowInTaskbar = false; v.Load += (o, e) => { v.Hide(); v.ShowInTaskbar = true; v.WindowState = FormWindowState.Normal; }; }
                Application.Run(v);
            }
        }
    }
}
