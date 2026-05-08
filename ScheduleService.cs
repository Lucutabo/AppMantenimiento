using System;
using System.Threading;
using System.Timers;
using System.Collections.Generic;
using System.Linq;

namespace AppMantenimiento
{
    public class SchedulerService
    {
        private System.Threading.Timer timer;
        private bool _ejecutando = false;

        public void Iniciar()
        {
            int.TryParse(DatabaseHelper.LeerConfiguracion("SchedulerIntervaloHoras"), out int horas);
            if (horas <= 0) horas = 24;

            var activo = DatabaseHelper.LeerConfiguracion("SchedulerActivo") ?? "1";
            if (activo == "0") return;

            timer = new System.Threading.Timer(
                VerificarMantenimientos,
                null,
                TimeSpan.FromHours(horas),
                TimeSpan.FromHours(horas));
        }

        public void Detener()
        {
            timer?.Dispose();
        }

        private void VerificarMantenimientos(object state)
        {
            if (_ejecutando) return;
            _ejecutando = true;
            try
            {
                ProcesarAvisos();
                SolicitarLecturasPendientes();
                ReintentarValidacionesSinRespuesta();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scheduler] {ex.Message}");
            }
            finally { _ejecutando = false; }
        }
        public void EjecutarAhoraManual()
        {
            ProcesarAvisos();
        }

