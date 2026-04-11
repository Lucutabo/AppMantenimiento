using System.Windows;

namespace AppMantenimiento
{
    public partial class EditarChatIdWindow : Window
    {
        private readonly Operario _operario;

        public EditarChatIdWindow(Operario operario)
        {
            InitializeComponent();
            _operario = operario;
            TxtTitulo.Text = $"Operario: {operario.Nombre}";
            TxtChatId.Text = operario.TelegramChatId ?? "";
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            DatabaseHelper.ActualizarTelegramChatId(_operario.Id, TxtChatId.Text.Trim());
            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}