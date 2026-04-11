using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AppMantenimiento
{
    public class DashboardRow
    {
        public string Nombre { get; set; }
        public string Ubicacion { get; set; }
        public string FrecuenciaTexto { get; set; }
        public string UltimaLectura { get; set; }
        public string RestanteTexto { get; set; }
        public string OperarioAsignado { get; set; }
        public string EstadoColor { get; set; }   // "Verde", "Naranja", "Rojo"
        public string EstadoTexto { get; set; }   // ← FIX: faltaba esta propiedad
    }

    public partial class DashboardWindow : Window
    {
        private List<DashboardRow> todos = new();
        private bool listo = false;
        private int umbralAviso = 50;
        private int umbralCritico = 10;

        public DashboardWindow()
        {
            InitializeComponent();
            TxtFechaHora.Text = DateTime.Now.ToString(
                "dddd, dd 'de' MMMM 'de' yyyy HH:mm",
                new System.Globalization.CultureInfo("es-ES"));
            listo = true;
            CargarDatos();

            this.Activated += (s, e) => { if (listo) CargarDatos(); };
        }

        private void CargarDatos()
        {
            if (!listo) return;

            int.TryParse(DatabaseHelper.LeerConfiguracion("UmbralAviso"), out int ua);
            int.TryParse(DatabaseHelper.LeerConfiguracion("UmbralCritico"), out int uc);
            umbralAviso = ua > 0 ? ua : 50;
            umbralCritico = uc > 0 ? uc : 10;

            try
            {
                var equipos = DatabaseHelper.GetEquipos().FindAll(e => e.Activo == 1);
                var asignaciones = DatabaseHelper.ObtenerAsignaciones();
                var operarios = DatabaseHelper.ObtenerOperarios();

                todos = new List<DashboardRow>();

                foreach (var eq in equipos)
                {
                    // ── Operario asignado ───────────────────────────────────
                    string nombreOp = "-";
                    var asig = asignaciones.FirstOrDefault(a => a.EquipoId == eq.Id);
                    if (asig != null)
                    {
                        var op = operarios.FirstOrDefault(o => o.Id == asig.OperarioId);
                        if (op != null) nombreOp = op.Nombre;
                    }

                    // ── Horas restantes y estado ────────────────────────────
                    string restanteTexto = "-";
                    string color = "Verde";
                    string estadoTexto = "Al día";
                    string frecuenciaTexto = "-";

                    if (eq.FrecuenciaHoras > 0)
                    {
                        double horasActuales = eq.HorasActuales;
                        double baseReset = DatabaseHelper.GetValorEnUltimoReset(eq.Id);
                        double delta = horasActuales - baseReset;
                        if (delta < 0) delta = horasActuales;

                        int restante = eq.FrecuenciaHoras - (int)delta;
                        frecuenciaTexto = $"Cada {eq.FrecuenciaHoras} h";

                        if (restante <= 0)
                        {
                            restanteTexto = $"VENCIDO ({Math.Abs(restante)} h)";
                            color = "Rojo";
                            estadoTexto = "🔴 Vencido";
                        }
                        else if (restante <= umbralCritico)
                        {
                            restanteTexto = $"{restante} h";
                            color = "Rojo";
                            estadoTexto = "🔴 Crítico";
                        }
                        else if (restante <= umbralAviso)
                        {
                            restanteTexto = $"{restante} h";
                            color = "Naranja";
                            estadoTexto = "🟠 Próximo";
                        }
                        else
                        {
                            restanteTexto = $"{restante} h";
                            color = "Verde";
                            estadoTexto = "✅ Al día";
                        }
                    }

                                        // ── Última lectura ──────────────────────────────────────
                    string ultimaLecturaTexto;
                    if (eq.HorasActuales > 0)
                        ultimaLecturaTexto = $"{eq.HorasActuales} h";
                    else
                        ultimaLecturaTexto = "Sin lectura";

                    todos.Add(new DashboardRow
                    {
                        Nombre = eq.Nombre,
                        Ubicacion = eq.Ubicacion ?? "-",
                        FrecuenciaTexto = frecuenciaTexto,
                        UltimaLectura = ultimaLecturaTexto,
                        RestanteTexto = restanteTexto,
                        OperarioAsignado = nombreOp,
                        EstadoColor = color,
                        EstadoTexto = estadoTexto
                    });
                }

                // FIX: ActualizarKpis y AplicarFiltro FUERA del foreach
                ActualizarKpis();
                AplicarFiltro();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar datos: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ActualizarKpis()
        {
            KpiTotal.Text = todos.Count.ToString();
            KpiVencidos.Text = todos.Count(r => r.EstadoColor == "Rojo").ToString();
            KpiProximos.Text = todos.Count(r => r.EstadoColor == "Naranja").ToString();
            KpiAlDia.Text = todos.Count(r => r.EstadoColor == "Verde").ToString();
        }

        private void AplicarFiltro()
        {
            if (!listo || GridDashboard == null) return;

            if (RbVencidos?.IsChecked == true)
                GridDashboard.ItemsSource = todos.Where(r => r.EstadoColor == "Rojo").ToList();
            else if (RbProximos?.IsChecked == true)
                GridDashboard.ItemsSource = todos.Where(r => r.EstadoColor == "Naranja").ToList();
            else if (RbAlDia?.IsChecked == true)
                GridDashboard.ItemsSource = todos.Where(r => r.EstadoColor == "Verde").ToList();
            else
                GridDashboard.ItemsSource = todos;
        }

        private void FiltroChanged(object sender, RoutedEventArgs e) => AplicarFiltro();

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            TxtFechaHora.Text = DateTime.Now.ToString(
                "dddd, dd 'de' MMMM 'de' yyyy HH:mm",
                new System.Globalization.CultureInfo("es-ES"));
            CargarDatos();
        }
    }
}