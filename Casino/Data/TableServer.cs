using System;
using System.Collections.Generic;
using System.Linq;

namespace Casino.Data
{
    public sealed class TableInfo
    {
        public string Name { get; }
        private readonly HashSet<string> _players = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> Players => _players;

        public TableInfo(string name)
        {
            Name = name;
        }

        public bool AddPlayer(string userName) => _players.Add(userName);
        public bool RemovePlayer(string userName) => _players.Remove(userName);
        public bool IsEmpty => _players.Count == 0;
    }

    /// <summary>
    /// Servidor de mesas en memoria (solo para boceto).
    /// </summary>
    public sealed class TableServer
    {
        private static readonly Lazy<TableServer> _instance = new(() => new TableServer());
        public static TableServer Instance => _instance.Value;

        private readonly Dictionary<string, TableInfo> _tables =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new();

        private TableServer() { }

        public (bool Success, string? Error, TableInfo? Table) CreateTable(string tableName, string userName)
        {
            tableName = tableName.Trim();
            userName = userName.Trim();

            if (string.IsNullOrWhiteSpace(tableName))
                return (false, "Mahaiaren izena ezin da hutsik egon.", null);

            lock (_lock)
            {
                if (_tables.ContainsKey(tableName))
                    return (false, "Izen hori duen mahai bat dago jada.", null);

                var table = new TableInfo(tableName);
                table.AddPlayer(userName);
                _tables[tableName] = table;
                return (true, null, table);
            }
        }

        public (bool Success, string? Error, TableInfo? Table) JoinTable(string tableName, string userName)
        {
            tableName = tableName.Trim();
            userName = userName.Trim();

            if (string.IsNullOrWhiteSpace(tableName))
                return (false, "El nombre de la mesa no puede estar vacio.", null);

            lock (_lock)
            {
                if (!_tables.TryGetValue(tableName, out var table))
                    return (false, "Ez dago izen hori duen mahairik.", null);

                if (!table.AddPlayer(userName))
                    return (false, "Dagoeneko mahai honetan zaude.", table);

                return (true, null, table);
            }
        }

        public void LeaveTable(string tableName, string userName)
        {
            tableName = tableName.Trim();
            userName = userName.Trim();

            lock (_lock)
            {
                if (!_tables.TryGetValue(tableName, out var table))
                    return;

                table.RemovePlayer(userName);
                if (table.IsEmpty)
                {
                    _tables.Remove(tableName);
                }
            }
        }
    }
}