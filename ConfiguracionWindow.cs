using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AppMantenimiento
{
    public partial class ConfiguracionWindow : Window
    {
        public ConfiguracionWindow()
        {
            InitializeComponent();
            CargarConfiguracion();
        }

        private void CargarConfiguracion()
        {
            TxtSmtpServidor.Text = DatabaseHelper.LeerConfiguracion("SmtpServidor");
            TxtSmtpPuerto.Text = DatabaseHelper.LeerConfiguracion("SmtpPuerto");
            TxtSmtpEmail.Text = DatabaseHelper.LeerConfiguracion("SmtpEmail");
            TxtSmtpPassword.Password = DatabaseHelper.LeerConfiguracion("SmtpPassword");
            TxtEmailSupervisor.Text = DatabaseHelper.LeerConfiguracion("EmailSupervisor");
            TxtTelegramToken.Text = DatabaseHelper.LeerConfiguracion("TelegramToken");
            TxtTelegramChatSupervisor.Text = DatabaseHelper.LeerConfiguracion("TelegramChatSupervisor");
            TxtUmbralAviso.Text = DatabaseHelper.LeerConfiguracion("UmbralAviso");
            TxtUmbralCritico.Text = DatabaseHelper.LeerConfiguracion("UmbralCritico");
            TxtSchedulerIntervalo.Text = DatabaseHelper.LeerConfiguracion("SchedulerIntervaloHoras") ?? "24";
            TxtDiasSinLectura.Text = DatabaseHelper.LeerConfiguracion("DiassinLectura") ?? "15";

            var activo = DatabaseHelper.LeerConfiguracion("SchedulerActivo") ?? "1";
            foreach (ComboBoxItem item in CmbSchedulerActivo.Items)
                if (item.Tag?.ToString() == activo) { CmbSchedulerActivo.SelectedItem = item; break; }
            if (CmbSchedulerActivo.SelectedItem == null) CmbSchedulerActivo.SelectedIndex = 0;

            if (string.IsNullOrEmpty(TxtUmbralAviso.Text)) TxtUmbralAviso.Text = "50";
            if (string.IsNullOrEmpty(TxtUmbralCritico.Text)) TxtUmbralCritico.Text = "10";
        }

        private void GuardarTodo()
        {
            DatabaseHelper.GuardarConfiguracion("SmtpServidor", TxtSmtpServidor.Text.Trim());
            DatabaseHelper.GuardarConfiguracion("SmtpPuerto", TxtSmtpPuerto.Text.Trim());
            DatabaseHelper.GuardarConfiguracion("SmtpEmail", TxtSmtpEmail.Text.Trim());
            DatabaseHelper.GuardarConfiguracion("SmtpPassword", TxtSmtpPassword.Password);
            DatabaseHelper.GuardarConfiguracion("EmailSupervisor", TxtEmailSupervisor.Text.Trim());
            DatabaseHelper.GuardarConfiguracion("TelegramToken", TxtTelegramToken.Text.Trim());
            DatabaseHelper.GuardarConfiguracion("TelegramChatSupervisor", TxtTelegramChatSupervisor.Text.Trim());
            DatabaseHelper.GuardarConfiguracion("UmbralAviso", TxtUmbralAviso.Text.Trim());
            DatabaseHelper.GuardarConfiguracion("UmbralCritico", TxtUmbralCritico.Text.Trim());
            DatabaseHelper.GuardarConfiguracion("SchedulerIntervaloHoras",
                string.IsNullOrWhiteSpace(TxtSchedulerIntervalo.Text) ? "24" : TxtSchedulerIntervalo.Text.Trim());
            DatabaseHelper.GuardarConfiguracion("DiassinLectura",
                string.IsNullOrWhiteSpace(TxtDiasSinLectura.Text) ? "15" : TxtDiasSinLectura.Text.Trim());
            var selActivo = (CmbSchedulerActivo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1";
            DatabaseHelper.GuardarConfiguracion("SchedulerActivo", selActivo);
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            GuardarTodo();
            MessageBox.Show("Configuración guardada correctamente.", "Guardado",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnTestEmail_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳ Enviando..."; }

            string emailDestino = TxtEmailSupervisor.Text.Trim();
            try
            {
                bool ok = false; string msg = "";
                await Task.Run(() =>
                {
                    var svc = new EmailService();
                    (ok, msg) = svc.EnviarAvisoMantenimiento(
                        emailDestino, "Supervisor", "Equipo de prueba", 0, "días", "AVISO");
                });
                MessageBox.Show(ok ? "✅ Email enviado correctamente." : $"❌ Error:\n{msg}",
                    "Prueba Email", MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error inesperado:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) { btn.IsEnabled = true; btn.Content = "Probar Email"; }
            }
        }

        private async void BtnTestTelegram_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳ Enviando..."; }

            string token = TxtTelegramToken.Text.Trim();
            string chatId = TxtTelegramChatSupervisor.Text.Trim();

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
            {
                MessageBox.Show("Rellena el Token y el Chat ID antes de probar.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                if (btn != null) { btn.IsEnabled = true; btn.Content = "Probar Telegram"; }
                return;
            }
            try
            {
                bool ok = false; string msg = "";
                await Task.Run(() =>
                {
                    var svc = new TelegramService();
                    (ok, msg) = svc.EnviarMensaje(chatId,
                        "✅ Mensaje de prueba desde App Mantenimiento.\nTelegram configurado correctamente.");
                });
                MessageBox.Show(ok ? "✅ Mensaje enviado correctamente." : $"❌ Error:\n{msg}",
                    "Prueba Telegram", MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error inesperado:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) { btn.IsEnabled = true; btn.Content = "Probar Telegram"; }
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnBackup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Guardar copia de seguridad",
                FileName = $"backup_mantenimiento_{DateTime.Now:yyyy-MM-dd_HH-mm}.db",
                DefaultExt = ".db",
                Filter = "Base de datos SQLite (*.db)|*.db|Todos los archivos (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                DatabaseHelper.HacerBackup(dlg.FileName);
                MessageBox.Show($"✅ Copia guardada correctamente en:\n{dlg.FileName}",
                    "Backup completado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar la copia:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRestaurar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Seleccionar copia de seguridad",
                DefaultExt = ".db",
                Filter = "Base de datos SQLite (*.db)|*.db|Todos los archivos (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            var confirm = MessageBox.Show(
                "⚠️ ATENCIÓN\n\nSe reemplazará TODA la base de datos actual con la seleccionada.\n" +
                "Esta acción no se puede deshacer.\n\n¿Confirmas la restauración?",
                "Confirmar restauración", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                DatabaseHelper.RestaurarBackup(dlg.FileName);
                MessageBox.Show("✅ Base de datos restaurada correctamente.\nLa aplicación se reiniciará ahora.",
                    "Restauración completada", MessageBoxButton.OK, MessageBoxImage.Information);
                Process.Start(Process.GetCurrentProcess().MainModule!.FileName);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al restaurar la copia:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── NUEVO: Limpiar base de datos ────────────────────────────────────
        private void BtnLimpiarBD_Click(object sender, RoutedEventArgs e)
        {
            // Primera confirmación
            var paso1 = MessageBox.Show(
                "⚠️ ATENCIÓN — ACCIÓN IRREVERSIBLE\n\n" +
                "Se borrarán TODOS los datos:\n" +
                "  • Equipos\n" +
                "  • Operarios\n" +
                "  • Lecturas e historial\n" +
                "  • Asignaciones y resets\n\n" +
                "La configuración (SMTP, Telegram, umbrales) se conservará.\n\n" +
                "¿Deseas continuar?",
                "Limpiar base de datos",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (paso1 != MessageBoxResult.Yes) return;

            // Segunda confirmación — barrera extra ante un click accidental
            var paso2 = MessageBox.Show(
                "🔴 ÚLTIMA ADVERTENCIA\n\n" +
                "Esta operación NO se puede deshacer.\n" +
                "¿Confirmas que quieres borrar todos los datos?",
                "Confirmar limpieza total",
                MessageBoxButton.YesNo, MessageBoxImage.Stop);

            if (paso2 != MessageBoxResult.Yes) return;

            try
            {
                DatabaseHelper.LimpiarTodosLosDatos();
                MessageBox.Show(
                    "✅ Base de datos limpiada correctamente.\nLa aplicación se reiniciará ahora.",
                    "Limpieza completada", MessageBoxButton.OK, MessageBoxImage.Information);

                Process.Start(Process.GetCurrentProcess().MainModule!.FileName);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al limpiar la base de datos:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnTestScheduler_Click(object sender, RoutedEventArgs e)
        {
            BtnTestScheduler.IsEnabled = false;
            BtnTestScheduler.Content = "⏳  Ejecutando...";
            try
            {
                await Task.Run(() =>
                {
                    var scheduler = new SchedulerService();
                    scheduler.EjecutarAhoraManual();
                });
                MessageBox.Show(
                    "✅ Scheduler ejecutado correctamente.\nRevisa Telegram y email para confirmar los avisos.",
                    "Scheduler", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al ejecutar el scheduler:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnTestScheduler.IsEnabled = true;
                BtnTestScheduler.Content = "▶  Ejecutar ahora";
            }
        }
        private void BtnImportarHistoricoExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Seleccionar archivo Excel histórico",
                    Filter = "Archivos Excel (*.xlsx)|*.xlsx",
                    Multiselect = false
                };

                bool? resultado = dialog.ShowDialog();

                if (resultado != true || string.IsNullOrWhiteSpace(dialog.FileName))
                    return;

                string rutaExcel = dialog.FileName;
                string resultadoImportacion = ImportadorHistoricosExcel.ImportarDesdeExcel(rutaExcel);

                MessageBox.Show(resultadoImportacion, "Resultado importación",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error al importar el histórico desde Excel:\n\n" + ex.Message,
                    "Error de importación",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}