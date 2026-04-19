using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AppMantenimiento
{
    public partial class LecturasWindow : Window
    {
        private List<Lectura> _todasLecturas = new();

        public LecturasWindow()
        {
            InitializeComponent();

            // Forzar estado inicial correcto DESPUÉS de que los controles existen
            PanelReset.Visibility = Visibility.Collapsed;
            LblValor.Text = "VALOR (h / km)";
            LblDescripcion.Text = "DESCRIPCIÓN (opcional)";

            CargarEquipos();
            CargarLecturas();
        }

        // ── Carga ──────────────────────────────────────────────────────────
        private void CargarEquipos()
        {
            var equipos = DatabaseHelper.GetEquipos();

            CmbEquipo.ItemsSource = equipos;
            if (CmbEquipo.Items.Count > 0) CmbEquipo.SelectedIndex = 0;

            var listaFiltro = new List<Equipo>
                { new Equipo { Id = -1, Nombre = "Todas las máquinas" } };
            listaFiltro.AddRange(equipos);
            CmbFiltroEquipo.ItemsSource = listaFiltro;
            CmbFiltroEquipo.SelectedIndex = 0;
        }

        private void CargarLecturas()
        {
            _todasLecturas = DatabaseHelper.ObtenerLecturas();
            AplicarFiltros();
        }

        // ── Filtros ────────────────────────────────────────────────────────
        private void AplicarFiltros()
        {
            if (GridLecturas == null) return;

            var resultado = _todasLecturas.AsEnumerable();

            if (CmbFiltroEquipo?.SelectedItem is Equipo eq && eq.Id != -1)
                resultado = resultado.Where(l => l.EquipoId == eq.Id);

            var tipo = (CmbFiltroTipo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            resultado = tipo switch
            {
                "Solo lecturas" => resultado.Where(l => l.Tipo == "Lectura"),
                "Solo mantenimientos" => resultado.Where(l => l.Tipo == "Mantenimiento"),
                _ => resultado
            };

            var lista = resultado.ToList();
            GridLecturas.ItemsSource = lista;
            TxtContador.Text = $"{lista.Count} registros";
            TxtCosteTotal.Text = $"{lista.Sum(l => l.Coste):N2} €";
        }

        private void Filtro_Changed(object sender, SelectionChangedEventArgs e)
            => AplicarFiltros();

        // ── Cambio de tipo (Lectura / Mantenimiento) ───────────────────────
        private void TipoRegistro_Changed(object sender, RoutedEventArgs e)
        {
            // Puede dispararse antes de InitializeComponent — ignorar
            if (PanelReset == null || LblValor == null) return;

            bool esMant = RbMantenimiento?.IsChecked == true;

            LblValor.Text = esMant ? "LECTURA ACTUAL (opcional)" : "VALOR (h / km)";
            LblDescripcion.Text = esMant ? "DESCRIPCIÓN DEL TRABAJO REALIZADO *"
                                         : "DESCRIPCIÓN (opcional)";
            PanelReset.Visibility = esMant ? Visibility.Visible : Visibility.Collapsed;

            if (!esMant && ChkResetContador != null)
                ChkResetContador.IsChecked = false;
        }

        // ── Registrar ──────────────────────────────────────────────────────
        private void BtnRegistrar_Click(object sender, RoutedEventArgs e)
        {
            if (CmbEquipo.SelectedItem is not Equipo equipo)
            {
                MessageBox.Show("Selecciona un equipo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool esMantenimiento = RbMantenimiento.IsChecked == true;

            if (esMantenimiento && string.IsNullOrWhiteSpace(TxtDescripcion.Text))
            {
                MessageBox.Show("Escribe la descripción del trabajo realizado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string valorStr = "";
            double horasAct = 0;

            if (!esMantenimiento && !string.IsNullOrWhiteSpace(TxtValor.Text))
            {
                if (!double.TryParse(TxtValor.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out horasAct))
                {
                    MessageBox.Show("El valor debe ser un número (ej: 1250 o 1250.5).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                valorStr = horasAct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            }

            var lectura = new Lectura
            {
                EquipoId = equipo.Id,
                NombreEquipo = equipo.Nombre,
                Tipo = esMantenimiento ? "Mantenimiento" : "Lectura",
                Valor = valorStr,
                Descripcion = TxtDescripcion.Text.Trim(),
                Operario = "Supervisor",
                Proveedor = TxtProveedor.Text.Trim(),
                Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };

            DatabaseHelper.AgregarLectura(lectura);

            // Sincronizar HorasActuales en Equipos para que el Dashboard lo recoja
            if (!esMantenimiento && horasAct > 0)
                DatabaseHelper.ActualizarHorasEquipo(equipo.Id, horasAct);

            TxtValor.Clear();
            TxtDescripcion.Clear();
            TxtProveedor.Clear();
            TxtCoste.Text = "0";
            CargarLecturas();

            MessageBox.Show($"Registro guardado para {equipo.Nombre}.", "Guardado",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Reset contador ─────────────────────────────────────────────────
        private void BtnResetContador_Click(object sender, RoutedEventArgs e)
        {
            if (CmbEquipo.SelectedItem is not Equipo equipo)
            { Aviso("Selecciona el equipo."); return; }

            if (ChkResetContador.IsChecked != true)
            { Aviso("Marca la casilla de confirmación antes de resetear."); return; }

            var res = MessageBox.Show(
                $"¿Confirmas que el mantenimiento de '{equipo.Nombre}' se ha completado?\n" +
                "Se reseteará el contador del equipo.",
                "Confirmar reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            double coste = 0;
            if (!string.IsNullOrWhiteSpace(TxtCoste.Text) && TxtCoste.Text != "0")
                double.TryParse(TxtCoste.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out coste);

            // Leer valor actual para usarlo como baseline del nuevo ciclo
            double valorActual = 0;
            if (!string.IsNullOrWhiteSpace(TxtValor.Text))
                double.TryParse(TxtValor.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out valorActual);

            // Si no se escribió nada, usar la última lectura registrada
            if (valorActual == 0)
                valorActual = DatabaseHelper.GetUltimaLecturaValor(equipo.Id);

            string desc = string.IsNullOrWhiteSpace(TxtDescripcion.Text)
                ? "Mantenimiento completado"
                : TxtDescripcion.Text.Trim();

            // El reset guarda el coste como lectura de mantenimiento
            DatabaseHelper.AgregarLectura(new Lectura
            {
                EquipoId = equipo.Id,
                NombreEquipo = equipo.Nombre,
                Tipo = "Mantenimiento",
                Valor = valorActual > 0
                  ? valorActual.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                  : "",
                Descripcion = "RESET CONTADOR - " + desc,
                Operario = "Supervisor",
                Proveedor = TxtProveedor.Text.Trim(),
                Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Coste = coste
            });

            // Guardar el nuevo punto base del ciclo
            DatabaseHelper.ResetarContadorMantenimiento(equipo.Id, valorActual);
            double baseResetPrueba = DatabaseHelper.GetValorEnUltimoReset(equipo.Id);

            MessageBox.Show(
                $"valorActual = {valorActual}\nbaseReset leída = {baseResetPrueba}",
                "Comprobación reset",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            // Mantener la lectura real del equipo, NO ponerla a cero
            DatabaseHelper.ActualizarHorasEquipo(equipo.Id, (int)Math.Round(valorActual));

            TxtDescripcion.Clear();
            TxtValor.Clear();
            TxtProveedor.Clear();
            TxtCoste.Text = "0";
            ChkResetContador.IsChecked = false;
            CargarLecturas();

            MessageBox.Show($"Contador reseteado para '{equipo.Nombre}'.\nPróximo ciclo parte desde {valorActual:F0} h.",
                "Reset completado", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Exportar CSV ───────────────────────────────────────────────────
        private void BtnExportarCSV_Click(object sender, RoutedEventArgs e)
        {
            var registros = (GridLecturas.ItemsSource as IEnumerable<Lectura>)?.ToList();
            if (registros == null || registros.Count == 0)
            { Aviso("No hay registros que exportar con el filtro actual."); return; }

            string maquina = "Todas";
            if (CmbFiltroEquipo.SelectedItem is Equipo eq && eq.Id != -1)
                maquina = eq.Nombre.Replace(" ", "_");

            var dlg = new SaveFileDialog
            {
                Title = "Exportar historial",
                FileName = $"Historial_{maquina}_{DateTime.Now:yyyyMMdd}",
                DefaultExt = ".csv",
                Filter = "CSV (*.csv)|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var sw = new StreamWriter(dlg.FileName, false,
                    new System.Text.UTF8Encoding(true)); // BOM para Excel
                sw.WriteLine("ID;Equipo;Tipo;Valor;Descripcion;Proveedor;Coste (EUR);Fecha;Registrado por");
                foreach (var r in registros)
                {
                    var desc = r.Descripcion?.Replace("\"", "\"\"") ?? "";
                    var proveedor = (r.Proveedor ?? "").Replace("\"", "\"\"");

                    sw.WriteLine(
                        $"{r.Id};{r.NombreEquipo};{r.Tipo};{r.Valor};" +
                        $"\"{desc}\";" +
                        $"\"{proveedor}\";" +
                        $"{r.Coste.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};" +
                        $"{r.Fecha};{r.Operario}");
                }
                MessageBox.Show(
                    $"✅ Exportado:\n{dlg.FileName}\n\n{registros.Count} registros.",
                    "CSV generado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Utilidad ───────────────────────────────────────────────────────
        private void Aviso(string msg) =>
            MessageBox.Show(msg, "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}