        // ── Avisos de mantenimiento próximo/vencido ───────────
        private void ProcesarAvisos()
        {
            var equipos = DatabaseHelper.GetEquipos();
            var telegramSvc = new TelegramService();
            var emailSvc = new EmailService();
            var chatSupervisor = DatabaseHelper.LeerConfiguracion("TelegramChatSupervisor");
            var emailSupervisor = DatabaseHelper.LeerConfiguracion("EmailSupervisor");
            var asignaciones = DatabaseHelper.ObtenerAsignaciones();
            var operarios = DatabaseHelper.GetOperarios();

            foreach (var equipo in equipos)
            {
                if (equipo.Activo != 1) continue;
                if (equipo.MantenimientoEnCurso == 1) continue;

                int limite = equipo.FrecuenciaHoras > 0 ? equipo.FrecuenciaHoras : equipo.FrecuenciaMantenimiento;
                if (limite <= 0) continue;

                double valorActual = equipo.HorasActuales;
                double baseReset = DatabaseHelper.GetValorEnUltimoReset(equipo.Id);
                double delta = valorActual - baseReset;
                if (delta < 0) delta = valorActual;

                int restante = limite - (int)delta;

                string estado;
                if (restante <= 0) estado = "VENCIDO";
                else if (restante <= 50) estado = "CRITICO";
                else if (restante <= 100) estado = "PROXIMO";
                else estado = "OK";

                if (estado == "OK") continue;

                bool avisar = false;

                if (string.IsNullOrWhiteSpace(equipo.UltimoAvisoMantenimiento) ||
                    string.IsNullOrWhiteSpace(equipo.EstadoUltimoAvisoMantenimiento))
                {
                    avisar = true;
                }
                else if (!string.Equals(equipo.EstadoUltimoAvisoMantenimiento, estado, StringComparison.OrdinalIgnoreCase))
                {
                    avisar = true;
                }
                else if (DateTime.TryParse(equipo.UltimoAvisoMantenimiento, out var ultimoAviso))
                {
                    if (ultimoAviso.Date < DateTime.Now.Date)
                        avisar = true;
                }
                else
                {
                    avisar = true;
                }

                if (!avisar) continue;

                string icono = estado == "VENCIDO" ? "🔴" : estado == "CRITICO" ? "🟠" : "🟡";
                string detalle = equipo.FrecuenciaHoras > 0
                    ? $"Horas restantes: {restante}h"
                    : $"Días restantes: {restante}";

                var asigsEquipo = asignaciones.FindAll(a => a.EquipoId == equipo.Id);

                var chatsEnviados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var asig in asigsEquipo)
                {
                    var op = operarios.Find(o => o.Id == asig.OperarioId && o.Activo == 1);
                    if (op == null) continue;
                    if (string.IsNullOrWhiteSpace(op.TelegramChatId)) continue;

                    if (chatsEnviados.Contains(op.TelegramChatId)) continue;
                    chatsEnviados.Add(op.TelegramChatId);

                    telegramSvc.EnviarMensaje(op.TelegramChatId,
                        $"{icono} **Aviso de mantenimiento: {estado}**\n\n" +
                        $"Equipo: **{equipo.Nombre}**\n" +
                        $"{detalle}\n\n" +
                        $"Usa `/completado {equipo.Id} descripción` cuando lo realices.");
                }

                if (!string.IsNullOrWhiteSpace(chatSupervisor))
                {
                    string nombreOp = "Sin asignar";

                    var primeraAsignacionValida = asigsEquipo
                        .Select(a => operarios.Find(o => o.Id == a.OperarioId && o.Activo == 1))
                        .FirstOrDefault(o => o != null);

                    if (primeraAsignacionValida != null)
                        nombreOp = primeraAsignacionValida.Nombre;

                    telegramSvc.EnviarMensaje(chatSupervisor,
                        $"{icono} **{estado}: {equipo.Nombre}**\n" +
                        $"Operario: {nombreOp}\n{detalle}");
                }

                if (!string.IsNullOrWhiteSpace(emailSupervisor))
                {
                    emailSvc.EnviarAvisoMantenimiento(
                        emailSupervisor,
                        "Supervisor",
                        equipo.Nombre,
                        restante,
                        equipo.FrecuenciaHoras > 0 ? "horas" : "días",
                        estado);
                }

                DatabaseHelper.ActualizarUltimoAvisoMantenimiento(
                    equipo.Id,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    estado);
            }
        }

        // ── Solicitar lecturas de horas cada 15 días ──────────
        private void SolicitarLecturasPendientes()
        {
            if (DateTime.Now.DayOfWeek == DayOfWeek.Saturday ||
                DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
            {
                return;
            }

            var telegramSvc = new TelegramService();
            var equipos = DatabaseHelper.GetEquipos();
            var asignaciones = DatabaseHelper.ObtenerAsignaciones();
            var operarios = DatabaseHelper.GetOperarios();

            int.TryParse(DatabaseHelper.LeerConfiguracion("DiassinLectura"), out int diasUmbral);
            if (diasUmbral <= 0) diasUmbral = 15;

            foreach (var equipo in equipos)
            {
                if (equipo.Activo != 1 || equipo.FrecuenciaHoras <= 0) continue;

                DateTime ultimaFechaLectura;
                if (!string.IsNullOrEmpty(equipo.UltimaLectura) &&
                    DateTime.TryParse(equipo.UltimaLectura, out var ul))
                    ultimaFechaLectura = ul;
                else if (!string.IsNullOrEmpty(equipo.FechaAlta) &&
                         DateTime.TryParse(equipo.FechaAlta, out var fa))
                    ultimaFechaLectura = fa;
                else
                    continue;

                int diasSinLectura = (DateTime.Now.Date - ultimaFechaLectura.Date).Days;
                if (diasSinLectura < diasUmbral) continue;

                DateTime ultimaSolicitud = DateTime.MinValue;

                bool haySolicitudPrevia =
                    !string.IsNullOrWhiteSpace(equipo.UltimaSolicitudLectura) &&
                    DateTime.TryParse(equipo.UltimaSolicitudLectura, out ultimaSolicitud);

                bool enviar = false;
                bool esPrimerAviso = false;

                if (!haySolicitudPrevia)
                {
                    enviar = true;
                    esPrimerAviso = true;
                }
                else
                {
                    int diasLaborables = ContarDiasLaborables(ultimaSolicitud.Date, DateTime.Now.Date);
                    if (diasLaborables >= 3)
                        enviar = true;
                }

                if (!enviar) continue;

                var asigs = asignaciones.FindAll(a => a.EquipoId == equipo.Id);

                foreach (var asig in asigs)
                {
                    var op = operarios.Find(o => o.Id == asig.OperarioId && o.Activo == 1);
                    if (op == null || string.IsNullOrWhiteSpace(op.TelegramChatId)) continue;

                    string mensajeTipo = esPrimerAviso
                        ? "📋 *Solicitud de lectura de horas*"
                        : $"🔔 *Recordatorio ({diasSinLectura} días sin lectura)*";

                    telegramSvc.EnviarMensaje(op.TelegramChatId,
                        $"{mensajeTipo}\n" +
                        $"Equipo: *{equipo.Nombre}*\n" +
                        $"Última lectura: hace *{diasSinLectura} días* ({equipo.HorasActuales}h)\n\n" +
                        $"Por favor, envía las horas actuales del equipo:\n" +
                        $"`lectura {equipo.Id} horasactuales`\n" +
                        $"Ejemplo: `lectura {equipo.Id} {equipo.HorasActuales + 80}`");
                }

                DatabaseHelper.ActualizarUltimaSolicitudLectura(
                    equipo.Id,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            }
        }
        private int ContarDiasLaborables(DateTime desde, DateTime hasta)
        {
            int dias = 0;
            var fecha = desde.AddDays(1);

            while (fecha <= hasta)
            {
                if (fecha.DayOfWeek != DayOfWeek.Saturday &&
                    fecha.DayOfWeek != DayOfWeek.Sunday)
                {
                    dias++;
                }

                fecha = fecha.AddDays(1);
            }

            return dias;
        }


        // ── Reintentar validaciones sin respuesta (>24h) ──────
        private void ReintentarValidacionesSinRespuesta()
        {
            if (DateTime.Now.DayOfWeek == DayOfWeek.Saturday ||
            DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
            {
                return;
            }
            var pendientes = DatabaseHelper.GetValidacionesPendientes();
            var telegramSvc = new TelegramService();
            var equipos = DatabaseHelper.GetEquipos();
            var operarios = DatabaseHelper.GetOperarios();

            foreach (var val in pendientes)
            {
                if (!DateTime.TryParse(val.FechaUltimoIntento, out var ultima)) continue;
                if ((DateTime.Now - ultima).TotalHours < 24) continue;
                if (val.Intentos >= 3) continue;

                var eq = equipos.Find(e => e.Id == val.EquipoId);
                var op = operarios.Find(o => o.Id == val.OperarioId);

                string chatSup = DatabaseHelper.LeerConfiguracion("TelegramChatSupervisor");
                if (!string.IsNullOrWhiteSpace(chatSup))
                {
                    telegramSvc.EnviarMensaje(chatSup,
                        $"🔁 <b>Reintento #{val.Intentos + 1} — " +
                        $"Validación #{val.Id} sin respuesta</b>\n\n" +
                        $"📌 Equipo: {eq?.Nombre}\n" +
                        $"👤 Operario: {op?.Nombre}\n" +
                        $"📉 {val.HorasAntiguas}h → {val.HorasNuevas}h\n" +
                        $"🗓 Solicitud: {val.FechaSolicitud}\n\n" +
                        $"✅ <code>/aprobar {val.Id}</code>\n" +
                        $"❌ <code>/rechazar {val.Id}</code>");
                }

                DatabaseHelper.ActualizarIntentoValidacion(val.Id);
            }
        }
    }
}