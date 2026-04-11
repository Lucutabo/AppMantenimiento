using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace AppMantenimiento
{
    public partial class App : Application
    {
        private SchedulerService _scheduler;
        private TelegramBotListener _botListener;
        private TaskbarIcon _trayIcon;
        private MainWindow _ventana;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DatabaseHelper.InicializarBaseDatos();

            _scheduler = new SchedulerService();
            _scheduler.Iniciar();

            _botListener = new TelegramBotListener();
            _botListener.Iniciar();

            // Icono de bandeja
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            _trayIcon.TrayMouseDoubleClick += (s, ev) => MostrarVentana();

            // Menú contextual del icono
            var menu = new System.Windows.Controls.ContextMenu();

            var itemAbrir = new System.Windows.Controls.MenuItem { Header = "Abrir" };
            itemAbrir.Click += (s, ev) => MostrarVentana();

            var itemSalir = new System.Windows.Controls.MenuItem { Header = "Salir" };
            itemSalir.Click += (s, ev) => CerrarApp();

            menu.Items.Add(itemAbrir);
            menu.Items.Add(itemSalir);
            _trayIcon.ContextMenu = menu;

            // Crear ventana sin mostrarla
            _ventana = new MainWindow();
            _ventana.Closing += (s, ev) =>
            {
                ev.Cancel = true;
                _ventana.Hide();
            };
        }

        private void MostrarVentana()
        {
            _ventana.Show();
            _ventana.WindowState = WindowState.Normal;
            _ventana.Activate();
        }

        private void CerrarApp()
        {
            _trayIcon.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _scheduler?.Detener();
            _botListener?.Detener();
            _trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}