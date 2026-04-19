using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AppMantenimiento
{
    public static class ImportadorHistoricosExcel
    {
        private const string MarcaImportacion = " DATOS HISTÓRICOS IMPORTADOS";

        private static readonly Dictionary<string, string> EquivalenciasEquipos = new(StringComparer.OrdinalIgnoreCase)
        {
            { "LINDE H30", "LINDE" },
            { "TELETRUCK", "JCB TELETRUCK" }
        };

        private static readonly Dictionary<string, (string Descripcion, string Ubicacion)> EquiposDefinidos = new(StringComparer.OrdinalIgnoreCase)
        {
            { "DS7 2119 MMH", ("COCHE", "FUENMAYOR") },
            { "PEUGEOT 3008 4817 MMK", ("COCHE", "CASTEJON") },
            { "PEUGEOT 308 3651 GDS", ("COCHE", "CASTEJON") },
            { "CAMIÓN 1258DMD", ("CAMION", "CASTEJON") },
            { "CAMION 1258DMD", ("CAMION", "CASTEJON") },
            { "CAMIÓN POSTES B1022DV", ("CAMION", "CASTEJON") },
            { "CAMION POSTES B1022DV", ("CAMION", "CASTEJON") },
            { "JCB 940", ("CARRETILLA", "CASTEJON") },
            { "MANITOU MSI30D", ("CARRETILLA", "CASTEJON") },
            { "LINDE H30", ("CARRETILLA", "CASTEJON") },
            { "TELETRUCK", ("CARRETILLA", "CASTEJON") },
            { "JCB 530-70 TELESCOPICA", ("CARRETILLA", "CASTEJON") },
            { "JCB 530-70 TELESCÓPICA", ("CARRETILLA", "CASTEJON") }
        };

        public static string ImportarDesdeExcel(string rutaExcel)
        {
            if (string.IsNullOrWhiteSpace(rutaExcel) || !File.Exists(rutaExcel))
                return "No se ha encontrado el archivo Excel.";

            var resumen = new List<string>();
            int equiposCreados = 0;
            int lecturasInsertadas = 0;
            int filasSaltadas = 0;

            using var workbook = new XLWorkbook(rutaExcel);

            foreach (var ws in workbook.Worksheets)
            {
                string nombreHojaOriginal = LimpiarTexto(ws.Name);
                if (string.IsNullOrWhiteSpace(nombreHojaOriginal))
                    continue;

                string nombreEquipoExcel = NormalizarNombreEquipo(nombreHojaOriginal);
                string nombreEquipoFinal = ResolverNombreEquipo(nombreEquipoExcel);

                var equipo = DatabaseHelper.ObtenerEquipos()
                    .FirstOrDefault(e => string.Equals(LimpiarTexto(e.Nombre), nombreEquipoFinal, StringComparison.OrdinalIgnoreCase));

                if (equipo == null)
                {
                    var datosEquipo = ObtenerDatosEquipo(nombreEquipoExcel);

                    var nuevoEquipo = new Equipo
                    {
                        Nombre = nombreEquipoFinal,
                        Descripcion = datosEquipo.Descripcion,
                        Ubicacion = datosEquipo.Ubicacion,
                        FrecuenciaHoras = 0,
                        FrecuenciaKm = 0,
                        FrecuenciaMantenimiento = 0,
                        FechaAlta = DateTime.Now.ToString("yyyy-MM-dd"),
                        UltimaLectura = null,
                        Activo = 1,
                        HorasActuales = 0,
                        HorasUltimoMantenimiento = 0,
                        FechaUltimoMantenimiento = null
                    };

                    DatabaseHelper.AgregarEquipo(nuevoEquipo);
                    equipo = DatabaseHelper.ObtenerEquipos()
                        .FirstOrDefault(e => string.Equals(LimpiarTexto(e.Nombre), nombreEquipoFinal, StringComparison.OrdinalIgnoreCase));

                    equiposCreados++;
                }

                if (equipo == null)
                {
                    resumen.Add($"No se pudo crear o localizar el equipo {nombreEquipoFinal}.");
                    continue;
                }

                var rango = ws.RangeUsed();
                if (rango == null)
                    continue;

                var rows = rango.RowsUsed();
                foreach (var row in rows)
                {
                    var valores = row.Cells().Select(c => LimpiarTexto(c.GetString())).ToList();
                    if (!valores.Any(v => !string.IsNullOrWhiteSpace(v)))
                        continue;

                    string lineaCompleta = string.Join(" ", valores).Trim();

                    if (EsLineaNoImportable(lineaCompleta))
                        continue;

                    DateTime? fecha = BuscarFechaEnFila(row);
                    double coste = ExtraerCoste(row);
                    string proveedor = ExtraerProveedor(row);
                    string operario = ExtraerOperario(row);
                    int horasActuales = ExtraerHorasMaquina(row);
                    string descripcion = ExtraerDescripcion(row, nombreEquipoFinal);

                    if (string.IsNullOrWhiteSpace(descripcion))
                    {
                        filasSaltadas++;
                        continue;
                    }

                    string tipoRegistro = DeterminarTipoRegistro(descripcion);

                    descripcion = descripcion.Trim();
                    if (!descripcion.EndsWith(MarcaImportacion, StringComparison.OrdinalIgnoreCase))
                        descripcion += " -" + MarcaImportacion;

                    string fechaNormalizada = FormatearFecha(fecha);
                    string descripcionNormalizada = LimpiarTexto(descripcion).ToUpperInvariant();
                    string operarioNormalizado = string.IsNullOrWhiteSpace(operario) ? "IMPORTACION" : LimpiarTexto(operario).ToUpperInvariant();
                    string proveedorNormalizado = LimpiarTexto(proveedor).ToUpperInvariant();

                    bool yaExiste = DatabaseHelper.ObtenerLecturasPorEquipo(equipo.Id)
                        .Any(l =>
                            string.Equals((l.Fecha ?? "").Trim(), fechaNormalizada, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(LimpiarTexto(l.Descripcion).ToUpperInvariant(), descripcionNormalizada, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(LimpiarTexto(l.Operario).ToUpperInvariant(), operarioNormalizado, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(LimpiarTexto(l.Proveedor).ToUpperInvariant(), proveedorNormalizado, StringComparison.OrdinalIgnoreCase) &&
                            l.HorasActuales == horasActuales &&
                            Math.Abs(l.Coste - coste) < 0.01);

                    if (yaExiste)
                    {
                        filasSaltadas++;
                        continue;
                    }

                    var lectura = new Lectura
                    {
                        EquipoId = equipo.Id,
                        NombreEquipo = equipo.Nombre,
                        Fecha = FormatearFecha(fecha),
                        Descripcion = descripcion,
                        Operario = string.IsNullOrWhiteSpace(operario) ? "IMPORTACION" : operario,
                        Proveedor = proveedor,
                        HorasActuales = horasActuales,
                        TipoRegistro = tipoRegistro,
                        Tipo = horasActuales > 0 ? "Lectura" : "Mantenimiento",
                        Valor = horasActuales > 0 ? horasActuales.ToString(CultureInfo.InvariantCulture) : "",
                        Coste = coste
                    };

                    DatabaseHelper.AgregarLectura(lectura);
                    lecturasInsertadas++;
                }
            }

            resumen.Add($"Equipos creados: {equiposCreados}");
            resumen.Add($"Lecturas importadas: {lecturasInsertadas}");
            resumen.Add($"Filas saltadas: {filasSaltadas}");

            return string.Join(Environment.NewLine, resumen);
        }

        private static string ResolverNombreEquipo(string nombreEquipoExcel)
        {
            if (EquivalenciasEquipos.TryGetValue(nombreEquipoExcel, out var equivalente))
                return equivalente;

            return nombreEquipoExcel;
        }

        private static (string Descripcion, string Ubicacion) ObtenerDatosEquipo(string nombreEquipoExcel)
        {
            if (EquiposDefinidos.TryGetValue(nombreEquipoExcel, out var datos))
                return datos;

            return ("MAQUINA", "CASTEJON");
        }

        private static bool EsLineaNoImportable(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return true;

            string t = texto.ToUpperInvariant();

            return t.Contains("TITLE") ||
               t.Contains("REFERENCIA") ||
               t.Contains("DESCRIPCIÓN IMPORTE FECHA PROVEEDOR HORAS") ||
               t.Contains("DESCRIPCION IMPORTE FECHA PROVEEDOR HORAS") ||
               t.Contains("PROXIMA REVISION HORAS") ||
               t.Contains("N DE FACTURA") ||
               t.StartsWith("---") ||
               t == "0";
        }

        private static string NormalizarNombreEquipo(string nombre)
        {
            string n = LimpiarTexto(nombre).ToUpperInvariant();

            n = n.Replace("COCHE ", "")
                 .Replace("CAMIN ", "CAMION ")
                 .Replace("CAMIÓN ", "CAMION ")
                 .Replace("GT LINE", "")
                 .Replace("  ", " ")
                 .Trim();

            if (n.Contains("PEUGEOT 3008") && n.Contains("4817"))
                return "PEUGEOT 3008 4817 MMK";

            if (n.Contains("PEUGEOT 308") && n.Contains("3651"))
                return "PEUGEOT 308 3651 GDS";

            if (n.Contains("DS7") && n.Contains("2119"))
                return "DS7 2119 MMH";

            if (n.Contains("1258DMD"))
                return "CAMION 1258DMD";

            if (n.Contains("B1022DV"))
                return "CAMION POSTES B1022DV";

            if (n.Contains("TELETRUCK"))
                return "TELETRUCK";

            if (n.Contains("JCB 940"))
                return "JCB 940";

            if (n.Contains("LINDE H30"))
                return "LINDE H30";

            if (n.Contains("MANITOU MSI30D"))
                return "MANITOU MSI30D";

            if (n.Contains("JCB 530-70"))
                return "JCB 530-70 TELESCOPICA";

            return LimpiarTexto(nombre).ToUpperInvariant();
        }

        private static string ExtraerDescripcion(IXLRangeRow row, string nombreEquipo)
        {
            string descripcion = LimpiarTexto(row.Cell(1).GetValue<string>());

            if (!string.IsNullOrWhiteSpace(descripcion))
                return descripcion;

            return $"MANTENIMIENTO - {nombreEquipo}";
        }

        private static double ExtraerCoste(IXLRangeRow row)
        {
            string texto = LimpiarTexto(row.Cell(2).GetValue<string>());

            if (string.IsNullOrWhiteSpace(texto))
                return 0;

            if (TryParseCoste(texto, out double coste))
                return coste;

            return 0;
        }
        private static string ExtraerProveedor(IXLRangeRow row)
        {
            return LimpiarTexto(row.Cell(4).GetValue<string>());
        }


        private static string ExtraerOperario(IXLRangeRow row)
        {
            return "IMPORTACION";
        }

        private static int ExtraerHorasMaquina(IXLRangeRow row)
        {
            string texto = LimpiarTexto(row.Cell(5).GetValue<string>());

            if (string.IsNullOrWhiteSpace(texto))
                return 0;

            texto = texto.Replace("horas", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("hora", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("h", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("kms", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("km", "", StringComparison.OrdinalIgnoreCase)
                         .Trim();

            if (double.TryParse(texto.Replace(",", "."),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double valor))
            {
                return (int)Math.Round(valor);
            }

            if (double.TryParse(texto,
                NumberStyles.Any,
                CultureInfo.GetCultureInfo("es-ES"),
                out valor))
            {
                return (int)Math.Round(valor);
            }

            return 0;
        }

        private static string DeterminarTipoRegistro(string descripcion)
        {
            string d = (descripcion ?? "").ToUpperInvariant();

            if (d.Contains("COMPROBACION HORAS") || d.Contains("REVISION") || d.Contains("MANTENIMIENTO"))
                return "Mantenimiento";

            return "Mantenimiento";
        }

        private static DateTime? BuscarFechaEnFila(IXLRangeRow row)
        {
            var cell = row.Cell(3);

            if (cell.DataType == XLDataType.DateTime)
                return cell.GetDateTime();

            string texto = LimpiarTexto(cell.GetValue<string>());

            if (TryParseFecha(texto, out var fecha))
                return fecha;

            return null;
        }

        private static string FormatearFecha(DateTime? fecha)
        {
            return (fecha ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm");
        }

        private static bool TryParseFecha(string texto, out DateTime fecha)
        {
            string[] formatos =
            {
                "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy",
                "d-M-yyyy", "dd-MM-yyyy", "d-M-yy", "dd-MM-yy",
                "d/M/yyyy H:mm", "dd/MM/yyyy H:mm",
                "d-MMM", "dd-MMM"
            };

            foreach (var formato in formatos)
            {
                if (DateTime.TryParseExact(texto, formato, CultureInfo.GetCultureInfo("es-ES"),
                    DateTimeStyles.None, out fecha))
                {
                    if (fecha.Year == DateTime.MinValue.Year)
                        fecha = new DateTime(DateTime.Now.Year, fecha.Month, fecha.Day);

                    return true;
                }
            }

            return DateTime.TryParse(texto, CultureInfo.GetCultureInfo("es-ES"), DateTimeStyles.None, out fecha);
        }

        private static bool TryParseCoste(string texto, out double coste)
        {
            coste = 0;
            if (string.IsNullOrWhiteSpace(texto))
                return false;

            string limpio = texto.ToUpperInvariant()
                .Replace("€", "")
                .Replace("EUR", "")
                .Replace("BASE IMPONIBLE", "")
                .Replace("TASA TRAFICO", "")
                .Replace("SEGÚN PPTO", "")
                .Replace("SEGN PPTO", "")
                .Replace("SEGUN PPTO", "")
                .Trim();

            limpio = limpio.Replace(" ", "");

            if (double.TryParse(limpio, NumberStyles.Any, CultureInfo.GetCultureInfo("es-ES"), out coste))
                return true;

            if (double.TryParse(limpio, NumberStyles.Any, CultureInfo.InvariantCulture, out coste))
                return true;

            return false;
        }

        private static bool EsFechaTexto(string texto)
        {
            return TryParseFecha(texto, out _);
        }

        private static bool EsNumeroImporte(string texto)
        {
            return TryParseCoste(texto, out _);
        }

        private static bool EsPosibleHoraNumero(string texto)
        {
            string t = texto.Replace(",", ".").Trim();
            return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
                || double.TryParse(t, NumberStyles.Any, CultureInfo.GetCultureInfo("es-ES"), out _);
        }

        private static bool EsOperarioConocido(string texto)
        {
            string t = LimpiarTexto(texto).ToUpperInvariant();

            string[] operarios =
            {
                "GUILLERMO", "JESUS", "ISLAM", "JAVIER", "ALFONSO", "TONI", "TONY", "DAVID",
                "JUAN", "JOSE", "JOSE ANTONIO", "RUBEN", "MANU", "MIGUEL", "LUIS", "LUISMI",
                "SOFIAN", "JON", "ANGEL", "JULIO", "ALEJANDRO", "ALVARO", "JP", "DANIEL",
                "FELIX", "MIKEL", "MANUEL", "CRISTINO", "DAMIAN", "RAFA", "JHON", "JHONNY",
                "JAVI", "JOSUE"
            };

            return operarios.Contains(t);
        }

        private static bool EsProveedorProbable(string texto)
        {
            string t = LimpiarTexto(texto).ToUpperInvariant();

            string[] proveedores =
            {
                "LOMAQ", "REYBESA", "LAGUN", "SAN RAFAEL", "NUEVOS EQUIPOS", "RECALVI",
                "NORAUTO", "RIOJA REVISIONES", "TV SD ATISAE", "SAYAS AUTOMOCION",
                "CARROCERIAS JESUS", "MANASA", "SUATEC", "FERROTORRES", "TOIN",
                "NEUMATICOS SAENZ", "NEUMATICOS LAGUN", "AMAZON", "RADA"
            };

            return proveedores.Any(p => t.Contains(p));
        }

        private static string LimpiarTexto(string texto)
        {
            return string.IsNullOrWhiteSpace(texto) ? "" : texto.Replace("\n", " ").Replace("\r", " ").Trim();
        }
    }
}