using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Casino.Data
{
    public static class DatabaseInitializer
    {
        private static readonly string _dbPath =
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                         "Casino", "casino.db");

        public static string ConnectionString => $"Data Source={_dbPath}";

        public static void EnsureCreated()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            const string createUsersTableSql = @"
                CREATE TABLE IF NOT EXISTS Users (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserName TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    Chips INTEGER NOT NULL DEFAULT 5000
                );";

            const string createTablesTableSql = @"
                CREATE TABLE IF NOT EXISTS Tables (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    OwnerUserName TEXT NOT NULL,
                    IsStarted INTEGER NOT NULL DEFAULT 0
                );";

            const string createTablePlayersSql = @"
                CREATE TABLE IF NOT EXISTS TablePlayers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TableName TEXT NOT NULL,
                    UserName TEXT NOT NULL,
                    IsReady INTEGER NOT NULL DEFAULT 0,
                    UNIQUE(TableName, UserName)
                );";

            const string createChatMessagesSql = @"
                CREATE TABLE IF NOT EXISTS ChatMessages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TableName TEXT NOT NULL,
                    SenderUserName TEXT NOT NULL,
                    Text TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );";

            const string createHandHistorySql = @"
                CREATE TABLE IF NOT EXISTS HandHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserName TEXT NOT NULL,
                    TableName TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    HoleCards TEXT NOT NULL,      -- ej: 'AS KD'
                    BoardCards TEXT NOT NULL,     -- ej: '2C 7H TD'
                    ChipsBefore INTEGER NOT NULL,
                    ChipsAfter INTEGER NOT NULL,
                    Net INTEGER NOT NULL,         -- ChipsAfter - ChipsBefore
                    Result TEXT NOT NULL          -- 'Win' | 'Loss' | 'Fold' | etc.
                );";

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = createUsersTableSql
                                  + createTablesTableSql
                                  + createTablePlayersSql
                                  + createChatMessagesSql
                                  + createHandHistorySql;
                cmd.ExecuteNonQuery();
            }

            // Asegurar columna Chips (migración simple)
            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "PRAGMA table_info(Users);";
                using var reader = checkCmd.ExecuteReader();
                var hasChips = false;
                while (reader.Read())
                {
                    var colName = reader.GetString(1);
                    if (string.Equals(colName, "Chips", System.StringComparison.OrdinalIgnoreCase))
                    {
                        hasChips = true;
                        break;
                    }
                }

                if (!hasChips)
                {
                    using var alterCmd = connection.CreateCommand();
                    alterCmd.CommandText = "ALTER TABLE Users ADD COLUMN Chips INTEGER NOT NULL DEFAULT 5000;";
                    alterCmd.ExecuteNonQuery();
                }
            }

            // Usuario de prueba: admin / admin
            const string checkUserSql = "SELECT COUNT(1) FROM Users WHERE UserName = 'admin';";
            using (var checkCmd2 = connection.CreateCommand())
            {
                checkCmd2.CommandText = checkUserSql;
                var exists = (long)checkCmd2.ExecuteScalar() > 0;
                if (!exists)
                {
                    var hash = HashPassword("admin");
                    const string insertSql = "INSERT INTO Users (UserName, PasswordHash, Chips) VALUES ('admin', $hash, 5000);";
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = insertSql;
                    insertCmd.Parameters.AddWithValue("$hash", hash);
                    insertCmd.ExecuteNonQuery();
                }
            }
        }

        private static string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha.ComputeHash(bytes);
            return System.Convert.ToHexString(hash);
        }
    }
}