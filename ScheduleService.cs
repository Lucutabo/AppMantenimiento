using System;
using System.Threading;
using System.Timers;

namespace AppMantenimiento
{
    public class SchedulerService
    {
        private System.Threading.Timer timer;
        private bool _ejecutando = false;

        public void Iniciar()
        {
            int.TryParse(DatabaseHelper.LeerConfiguracion("SchedulerIntervaloHoras"), out int dias);
            if (dias <= 0) dias = 1;

            var activo = DatabaseHelper.LeerConfiguracion("SchedulerActivo") ?? "1";
            if (activo == "0") return;

            // Scheduler desactivado

            timer = new System.Threading.Timer(VerificarMantenimientos, null,
            TimeSpan.Zero, TimeSpan.FromDays(dias));
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

                int limite = equipo.FrecuenciaHoras > 0
                    ? equipo.FrecuenciaHoras
                    : equipo.FrecuenciaMantenimiento;

                if (limite <= 0) continue;

                double valorActual = equipo.HorasActuales;
                double baseReset = DatabaseHelper.GetValorEnUltimoReset(equipo.Id);
                double delta = valorActual - baseReset;

                if (delta < 0)
                    delta = valorActual;

                int restante = limite - (int)delta;

                string estado;
                if (restante <= 0)
                    estado = "VENCIDO";
                else if (restante <= 50)
                    estado = "CRITICO";
                else if (restante <= 100)
                    estado = "PROXIMO";
                else
                    estado = "OK";

                if (estado == "OK") continue;

                var asigs = asignaciones.FindAll(a => a.EquipoId == equipo.Id);

                foreach (var asig in asigs)
                {
                    var op = operarios.Find(o => o.Id == asig.OperarioId && o.Activo == 1);
                    if (op == null) continue;

                    string icono = estado == "VENCIDO" ? "🔴" :
                                   estado == "CRITICO" ? "🟠" : "🟡";

                    string detalle = equipo.FrecuenciaHoras > 0
                        ? $"Horas restantes: {restante}h"
                        : $"Días restantes: {restante}";

                    if (!string.IsNullOrWhiteSpace(op.TelegramChatId))
                    {
                        telegramSvc.EnviarMensaje(op.TelegramChatId,
                            $"{icono} <b>Aviso de mantenimiento: {estado}</b>\n\n" +
                            $"Equipo: <b>{equipo.Nombre}</b>\n" +
                            $"{detalle}\n\n" +
                            $"Usa <code>/completado {equipo.Id} descripción</code> cuando lo realices.");
                    }
                }

                if (!string.IsNullOrWhiteSpace(chatSupervisor))
                {
                    string icono = estado == "VENCIDO" ? "🔴" :
                                   estado == "CRITICO" ? "🟠" : "🟡";

                    string detalle = equipo.FrecuenciaHoras > 0
                        ? $"Horas restantes: {restante}h"
                        : $"Días restantes: {restante}";

                    string nombreOp = asigs.Count > 0
                        ? operarios.Find(o => o.Id == asigs[0].OperarioId)?.Nombre ?? "Sin asignar"
                        : "Sin asignar";

                    telegramSvc.EnviarMensaje(chatSupervisor,
                        $"{icono} <b>{estado}: {equipo.Nombre}</b>\n" +
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
            }
        }

        // ── Solicitar lecturas de horas cada 15 días ──────────
        private void SolicitarLecturasPendientes()
        {
            var telegramSvc = new TelegramService();
            var equipos = DatabaseHelper.GetEquipos();
            var asignaciones = DatabaseHelper.ObtenerAsignaciones();
            var operarios = DatabaseHelper.GetOperarios();

            int.TryParse(DatabaseHelper.LeerConfiguracion("DiassinLectura"), out int diasUmbral);
            if (diasUmbral <= 0) diasUmbral = 15;

            foreach (var equipo in equipos)
            {
                if (equipo.Activo != 1 || equipo.FrecuenciaHoras <= 0) continue;

                // Calcular días desde la ÚLTIMA LECTURA REAL registrada
                DateTime ultimaFecha;
                if (!string.IsNullOrEmpty(equipo.UltimaLectura) &&
                    DateTime.TryParse(equipo.UltimaLectura, out var ul))
                    ultimaFecha = ul;
                else if (!string.IsNullOrEmpty(equipo.FechaAlta) &&
                         DateTime.TryParse(equipo.FechaAlta, out var fa))
                    ultimaFecha = fa;
                else
                    continue;

                int diasSinLectura = (DateTime.Now - ultimaFecha).Days;

                // Solo avisar si supera el umbral configurado
                // Si ya superó el umbral → avisar cada día (reintento diario)
                if (diasSinLectura < diasUmbral) continue;

                // Buscar operarios asignados con Chat ID
                var asigs = asignaciones.FindAll(a => a.EquipoId == equipo.Id);
                foreach (var asig in asigs)
                {
                    var op = operarios.Find(o => o.Id == asig.OperarioId && o.Activo == 1);
                    if (op == null || string.IsNullOrWhiteSpace(op.TelegramChatId)) continue;

                    string mensajeTipo = diasSinLectura == diasUmbral
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
            }
        }
        

        // ── Reintentar validaciones sin respuesta (>24h) ──────
        private void ReintentarValidacionesSinRespuesta()
        {
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