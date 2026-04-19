using System;
using System.Windows;
using System.Windows.Controls;

namespace AppMantenimiento
{
    public partial class EquiposWindow : Window
    {
        private Equipo _equipoEnEdicion = null;
        public EquiposWindow()
        {
            InitializeComponent();
            CargarEquipos();

            // ← NUEVO: recargar datos cada vez que la ventana recibe el foco
            this.Activated += (s, e) => CargarEquipos();
        }

        private void CargarEquipos()
                    {
            try
            {
                if (GridEquipos == null) return;
                var todos = DatabaseHelper.ObtenerEquipos();
                if (RbActivos?.IsChecked == true)
                    GridEquipos.ItemsSource = todos.FindAll(e => e.Activo == 1);
                else
                    GridEquipos.ItemsSource = todos;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cargando equipos: {ex.Message}");
            }
        }

        private void CargarEquipoEnFormulario(Equipo eq)
        {
            if (eq == null) return;

            _equipoEnEdicion = eq;

            TxtNombre.Text = eq.Nombre ?? "";
            TxtUbicacion.Text = eq.Ubicacion ?? "";
            TxtDescripcion.Text = eq.Descripcion ?? "";
            TxtFrecHoras.Text = eq.FrecuenciaHoras > 0 ? eq.FrecuenciaHoras.ToString() : "";
            TxtFrecKm.Text = eq.FrecuenciaKm > 0 ? eq.FrecuenciaKm.ToString() : "";

            TxtSeleccionado.Text = $"Equipo seleccionado: {eq.Nombre} (ID {eq.Id})";
        }

        private void LimpiarFormulario()
        {
            _equipoEnEdicion = null;

            TxtNombre.Clear();
            TxtDescripcion.Clear();
            TxtUbicacion.Clear();
            TxtFrecHoras.Clear();
            TxtFrecKm.Clear();

            TxtSeleccionado.Text = "Selecciona un equipo de la tabla";
        }

        private void GridEquipos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var eq = GridEquipos.SelectedItem as Equipo;
            if (eq == null) return;

            CargarEquipoEnFormulario(eq);
        }

        private void FiltroChanged(object sender, RoutedEventArgs e)
        {
            if (GridEquipos != null)
                CargarEquipos();
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("El nombre del equipo es obligatorio.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int.TryParse(TxtFrecHoras.Text, out int horas);
            int.TryParse(TxtFrecKm.Text, out int km);

            var equipo = new Equipo
            {
                Nombre = TxtNombre.Text.Trim(),
                Descripcion = TxtDescripcion.Text.Trim(),
                Ubicacion = TxtUbicacion.Text.Trim(),
                FrecuenciaHoras = horas,
                FrecuenciaKm = km,
                FrecuenciaMantenimiento = horas,
                Activo = 1
            };
            DatabaseHelper.AgregarEquipo(equipo);
            LimpiarFormulario();
            CargarEquipos();
        }

        private void BtnModificar_Click(object sender, RoutedEventArgs e)
        {
            if (_equipoEnEdicion == null)
            {
                MessageBox.Show("Selecciona primero un equipo de la tabla.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("El nombre del equipo es obligatorio.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int.TryParse(TxtFrecHoras.Text, out int horas);
            int.TryParse(TxtFrecKm.Text, out int km);

            _equipoEnEdicion.Nombre = TxtNombre.Text.Trim();
            _equipoEnEdicion.Descripcion = TxtDescripcion.Text.Trim();
            _equipoEnEdicion.Ubicacion = TxtUbicacion.Text.Trim();
            _equipoEnEdicion.FrecuenciaHoras = horas;
            _equipoEnEdicion.FrecuenciaKm = km;
            _equipoEnEdicion.FrecuenciaMantenimiento = horas;

            DatabaseHelper.ActualizarEquipo(_equipoEnEdicion);

            MessageBox.Show("Equipo actualizado correctamente.", "OK",
                MessageBoxButton.OK, MessageBoxImage.Information);

            CargarEquipos();
            LimpiarFormulario();
        }

        private Equipo EquipoSeleccionado()
        {
            var eq = GridEquipos.SelectedItem as Equipo;
            if (eq == null)
                MessageBox.Show("Selecciona un equipo de la tabla.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return eq;
        }

        private void BtnDarDeBaja_Click(object sender, RoutedEventArgs e)
        {
            var eq = EquipoSeleccionado();
            if (eq == null) return;
            if (eq.Activo == 0)
            { MessageBox.Show("El equipo ya está dado de baja."); return; }

            var r = MessageBox.Show($"¿Dar de baja '{eq.Nombre}'?\n" +
                "Se conservan todos sus registros pero dejará de recibir avisos.",
                "Confirmar baja", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                DatabaseHelper.DarDeBajaEquipo(eq.Id);
                CargarEquipos();
            }
        }

        private void BtnReactivar_Click(object sender, RoutedEventArgs e)
        {
            var eq = EquipoSeleccionado();
            if (eq == null) return;
            if (eq.Activo == 1)
            { MessageBox.Show("El equipo ya está activo."); return; }

            DatabaseHelper.ReactivarEquipo(eq.Id);   // ← ahora usa el método correcto
            CargarEquipos();
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            var eq = EquipoSeleccionado();
            if (eq == null) return;

            var r = MessageBox.Show(
                $"¿ELIMINAR DEFINITIVAMENTE '{eq.Nombre}'?\n" +
                "Se borrarán también todas sus lecturas y asignaciones.\n" +
                "Esta acción NO se puede deshacer.",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes)
            {
                DatabaseHelper.BorrarEquipo(eq.Id);
                CargarEquipos();
            }
        }
    }
}