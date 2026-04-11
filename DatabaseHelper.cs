using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace AppMantenimiento
{
    // ═══════════════════════════════════════════════════════
    // MODELOS
    // ═══════════════════════════════════════════════════════

    public class Equipo
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public string Ubicacion { get; set; }
        public int FrecuenciaHoras { get; set; }
        public int FrecuenciaKm { get; set; }
        public int FrecuenciaMantenimiento { get; set; }
        public string FechaAlta { get; set; }
        public string UltimaLectura { get; set; }
        public int Activo { get; set; }
        public int HorasActuales { get; set; }
        public int HorasUltimoMantenimiento { get; set; }
        public string FechaUltimoMantenimiento { get; set; }

        public static string RutaDb => RutaDb;

        // Propiedades calculadas (no van en BD)
        public int DiasParaMantenimiento { get; set; }
        public int HorasParaMantenimiento { get; set; }

        // ← AÑADIR ESTAS DOS LÍNEAS:
        public int HorasRestantes { get; set; }
        public string Estado { get; set; }
    }

    public class Operario
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Telefono { get; set; }
        public string Email { get; set; }
        public string Rol { get; set; }
        public int Activo { get; set; }
        public string TelegramChatId { get; set; }
    }

    public class Asignacion
    {
        public int Id { get; set; }
        public int EquipoId { get; set; }
        public int OperarioId { get; set; }
        public string NombreEquipo { get; set; }
        public string NombreOperario { get; set; }
    }

    public class Lectura
    {
        public int Id { get; set; }
        public int EquipoId { get; set; }
        public string NombreEquipo { get; set; }
        public string Tipo { get; set; }  // "Lectura" o "Mantenimiento"
        public string Valor { get; set; }  // Número de horas/km (solo en Lectura)
        public string Descripcion { get; set; }
        public string Operario { get; set; }// "Registrado por"
        public string Fecha { get; set; }
        public double Coste { get; set; }
        public int HorasActuales { get; set; }      
        public string TipoRegistro { get; set; }
    }

    public class Supervisor
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Email { get; set; }
        public int Activo { get; set; }
    }

    public class ValidacionPendiente
    {
        public int Id { get; set; }
        public int EquipoId { get; set; }
        public int OperarioId { get; set; }
        public int HorasAntiguas { get; set; }
        public int HorasNuevas { get; set; }
        public string FechaSolicitud { get; set; }
        public string Estado { get; set; }
        public int Intentos { get; set; }
        public string FechaUltimoIntento { get; set; }
        public string ChatIdOperario { get; set; }
    }
    public class Configuracion
    {
        public string SmtpServidor { get; set; }
        public string SmtpPuerto { get; set; }
        public string SmtpEmail { get; set; }
        public string SmtpPassword { get; set; }
        public string EmailSupervisor { get; set; }
        public string TelegramToken { get; set; }
        public string TelegramChatSupervisor { get; set; }
        public int UmbralAviso { get; set; }
        public int UmbralCritico { get; set; }
    }

    // ═══════════════════════════════════════════════════════
    // ACCESO A DATOS
    // ═══════════════════════════════════════════════════════

    public class DatabaseHelper
    {
        private static string rutaDb = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "botmantenimiento.db");

        // ── INICIALIZAR ──────────────────────────────────────
        public static void InicializarBaseDatos()
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Configuracion (
                    Clave TEXT PRIMARY KEY, Valor TEXT
                );
                CREATE TABLE IF NOT EXISTS Equipos (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nombre TEXT NOT NULL, Descripcion TEXT, Ubicacion TEXT,
                    FrecuenciaHoras INTEGER DEFAULT 0,
                    FrecuenciaKm INTEGER DEFAULT 0,
                    FrecuenciaMantenimiento INTEGER DEFAULT 0,
                    FechaAlta TEXT, UltimaLectura TEXT, Activo INTEGER DEFAULT 1,
                    HorasActuales INTEGER DEFAULT 0,
                    HorasUltimoMantenimiento INTEGER DEFAULT 0,
                    FechaUltimoMantenimiento TEXT
                );
                CREATE TABLE IF NOT EXISTS Operarios (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nombre TEXT NOT NULL, Telefono TEXT, Email TEXT,
                    Rol TEXT, Activo INTEGER DEFAULT 1, TelegramChatId TEXT
                );
                CREATE TABLE IF NOT EXISTS Supervisores (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nombre TEXT NOT NULL, Email TEXT NOT NULL, Activo INTEGER DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS Asignaciones (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    EquipoId INTEGER NOT NULL, OperarioId INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Lecturas (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    EquipoId INTEGER, NombreEquipo TEXT, Fecha TEXT,
                    Descripcion TEXT, Operario TEXT,
                    HorasActuales INTEGER DEFAULT 0,
                    TipoRegistro TEXT DEFAULT 'Mantenimiento'
                );
                CREATE TABLE IF NOT EXISTS ValidacionesPendientes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    EquipoId INTEGER NOT NULL, OperarioId INTEGER NOT NULL,
                    HorasAntiguas INTEGER DEFAULT 0, HorasNuevas INTEGER DEFAULT 0,
                    FechaSolicitud TEXT, Estado TEXT DEFAULT 'Pendiente',
                    Intentos INTEGER DEFAULT 1, FechaUltimoIntento TEXT,
                    ChatIdOperario TEXT
                );
                CREATE TABLE IF NOT EXISTS Lecturas (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    EquipoId INTEGER, NombreEquipo TEXT,
                    Tipo TEXT DEFAULT 'Lectura',
                    Valor TEXT DEFAULT '',
                    Fecha TEXT, Descripcion TEXT, Operario TEXT);
            ";
            cmd.ExecuteNonQuery();

            // Migración: añadir columnas nuevas si no existen
            try { var m = con.CreateCommand(); m.CommandText = "ALTER TABLE Lecturas ADD COLUMN Tipo TEXT DEFAULT 'Lectura'"; m.ExecuteNonQuery(); } catch { }
            try { var m = con.CreateCommand(); m.CommandText = "ALTER TABLE Lecturas ADD COLUMN Valor TEXT DEFAULT ''"; m.ExecuteNonQuery(); } catch { }
            try { var m = con.CreateCommand(); m.CommandText = "ALTER TABLE Equipos ADD COLUMN FechaUltimoMantenimiento TEXT DEFAULT ''"; m.ExecuteNonQuery(); } catch { }
            try { var m = con.CreateCommand(); m.CommandText = "ALTER TABLE Lecturas ADD COLUMN Coste REAL DEFAULT 0"; m.ExecuteNonQuery(); } catch { }
            try { var m = con.CreateCommand(); m.CommandText = "ALTER TABLE Operarios ADD COLUMN TelegramChatId TEXT DEFAULT ''"; m.ExecuteNonQuery(); } catch { }
            EnsureConfigKey("UmbralAviso", "50");
            EnsureConfigKey("UmbralCritico", "10");

            // Migraciones seguras — añaden columnas si no existen, nunca borran datos
            Migrar(con, "ALTER TABLE Operarios ADD COLUMN TelegramChatId TEXT");
            Migrar(con, "ALTER TABLE Operarios ADD COLUMN Activo INTEGER DEFAULT 1");
            Migrar(con, "ALTER TABLE Equipos ADD COLUMN UltimaLectura TEXT");
            Migrar(con, "ALTER TABLE Equipos ADD COLUMN Activo INTEGER DEFAULT 1");
            Migrar(con, "ALTER TABLE Equipos ADD COLUMN HorasActuales INTEGER DEFAULT 0");
            Migrar(con, "ALTER TABLE Equipos ADD COLUMN HorasUltimoMantenimiento INTEGER DEFAULT 0");
            Migrar(con, "ALTER TABLE Equipos ADD COLUMN FechaUltimoMantenimiento TEXT");
            Migrar(con, "ALTER TABLE Equipos ADD COLUMN FrecuenciaHoras INTEGER DEFAULT 0");
            Migrar(con, "ALTER TABLE Equipos ADD COLUMN FrecuenciaKm INTEGER DEFAULT 0");
            Migrar(con, "ALTER TABLE Lecturas ADD COLUMN HorasActuales INTEGER DEFAULT 0");
            Migrar(con, "ALTER TABLE Lecturas ADD COLUMN TipoRegistro TEXT DEFAULT 'Mantenimiento'");
        }

        private static void Migrar(SqliteConnection con, string sql)
        {
            try
            {
                var c = con.CreateCommand();
                c.CommandText = sql;
                c.ExecuteNonQuery();
            }
            catch { /* columna ya existe, ignorar */ }
        }

        // ── CONFIGURACIÓN ────────────────────────────────────
        public static void GuardarConfiguracion(string clave, string valor)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"INSERT INTO Configuracion(Clave,Valor) VALUES(@c,@v)
                ON CONFLICT(Clave) DO UPDATE SET Valor=@v";
            cmd.Parameters.AddWithValue("@c", clave);
            cmd.Parameters.AddWithValue("@v", valor);
            cmd.ExecuteNonQuery();
        }
        public static void GuardarConfiguracionSiVacia(string clave, string valorDefecto)
        {
            if (string.IsNullOrEmpty(LeerConfiguracion(clave)))
                GuardarConfiguracion(clave, valorDefecto);
        }

        public static string LeerConfiguracion(string clave)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Valor FROM Configuracion WHERE Clave=@c";
            cmd.Parameters.AddWithValue("@c", clave);
            return cmd.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        // ── EQUIPOS ──────────────────────────────────────────
        public static void AgregarEquipo(Equipo e)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"INSERT INTO Equipos
                (Nombre,Descripcion,Ubicacion,FrecuenciaMantenimiento,
                 FrecuenciaHoras,FrecuenciaKm,FechaAlta,Activo)
                VALUES(@n,@d,@u,@f,@fh,@fk,@fa,1)";
            cmd.Parameters.AddWithValue("@n", e.Nombre);
            cmd.Parameters.AddWithValue("@d", e.Descripcion ?? "");
            cmd.Parameters.AddWithValue("@u", e.Ubicacion ?? "");
            cmd.Parameters.AddWithValue("@f", e.FrecuenciaMantenimiento);
            cmd.Parameters.AddWithValue("@fh", e.FrecuenciaHoras);
            cmd.Parameters.AddWithValue("@fk", e.FrecuenciaKm);
            cmd.Parameters.AddWithValue("@fa", DateTime.Now.ToString("yyyy-MM-dd"));
            cmd.ExecuteNonQuery();
        }

        public static List<Equipo> ObtenerEquipos() => GetEquipos();
        public static List<Equipo> GetEquipos()
        {
            var lista = new List<Equipo>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id,Nombre,Descripcion,Ubicacion,
                FrecuenciaHoras,FrecuenciaKm,FrecuenciaMantenimiento,
                FechaAlta,UltimaLectura,Activo,
                HorasActuales,HorasUltimoMantenimiento,FechaUltimoMantenimiento
                FROM Equipos";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Equipo
                {
                    Id = r.GetInt32(0),
                    Nombre = r.GetString(1),
                    Descripcion = r.IsDBNull(2) ? "" : r.GetString(2),
                    Ubicacion = r.IsDBNull(3) ? "" : r.GetString(3),
                    FrecuenciaHoras = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    FrecuenciaKm = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                    FrecuenciaMantenimiento = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                    FechaAlta = r.IsDBNull(7) ? "" : r.GetString(7),
                    UltimaLectura = r.IsDBNull(8) ? "" : r.GetString(8),
                    Activo = r.IsDBNull(9) ? 1 : r.GetInt32(9),
                    HorasActuales = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                    HorasUltimoMantenimiento = r.IsDBNull(11) ? 0 : r.GetInt32(11),
                    FechaUltimoMantenimiento = r.IsDBNull(12) ? "" : r.GetString(12)
                });
            return lista;
        }

        public static void DarDeBajaEquipo(int id)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Equipos SET Activo=0 WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        public static void ReactivarEquipo(int id)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Equipos SET Activo=1 WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        public static void BorrarEquipo(int id)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM Asignaciones WHERE EquipoId=@id; DELETE FROM Equipos WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static void ActualizarUltimaLectura(int equipoId)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Equipos SET UltimaLectura=@f WHERE Id=@id";
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@id", equipoId);
            cmd.ExecuteNonQuery();
        }



        // ── OPERARIOS ────────────────────────────────────────
        public static void AgregarOperario(Operario o)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"INSERT INTO Operarios
                (Nombre,Telefono,Email,Rol,Activo,TelegramChatId)
                VALUES(@n,@t,@e,@r,1,@tc)";
            cmd.Parameters.AddWithValue("@n", o.Nombre);
            cmd.Parameters.AddWithValue("@t", o.Telefono ?? "");
            cmd.Parameters.AddWithValue("@e", o.Email ?? "");
            cmd.Parameters.AddWithValue("@r", o.Rol ?? "Operario");
            cmd.Parameters.AddWithValue("@tc", o.TelegramChatId ?? "");
            cmd.ExecuteNonQuery();
        }

        public static List<Operario> ObtenerOperarios() => GetOperarios();
        public static List<Operario> GetOperarios()
        {
            var lista = new List<Operario>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Id,Nombre,Telefono,Email,Rol,Activo,TelegramChatId FROM Operarios";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Operario
                {
                    Id = r.GetInt32(0),
                    Nombre = r.GetString(1),
                    Telefono = r.IsDBNull(2) ? "" : r.GetString(2),
                    Email = r.IsDBNull(3) ? "" : r.GetString(3),
                    Rol = r.IsDBNull(4) ? "Operario" : r.GetString(4),
                    Activo = r.IsDBNull(5) ? 1 : r.GetInt32(5),
                    TelegramChatId = r.IsDBNull(6) ? "" : r.GetString(6)
                });
            return lista;
        }

        public static void DarDeBajaOperario(int id)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Operarios SET Activo=0 WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        public static void ReactivarOperario(int id)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Operarios SET Activo=1 WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        public static void BorrarOperario(int id)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM Asignaciones WHERE OperarioId=@id; DELETE FROM Operarios WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static void ActualizarTelegramChatId(int operarioId, string chatId)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Operarios SET TelegramChatId=@tc WHERE Id=@id";
            cmd.Parameters.AddWithValue("@tc", chatId ?? "");
            cmd.Parameters.AddWithValue("@id", operarioId);
            cmd.ExecuteNonQuery();
        }

        // ── ASIGNACIONES ─────────────────────────────────────
        public static void AsignarEquipoOperario(int equipoId, int operarioId)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "INSERT INTO Asignaciones(EquipoId,OperarioId) VALUES(@e,@o)";
            cmd.Parameters.AddWithValue("@e", equipoId);
            cmd.Parameters.AddWithValue("@o", operarioId);
            cmd.ExecuteNonQuery();
        }

        public static List<Asignacion> ObtenerAsignaciones()
        {
            var lista = new List<Asignacion>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT a.Id, a.EquipoId, a.OperarioId,
                COALESCE(e.Nombre,''), COALESCE(o.Nombre,'')
                FROM Asignaciones a
                LEFT JOIN Equipos e ON e.Id=a.EquipoId
                LEFT JOIN Operarios o ON o.Id=a.OperarioId";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Asignacion
                {
                    Id = r.GetInt32(0),
                    EquipoId = r.GetInt32(1),
                    OperarioId = r.GetInt32(2),
                    NombreEquipo = r.GetString(3),
                    NombreOperario = r.GetString(4)
                });
            return lista;
        }
        public static void EliminarAsignacion(int id)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM Asignaciones WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ── LECTURAS ─────────────────────────────────────────
        public static void AgregarLectura(Lectura lectura)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();

            // 1. Insertar en historial
            var cmd = con.CreateCommand();
            cmd.CommandText = @"INSERT INTO Lecturas
        (EquipoId, NombreEquipo, Tipo, Valor, Fecha, Descripcion, Operario, Coste)
        VALUES (@eq, @ne, @ti, @va, @fe, @de, @op, @co)";
            cmd.Parameters.AddWithValue("@eq", lectura.EquipoId);
            cmd.Parameters.AddWithValue("@ne", lectura.NombreEquipo ?? "");
            cmd.Parameters.AddWithValue("@ti", lectura.Tipo ?? "Lectura");
            cmd.Parameters.AddWithValue("@va", lectura.Valor ?? "");
            cmd.Parameters.AddWithValue("@fe", lectura.Fecha ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@de", lectura.Descripcion ?? "");
            cmd.Parameters.AddWithValue("@op", lectura.Operario ?? "Supervisor");
            cmd.Parameters.AddWithValue("@co", lectura.Coste);
            cmd.ExecuteNonQuery();

            // 2. Si es una lectura numérica, actualizar HorasActuales y UltimaLectura en Equipos
            if ((lectura.Tipo == "Lectura" || string.IsNullOrEmpty(lectura.Tipo)) &&
                !string.IsNullOrWhiteSpace(lectura.Valor) &&
                double.TryParse(lectura.Valor.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double valorNum) &&
                valorNum > 0)
            {
                var cmd2 = con.CreateCommand();
                cmd2.CommandText = @"UPDATE Equipos
                             SET HorasActuales  = @h,
                                 UltimaLectura  = @f
                             WHERE Id = @id";
                cmd2.Parameters.AddWithValue("@h", (int)valorNum);
                cmd2.Parameters.AddWithValue("@f", lectura.Fecha ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                cmd2.Parameters.AddWithValue("@id", lectura.EquipoId);
                cmd2.ExecuteNonQuery();
            }
        }

        public static List<Lectura> ObtenerLecturas()
        {
            var lista = new List<Lectura>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id, EquipoId, NombreEquipo, Tipo, Valor,
                               Fecha, Descripcion, Operario, COALESCE(Coste,0)
                        FROM Lecturas ORDER BY Id DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Lectura
                {
                    Id = r.GetInt32(0),
                    EquipoId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    NombreEquipo = r.IsDBNull(2) ? "" : r.GetString(2),
                    Tipo = r.IsDBNull(3) ? "Lectura" : r.GetString(3),
                    Valor = r.IsDBNull(4) ? "" : r.GetString(4),
                    Fecha = r.IsDBNull(5) ? "" : r.GetString(5),
                    Descripcion = r.IsDBNull(6) ? "" : r.GetString(6),
                    Operario = r.IsDBNull(7) ? "" : r.GetString(7),
                    Coste = r.IsDBNull(8) ? 0 : r.GetDouble(8)
                });
            return lista;
        }
        public static double GetUltimaLecturaNumerica(int equipoId)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Descripcion FROM Lecturas WHERE EquipoId=@id ORDER BY Id DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@id", equipoId);
            var desc = cmd.ExecuteScalar()?.ToString() ?? "";
            var match = System.Text.RegularExpressions.Regex.Match(desc, @"[\d.,]+");
            if (match.Success &&
                double.TryParse(match.Value.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                return val;
            return 0;
        }

        // ── SUPERVISORES ─────────────────────────────────────
        public static void AgregarSupervisor(Supervisor s)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "INSERT INTO Supervisores(Nombre,Email,Activo) VALUES(@n,@e,1)";
            cmd.Parameters.AddWithValue("@n", s.Nombre);
            cmd.Parameters.AddWithValue("@e", s.Email);
            cmd.ExecuteNonQuery();
        }

        public static List<Supervisor> ObtenerSupervisores()
        {
            var lista = new List<Supervisor>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Id,Nombre,Email,Activo FROM Supervisores";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Supervisor
                {
                    Id = r.GetInt32(0),
                    Nombre = r.GetString(1),
                    Email = r.IsDBNull(2) ? "" : r.GetString(2),
                    Activo = r.IsDBNull(3) ? 1 : r.GetInt32(3)
                });
            return lista;
        }

        // ── LECTURAS DE HORAS ────────────────────────────────
        public static void RegistrarLecturaHoras(int equipoId, int horasActuales, string operario)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();

            cmd.CommandText = @"UPDATE Equipos
                SET HorasActuales=@h, UltimaLectura=@f WHERE Id=@id";
            cmd.Parameters.AddWithValue("@h", horasActuales);
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@id", equipoId);
            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();
            cmd.CommandText = @"INSERT INTO Lecturas
                (EquipoId,NombreEquipo,Fecha,Descripcion,Operario,HorasActuales,TipoRegistro)
                SELECT @ei,Nombre,@f,@d,@op,@h,'Lectura' FROM Equipos WHERE Id=@ei";
            cmd.Parameters.AddWithValue("@ei", equipoId);
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@d", $"Lectura de horas: {horasActuales}h");
            cmd.Parameters.AddWithValue("@op", operario);
            cmd.Parameters.AddWithValue("@h", horasActuales);
            cmd.ExecuteNonQuery();
        }

        // ── REGISTRAR MANTENIMIENTO (cierra ciclo) ────────────
        public static void RegistrarMantenimiento(int equipoId, string descripcion, string operario)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();

            cmd.CommandText = "SELECT HorasActuales FROM Equipos WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", equipoId);
            int horasActuales = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);

            cmd.Parameters.Clear();
            cmd.CommandText = @"UPDATE Equipos
                SET HorasUltimoMantenimiento=@h,
                    FechaUltimoMantenimiento=@f,
                    UltimaLectura=@f
                WHERE Id=@id";
            cmd.Parameters.AddWithValue("@h", horasActuales);
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@id", equipoId);
            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();
            cmd.CommandText = @"INSERT INTO Lecturas
                (EquipoId,NombreEquipo,Fecha,Descripcion,Operario,HorasActuales,TipoRegistro)
                SELECT @ei,Nombre,@f,@d,@op,@h,'Mantenimiento' FROM Equipos WHERE Id=@ei";
            cmd.Parameters.AddWithValue("@ei", equipoId);
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@d", descripcion);
            cmd.Parameters.AddWithValue("@op", operario);
            cmd.Parameters.AddWithValue("@h", horasActuales);
            cmd.ExecuteNonQuery();
        }

        // ── CALCULAR ESTADO ──────────────────────────────────
        // Devuelve: "OK" | "PROXIMO" | "CRITICO" | "VENCIDO"
        public static string CalcularEstado(Equipo eq)
        {
            eq.HorasParaMantenimiento = 0;
            eq.DiasParaMantenimiento = 0;

            if (eq == null) return "OK";
            if (eq.Activo != 1) return "OK";
            if (eq.FrecuenciaHoras <= 0) return "OK";

            int horasActuales = eq.HorasActuales;
            int horasBase = eq.HorasUltimoMantenimiento;

            if (horasActuales < horasBase)
                return "OK";

            int horasAcumuladas = horasActuales - horasBase;
            int horasRestantes = eq.FrecuenciaHoras - horasAcumuladas;

            eq.HorasParaMantenimiento = horasRestantes;

            if (horasRestantes <= 0) return "VENCIDO";
            if (horasRestantes <= (int)Math.Ceiling(eq.FrecuenciaHoras * 0.10)) return "CRITICO";
            if (horasRestantes <= (int)Math.Ceiling(eq.FrecuenciaHoras * 0.20)) return "PROXIMO";
            return "OK";
        }

        // ── VALIDACIONES PENDIENTES ──────────────────────────
        public static int CrearValidacionPendiente(int equipoId, int operarioId,
            int horasAntiguas, int horasNuevas, string chatIdOperario)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"INSERT INTO ValidacionesPendientes
                (EquipoId,OperarioId,HorasAntiguas,HorasNuevas,
                 FechaSolicitud,Estado,Intentos,FechaUltimoIntento,ChatIdOperario)
                VALUES(@eq,@op,@ha,@hn,@f,'Pendiente',1,@f,@cid)";
            cmd.Parameters.AddWithValue("@eq", equipoId);
            cmd.Parameters.AddWithValue("@op", operarioId);
            cmd.Parameters.AddWithValue("@ha", horasAntiguas);
            cmd.Parameters.AddWithValue("@hn", horasNuevas);
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@cid", chatIdOperario);
            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT last_insert_rowid()";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public static void ResolverValidacion(int id, string estado)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE ValidacionesPendientes SET Estado=@e WHERE Id=@id";
            cmd.Parameters.AddWithValue("@e", estado);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static List<ValidacionPendiente> GetValidacionesPendientes()
        {
            var lista = new List<ValidacionPendiente>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id,EquipoId,OperarioId,HorasAntiguas,HorasNuevas,
                FechaSolicitud,Estado,Intentos,FechaUltimoIntento,ChatIdOperario
                FROM ValidacionesPendientes WHERE Estado='Pendiente'";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new ValidacionPendiente
                {
                    Id = r.GetInt32(0),
                    EquipoId = r.GetInt32(1),
                    OperarioId = r.GetInt32(2),
                    HorasAntiguas = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    HorasNuevas = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    FechaSolicitud = r.IsDBNull(5) ? "" : r.GetString(5),
                    Estado = r.IsDBNull(6) ? "" : r.GetString(6),
                    Intentos = r.IsDBNull(7) ? 1 : r.GetInt32(7),
                    FechaUltimoIntento = r.IsDBNull(8) ? "" : r.GetString(8),
                    ChatIdOperario = r.IsDBNull(9) ? "" : r.GetString(9)
                });
            return lista;
        }

        public static void ActualizarIntentoValidacion(int id)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"UPDATE ValidacionesPendientes
                SET Intentos=Intentos+1, FechaUltimoIntento=@f WHERE Id=@id";
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // Comprueba si ya existe lectura de horas hoy para ese equipo
        public static bool ExisteLecturaHoy(int equipoId)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) FROM Lecturas
                WHERE EquipoId=@id AND TipoRegistro='Lectura'
                AND date(Fecha)=date('now','localtime')";
            cmd.Parameters.AddWithValue("@id", equipoId);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        public static List<Lectura> ObtenerLecturasDeEquipo(int equipoId)
        {
            var lista = new List<Lectura>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id,EquipoId,NombreEquipo,Fecha,Descripcion,Operario
                        FROM Lecturas WHERE EquipoId=@id ORDER BY Fecha DESC";
            cmd.Parameters.AddWithValue("@id", equipoId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Lectura
                {
                    Id = r.GetInt32(0),
                    EquipoId = r.GetInt32(1),
                    NombreEquipo = r.IsDBNull(2) ? "" : r.GetString(2),
                    Fecha = r.IsDBNull(3) ? "" : r.GetString(3),
                    Descripcion = r.IsDBNull(4) ? "" : r.GetString(4),
                    Operario = r.IsDBNull(5) ? "" : r.GetString(5)
                });
            return lista;
        }

        public static void BorrarLecturasHoyDeEquipo(int equipoId)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM Lecturas WHERE EquipoId=@id AND Fecha LIKE @hoy";
            cmd.Parameters.AddWithValue("@id", equipoId);
            cmd.Parameters.AddWithValue("@hoy", DateTime.Now.ToString("yyyy-MM-dd") + "%");
            cmd.ExecuteNonQuery();
        }

        public static void ActualizarUltimaLectura(int equipoId, string valorTexto)
        {
            // Extraer el número del texto "492.5h" → 492
            var limpio = valorTexto.Replace("h", "").Replace(",", ".").Trim();
            double.TryParse(limpio,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double horas);

            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Equipos SET UltimaLectura=@f WHERE Id=@id";
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@id", equipoId);
            cmd.ExecuteNonQuery();
        }
        public static void GuardarLectura(Lectura l)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"INSERT INTO Lecturas 
                        (EquipoId, NombreEquipo, Fecha, Descripcion, Operario,
                         HorasActuales, TipoRegistro, Valor, Tipo)
                        VALUES (@eq, @ne, @fe, @de, @op, @ha, @tr, @val, @tip)";
            cmd.Parameters.AddWithValue("@eq", l.EquipoId);
            cmd.Parameters.AddWithValue("@ne", l.NombreEquipo ?? "");
            cmd.Parameters.AddWithValue("@fe", l.Fecha ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@de", l.Descripcion ?? "");
            cmd.Parameters.AddWithValue("@op", l.Operario ?? "");
            cmd.Parameters.AddWithValue("@ha", l.HorasActuales);
            cmd.Parameters.AddWithValue("@tr", l.TipoRegistro ?? "");
            cmd.Parameters.AddWithValue("@val", l.Valor ?? (l.HorasActuales > 0 ? l.HorasActuales.ToString() : ""));
            cmd.Parameters.AddWithValue("@tip", l.Tipo ?? l.TipoRegistro ?? "Lectura");
            cmd.ExecuteNonQuery();
        }
        public static void ResetarContadorMantenimiento(int equipoId, double valorActual = 0)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();

            // Actualizar fecha último mantenimiento
            var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Equipos SET FechaUltimoMantenimiento = @fecha WHERE Id = @id";
            cmd.Parameters.AddWithValue("@fecha", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@id", equipoId);
            cmd.ExecuteNonQuery();

            var nombre = con.CreateCommand();
            nombre.CommandText = "SELECT Nombre FROM Equipos WHERE Id = @id";
            nombre.Parameters.AddWithValue("@id", equipoId);
            string nomEquipo = nombre.ExecuteScalar()?.ToString() ?? "";

            // Insertar registro de reset con el valor actual como baseline
            var cmd3 = con.CreateCommand();
            cmd3.CommandText = @"INSERT INTO Lecturas
        (EquipoId, NombreEquipo, Tipo, Valor, Fecha, Descripcion, Operario)
        VALUES (@eq, @ne, 'Mantenimiento', @val, @fe, @de, 'Supervisor')";
            cmd3.Parameters.AddWithValue("@eq", equipoId);
            cmd3.Parameters.AddWithValue("@ne", nomEquipo);
            // Guardamos el valor actual como punto cero del siguiente ciclo
            cmd3.Parameters.AddWithValue("@val",
                valorActual > 0
                ? valorActual.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                : "");
            cmd3.Parameters.AddWithValue("@fe", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd3.Parameters.AddWithValue("@de", "RESET CONTADOR - Mantenimiento completado");
            cmd3.ExecuteNonQuery();
        }
        public static List<Lectura> ObtenerLecturasPorEquipo(int equipoId)
        {
            var todas = ObtenerLecturas();
            return equipoId == -1 ? todas : todas.Where(l => l.EquipoId == equipoId).ToList();
        }
        // Supervisores y admins con Telegram configurado
        public static List<Operario> GetSupervisoresConTelegram()
        {
            var lista = new List<Operario>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id, Nombre, Telefono, Email, Rol, Activo,
                               COALESCE(TelegramChatId,'')
                        FROM Operarios
                        WHERE Activo = 1
                          AND (Rol = 'Supervisor' OR Rol = 'Administrador')
                          AND COALESCE(TelegramChatId,'') != ''";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Operario
                {
                    Id = r.GetInt32(0),
                    Nombre = r.GetString(1),
                    Telefono = r.IsDBNull(2) ? "" : r.GetString(2),
                    Email = r.IsDBNull(3) ? "" : r.GetString(3),
                    Rol = r.IsDBNull(4) ? "" : r.GetString(4),
                    Activo = r.GetInt32(5),
                    TelegramChatId = r.GetString(6)
                });
            return lista;
        }

        // Supervisores con email para notificaciones
        public static List<Operario> GetSupervisoresConEmail()
        {
            var lista = new List<Operario>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id, Nombre, Telefono, Email, Rol, Activo,
                               COALESCE(TelegramChatId,'')
                        FROM Operarios
                        WHERE Activo = 1
                          AND (Rol = 'Supervisor' OR Rol = 'Administrador')
                          AND COALESCE(Email,'') != ''";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Operario
                {
                    Id = r.GetInt32(0),
                    Nombre = r.GetString(1),
                    Telefono = r.IsDBNull(2) ? "" : r.GetString(2),
                    Email = r.IsDBNull(3) ? "" : r.GetString(3),
                    Rol = r.IsDBNull(4) ? "" : r.GetString(4),
                    Activo = r.GetInt32(5),
                    TelegramChatId = r.GetString(6)
                });
            return lista;
        }

        // Valor de la última lectura registrada para un equipo (para calcular delta)
        public static double GetUltimaLecturaValor(int equipoId)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();

            // Intento 1: campo Valor con cualquier Tipo (sin filtrar por 'Lectura')
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Valor FROM Lecturas
                        WHERE EquipoId = @id
                          AND Valor != ''
                          AND Valor IS NOT NULL
                          AND (Descripcion NOT LIKE 'RESET%' OR Descripcion IS NULL)
                        ORDER BY Id DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@id", equipoId);
            var res = cmd.ExecuteScalar()?.ToString();
            if (double.TryParse(res,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0)
                return v;

            // Intento 2: campo HorasActuales en tabla Lecturas
            var cmd2 = con.CreateCommand();
            cmd2.CommandText = @"SELECT HorasActuales FROM Lecturas
                         WHERE EquipoId = @id
                           AND HorasActuales > 0
                         ORDER BY Id DESC LIMIT 1";
            cmd2.Parameters.AddWithValue("@id", equipoId);
            var res2 = cmd2.ExecuteScalar();
            if (res2 != null && res2 != DBNull.Value && Convert.ToDouble(res2) > 0)
                return Convert.ToDouble(res2);

            // Intento 3: campo HorasActuales directo en tabla Equipos
            var cmd3 = con.CreateCommand();
            cmd3.CommandText = "SELECT HorasActuales FROM Equipos WHERE Id = @id";
            cmd3.Parameters.AddWithValue("@id", equipoId);
            var res3 = cmd3.ExecuteScalar();
            if (res3 != null && res3 != DBNull.Value && Convert.ToDouble(res3) > 0)
                return Convert.ToDouble(res3);

            return 0;
        }

        // Valor de la lectura en el momento del último reset (baseline)
        public static double GetValorEnUltimoReset(int equipoId)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Valor FROM Lecturas
                        WHERE EquipoId = @id
                          AND Tipo = 'Mantenimiento'
                          AND (Descripcion LIKE 'RESET%' OR Descripcion LIKE '%RESET%')
                          AND Valor != '' AND Valor IS NOT NULL
                        ORDER BY Id DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@id", equipoId);
            var res = cmd.ExecuteScalar()?.ToString();
            if (double.TryParse(res,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0)
                return v;

            // Sin reset previo → el contador empieza desde 0
            return 0;
        }
        public static Operario GetOperarioPorChatId(string chatId)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id, Nombre, Telefono, Email, Rol, Activo, 
                               COALESCE(TelegramChatId,'')
                        FROM Operarios 
                        WHERE TelegramChatId = @cid AND Activo = 1 
                        LIMIT 1";
            cmd.Parameters.AddWithValue("@cid", chatId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new Operario
            {
                Id = r.GetInt32(0),
                Nombre = r.GetString(1),
                Telefono = r.IsDBNull(2) ? "" : r.GetString(2),
                Email = r.IsDBNull(3) ? "" : r.GetString(3),
                Rol = r.IsDBNull(4) ? "" : r.GetString(4),
                Activo = r.GetInt32(5),
                TelegramChatId = r.GetString(6)
            };
        }

        public static Equipo GetEquipoPorNombre(string nombre)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id, Nombre, Descripcion, Ubicacion, 
                               FrecuenciaMantenimiento, FechaAlta
                        FROM Equipos 
                        WHERE LOWER(Nombre) = LOWER(@n) 
                        LIMIT 1";
            cmd.Parameters.AddWithValue("@n", nombre.Trim());
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new Equipo
            {
                Id = r.GetInt32(0),
                Nombre = r.GetString(1),
                Descripcion = r.IsDBNull(2) ? "" : r.GetString(2),
                Ubicacion = r.IsDBNull(3) ? "" : r.GetString(3),
                FrecuenciaMantenimiento = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                FechaAlta = r.IsDBNull(5) ? "" : r.GetString(5)
            };
        }

        public static List<Equipo> GetEquiposConMantenimientoPendiente(int umbralHoras = 50)
        {
            int.TryParse(LeerConfiguracion("UmbralAviso"), out int ua);
            int.TryParse(LeerConfiguracion("UmbralCritico"), out int uc);
            int umbralAviso = ua > 0 ? ua : 50;
            int umbralCritico = uc > 0 ? uc : 10;
            GuardarConfiguracionSiVacia("SchedulerIntervalodias", "1");
            GuardarConfiguracionSiVacia("SchedulerActivo", "1");
            GuardarConfiguracionSiVacia("DiassinLectura", "15");
            var lista = new List<Equipo>();
            var equipos = GetEquipos().FindAll(e => e.Activo == 1 && e.FrecuenciaMantenimiento > 0);

            foreach (var eq in equipos)
            {
                // Frecuencia real: priorizar horas, luego km, luego días
                int freqHoras = eq.FrecuenciaHoras > 0 ? eq.FrecuenciaHoras : 0;
                int freqKm = eq.FrecuenciaKm > 0 ? eq.FrecuenciaKm : 0;
                int freqDias = eq.FrecuenciaMantenimiento > 0 ? eq.FrecuenciaMantenimiento : 0;

                // Si hay lecturas numéricas en la BD, tratar como horas aunque
                // la frecuencia esté en FrecuenciaMantenimiento
                double ultimaLectura = DatabaseHelper.GetUltimaLecturaValor(eq.Id);
                if (freqHoras == 0 && freqKm == 0 && ultimaLectura > 0)
                    freqHoras = freqDias;   // La frecuencia "días" era realmente horas

                string frecuenciaTexto, restanteTexto, color;

                if (freqHoras > 0)
                {
                    double baseReset = DatabaseHelper.GetValorEnUltimoReset(eq.Id);
                    double delta = ultimaLectura - baseReset;
                    if (delta < 0) delta = ultimaLectura;
                    int restante = (int)(freqHoras - delta);

                    frecuenciaTexto = $"{freqHoras} h";
                    restanteTexto = restante > 0 ? $"{restante} h" : "VENCIDO";
                    color = restante <= 0 ? "Rojo"
                          : restante <= umbralCritico ? "Rojo"
                          : restante <= umbralAviso ? "Naranja"
                          : "Verde";
                }
                else if (freqKm > 0)
                {
                    double baseReset = DatabaseHelper.GetValorEnUltimoReset(eq.Id);
                    double delta = ultimaLectura - baseReset;
                    if (delta < 0) delta = ultimaLectura;
                    int restante = (int)(freqKm - delta);

                    frecuenciaTexto = $"{freqKm:N0} km";
                    restanteTexto = restante > 0 ? $"{restante:N0} km" : "VENCIDO";
                    color = restante <= 0 ? "Rojo"
                          : restante <= umbralCritico ? "Rojo"
                          : restante <= umbralAviso ? "Naranja"
                          : "Verde";
                }
                else if (freqDias > 0)
                {
                    DateTime referencia;
                    if (!string.IsNullOrEmpty(eq.FechaUltimoMantenimiento) &&
                        DateTime.TryParse(eq.FechaUltimoMantenimiento, out var fum))
                        referencia = fum;
                    else if (DateTime.TryParse(eq.FechaAlta, out var fa))
                        referencia = fa;
                    else
                        referencia = DateTime.Now.AddDays(-freqDias);

                    int dias = freqDias - (int)(DateTime.Now - referencia).TotalDays;

                    frecuenciaTexto = $"{freqDias} días";
                    restanteTexto = dias > 0 ? $"{dias} días" : "VENCIDO";
                    color = dias <= 0 ? "Rojo"
                          : dias <= 3 ? "Rojo"
                          : dias <= 7 ? "Naranja"
                          : "Verde";
                }
                else
                {
                    frecuenciaTexto = "No definida";
                    restanteTexto = "-";
                    color = "Verde";
                }
            }
            return lista;
        }

        // ... resto igual (operario asignado, Add a todos)
        // ── Obtener equipo por Id ─────────────────────────────────────────
        public static Equipo ObtenerEquipoPorId(int id)
        {
            using var conexion = new SqliteConnection($"Data Source={rutaDb}");
            conexion.Open();

            var cmd = conexion.CreateCommand();
            cmd.CommandText = @"
        SELECT 
            Id,
            Nombre,
            Descripcion,
            Ubicacion,
            FrecuenciaMantenimiento,
            HorasActuales,
            UltimaLectura,
            HorasUltimoMantenimiento,
            FechaAlta
        FROM Equipos
        WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            return new Equipo
            {
                Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                Nombre = reader["Nombre"]?.ToString() ?? "",
                Descripcion = reader["Descripcion"]?.ToString() ?? "",
                Ubicacion = reader["Ubicacion"]?.ToString() ?? "",
                FrecuenciaMantenimiento = reader["FrecuenciaMantenimiento"] != DBNull.Value ? Convert.ToInt32(reader["FrecuenciaMantenimiento"]) : 0,
                HorasActuales = reader["HorasActuales"] != DBNull.Value ? Convert.ToInt32(reader["HorasActuales"]) : 0,
                UltimaLectura = reader["UltimaLectura"]?.ToString() ?? "",
                HorasUltimoMantenimiento = reader["HorasUltimoMantenimiento"] != DBNull.Value ? Convert.ToInt32(reader["HorasUltimoMantenimiento"]) : 0,
                FechaAlta = reader["FechaAlta"]?.ToString() ?? ""
            };
        }

        // ── Obtener operario por ChatId de Telegram ───────────────────────
        public static Operario ObtenerOperarioPorChatId(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return null;
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id,Nombre,Telefono,Email,Rol,Activo,
                               COALESCE(TelegramChatId,'')
                        FROM Operarios
                        WHERE COALESCE(TelegramChatId,'') = @cid
                          AND Activo = 1";
            cmd.Parameters.AddWithValue("@cid", chatId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new Operario
            {
                Id = r.GetInt32(0),
                Nombre = r.GetString(1),
                Telefono = r.IsDBNull(2) ? "" : r.GetString(2),
                Email = r.IsDBNull(3) ? "" : r.GetString(3),
                Rol = r.IsDBNull(4) ? "" : r.GetString(4),
                Activo = r.GetInt32(5),
                TelegramChatId = r.GetString(6)
            };
        }

        // ── Asignaciones de un operario ───────────────────────────────────
        public static List<Asignacion> ObtenerAsignacionesPorOperario(int operarioId)
        {
            var lista = new List<Asignacion>();
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT a.Id, a.EquipoId, a.OperarioId,
                               e.Nombre, o.Nombre
                        FROM Asignaciones a
                        JOIN Equipos   e ON e.Id = a.EquipoId
                        JOIN Operarios o ON o.Id = a.OperarioId
                        WHERE a.OperarioId = @oid
                          AND e.Activo = 1";
            cmd.Parameters.AddWithValue("@oid", operarioId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Asignacion
                {
                    Id = r.GetInt32(0),
                    EquipoId = r.GetInt32(1),
                    OperarioId = r.GetInt32(2),
                    NombreEquipo = r.IsDBNull(3) ? "" : r.GetString(3),
                    NombreOperario = r.IsDBNull(4) ? "" : r.GetString(4)
                });
            return lista;
        }

        // ── Tabla y métodos de Reset (punto cero para umbral) ─────────────
        public static void InicializarTablaResets(SqliteConnection con)
        {
            var cmd = con.CreateCommand();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ResetLecturas
                        (Id INTEGER PRIMARY KEY AUTOINCREMENT,
                         EquipoId INTEGER NOT NULL,
                         Valor REAL NOT NULL,
                         Fecha TEXT NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        public static void RegistrarReset(int equipoId, double valor)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            InicializarTablaResets(con);
            var cmd = con.CreateCommand();
            cmd.CommandText = @"INSERT INTO ResetLecturas(EquipoId,Valor,Fecha)
                        VALUES(@eid,@v,@f)";
            cmd.Parameters.AddWithValue("@eid", equipoId);
            cmd.Parameters.AddWithValue("@v", valor);
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.ExecuteNonQuery();
        }

        public class EquipoDashboardItem
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public string Ubicacion { get; set; }
            public string Estado { get; set; }   // OK / AVISO / CRITICO / VENCIDO
            public string FrecuenciaTexto { get; set; }   // "500 h" o "10.000 km"
            public string RestanteTexto { get; set; }   // "120 h" o "3.200 km"
            public string UltimaLectura { get; set; }
            public string OperarioAsignado { get; set; }
        }

        public static Configuracion GetConfiguracion()
        {
            var cfg = new Configuracion
            {
                SmtpServidor = LeerConfiguracion("SmtpServidor"),
                SmtpPuerto = LeerConfiguracion("SmtpPuerto"),
                SmtpEmail = LeerConfiguracion("SmtpEmail"),
                SmtpPassword = LeerConfiguracion("SmtpPassword"),
                EmailSupervisor = LeerConfiguracion("EmailSupervisor"),
                TelegramToken = LeerConfiguracion("TelegramToken"),
                TelegramChatSupervisor = LeerConfiguracion("TelegramChatSupervisor")
            };

            int.TryParse(LeerConfiguracion("UmbralAviso"), out int ua);
            int.TryParse(LeerConfiguracion("UmbralCritico"), out int uc);
            cfg.UmbralAviso = ua > 0 ? ua : 50;  // valor por defecto 50h
            cfg.UmbralCritico = uc > 0 ? uc : 10;  // valor por defecto 10h

            return cfg;
        }
        private static void EnsureConfigKey(string clave, string valorDefecto)
        {
            if (string.IsNullOrEmpty(LeerConfiguracion(clave)))
                GuardarConfiguracion(clave, valorDefecto);
        }
        public static void ActualizarHorasEquipo(int equipoId, double horas)
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText = @"UPDATE Equipos 
                        SET HorasActuales = @h,
                            UltimaLectura = @f
                        WHERE Id = @id";
            cmd.Parameters.AddWithValue("@h", (int)horas);
            cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cmd.Parameters.AddWithValue("@id", equipoId);
            cmd.ExecuteNonQuery();
        }
        public static void HacerBackup(string rutaDestino)
        {
            File.Copy(rutaDb, rutaDestino, overwrite: true);
        }

        public static void RestaurarBackup(string rutaOrigen)
        {
            File.Copy(rutaOrigen, rutaDb, overwrite: true);
        }
        public static void LimpiarTodosLosDatos()
        {
            using var con = new SqliteConnection($"Data Source={rutaDb}");
            con.Open();

            // Obtener todas las tablas excepto Configuracion
            var tablas = new List<string>();
            var cmdLista = con.CreateCommand();
            cmdLista.CommandText = @"
                SELECT name FROM sqlite_master 
                WHERE type='table' 
                  AND name NOT LIKE 'sqlite_%'
                  AND name != 'Configuracion'";
                    using (var r = cmdLista.ExecuteReader())
                        while (r.Read())
                            tablas.Add(r.GetString(0));

            // Borrar datos de cada tabla encontrada
            foreach (var tabla in tablas)
            {
                var cmdDel = con.CreateCommand();
                cmdDel.CommandText = $"DELETE FROM [{tabla}]";
                cmdDel.ExecuteNonQuery();
            }

            // Resetear contadores de IDs automáticos
            var cmdSeq = con.CreateCommand();
                cmdSeq.CommandText = @"
                DELETE FROM sqlite_sequence 
                WHERE name != 'Configuracion'";
            cmdSeq.ExecuteNonQuery();
        }
    }   
}