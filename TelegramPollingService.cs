using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace AppMantenimiento
{
    public class TelegramPollingService
    {
        private static readonly HttpClient http = new HttpClient();
        private Timer _timer;
        private bool _procesando = false;
        private long _lastUpdateId = 0;

        public void Iniciar()
        {
            long.TryParse(DatabaseHelper.LeerConfiguracion("TelegramLastUpdateId"),
                          out _lastUpdateId);
            _timer = new Timer(PollUpdates, null,
                TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        public void Detener() => _timer?.Dispose();

        // ═══════════════════════════════════════════════════════
        private void PollUpdates(object state)
        {
            if (_procesando) return;
            _procesando = true;
            try
            {
                var token = DatabaseHelper.LeerConfiguracion("TelegramToken");
                if (string.IsNullOrWhiteSpace(token)) return;

                var url = $"https://api.telegram.org/bot{token}/getUpdates" +
                           $"?offset={_lastUpdateId + 1}&timeout=0&limit=20";
                var json = http.GetStringAsync(url).Result;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.GetProperty("ok").GetBoolean()) return;

                foreach (var update in root.GetProperty("result").EnumerateArray())
                {
                    _lastUpdateId = update.GetProperty("update_id").GetInt64();

                    if (update.TryGetProperty("message", out var msg))
                    {
                        var chatId = msg.GetProperty("chat").GetProperty("id")
                                          .GetInt64().ToString();
                        var texto = msg.TryGetProperty("text", out var t)
                                       ? t.GetString() ?? "" : "";
                        var fromName = msg.GetProperty("from")
                                          .TryGetProperty("first_name", out var fn)
                                       ? fn.GetString() ?? "" : "";

                        // Log visible en Ver → Salida → Depurar (Ctrl+Alt+O)
                        System.Diagnostics.Debug.WriteLine(
                            $"[Telegram] chatId={chatId} | nombre={fromName} | texto={texto}");

                        ProcesarMensaje(chatId, texto.Trim(), fromName);
                    }

                    DatabaseHelper.GuardarConfiguracion("TelegramLastUpdateId",
                        _lastUpdateId.ToString());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Polling ERROR] {ex.Message}");
            }
            finally { _procesando = false; }
        }

        // ═══════════════════════════════════════════════════════
        private void ProcesarMensaje(string chatId, string texto, string nombre)
        {
            var svc = new TelegramService();

            // ── /start ────────────────────────────────────────
            if (texto.StartsWith("/start"))
            {
                svc.EnviarMensaje(chatId,
                    $"👋 Hola <b>{nombre}</b>!\n\n" +
                    $"Tu <b>Chat ID</b> es:\n<code>{chatId}</code>\n\n" +
                    $"📋 Dáselo al supervisor para que te vincule.\n\n" +
                    $"<b>Comandos disponibles:</b>\n" +
                    $"/mis_equipos — Ver tus equipos y estado\n" +
                    $"/lectura [ID] [horas] — Registrar horas actuales\n" +
                    $"/completado [ID] [notas] — Registrar mantenimiento\n" +
                    $"/estado — Tu información\n" +
                    $"/ayuda — Todos los comandos");
                return;
            }

            // ── /ayuda ────────────────────────────────────────
            if (texto == "/ayuda" || texto == "/help")
            {
                svc.EnviarMensaje(chatId,
                    "📖 <b>Comandos disponibles:</b>\n\n" +
                    "/mis_equipos — Lista tus equipos con estado actual\n\n" +
                    "/lectura [ID] [horas] — Registra las horas actuales\n" +
                    "   Ejemplo: <code>/lectura 3 1540</code>\n\n" +
                    "/completado [ID] [notas] — Registra mantenimiento realizado\n" +
                    "   Ejemplo: <code>/completado 3 Cambio de aceite</code>\n\n" +
                    "/estado — Muestra tu perfil\n" +
                    "/start — Muestra tu Chat ID");
                return;
            }

            // ── /aprobar y /rechazar — solo supervisores ──────
            if (texto.StartsWith("/aprobar") || texto.StartsWith("/rechazar"))
            {
                // Buscar operario para verificar rol
                var ops = DatabaseHelper.GetOperarios();
                var opVal = ops.Find(o =>
                    !string.IsNullOrWhiteSpace(o.TelegramChatId) &&
                    o.TelegramChatId.Trim() == chatId.Trim() &&
                    o.Activo == 1);
                ProcesarValidacion(chatId, texto, svc, opVal);
                return;
            }

            // ── Verificar operario registrado ─────────────────
            var operarios = DatabaseHelper.GetOperarios();
            var operario = operarios.Find(o =>
                !string.IsNullOrWhiteSpace(o.TelegramChatId) &&
                o.TelegramChatId.Trim() == chatId.Trim() &&
                o.Activo == 1);

            System.Diagnostics.Debug.WriteLine(
                $"[Telegram] Operario encontrado: {operario?.Nombre ?? "NINGUNO"} " +
                $"para chatId={chatId}");

            if (operario == null)
            {
                svc.EnviarMensaje(chatId,
                    $"⚠️ <b>No estás registrado en el sistema.</b>\n\n" +
                    $"Tu Chat ID: <code>{chatId}</code>\n" +
                    $"Comunícaselo al supervisor para que te vincule.\n\n" +
                    $"Si ya te registraron, escribe /start para verificar tu ID.");
                return;
            }

            // ── /mis_equipos ──────────────────────────────────
            if (texto.StartsWith("/mis_equipos") || texto == "/equipos")
            {
                MostrarMisEquipos(chatId, operario, svc);
                return;
            }

            // ── /lectura [ID] [horas] ─────────────────────────
            if (texto.StartsWith("/lectura"))
            {
                ProcesarLectura(chatId, texto, operario, svc);
                return;
            }

            // ── /completado [ID] [descripción] ───────────────
            if (texto.StartsWith("/completado"))
            {
                ProcesarCompletado(chatId, texto, operario, svc);
                return;
            }

            // ── /estado ───────────────────────────────────────
            if (texto == "/estado")
            {
                var asigs = DatabaseHelper.ObtenerAsignaciones()
                                          .FindAll(a => a.OperarioId == operario.Id);
                svc.EnviarMensaje(chatId,
                    $"👤 <b>{operario.Nombre}</b>\n" +
                    $"Rol: {operario.Rol}\n" +
                    $"Estado: ✅ Activo\n" +
                    $"Equipos asignados: {asigs.Count}\n\n" +
                    $"/mis_equipos para ver el detalle");
                return;
            }

            // ── Mensaje no reconocido ─────────────────────────
            svc.EnviarMensaje(chatId,
                "🤖 No entiendo ese mensaje.\n\nEscribe /ayuda para ver los comandos.");
        }

        // ═══════════════════════════════════════════════════════
        private void MostrarMisEquipos(string chatId, Operario operario, TelegramService svc)
        {
            var asigs = DatabaseHelper.ObtenerAsignaciones()
                                      .FindAll(a => a.OperarioId == operario.Id);
            if (asigs.Count == 0)
            {
                svc.EnviarMensaje(chatId, "No tienes equipos asignados actualmente.");
                return;
            }

            var equipos = DatabaseHelper.GetEquipos();
            var msg = $"🔧 <b>Equipos de {operario.Nombre}:</b>\n\n";

            foreach (var asig in asigs)
            {
                var eq = equipos.Find(e => e.Id == asig.EquipoId);
                if (eq == null) continue;

                var estado = DatabaseHelper.CalcularEstado(eq);
                string icono = estado switch
                {
                    "VENCIDO" => "🔴",
                    "CRITICO" => "🟠",
                    "PROXIMO" => "🟡",
                    _ => "🟢"
                };

                string detalle = eq.FrecuenciaHoras > 0
                    ? $"Horas: {eq.HorasActuales}h | Restante: {eq.HorasParaMantenimiento}h"
                    : $"Días restantes: {eq.HorasParaMantenimiento}";

                string ultimaLect = string.IsNullOrEmpty(eq.UltimaLectura)
                    ? "Nunca" : eq.UltimaLectura;

                msg += $"{icono} <b>[{eq.Id}] {eq.Nombre}</b> — {estado}\n" +
                       $"   {detalle}\n" +
                       $"   Última lectura: {ultimaLect}\n\n";
            }

            msg += "📌 <code>/lectura [ID] [horas]</code> — actualizar horas\n" +
                   "✅ <code>/completado [ID] notas</code> — registrar mantenimiento";
            svc.EnviarMensaje(chatId, msg);
        }

        // ═══════════════════════════════════════════════════════
        private void ProcesarLectura(string chatId, string texto,
            Operario operario, TelegramService svc)
        {
            var p = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3 || !int.TryParse(p[1], out int eqId) ||
                !int.TryParse(p[2], out int horas))
            {
                svc.EnviarMensaje(chatId,
                    "❓ Uso correcto:\n<code>/lectura [ID_equipo] [horas_actuales]</code>\n\n" +
                    "Ejemplo: <code>/lectura 3 1540</code>\n\n" +
                    "Usa /mis_equipos para ver los IDs.");
                return;
            }

            var eq = DatabaseHelper.GetEquipos().Find(e => e.Id == eqId);
            if (eq == null)
            {
                svc.EnviarMensaje(chatId, $"❌ No existe ningún equipo con ID {eqId}.");
                return;
            }

            var misAsigs = DatabaseHelper.ObtenerAsignaciones()
                                         .FindAll(a => a.OperarioId == operario.Id);
            if (misAsigs.Find(a => a.EquipoId == eqId) == null)
            {
                svc.EnviarMensaje(chatId,
                    $"❌ No tienes asignado el equipo <b>{eq.Nombre}</b>.\n" +
                    "Usa /mis_equipos para ver tus equipos.");
                return;
            }

            // Lectura duplicada hoy
            if (DatabaseHelper.ExisteLecturaHoy(eqId))
            {
                svc.EnviarMensaje(chatId,
                    $"⚠️ Ya existe una lectura hoy para <b>{eq.Nombre}</b>.\n\n" +
                    $"Si necesitas corregirla, comunícalo al supervisor.");
                return;
            }

            // Horas inválidas
            if (horas <= 0)
            {
                svc.EnviarMensaje(chatId,
                    $"❌ Las horas deben ser un número positivo. Recibido: {horas}");
                return;
            }

            // Reducción de horas → validación
            if (horas < eq.HorasActuales)
            {
                int valId = DatabaseHelper.CrearValidacionPendiente(
                    eqId, operario.Id, eq.HorasActuales, horas, chatId);

                svc.EnviarMensaje(chatId,
                    $"⚠️ <b>Lectura pendiente de validación</b>\n\n" +
                    $"Las horas indicadas ({horas}h) son menores que las registradas " +
                    $"({eq.HorasActuales}h).\n\n" +
                    $"Se ha notificado al supervisor.\n" +
                    $"🔖 Código: <b>#{valId}</b>\n\n" +
                    $"Recibirás confirmación cuando se resuelva.");

                NotificarSupervisoresValidacion(valId, eq, operario, horas, svc);
                return;
            }

            // Registrar lectura normal
            DatabaseHelper.RegistrarLecturaHoras(eqId, horas, operario.Nombre);
            eq.HorasActuales = horas;
            var estado = DatabaseHelper.CalcularEstado(eq);

            string respuesta =
                $"✅ <b>Lectura registrada</b>\n\n" +
                $"📌 Equipo: <b>{eq.Nombre}</b>\n" +
                $"⏱ Horas actuales: <b>{horas}h</b>\n" +
                $"🗓 {DateTime.Now:dd/MM/yyyy HH:mm}\n";

            if (eq.FrecuenciaHoras > 0)
            {
                int restantes = eq.HorasParaMantenimiento;
                respuesta += $"📊 Estado: <b>{estado}</b>";
                if (restantes <= 0)
                    respuesta += "\n\n⛔ <b>MANTENIMIENTO VENCIDO</b> — Avisa al supervisor.";
                else if (estado == "CRITICO")
                    respuesta += $"\n\n🚨 Solo quedan <b>{restantes}h</b>. Planifica urgente.";
                else if (estado == "PROXIMO")
                    respuesta += $"\n\n⏰ Quedan <b>{restantes}h</b>. Planifica pronto.";
            }

            svc.EnviarMensaje(chatId, respuesta);

            if (estado == "CRITICO" || estado == "VENCIDO")
            {
                string chatSup = DatabaseHelper.LeerConfiguracion("TelegramChatSupervisor");
                if (!string.IsNullOrWhiteSpace(chatSup))
                {
                    string ico = estado == "VENCIDO" ? "🔴" : "🟠";
                    svc.EnviarMensaje(chatSup,
                        $"{ico} <b>{estado}: {eq.Nombre}</b>\n" +
                        $"Operario: {operario.Nombre}\n" +
                        $"Horas: {horas}h — Restante: {eq.HorasParaMantenimiento}h");
                }
            }
        }

        // ═══════════════════════════════════════════════════════
        private void ProcesarCompletado(string chatId, string texto,
            Operario operario, TelegramService svc)
        {
            var p = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2 || !int.TryParse(p[1], out int eqId))
            {
                svc.EnviarMensaje(chatId,
                    "❓ Uso correcto:\n<code>/completado [ID_equipo] [notas]</code>\n\n" +
                    "Ejemplo: <code>/completado 3 Cambio de aceite</code>\n\n" +
                    "Usa /mis_equipos para ver los IDs.");
                return;
            }

            var eq = DatabaseHelper.GetEquipos().Find(e => e.Id == eqId);
            if (eq == null)
            {
                svc.EnviarMensaje(chatId, $"❌ No existe ningún equipo con ID {eqId}.");
                return;
            }

            var misAsigs = DatabaseHelper.ObtenerAsignaciones()
                                         .FindAll(a => a.OperarioId == operario.Id);
            if (misAsigs.Find(a => a.EquipoId == eqId) == null)
            {
                svc.EnviarMensaje(chatId,
                    $"❌ No tienes asignado el equipo <b>{eq.Nombre}</b>.");
                return;
            }

            string notas = p.Length > 2
                ? string.Join(" ", p, 2, p.Length - 2)
                : "Mantenimiento completado";

            DatabaseHelper.RegistrarMantenimiento(eqId, notas, operario.Nombre);

            svc.EnviarMensaje(chatId,
                $"✅ <b>Mantenimiento registrado — ciclo reiniciado</b>\n\n" +
                $"📌 Equipo: <b>{eq.Nombre}</b>\n" +
                $"⏱ Horas al cierre: <b>{eq.HorasActuales}h</b>\n" +
                $"🗓 {DateTime.Now:dd/MM/yyyy HH:mm}\n" +
                $"👤 {operario.Nombre}\n" +
                $"📝 {notas}\n\n" +
                $"🔄 Contador reiniciado desde <b>{eq.HorasActuales}h</b>. ¡Gracias! 👍");

            string chatSup = DatabaseHelper.LeerConfiguracion("TelegramChatSupervisor");
            if (!string.IsNullOrWhiteSpace(chatSup))
                svc.EnviarMensaje(chatSup,
                    $"🔔 <b>Mantenimiento completado</b>\n\n" +
                    $"📌 {eq.Nombre}\n👤 {operario.Nombre} — {eq.HorasActuales}h\n" +
                    $"🗓 {DateTime.Now:dd/MM/yyyy HH:mm}\n📝 {notas}");
        }

        // ═══════════════════════════════════════════════════════
        private void ProcesarValidacion(string chatId, string texto,
            TelegramService svc, Operario operario)
        {
            bool esSupervisor = operario != null &&
                (operario.Rol == "Supervisor" || operario.Rol == "Administrador");
            string chatSup = DatabaseHelper.LeerConfiguracion("TelegramChatSupervisor");

            if (!esSupervisor && chatId.Trim() != chatSup.Trim())
            {
                svc.EnviarMensaje(chatId,
                    "❌ Solo los supervisores pueden aprobar o rechazar validaciones.");
                return;
            }

            var p = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2 || !int.TryParse(p[1], out int valId))
            {
                svc.EnviarMensaje(chatId,
                    "❓ Uso:\n<code>/aprobar [ID]</code>\n<code>/rechazar [ID]</code>");
                return;
            }

            bool esAprobar = texto.StartsWith("/aprobar");
            var validaciones = DatabaseHelper.GetValidacionesPendientes();
            var val = validaciones.Find(v => v.Id == valId);

            if (val == null)
            {
                svc.EnviarMensaje(chatId,
                    $"❌ No existe la validación #{valId} o ya fue resuelta.");
                return;
            }

            var equipos = DatabaseHelper.GetEquipos();
            var eq = equipos.Find(e => e.Id == val.EquipoId);

            if (esAprobar)
            {
                DatabaseHelper.RegistrarLecturaHoras(
                    val.EquipoId, val.HorasNuevas,
                    operario?.Nombre ?? "Supervisor");
                DatabaseHelper.ResolverValidacion(valId, "Aprobada");

                svc.EnviarMensaje(chatId,
                    $"✅ Validación #{valId} <b>aprobada</b>\n" +
                    $"Equipo: {eq?.Nombre}\n" +
                    $"{val.HorasAntiguas}h → {val.HorasNuevas}h");

                if (!string.IsNullOrWhiteSpace(val.ChatIdOperario))
                    svc.EnviarMensaje(val.ChatIdOperario,
                        $"✅ Tu lectura reducida fue <b>aprobada</b>.\n" +
                        $"Equipo: {eq?.Nombre} — Horas: <b>{val.HorasNuevas}h</b>");
            }
            else
            {
                DatabaseHelper.ResolverValidacion(valId, "Rechazada");

                svc.EnviarMensaje(chatId,
                    $"❌ Validación #{valId} <b>rechazada</b>\n" +
                    $"Equipo: {eq?.Nombre} — Se mantienen {val.HorasAntiguas}h");

                if (!string.IsNullOrWhiteSpace(val.ChatIdOperario))
                    svc.EnviarMensaje(val.ChatIdOperario,
                        $"❌ Tu lectura reducida fue <b>rechazada</b>.\n" +
                        $"Equipo: {eq?.Nombre} — Se mantienen <b>{val.HorasAntiguas}h</b>");
            }
        }

        // ═══════════════════════════════════════════════════════
        private void NotificarSupervisoresValidacion(int valId, Equipo eq,
            Operario operario, int horasNuevas, TelegramService svc)
        {
            string msg =
                $"🔔 <b>Solicitud de validación #{valId}</b>\n\n" +
                $"Operario <b>{operario.Nombre}</b> reporta horas reducidas:\n\n" +
                $"📌 Equipo: <b>{eq.Nombre}</b>\n" +
                $"📉 Anteriores: {eq.HorasActuales}h → Reportadas: {horasNuevas}h\n" +
                $"🗓 {DateTime.Now:dd/MM/yyyy HH:mm}\n\n" +
                $"✅ <code>/aprobar {valId}</code>\n" +
                $"❌ <code>/rechazar {valId}</code>";

            string chatSup = DatabaseHelper.LeerConfiguracion("TelegramChatSupervisor");
            if (!string.IsNullOrWhiteSpace(chatSup))
                svc.EnviarMensaje(chatSup, msg);

            // También a supervisores/admins con Chat ID registrado
            foreach (var op in DatabaseHelper.GetOperarios())
            {
                if ((op.Rol == "Supervisor" || op.Rol == "Administrador") &&
                    op.Activo == 1 &&
                    !string.IsNullOrWhiteSpace(op.TelegramChatId) &&
                    op.TelegramChatId.Trim() != chatSup.Trim())
                {
                    svc.EnviarMensaje(op.TelegramChatId.Trim(), msg);
                }
            }
        }
    }
}