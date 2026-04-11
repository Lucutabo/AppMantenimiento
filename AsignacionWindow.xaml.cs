using System.Windows;

namespace AppMantenimiento
{
    public partial class AsignacionWindow : Window
    {
        public AsignacionWindow()
        {
            InitializeComponent();
            CargarCombos();
            CargarAsignaciones();
        }

        private void CargarCombos()
        {
            try
            {
                CmbEquipo.ItemsSource = DatabaseHelper.GetEquipos();
                CmbOperario.ItemsSource = DatabaseHelper.GetOperarios();

                if (CmbEquipo.Items.Count > 0) CmbEquipo.SelectedIndex = 0;
                if (CmbOperario.Items.Count > 0) CmbOperario.SelectedIndex = 0;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Error al cargar datos: " + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CargarAsignaciones()
        {
            try
            {
                GridAsignaciones.ItemsSource = DatabaseHelper.ObtenerAsignaciones();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Error al cargar asignaciones: " + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAsignar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var equipo = CmbEquipo.SelectedItem as Equipo;
                var operario = CmbOperario.SelectedItem as Operario;

                if (equipo == null || operario == null)
                {
                    MessageBox.Show("Selecciona un equipo y un operario.",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                DatabaseHelper.AsignarEquipoOperario(equipo.Id, operario.Id);
                CargarAsignaciones();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Error al asignar: " + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (GridAsignaciones.SelectedItem is not Asignacion sel)
            {
                MessageBox.Show("Selecciona una asignacion de la lista.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Eliminar la asignacion de '{sel.NombreOperario}' en '{sel.NombreEquipo}'?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                DatabaseHelper.EliminarAsignacion(sel.Id);
                CargarAsignaciones();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Error al eliminar: " + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}