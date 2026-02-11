using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Casino.Data
{
    public sealed class TableRepository
    {
        public async Task<(bool Success, string? Error)> CreateTableAsync(string tableName, string ownerUserName)
        {
            tableName = tableName.Trim();
            ownerUserName = ownerUserName.Trim();

            Console.WriteLine($"[TableRepository] CreateTableAsync: table='{tableName}', owner='{ownerUserName}'");

            if (string.IsNullOrWhiteSpace(tableName))
            {
                Console.WriteLine("[TableRepository] CreateTableAsync: nombre vacio");
                return (false, "Mahaiaren izena ezin da hutsik egon.");
            }

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false) as SqliteTransaction;

            const string existsSql = "SELECT COUNT(1) FROM Tables WHERE Name = $name;";
            await using (var existsCmd = connection.CreateCommand())
            {
                existsCmd.Transaction = tx;
                existsCmd.CommandText = existsSql;
                existsCmd.Parameters.AddWithValue("$name", tableName);
                var count = (long)await existsCmd.ExecuteScalarAsync().ConfigureAwait(false);
                Console.WriteLine($"[TableRepository] CreateTableAsync: existe count={count}");
                if (count > 0)
                {
                    Console.WriteLine("[TableRepository] CreateTableAsync: ya existe mesa");
                    return (false, "Izen hori duen mahai bat dago jada.");
                }
            }

            const string insertTableSql = "INSERT INTO Tables (Name, OwnerUserName, IsStarted) VALUES ($name, $owner, 0);";
            await using (var insertTableCmd = connection.CreateCommand())
            {
                insertTableCmd.Transaction = tx;
                insertTableCmd.CommandText = insertTableSql;
                insertTableCmd.Parameters.AddWithValue("$name", tableName);
                insertTableCmd.Parameters.AddWithValue("$owner", ownerUserName);
                var a = await insertTableCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                Console.WriteLine($"[TableRepository] CreateTableAsync: insert Tables affected={a}");
            }

            const string insertPlayerSql = @"
                INSERT OR IGNORE INTO TablePlayers (TableName, UserName, IsReady)
                VALUES ($table, $user, 0);";
            await using (var insertPlayerCmd = connection.CreateCommand())
            {
                insertPlayerCmd.Transaction = tx;
                insertPlayerCmd.CommandText = insertPlayerSql;
                insertPlayerCmd.Parameters.AddWithValue("$table", tableName);
                insertPlayerCmd.Parameters.AddWithValue("$user", ownerUserName);
                var a = await insertPlayerCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                Console.WriteLine($"[TableRepository] CreateTableAsync: insert owner in TablePlayers affected={a}");
            }

            if (tx is not null)
                await tx.CommitAsync().ConfigureAwait(false);

            Console.WriteLine("[TableRepository] CreateTableAsync: OK");
            return (true, null);
        }

        public async Task<(bool Exists, string? Error)> TableExistsAsync(string tableName)
        {
            tableName = tableName.Trim();
            Console.WriteLine($"[TableRepository] TableExistsAsync: table='{tableName}'");

            if (string.IsNullOrWhiteSpace(tableName))
            {
                Console.WriteLine("[TableRepository] TableExistsAsync: nombre vacio");
                return (false, "Mahaiaren izena ezin da hutsik egon.");
            }

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "SELECT COUNT(1) FROM Tables WHERE Name = $name;";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$name", tableName);

            var count = (long)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            Console.WriteLine($"[TableRepository] TableExistsAsync: count={count}");

            return (count > 0, count > 0 ? null : "Ez dago izen hori duen mahairik.");
        }

        public async Task<(bool Success, string? Error)> JoinTableAsync(string tableName, string userName)
        {
            tableName = tableName.Trim();
            userName = userName.Trim();

            Console.WriteLine($"[TableRepository] JoinTableAsync: table='{tableName}', user='{userName}'");

            var (exists, error) = await TableExistsAsync(tableName).ConfigureAwait(false);
            Console.WriteLine($"[TableRepository] JoinTableAsync: exists={exists}, error='{error}'");

            if (!exists)
                return (false, error);

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string insertPlayerSql = @"
                INSERT OR IGNORE INTO TablePlayers (TableName, UserName, IsReady)
                VALUES ($table, $user, 0);";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = insertPlayerSql;
            cmd.Parameters.AddWithValue("$table", tableName);
            cmd.Parameters.AddWithValue("$user", userName);
            var affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            Console.WriteLine($"[TableRepository] JoinTableAsync: ExecuteNonQuery affected={affected}");

            if (affected == 0)
            {
                Console.WriteLine("[TableRepository] JoinTableAsync: NO se pudo insertar jugador (ya estaba o error).");
                return (false, "Ezin_izan_da_mahaira_sartu");
            }

            Console.WriteLine("[TableRepository] JoinTableAsync: OK");
            return (true, null);
        }

        public async Task SetReadyAsync(string tableName, string userName, bool isReady)
        {
            tableName = tableName.Trim();
            userName = userName.Trim();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                UPDATE TablePlayers
                SET IsReady = $ready
                WHERE TableName = $table AND UserName = $user;";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ready", isReady ? 1 : 0);
            cmd.Parameters.AddWithValue("$table", tableName);
            cmd.Parameters.AddWithValue("$user", userName);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<bool> AreAllPlayersReadyAsync(string tableName)
        {
            tableName = tableName.Trim();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT COUNT(1) AS Total,
                       SUM(CASE WHEN IsReady = 1 THEN 1 ELSE 0 END) AS ReadyCount
                FROM TablePlayers
                WHERE TableName = $table;";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$table", tableName);

            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await reader.ReadAsync().ConfigureAwait(false))
                return false;

            var total = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
            var ready = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);

            return total > 0 && total == ready;
        }

        public async Task MarkGameStartedAsync(string tableName)
        {
            tableName = tableName.Trim();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "UPDATE Tables SET IsStarted = 1 WHERE Name = $name;";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$name", tableName);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task MarkGameFinishedAsync(string tableName)
        {
            tableName = tableName.Trim();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "UPDATE Tables SET IsStarted = 0 WHERE Name = $name;";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$name", tableName);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<(int Total, int Ready)> GetPlayerCountsAsync(string tableName)
        {
            tableName = tableName.Trim();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT COUNT(1) AS Total,
                       SUM(CASE WHEN IsReady = 1 THEN 1 ELSE 0 END) AS ReadyCount
                FROM TablePlayers
                WHERE TableName = $table;";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$table", tableName);

            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await reader.ReadAsync().ConfigureAwait(false))
                return (0, 0);

            var total = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
            var ready = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);

            return ((int)total, (int)ready);
        }

        /// <summary>
        /// Elimina al jugador de la mesa y, si no queda nadie, borra tambien la mesa.
        /// </summary>
        public async Task LeaveTableAsync(string tableName, string userName)
        {
            tableName = tableName.Trim();
            userName = userName.Trim();

            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(userName))
                return;

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false) as SqliteTransaction;

            // 1) borrar jugador de TablePlayers
            const string deletePlayerSql = @"
                DELETE FROM TablePlayers
                WHERE TableName = $table AND UserName = $user;";
            await using (var deletePlayerCmd = connection.CreateCommand())
            {
                deletePlayerCmd.Transaction = tx;
                deletePlayerCmd.CommandText = deletePlayerSql;
                deletePlayerCmd.Parameters.AddWithValue("$table", tableName);
                deletePlayerCmd.Parameters.AddWithValue("$user", userName);
                await deletePlayerCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // 2) comprobar cuantos jugadores quedan en esa mesa
            const string countPlayersSql = @"
                SELECT COUNT(1)
                FROM TablePlayers
                WHERE TableName = $table;";
            long remainingPlayers;
            await using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = tx;
                countCmd.CommandText = countPlayersSql;
                countCmd.Parameters.AddWithValue("$table", tableName);
                var result = await countCmd.ExecuteScalarAsync().ConfigureAwait(false);
                remainingPlayers = (result is long l) ? l : 0;
            }

            // 3) si no queda nadie, borrar la mesa de Tables
            if (remainingPlayers == 0)
            {
                const string deleteTableSql = @"
                    DELETE FROM Tables
                    WHERE Name = $table;";
                await using var deleteTableCmd = connection.CreateCommand();
                deleteTableCmd.Transaction = tx;
                deleteTableCmd.CommandText = deleteTableSql;
                deleteTableCmd.Parameters.AddWithValue("$table", tableName);
                await deleteTableCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            if (tx is not null)
                await tx.CommitAsync().ConfigureAwait(false);
        }

        public async Task<bool> IsGameStartedAsync(string tableName)
        {
            tableName = tableName.Trim();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "SELECT IsStarted FROM Tables WHERE Name = $name;";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$name", tableName);

            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return result is long l && l != 0;
        }

        public async Task<long> InsertChatMessageAsync(string tableName, string senderUserName, string text)
        {
            tableName = tableName.Trim();
            senderUserName = senderUserName.Trim();
            text = text.Trim();

            if (string.IsNullOrWhiteSpace(tableName) ||
                string.IsNullOrWhiteSpace(senderUserName) ||
                string.IsNullOrWhiteSpace(text))
                return 0;

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                INSERT INTO ChatMessages (TableName, SenderUserName, Text, CreatedAt)
                VALUES ($table, $sender, $text, $createdAt);
                SELECT last_insert_rowid();";

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$table", tableName);
            cmd.Parameters.AddWithValue("$sender", senderUserName);
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));

            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return result is long id ? id : 0;
        }

        public async Task<IReadOnlyList<(long Id, string Sender, string Text)>> GetChatMessagesSinceAsync(
            string tableName, long lastMessageId)
        {
            tableName = tableName.Trim();

            var messages = new List<(long, string, string)>();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT Id, SenderUserName, Text
                FROM ChatMessages
                WHERE TableName = $table AND Id > $lastId
                ORDER BY Id ASC;";

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$table", tableName);
            cmd.Parameters.AddWithValue("$lastId", lastMessageId);

            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var sender = reader.GetString(1);
                var body = reader.GetString(2);
                messages.Add((id, sender, body));
            }

            return messages;
        }

        public async Task ResetAllReadyAsync(string tableName)
        {
            tableName = tableName.Trim();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                UPDATE TablePlayers
                SET IsReady = 0
                WHERE TableName = $table;";

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$table", tableName);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task InsertHandHistoryAsync(
            string userName,
            string tableName,
            string holeCards,
            string boardCards,
            int chipsBefore,
            int chipsAfter,
            string result)
        {
            userName = userName.Trim();
            tableName = tableName.Trim();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                INSERT INTO HandHistory
                    (UserName, TableName, CreatedAt, HoleCards, BoardCards, ChipsBefore, ChipsAfter, Net, Result)
                VALUES
                    ($user, $table, $createdAt, $hole, $board, $before, $after, $net, $result);";

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$user", userName);
            cmd.Parameters.AddWithValue("$table", tableName);
            cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$hole", holeCards);
            cmd.Parameters.AddWithValue("$board", boardCards);
            cmd.Parameters.AddWithValue("$before", chipsBefore);
            cmd.Parameters.AddWithValue("$after", chipsAfter);
            cmd.Parameters.AddWithValue("$net", chipsAfter - chipsBefore);
            cmd.Parameters.AddWithValue("$result", result);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<(DateTime CreatedAt, string TableName, string HoleCards, string BoardCards, int Net)>>
            GetHandHistoryForUserAsync(string userName, int maxRows = 50)
        {
            userName = userName.Trim();

            var list = new List<(DateTime, string, string, string, int)>();

            await using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT CreatedAt, TableName, HoleCards, BoardCards, Net
                FROM HandHistory
                WHERE UserName = $user
                ORDER BY Id DESC
                LIMIT $max;";
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$user", userName);
            cmd.Parameters.AddWithValue("$max", maxRows);

            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var createdAtStr = reader.GetString(0);
                var tableName = reader.GetString(1);
                var hole = reader.GetString(2);
                var board = reader.GetString(3);
                var net = reader.GetInt32(4);

                var createdAt = DateTime.Parse(createdAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind);
                list.Add((createdAt, tableName, hole, board, net));
            }

            Console.WriteLine($"[History] GetHandHistoryForUserAsync user={userName}, rows={list.Count}");
            return list;
        }
    }
}