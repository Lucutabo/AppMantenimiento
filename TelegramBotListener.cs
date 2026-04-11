using Org.BouncyCastle.Utilities;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AppMantenimiento
{
    public class TelegramBotListener
    {
        private static readonly HttpClient _http = new HttpClient();
        private string Token => DatabaseHelper.LeerConfiguracion("TelegramToken");
        private int _lastUpdateId = 0;
        private bool _running = false;
        private readonly Dictionary<long, EstadoConversacion> _estados = new();

        public void Iniciar()
        {
            if (_running) return;
            _running = true;
            Task.Run(BucleEscucha);
        }

        public void Detener() => _running = false;

        // ─────────────────────────────────────────────────────────────
        //  BUCLE PRINCIPAL DE ESCUCHA
        // ─────────────────────────────────────────────────────────────
        private async Task BucleEscucha()
        {
            while (_running)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(Token)) { await Task.Delay(5000); continue; }

                    var url = $"https://api.telegram.org/bot{Token}/getUpdates?offset={_lastUpdateId + 1}&timeout=20";
                    var resp = await _http.GetStringAsync(url);
                    var doc = JsonDocument.Parse(resp);
                    var root = doc.RootElement;

                    if (!root.GetProperty("ok").GetBoolean()) { await Task.Delay(3000); continue; }

                    foreach (var upd in root.GetProperty("result").EnumerateArray())
                    {
                        _lastUpdateId = upd.GetProperty("update_id").GetInt32();
                        if (!upd.TryGetProperty("message", out var msg)) continue;
                        if (!msg.TryGetProperty("text", out var txtEl)) continue;

                        long chatId = msg.GetProperty("chat").GetProperty("id").GetInt64();
                        string texto = txtEl.GetString() ?? "";
                        await ProcesarMensaje(chatId, texto.Trim());
                    }
                }
                catch { }

                await Task.Delay(1500);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  PROCESAR MENSAJE ENTRANTE
        // ─────────────────────────────────────────────────────────────
        private async Task ProcesarMensaje(long chatId, string texto)
        {
            var op = DatabaseHelper.ObtenerOperarioPorChatId(chatId.ToString());
            if (op == null)
            {
                await Enviar(chatId.ToString(),
                    "❌ Tu Telegram no está registrado en el sistema\\.\n" +
                    $"Tu Chat ID es: `{chatId}`\n\n" +
                    "Pide al supervisor que configure tu Chat ID\\.");
                return;
            }

            if (texto.StartsWith("/"))
            {
                _estados.Remove(chatId);

                if (texto == "/start")
                {
                    await Enviar(chatId.ToString(),
                        $"👋 Hola *{EscaparMarkdown(op.Nombre)}*\\!\n\n" +
                        "Comandos disponibles:\n" +
                        "• /mis\\_equipos — Ver tus equipos\n" +
                        "• /lectura — Registrar lectura\n" +
                        "• /observacion — Añadir observación\n" +
                        "• /completado — Marcar mantenimiento completado");
                    return;
                }
                if (texto == "/mis_equipos") { await MostrarMisEquipos(chatId, op); return; }
                if (texto == "/lectura") { await IniciarFlujoLectura(chatId, op); return; }
                if (texto == "/observacion") { await IniciarFlujoObservacion(chatId, op); return; }
                if (texto == "/completado") { await IniciarFlujoCompletado(chatId, op); return; }
                if (texto == "/cancelar") { await Enviar(chatId.ToString(), "✅ Operación cancelada\\."); return; }

                await Enviar(chatId.ToString(), "❓ Comando no reconocido\\. Usa /start para ver los disponibles\\.");
                return;
            }

            if (_estados.TryGetValue(chatId, out var estado))
            {
                await ContinuarFlujo(chatId, op, estado, texto);
                return;
            }

            await Enviar(chatId.ToString(), "Usa /start para ver los comandos disponibles\\.");
        }

        // ─────────────────────────────────────────────────────────────
        //  MIS EQUIPOS (con cálculo correcto igual que el Dashboard)
        // ─────────────────────────────────────────────────────────────
        private async Task MostrarMisEquipos(long chatId, Operario op)
        {
            var asignaciones = DatabaseHelper.ObtenerAsignacionesPorOperario(op.Id);
            if (asignaciones.Count == 0)
            {
                await Enviar(chatId.ToString(), "📋 No tienes equipos asignados actualmente\\.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("🔧 *Tus equipos asignados:*\n");

            foreach (var a in asignaciones)
            {
                var eq = DatabaseHelper.ObtenerEquipoPorId(a.EquipoId);
                if (eq == null) continue;

                // Mismo cálculo que el Dashboard
                string restanteTexto = "Sin configurar";
                string estadoEmoji = "✅";

                if (eq.FrecuenciaMantenimiento > 0)
                {
                    double baseReset = DatabaseHelper.GetValorEnUltimoReset(eq.Id);
                    double delta = eq.HorasActuales - baseReset;
                    if (delta < 0) delta = eq.HorasActuales;
                    int restante = eq.FrecuenciaMantenimiento - (int)delta;

                    if (restante <= 0)
                    {
                        restanteTexto = $"VENCIDO \\({Math.Abs(restante)} h\\)";
                        estadoEmoji = "🔴";
                    }
                    else if (restante <= 50)
                    {
                        restanteTexto = $"{restante} h restantes";
                        estadoEmoji = "🟠";
                    }
                    else
                    {
                        restanteTexto = $"{restante} h restantes";
                        estadoEmoji = "✅";
                    }
                }

                string ultimaLect = eq.HorasActuales > 0
                    ? $"{eq.HorasActuales} h"
                    : "Sin lecturas";

                sb.AppendLine($"{estadoEmoji} *{EscaparMarkdown(eq.Nombre)}*");
                sb.AppendLine($"  📊 Lectura actual: {EscaparMarkdown(ultimaLect)}");
                sb.AppendLine($"  🔔 Restante: {EscaparMarkdown(restanteTexto)}");
                sb.AppendLine($"  ⚙️ Frecuencia: cada {eq.FrecuenciaMantenimiento} h\n");
            }

            await Enviar(chatId.ToString(), sb.ToString());
        }

        // ─────────────────────────────────────────────────────────────
        //  INICIO DE FLUJOS
        // ─────────────────────────────────────────────────────────────
        private async Task IniciarFlujoLectura(long chatId, Operario op)
        {
            var asignaciones = DatabaseHelper.ObtenerAsignacionesPorOperario(op.Id);
            if (asignaciones.Count == 0)
            {
                await Enviar(chatId.ToString(), "📋 No tienes equipos asignados\\.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("🔧 *¿De qué equipo quieres registrar lectura?*\n");
            for (int i = 0; i < asignaciones.Count; i++)
            {
                var eq = DatabaseHelper.ObtenerEquipoPorId(asignaciones[i].EquipoId);
                if (eq != null) sb.AppendLine($"{i + 1}\\. {EscaparMarkdown(eq.Nombre)}");
            }
            sb.AppendLine("\nResponde con el número\\.");
            _estados[chatId] = new EstadoConversacion { Paso = "lectura_seleccionar_equipo", Asignaciones = asignaciones };
            await Enviar(chatId.ToString(), sb.ToString());
        }

        private async Task IniciarFlujoObservacion(long chatId, Operario op)
        {
            var asignaciones = DatabaseHelper.ObtenerAsignacionesPorOperario(op.Id);
            if (asignaciones.Count == 0)
            {
                await Enviar(chatId.ToString(), "📋 No tienes equipos asignados\\.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("📝 *¿En qué equipo quieres añadir una observación?*\n");
            for (int i = 0; i < asignaciones.Count; i++)
            {
                var eq = DatabaseHelper.ObtenerEquipoPorId(asignaciones[i].EquipoId);
                if (eq != null) sb.AppendLine($"{i + 1}\\. {EscaparMarkdown(eq.Nombre)}");
            }
            sb.AppendLine("\nResponde con el número\\.");
            _estados[chatId] = new EstadoConversacion { Paso = "obs_seleccionar_equipo", Asignaciones = asignaciones };
            await Enviar(chatId.ToString(), sb.ToString());
        }

        private async Task IniciarFlujoCompletado(long chatId, Operario op)
        {
            var asignaciones = DatabaseHelper.ObtenerAsignacionesPorOperario(op.Id);
            if (asignaciones.Count == 0)
            {
                await Enviar(chatId.ToString(), "📋 No tienes equipos asignados\\.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("✅ *¿Qué equipo has completado?*\n");
            for (int i = 0; i < asignaciones.Count; i++)
            {
                var eq = DatabaseHelper.ObtenerEquipoPorId(asignaciones[i].EquipoId);
                if (eq != null) sb.AppendLine($"{i + 1}\\. {EscaparMarkdown(eq.Nombre)}");
            }
            sb.AppendLine("\nResponde con el número\\.");
            _estados[chatId] = new EstadoConversacion { Paso = "completado_seleccionar_equipo", Asignaciones = asignaciones };
            await Enviar(chatId.ToString(), sb.ToString());
        }

        // ─────────────────────────────────────────────────────────────
        //  MÁQUINA DE ESTADOS
        // ─────────────────────────────────────────────────────────────
        private async Task ContinuarFlujo(long chatId, Operario op, EstadoConversacion estado, string texto)
        {
            switch (estado.Paso)
            {
                // ── LECTURA: seleccionar equipo ───────────────────────
                case "lectura_seleccionar_equipo":
                    {
                        if (!int.TryParse(texto, out int idx) || idx < 1 || idx > estado.Asignaciones.Count)
                        { await Enviar(chatId.ToString(), "❌ Número no válido\\. Inténtalo de nuevo\\."); return; }

                        var equipo = DatabaseHelper.ObtenerEquipoPorId(estado.Asignaciones[idx - 1].EquipoId);
                        if (equipo == null) { _estados.Remove(chatId); return; }

                        // Comprobar si ya existe lectura hoy
                        if (DatabaseHelper.ExisteLecturaHoy(equipo.Id))
                        {
                            _estados.Remove(chatId);
                            await Enviar(chatId.ToString(),
                                $"⚠️ *Ya existe una lectura hoy* para *{EscaparMarkdown(equipo.Nombre)}*\\.\n\n" +
                                $"📊 Lectura registrada: *{EscaparMarkdown(equipo.HorasActuales.ToString())} h*\n\n" +
                                $"Si necesitas corregirla, comunícaselo al supervisor\\.");
                            return;
                        }

                        estado.EquipoSeleccionado = equipo;
                        estado.Paso = "lectura_introducir_valor";
                        string limInfo = equipo.FrecuenciaMantenimiento > 0
                            ? $"Límite: *{equipo.FrecuenciaMantenimiento}h*" : "Sin límite configurado";
                        await Enviar(chatId.ToString(),
                            $"🔧 Equipo: *{EscaparMarkdown(equipo.Nombre)}*\n{EscaparMarkdown(limInfo)}\n\n" +
                            "Introduce el valor de la lectura \\(horas\\):");
                        break;
                    }

                // ── LECTURA: introducir y validar valor ───────────────
                case "lectura_introducir_valor":
                    {
                        if (!double.TryParse(texto.Replace(",", "."),
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double valor))
                        {
                            await Enviar(chatId.ToString(),
                                "❌ Valor no válido\\. Introduce solo el número \\(ej: 492\\.5\\):");
                            return;
                        }

                        var equipo = estado.EquipoSeleccionado;

                        // Validación 1: valor negativo o cero
                        if (valor <= 0)
                        {
                            await Enviar(chatId.ToString(), "❌ El valor debe ser mayor que cero\\.");
                            return;
                        }

                        double lecturaAnterior = ParsearLectura(equipo.UltimaLectura);

                        // Validación 2: lectura INFERIOR → rechazar
                        if (lecturaAnterior > 0 && valor < lecturaAnterior)
                        {
                            _estados.Remove(chatId);
                            await Enviar(chatId.ToString(),
                                $"❌ *Lectura rechazada*\n\n" +
                                $"⚙️ Equipo: *{EscaparMarkdown(equipo.Nombre)}*\n" +
                                $"📊 Última lectura: *{EscaparMarkdown(lecturaAnterior.ToString("F1"))}h*\n" +
                                $"📉 Valor introducido: *{EscaparMarkdown(valor.ToString("F1"))}h*\n\n" +
                                $"Un equipo *no puede bajar horas*\\.\n" +
                                $"Revisa el valor e inténtalo de nuevo con /lectura\\.");
                            await NotificarSupervisores(
                                $"⚠️ *Intento de lectura anómala*\n\n" +
                                $"⚙️ Equipo: *{EscaparMarkdown(equipo.Nombre)}*\n" +
                                $"👤 Operario: *{EscaparMarkdown(op.Nombre)}*\n" +
                                $"📊 Última lectura: *{EscaparMarkdown(lecturaAnterior.ToString("F1"))}h*\n" +
                                $"📉 Valor intentado: *{EscaparMarkdown(valor.ToString("F1"))}h*\n" +
                                $"🕐 {EscaparMarkdown(DateTime.Now.ToString("dd/MM/yyyy HH:mm"))}",
                                equipo.Nombre, op.Nombre);
                            return;
                        }

                        // Validación 3: lectura IGUAL → pedir confirmación
                        if (lecturaAnterior > 0 && valor == lecturaAnterior)
                        {
                            estado.Paso = "lectura_confirmar_valor";
                            estado.ValorPendiente = valor;
                            await Enviar(chatId.ToString(),
                                $"⚠️ La lectura *{EscaparMarkdown(valor.ToString("F1"))}h* es *idéntica* a la anterior\\.\n\n" +
                                $"¿Es correcta? Responde *sí* para confirmar o *no* para corregirla\\.");
                            return;
                        }

                        // Validación 4: salto sospechosamente alto (>50% frecuencia) → pedir confirmación
                        if (lecturaAnterior > 0 && equipo.FrecuenciaMantenimiento > 0)
                        {
                            double salto = valor - lecturaAnterior;
                            double umbral = equipo.FrecuenciaMantenimiento * 0.5;
                            if (salto > umbral)
                            {
                                estado.Paso = "lectura_confirmar_valor";
                                estado.ValorPendiente = valor;
                                await Enviar(chatId.ToString(),
                                    $"⚠️ *Salto de horas inusualmente alto*\n\n" +
                                    $"📊 Lectura anterior: *{EscaparMarkdown(lecturaAnterior.ToString("F1"))}h*\n" +
                                    $"📈 Lectura nueva: *{EscaparMarkdown(valor.ToString("F1"))}h*\n" +
                                    $"↕️ Diferencia: *{EscaparMarkdown(salto.ToString("F1"))}h*\n\n" +
                                    $"¿Es correcto? Responde *sí* para confirmar o *no* para corregirla\\.");
                                return;
                            }
                        }

                        // Todo correcto → guardar
                        await GuardarLecturaValida(chatId, op, equipo, valor);
                        break;
                    }

                // ── LECTURA: confirmar valor sospechoso ───────────────
                case "lectura_confirmar_valor":
                    {
                        var respuesta = texto.ToLower().Trim();
                        if (respuesta == "sí" || respuesta == "si" || respuesta == "s")
                            await GuardarLecturaValida(chatId, op, estado.EquipoSeleccionado, estado.ValorPendiente);
                        else
                        {
                            _estados.Remove(chatId);
                            await Enviar(chatId.ToString(),
                                "↩️ Lectura cancelada\\. Usa /lectura para introducir el valor correcto\\.");
                        }
                        break;
                    }

                // ── OBSERVACIÓN: seleccionar equipo ──────────────────
                case "obs_seleccionar_equipo":
                    {
                        if (!int.TryParse(texto, out int idx) || idx < 1 || idx > estado.Asignaciones.Count)
                        { await Enviar(chatId.ToString(), "❌ Número no válido\\."); return; }

                        var equipo = DatabaseHelper.ObtenerEquipoPorId(estado.Asignaciones[idx - 1].EquipoId);
                        if (equipo == null) { _estados.Remove(chatId); return; }
                        estado.EquipoSeleccionado = equipo;
                        estado.Paso = "obs_introducir_texto";
                        await Enviar(chatId.ToString(),
                            $"📝 Equipo: *{EscaparMarkdown(equipo.Nombre)}*\n\nEscribe la observación:");
                        break;
                    }

                // ── OBSERVACIÓN: guardar texto ────────────────────────
                case "obs_introducir_texto":
                    {
                        var equipo = estado.EquipoSeleccionado;
                        DatabaseHelper.GuardarLectura(new Lectura
                        {
                            EquipoId = equipo.Id,
                            NombreEquipo = equipo.Nombre,
                            Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                            Descripcion = $"[OBS] {texto}",
                            Operario = op.Nombre,
                            TipoRegistro = "Observacion"
                        });
                        _estados.Remove(chatId);
                        await Enviar(chatId.ToString(),
                            $"✅ Observación registrada en *{EscaparMarkdown(equipo.Nombre)}*\\.");
                        await NotificarSupervisores(
                            $"📝 *Observación de operario*\n\n" +
                            $"⚙️ Equipo: *{EscaparMarkdown(equipo.Nombre)}*\n" +
                            $"👤 Operario: {EscaparMarkdown(op.Nombre)}\n" +
                            $"🕐 {EscaparMarkdown(DateTime.Now.ToString("dd/MM/yyyy HH:mm"))}\n\n" +
                            $"📋 _{EscaparMarkdown(texto)}_",
                            equipo.Nombre, op.Nombre);
                        break;
                    }

                // ── COMPLETADO: seleccionar equipo ────────────────────
                case "completado_seleccionar_equipo":
                    {
                        if (!int.TryParse(texto, out int idx) || idx < 1 || idx > estado.Asignaciones.Count)
                        { await Enviar(chatId.ToString(), "❌ Número no válido\\."); return; }

                        var equipo = DatabaseHelper.ObtenerEquipoPorId(estado.Asignaciones[idx - 1].EquipoId);
                        if (equipo == null) { _estados.Remove(chatId); return; }
                        estado.EquipoSeleccionado = equipo;
                        estado.Paso = "completado_introducir_lectura";
                        await Enviar(chatId.ToString(),
                            $"✅ Equipo: *{EscaparMarkdown(equipo.Nombre)}*\n\n" +
                            "Introduce la lectura actual al completar \\(horas\\):");
                        break;
                    }

                // ── COMPLETADO: guardar lectura final ─────────────────
                case "completado_introducir_lectura":
                    {
                        if (!double.TryParse(texto.Replace(",", "."),
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double valorFinal))
                        { await Enviar(chatId.ToString(), "❌ Valor no válido\\. Introduce solo el número:"); return; }

                        var equipo = estado.EquipoSeleccionado;
                        DatabaseHelper.GuardarLectura(new Lectura
                        {
                            EquipoId = equipo.Id,
                            NombreEquipo = equipo.Nombre,
                            Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                            Descripcion = "[COMPLETADO]",
                            Operario = op.Nombre,
                            HorasActuales = (int)valorFinal,
                            TipoRegistro = "Completado"
                        });
                        DatabaseHelper.ActualizarUltimaLectura(equipo.Id);
                        DatabaseHelper.RegistrarReset(equipo.Id, valorFinal);
                        _estados.Remove(chatId);
                        await Enviar(chatId.ToString(),
                            $"🎉 ¡Mantenimiento completado\\!\n" +
                            $"Equipo: *{EscaparMarkdown(equipo.Nombre)}*\n" +
                            $"Lectura: *{EscaparMarkdown(valorFinal.ToString("F1"))}h*");
                        await NotificarSupervisores(
                            $"✅ *Mantenimiento completado*\n\n" +
                            $"⚙️ Equipo: *{EscaparMarkdown(equipo.Nombre)}*\n" +
                            $"👤 Operario: {EscaparMarkdown(op.Nombre)}\n" +
                            $"🕐 {EscaparMarkdown(DateTime.Now.ToString("dd/MM/yyyy HH:mm"))}\n" +
                            $"📊 Lectura: *{EscaparMarkdown(valorFinal.ToString("F1"))}h*",
                            equipo.Nombre, op.Nombre);
                        break;
                    }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  GUARDAR LECTURA VÁLIDA
        // ─────────────────────────────────────────────────────────────
        private async Task GuardarLecturaValida(long chatId, Operario op, Equipo equipo, double valor)
        {
            DatabaseHelper.GuardarLectura(new Lectura
            {
                EquipoId = equipo.Id,
                NombreEquipo = equipo.Nombre,
                Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Descripcion = "Lectura de horas",
                Operario = op.Nombre,
                HorasActuales = (int)valor,
                TipoRegistro = "Lectura",
                Valor = valor.ToString("F1"),  
                Tipo = "Lectura"
            });

            DatabaseHelper.RegistrarLecturaHoras(equipo.Id, (int)valor, op.Nombre);
            _estados.Remove(chatId);

            await Enviar(chatId.ToString(),
                $"✅ Lectura de *{EscaparMarkdown(valor.ToString("F1"))}h* registrada en " +
                $"*{EscaparMarkdown(equipo.Nombre)}*\\.");
            await ComprobarUmbralYNotificar(equipo, valor, op.Nombre);
        }

        // ─────────────────────────────────────────────────────────────
        //  PARSEAR LECTURA ANTERIOR ("492.5h" → 492.5)
        // ─────────────────────────────────────────────────────────────
        private static double ParsearLectura(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return 0;
            var limpio = texto.Replace("h", "").Replace(",", ".").Trim();
            return double.TryParse(limpio,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double v) ? v : 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  COMPROBAR UMBRAL Y NOTIFICAR
        // ─────────────────────────────────────────────────────────────
        private async Task ComprobarUmbralYNotificar(Equipo equipo, double valorActual, string operario)
        {
            if (equipo == null) return;

            int limite = equipo.FrecuenciaHoras;
            if (limite <= 0) return;

            // SIEMPRE leer el reset real desde base de datos
            double baseReset = DatabaseHelper.GetValorEnUltimoReset(equipo.Id);
            double delta = valorActual - baseReset;

            // Si por cualquier motivo llega una lectura menor que el reset, no avisar
            if (delta < 0) return;

            double porcentaje = (delta / limite) * 100.0;
            if (porcentaje < 80) return;

            bool vencido = delta >= limite;
            double restante = limite - delta;

            string emoji = vencido ? "🚨" : "⚠️";
            string estadoTxt = vencido
                ? $"VENCIDO \\(exceso: {EscaparMarkdown(Math.Abs(restante).ToString("F0"))}h\\)"
                : $"PRÓXIMO — quedan {EscaparMarkdown(restante.ToString("F0"))}h";

            await NotificarSupervisores(
                $"{emoji} *Alerta de mantenimiento*\n\n" +
                $"⚙️ Equipo: *{EscaparMarkdown(equipo.Nombre)}*\n" +
                $"👤 Operario: {EscaparMarkdown(operario)}\n" +
                $"📊 Lectura actual: *{EscaparMarkdown(valorActual.ToString("F1"))}h*\n" +
                $"🧮 Desde último mantenimiento: *{EscaparMarkdown(delta.ToString("F1"))}h* de *{EscaparMarkdown(limite.ToString())}h*\n" +
                $"🔔 Estado: {estadoTxt}",
                equipo.Nombre, operario, valorActual, limite);
        }

        // ─────────────────────────────────────────────────────────────
        //  NOTIFICAR SUPERVISORES (Telegram + Email)
        // ─────────────────────────────────────────────────────────────
        private async Task NotificarSupervisores(
            string mensajeTelegram,
            string nombreEquipo = null,
            string nombreOperario = "Operario",
            double valorActual = 0,
            int limite = 0)
        {
            var chatSupervisor = DatabaseHelper.LeerConfiguracion("TelegramChatSupervisor");
            if (!string.IsNullOrWhiteSpace(chatSupervisor))
                await Enviar(chatSupervisor, mensajeTelegram);

            var supervisoresTg = DatabaseHelper.GetSupervisoresConTelegram();
            foreach (var sup in supervisoresTg)
                if (sup.TelegramChatId != chatSupervisor)
                    await Enviar(sup.TelegramChatId, mensajeTelegram);

            if (nombreEquipo == null) return;

            var emailSupervisor = DatabaseHelper.LeerConfiguracion("EmailSupervisor");
            if (string.IsNullOrWhiteSpace(emailSupervisor))
                emailSupervisor = DatabaseHelper.LeerConfiguracion("SmtpEmailSupervisor");

            if (!string.IsNullOrWhiteSpace(emailSupervisor))
            {
                try
                {
                    int horasRestantes = limite > 0 ? (int)(limite - valorActual) : 0;
                    new EmailService().EnviarAvisoMantenimiento(
                        emailSupervisor, "Supervisor",
                        nombreEquipo, horasRestantes, "horas", "AVISO");
                }
                catch { }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  ENVIAR MENSAJE TELEGRAM
        // ─────────────────────────────────────────────────────────────
        private async Task Enviar(string chatId, string texto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(chatId)) return;
                var url = $"https://api.telegram.org/bot{Token}/sendMessage";
                var payload = new { chat_id = chatId, text = texto, parse_mode = "MarkdownV2" };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _http.PostAsync(url, content);
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────
        //  ESCAPAR CARACTERES MARKDOWNV2
        // ─────────────────────────────────────────────────────────────
        private static string EscaparMarkdown(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return "";
            foreach (char c in new[] { '_','*','[',']','(',')',
                                       '~','`','>','#','+','-',
                                       '=','|','{','}','.','!' })
                texto = texto.Replace(c.ToString(), "\\" + c);
            return texto;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  MODELO DE ESTADO DE CONVERSACIÓN
    // ─────────────────────────────────────────────────────────────────
    public class EstadoConversacion
    {
        public string Paso { get; set; }
        public Equipo EquipoSeleccionado { get; set; }
        public List<Asignacion> Asignaciones { get; set; } = new();
        public double ValorPendiente { get; set; }
    }
}