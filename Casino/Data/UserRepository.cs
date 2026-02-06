using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Casino.Models;
using Microsoft.Data.Sqlite;

namespace Casino.Data
{
    public sealed class UserRepository
    {
        public async Task<bool> UserExistsAsync(string userName)
        {
            using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "SELECT COUNT(1) FROM Users WHERE UserName = $name;";
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$name", userName);

            var result = (long)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return result > 0;
        }

        public async Task CreateUserAsync(string userName, string password)
        {
            var hash = HashPassword(password);

            using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "INSERT INTO Users (UserName, PasswordHash, Chips) VALUES ($name, $hash, 5000);";
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$name", userName);
            cmd.Parameters.AddWithValue("$hash", hash);

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<User?> ValidateUserAsync(string userName, string password)
        {
            var hash = HashPassword(password);

            using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = @"
                SELECT Id, UserName, PasswordHash, Chips
                FROM Users
                WHERE UserName = $name AND PasswordHash = $hash;";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$name", userName);
            cmd.Parameters.AddWithValue("$hash", hash);

            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                return new User
                {
                    Id = reader.GetInt32(0),
                    UserName = reader.GetString(1),
                    PasswordHash = reader.GetString(2),
                    Chips = reader.GetInt32(3)
                };
            }

            return null;
        }

        public async Task<int> GetChipsAsync(string userName)
        {
            using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "SELECT Chips FROM Users WHERE UserName = $name;";
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$name", userName);

            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return result is long l ? (int)l : 0;
        }

        public async Task UpdateChipsAsync(string userName, int newChips)
        {
            using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            const string sql = "UPDATE Users SET Chips = $chips WHERE UserName = $name;";
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$name", userName);
            cmd.Parameters.AddWithValue("$chips", newChips);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static string HashPassword(string password)
        {
            // Hash muy simple, suficiente para el boceto
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }
    }
}