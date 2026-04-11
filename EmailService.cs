using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;

namespace AppMantenimiento
{
    public class EmailService
    {
        private readonly string _servidor;
        private readonly int _puerto;
        private readonly string _email;
        private readonly string _password;

        public EmailService()
        {
            _servidor = DatabaseHelper.LeerConfiguracion("SmtpServidor");
            _puerto = int.TryParse(DatabaseHelper.LeerConfiguracion("SmtpPuerto"), out int p) ? p : 587;
            _email = DatabaseHelper.LeerConfiguracion("SmtpEmail");
            _password = DatabaseHelper.LeerConfiguracion("SmtpPassword");
        }

        public (bool ok, string msg) EnviarAvisoMantenimiento(
            string destinatario, string nombreDestinatario,
            string nombreEquipo, int valor, string unidad, string estado = "AVISO")
        {
            string icono = estado == "VENCIDO" ? "🔴" :
                           estado == "CRITICO" ? "🟠" : "🟡";

            string asunto = $"{icono} Mantenimiento {estado} – {nombreEquipo}";

            string cuerpo = $@"
                <h2>{icono} Aviso de Mantenimiento</h2>
                <p>Hola <b>{nombreDestinatario}</b>,</p>
                <p>El equipo <b>{nombreEquipo}</b> requiere atención:</p>
                <table style='border-collapse:collapse;'>
                  <tr>
                    <td style='padding:4px 12px;'><b>Estado</b></td>
                    <td style='padding:4px 12px;'>{estado}</td>
                  </tr>
                  <tr>
                    <td style='padding:4px 12px;'><b>Tiempo restante</b></td>
                    <td style='padding:4px 12px;'>{valor} {unidad}</td>
                  </tr>
                </table>
                <p>Por favor verifica que el operario ha sido notificado.</p>";

            return EnviarEmail(destinatario, asunto, cuerpo);
        }

        private (bool ok, string msg) EnviarEmail(
            string destinatario, string asunto, string cuerpoHtml)
        {
            try
            {
                var mensaje = new MimeMessage();
                mensaje.From.Add(MailboxAddress.Parse(_email));
                mensaje.To.Add(MailboxAddress.Parse(destinatario));
                mensaje.Subject = asunto;

                var builder = new BodyBuilder { HtmlBody = cuerpoHtml };
                mensaje.Body = builder.ToMessageBody();

                using var client = new SmtpClient();
                var ssl = _puerto == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                client.Connect(_servidor, _puerto, ssl);
                client.Authenticate(_email, _password);
                client.Send(mensaje);
                client.Disconnect(true);

                return (true, "Email enviado correctamente.");
            }
            catch (Exception ex)
            {
                return (false, $"Error al enviar email: {ex.Message}");
            }
        }
    }
}