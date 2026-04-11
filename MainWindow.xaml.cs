using System.Windows;
using System.Windows.Controls;

namespace AppMantenimiento
{
        public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private string RolActual =>
            (CmbRol.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Operario";

        private void BtnEquipos_Click(object sender, RoutedEventArgs e)
        {
            new EquiposWindow().ShowDialog();
        }

        private void BtnOperarios_Click(object sender, RoutedEventArgs e)
        {
            if (RolActual == "Operario")
            {
                MessageBox.Show("No tienes permiso para acceder a esta sección.", "Acceso denegado",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            new OperariosWindow().ShowDialog();
        }

        private void BtnAsignaciones_Click(object sender, RoutedEventArgs e)
        {
            if (RolActual == "Operario")
            {
                MessageBox.Show("No tienes permiso para acceder a esta sección.", "Acceso denegado",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            new AsignacionWindow().ShowDialog();
        }

        private void BtnLecturas_Click(object sender, RoutedEventArgs e)
        {
            new LecturasWindow().ShowDialog();
        }

        private void BtnConfiguracion_Click(object sender, RoutedEventArgs e)
        {
            if (RolActual != "Administrador")
            {
                MessageBox.Show("Solo el Administrador puede acceder a la configuración.", "Acceso denegado",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            new ConfiguracionWindow().ShowDialog();
        }
        private void BtnDashboardClick(object sender, RoutedEventArgs e)
        {
            new DashboardWindow { Owner = this }.ShowDialog();
        }
    }
}