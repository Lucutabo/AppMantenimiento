using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AppMantenimiento
{
    public class TelegramService
    {
        private readonly string _token;
        private static readonly HttpClient _http = new HttpClient();

        public TelegramService()
        {
            _token = DatabaseHelper.LeerConfiguracion("TelegramToken");
        }

        // Método base: envía cualquier mensaje a un Chat ID
        public (bool ok, string mensaje) EnviarMensaje(string chatId, string texto)
        {
            if (string.IsNullOrWhiteSpace(_token))
                return (false, "Configura primero el Token del bot en Configuración.");
            if (string.IsNullOrWhiteSpace(chatId))
                return (false, "Chat ID no configurado.");

            try
            {
                var url = $"https://api.telegram.org/bot{_token}/sendMessage";
                var body = JsonSerializer.Serialize(new
                {
                    chat_id = chatId,
                    text = texto,
                    parse_mode = "HTML"
                });
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = _http.PostAsync(url, content).Result;
                var respBody = response.Content.ReadAsStringAsync().Result;

                return response.IsSuccessStatusCode
                    ? (true, $"Mensaje enviado al chat {chatId}")
                    : (false, $"Error Telegram: {respBody}");
            }
            catch (Exception ex)
            {
                return (false, $"Excepción: {ex.Message}");
            }
        }

        // Aviso al SUPERVISOR
        public (bool ok, string msg) EnviarAvisoSupervisor(
            string chatIdSupervisor, string nombreEquipo,
            string nombreOperario, int horasRestantes)
        {
            string texto = $"<b>⚠️ Aviso de Mantenimiento</b>\n\n" +
                           $"Equipo: <b>{nombreEquipo}</b>\n" +
                           $"Operario asignado: {nombreOperario}\n" +
                           $"Tiempo restante: <b>{(horasRestantes <= 0 ? "VENCIDO" : horasRestantes + " h")}</b>\n\n" +
                           $"Verifica que el operario ha sido notificado y registra la lectura al completar.";
            return EnviarMensaje(chatIdSupervisor, texto);
        }

        // Aviso al OPERARIO
        public (bool ok, string msg) EnviarAvisoOperario(
            string chatIdOperario, string nombreEquipo, int horasRestantes)
        {
            string texto = $"<b>🔧 Mantenimiento próximo</b>\n\n" +
                           $"Equipo: <b>{nombreEquipo}</b>\n" +
                           $"Horas restantes: <b>{(horasRestantes <= 0 ? "VENCIDO" : horasRestantes + " h")}</b>\n\n" +
                           $"Recuerda registrar la lectura una vez completado.";
            return EnviarMensaje(chatIdOperario, texto);
        }
    }
}