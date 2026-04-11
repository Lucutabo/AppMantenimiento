using System;
using System.Windows;
using System.Windows.Controls;

namespace AppMantenimiento
{
    public partial class OperariosWindow : Window
    {
        public OperariosWindow()
        {
            InitializeComponent();
            CargarOperarios();
        }

        private void CargarOperarios()
        {
            try
            {
                if (GridOperarios == null) return;
                var todos = DatabaseHelper.GetOperarios();
                GridOperarios.ItemsSource = RbActivos?.IsChecked == true
                    ? todos.FindAll(o => o.Activo == 1)
                    : todos;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cargando operarios: {ex.Message}");
            }
        }

        private void FiltroChanged(object sender, RoutedEventArgs e)
        {
            if (GridOperarios != null) CargarOperarios();
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("El nombre del operario es obligatorio.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rolSel = CmbRol.SelectedItem as ComboBoxItem;
            var operario = new Operario
            {
                Nombre = TxtNombre.Text.Trim(),
                Telefono = TxtTelefono.Text.Trim(),
                Email = TxtEmail.Text.Trim(),
                Rol = rolSel?.Content?.ToString() ?? "Operario",
                Activo = 1
            };

            DatabaseHelper.AgregarOperario(operario);
            TxtNombre.Clear();
            TxtTelefono.Clear();
            TxtEmail.Clear();
            CmbRol.SelectedIndex = 0;
            CargarOperarios();
        }

        private Operario OperarioSeleccionado()
        {
            var op = GridOperarios.SelectedItem as Operario;
            if (op == null)
                MessageBox.Show("Selecciona un operario de la tabla.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return op;
        }

        private void BtnDarDeBaja_Click(object sender, RoutedEventArgs e)
        {
            var op = OperarioSeleccionado();
            if (op == null) return;

            if (op.Activo == 0)
            {
                MessageBox.Show("El operario ya está dado de baja."); return;
            }

            var r = MessageBox.Show(
                $"¿Dar de baja a {op.Nombre}?\nSe conservarán sus registros.",
                "Confirmar baja", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (r == MessageBoxResult.Yes)
            {
                DatabaseHelper.DarDeBajaOperario(op.Id);
                CargarOperarios();
            }
        }

        private void BtnReactivar_Click(object sender, RoutedEventArgs e)
        {
            var op = OperarioSeleccionado();
            if (op == null) return;

            if (op.Activo == 1)
            {
                MessageBox.Show("El operario ya está activo."); return;
            }

            DatabaseHelper.ReactivarOperario(op.Id);
            CargarOperarios();
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            var op = OperarioSeleccionado();
            if (op == null) return;

            var r = MessageBox.Show(
                $"¿ELIMINAR DEFINITIVAMENTE a {op.Nombre}?\n" +
                "Se borrarán también todas sus asignaciones.\n" +
                "Esta acción NO se puede deshacer.",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (r == MessageBoxResult.Yes)
            {
                DatabaseHelper.BorrarOperario(op.Id);
                CargarOperarios();
            }
        }

        private void BtnEditarChatId_Click(object sender, RoutedEventArgs e)
{
    var op = OperarioSeleccionado();
    if (op == null) return;

    var ventana = new Window
    {
        Title                 = $"Chat ID — {op.Nombre}",
        Width                 = 420, Height = 200,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner                 = this,
        ResizeMode            = ResizeMode.NoResize,
        Background            = System.Windows.Media.Brushes.White
    };

    var panel = new StackPanel { Margin = new Thickness(20) };
    panel.Children.Add(new TextBlock {
        Text = $"Chat ID de {op.Nombre}:", FontSize = 13,
        FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,10)
    });

    var txt = new TextBox {
        Text = op.TelegramChatId ?? "", Height = 34,
        Padding = new Thickness(8,4,8,4), FontSize = 13,
        BorderBrush = System.Windows.Media.Brushes.LightGray,
        BorderThickness = new Thickness(1), Margin = new Thickness(0,0,0,14)
    };
    panel.Children.Add(txt);

    var btnPanel = new StackPanel {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right
    };

    int opId = op.Id; string opNombre = op.Nombre;

    var btnGuardar = new Button {
        Content = "Guardar", Width = 100, Height = 34,
        Margin = new Thickness(0,0,8,0),
        Background = System.Windows.Media.Brushes.ForestGreen,
        Foreground = System.Windows.Media.Brushes.White,
        BorderThickness = new Thickness(0)
    };
    btnGuardar.Click += (s, ev) => {
        DatabaseHelper.ActualizarTelegramChatId(opId, txt.Text.Trim());
        ventana.Close();
        CargarOperarios();
        MessageBox.Show($"Chat ID actualizado para {opNombre}.",
            "Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
    };

    var btnCancelar = new Button { Content = "Cancelar", Width = 90, Height = 34 };
    btnCancelar.Click += (s, ev) => ventana.Close();

    btnPanel.Children.Add(btnGuardar);
    btnPanel.Children.Add(btnCancelar);
    panel.Children.Add(btnPanel);
    ventana.Content = panel;
    ventana.ShowDialog();
}

        // ... último método que hay ...

        private void BtnProbarTelegram_Click(object sender, RoutedEventArgs e)
        {
            var op = OperarioSeleccionado();
            if (op == null) return;

            if (string.IsNullOrWhiteSpace(op.TelegramChatId))
            {
                MessageBox.Show(
                    $"{op.Nombre} no tiene Chat ID configurado.\n\n" +
                    "Primero pulsa 'Editar Chat ID' para asignárselo.",
                    "Sin Chat ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var svc = new TelegramService();
            var (ok, msg) = svc.EnviarMensaje(op.TelegramChatId.Trim(),
                $"✅ <b>Prueba de conexión</b>\n\n" +
                $"Hola <b>{op.Nombre}</b>, el bot está correctamente configurado.\n" +
                $"Ya puedes usar /mis_equipos, /lectura y /completado.");

            MessageBox.Show(msg,
                ok ? "Telegram OK" : "Error al enviar",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        }

    }  // ← cierre de la clase
}      // ← cierre del namespace