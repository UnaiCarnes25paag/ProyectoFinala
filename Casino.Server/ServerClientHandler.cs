using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Casino.Data;

namespace Casino.Server
{
    internal static class ServerClientHandler
    {
        private static readonly UserRepository _userRepository = new();
        private static readonly TableRepository _tableRepository = new();

        // Estado de la mano de poker en memoria, por nombre de mesa
        private sealed class PokerTableState
        {
            public string TableName { get; }
            public List<string> Deck { get; } = new();
            public Dictionary<string, (string Card1, string Card2)> HoleCards { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> CommunityCards { get; } = new();

            public List<string> PlayersInOrder { get; } = new();
            public int CurrentPlayerIndex { get; set; } = 0;
            public int LastAggressorIndex { get; set; } = 0;

            public int Phase { get; set; } = 0;

            public Dictionary<string, bool> IsFolded { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> PlayerChips { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> CurrentBets { get; } = new(StringComparer.OrdinalIgnoreCase);

            // NUEVO: snapshot de fichas al inicio de la mano
            public Dictionary<string, int> StartingChips { get; } = new(StringComparer.OrdinalIgnoreCase);

            public int Pot { get; set; }
            public int SmallBlind { get; set; } = 10;
            public int BigBlind { get; set; } = 20;
            public int DealerIndex { get; set; } = 0;
            public int CurrentBetAmount { get; set; } = 0;

            public bool HasStartedAtLeastOneHand { get; set; }

            public PokerTableState(string tableName)
            {
                TableName = tableName;
            }

            public string CurrentPlayer => PlayersInOrder.Count == 0
                ? ""
                : PlayersInOrder[Math.Clamp(CurrentPlayerIndex, 0, PlayersInOrder.Count - 1)];
        }

        // Mesa -> estado actual de poker
        private static readonly Dictionary<string, PokerTableState> _pokerTables =
            new(StringComparer.OrdinalIgnoreCase);

        // RNG para barajar
        private static readonly Random _rng = new();

        // Cada cliente mantiene su estado de sesion en memoria
        private sealed class ClientSession
        {
            public string? UserName { get; set; }
            public string? CurrentTable { get; set; }
            public long LastChatMessageId { get; set; }
        }

        public static async Task HandleClientAsync(TcpClient tcpClient)
        {
            using var client = tcpClient;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var session = new ClientSession();

            try
            {
                Console.WriteLine($"[Server] Nuevo cliente: {tcpClient.Client.RemoteEndPoint}");
                await writer.WriteLineAsync("WELCOME CasinoServer 1.0").ConfigureAwait(false);

                // Bucle principal de comando por linea
                while (true)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                        break; // cliente desconectado

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    Console.WriteLine($"[Server] Recibido comando: '{line}' (User='{session.UserName}', Table='{session.CurrentTable}')");

                    var response = await ProcessCommandAsync(line, session).ConfigureAwait(false);

                    Console.WriteLine($"[Server] Respuesta: '{response.Replace("\n", "\\n")}'");

                    // Las respuestas pueden ser varias lineas. Las separamos por '\n' para enviarlas.
                    var parts = response.Split('\n');
                    foreach (var part in parts)
                    {
                        await writer.WriteLineAsync(part).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error con cliente: {ex}");
            }

            Console.WriteLine("Cliente desconectado.");
        }

        private static async Task<string> ProcessCommandAsync(string line, ClientSession session)
        {
            // Comandos en formato MUY simple, separados por espacios
            // Ejemplos:
            //  LOGIN user password
            //  CREATE_TABLE mesa1
            //  JOIN_TABLE mesa1
            //  SET_READY
            //  LEAVE_TABLE
            //  SEND_CHAT texto libre...
            //  POLL_STATE

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToUpperInvariant();
            var args = parts.Length > 1 ? parts[1] : "";

            switch (cmd)
            {
                case "LOGIN":
                    return await HandleLoginAsync(args, session).ConfigureAwait(false);

                case "CREATE_TABLE":
                    return await HandleCreateTableAsync(args, session).ConfigureAwait(false);

                case "JOIN_TABLE":
                    return await HandleJoinTableAsync(args, session).ConfigureAwait(false);

                case "SET_READY":
                    return await HandleSetReadyAsync(session).ConfigureAwait(false);

                case "LEAVE_TABLE":
                    return await HandleLeaveTableAsync(session).ConfigureAwait(false);

                case "SEND_CHAT":
                    return await HandleSendChatAsync(args, session).ConfigureAwait(false);

                case "POLL_STATE":
                    return await HandlePollStateAsync(session).ConfigureAwait(false);

                case "PLAYER_ACTION":
                    return await HandlePlayerActionAsync(args, session).ConfigureAwait(false);

                case "HISTORY":
                    return await HandleHistoryAsync(session).ConfigureAwait(false);

                default:
                    return "ERR Comando_desconocido";
            }
        }

        private static async Task<string> HandleLoginAsync(string args, ClientSession session)
        {
            // LOGIN user password
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return "ERR Formato_LOGIN_invalido";

            var user = parts[0].Trim();
            var pass = parts[1].Trim();

            // Solo validar, no crear usuarios aquí
            var existing = await _userRepository.ValidateUserAsync(user, pass).ConfigureAwait(false);
            if (existing is null)
            {
                return "ERR Credenciales_invalidas";
            }

            session.UserName = user;
            session.CurrentTable = null;
            session.LastChatMessageId = 0;

            return "OK LOGIN";
        }

        private static async Task<string> HandleCreateTableAsync(string args, ClientSession session)
        {
            if (session.UserName is null)
                return "ERR No_logueado";

            var tableName = args.Trim();
            if (string.IsNullOrWhiteSpace(tableName))
                return "ERR Nombre_mesa_vacio";

            var (success, error) = await _tableRepository.CreateTableAsync(tableName, session.UserName).ConfigureAwait(false);
            if (!success)
                return $"ERR {error ?? "No_se_pudo_crear_mesa"}";

            session.CurrentTable = tableName;
            session.LastChatMessageId = 0;

            return "OK CREATE_TABLE";
        }

        private static async Task<string> HandleJoinTableAsync(string args, ClientSession session)
        {
            if (session.UserName is null)
                return "ERR No_logueado";

            var tableName = args.Trim();
            Console.WriteLine($"[Server] HandleJoinTableAsync: User='{session.UserName}', requestedTable='{tableName}'");
            if (string.IsNullOrWhiteSpace(tableName))
                return "ERR Nombre_mesa_vacio";

            var (success, error) = await _tableRepository.JoinTableAsync(tableName, session.UserName).ConfigureAwait(false);
            Console.WriteLine($"[Server] HandleJoinTableAsync: JoinTableAsync success={success}, error='{error}'");
            if (!success)
                return $"ERR {error ?? "No_se_pudo_unir_mesa"}";

            session.CurrentTable = tableName;
            session.LastChatMessageId = 0;

            Console.WriteLine($"[Server] HandleJoinTableAsync: User='{session.UserName}' ahora en mesa='{session.CurrentTable}'");
            return "OK JOIN_TABLE";
        }

        private static async Task<string> HandleSetReadyAsync(ClientSession session)
        {
            if (session.UserName is null)
                return "ERR No_logueado";

            if (string.IsNullOrWhiteSpace(session.CurrentTable))
                return "ERR No_estoy_en_mesa";

            var tableName = session.CurrentTable;

            await _tableRepository.SetReadyAsync(tableName, session.UserName, true);
            await _tableRepository.InsertChatMessageAsync(tableName, "Servidor",
                $"{session.UserName} esta listo.");

            // comprobar si todos estan listos
            var allReady = await _tableRepository.AreAllPlayersReadyAsync(tableName).ConfigureAwait(false);

            // DEBUG: log de cuantos hay y cuantos listos
            var (total, ready) = await _tableRepository.GetPlayerCountsAsync(tableName).ConfigureAwait(false);
            Console.WriteLine($"[DEBUG] Mesa {tableName}: Total={total}, Ready={ready}, AllReady={allReady}");

            if (allReady)
            {
                await _tableRepository.MarkGameStartedAsync(tableName).ConfigureAwait(false);
                await _tableRepository.InsertChatMessageAsync(tableName, "Servidor",
                    $"Todos los jugadores estan listos en {tableName}. La partida comienza.")
                    .ConfigureAwait(false);

                // Iniciar estado de poker en memoria y repartir cartas
                InitPokerHand(tableName);
            }

            return "OK SET_READY";
        }

        private static async Task<string> HandleLeaveTableAsync(ClientSession session)
        {
            if (session.UserName is null)
                return "ERR No_logueado";

            if (string.IsNullOrWhiteSpace(session.CurrentTable))
                return "OK LEAVE_TABLE"; // nada que hacer

            var table = session.CurrentTable;
            var user = session.UserName;

            // NUEVO: si hay mano en curso, el jugador pierde lo apostado y se marca fold
            if (_pokerTables.TryGetValue(table, out var state))
            {
                if (state.CurrentBets.TryGetValue(user, out var betAmount) && betAmount > 0)
                {
                    Console.WriteLine($"[Poker] Mesa '{table}': {user} sale de la mano, pierde sus {betAmount} fichas apostadas.");
                    state.IsFolded[user] = true;
                    // sus fichas ya fueron descontadas de PlayerChips al apostar
                }

                // quitarlo del orden de jugadores
                state.PlayersInOrder.RemoveAll(p => string.Equals(p, user, StringComparison.OrdinalIgnoreCase));
            }

            await _tableRepository.InsertChatMessageAsync(table, "Servidor",
                $"{session.UserName} ha salido de la mesa.").ConfigureAwait(false);

            await _tableRepository.LeaveTableAsync(table, session.UserName).ConfigureAwait(false);

            session.CurrentTable = null;
            session.LastChatMessageId = 0;

            return "OK LEAVE_TABLE";
        }

        private static async Task<string> HandleSendChatAsync(string args, ClientSession session)
        {
            if (session.UserName is null)
                return "ERR No_logueado";

            if (string.IsNullOrWhiteSpace(session.CurrentTable))
                return "ERR No_estoy_en_mesa";

            var text = args.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "ERR Mensaje_vacio";

            await _tableRepository.InsertChatMessageAsync(session.CurrentTable, session.UserName, text)
                .ConfigureAwait(false);

            return "OK SEND_CHAT";
        }

        private static async Task<string> HandlePollStateAsync(ClientSession session)
        {
            if (session.UserName is null)
                return "ERR No_logueado";

            if (string.IsNullOrWhiteSpace(session.CurrentTable))
                return "ERR No_estoy_en_mesa";

            var tableName = session.CurrentTable;

            // jugadores/ready
            var (total, ready) = await _tableRepository.GetPlayerCountsAsync(tableName).ConfigureAwait(false);

            // estado de partida
            var started = await _tableRepository.IsGameStartedAsync(tableName).ConfigureAwait(false);

            // nuevos mensajes de chat
            var messages = await _tableRepository.GetChatMessagesSinceAsync(tableName, session.LastChatMessageId)
                .ConfigureAwait(false);

            var sb = new StringBuilder();
            // cabecera de estado
            sb.Append("OK POLL_STATE ")
              .Append(total.ToString(CultureInfo.InvariantCulture)).Append(' ')
              .Append(ready.ToString(CultureInfo.InvariantCulture)).Append(' ')
              .Append(started ? "1" : "0")
              .Append('\n');

            // Si la partida ha empezado y tenemos estado de poker, mandamos cartas, turno y fase
            if (started && _pokerTables.TryGetValue(tableName, out var pokerState))
            {
                // Cartas del jugador
                if (pokerState.HoleCards.TryGetValue(session.UserName, out var hc))
                {
                    sb.Append("PLAYER_CARDS ")
                      .Append(hc.Card1).Append(' ')
                      .Append(hc.Card2)
                      .Append('\n');
                }

                // Comunitarias
                if (pokerState.CommunityCards.Count > 0)
                {
                    sb.Append("COMMUNITY");
                    foreach (var c in pokerState.CommunityCards)
                    {
                        sb.Append(' ').Append(c);
                    }
                    sb.Append('\n');
                }

                // Turno actual
                var currentPlayer = pokerState.CurrentPlayer;
                if (!string.IsNullOrWhiteSpace(currentPlayer))
                {
                    sb.Append("CURRENT_TURN ")
                      .Append(currentPlayer)
                      .Append('\n');
                }

                // Fase
                var phaseName = pokerState.Phase switch
                {
                    0 => "Preflop",
                    1 => "Flop",
                    2 => "Turn",
                    3 => "River",
                    _ => "Desconocida"
                };
                sb.Append("PHASE ").Append(phaseName).Append('\n');

                // NUEVO: Pot total
                sb.Append("POT ")
                  .Append(pokerState.Pot.ToString(CultureInfo.InvariantCulture))
                  .Append('\n');

                // NUEVO: estado por jugador: nombre, fichas, apuesta current, folded
                foreach (var p in pokerState.PlayersInOrder)
                {
                    var chips = pokerState.PlayerChips.TryGetValue(p, out var c) ? c : 0;
                    var bet = pokerState.CurrentBets.TryGetValue(p, out var b) ? b : 0;
                    var folded = pokerState.IsFolded.TryGetValue(p, out var f) && f ? 1 : 0;

                    sb.Append("PLAYER_STATE ")
                      .Append(p).Append(' ')
                      .Append(chips.ToString(CultureInfo.InvariantCulture)).Append(' ')
                      .Append(bet.ToString(CultureInfo.InvariantCulture)).Append(' ')
                      .Append(folded.ToString(CultureInfo.InvariantCulture))
                      .Append('\n');
                }
            }

            // lineas de chat: CHAT <id> <sender> <text>
            foreach (var (id, sender, text) in messages)
            {
                session.LastChatMessageId = id;
                var safeSender = sender.Replace("|", "\\|");
                var safeText = text.Replace("|", "\\|");
                sb.Append("CHAT ")
                  .Append(id.ToString(CultureInfo.InvariantCulture)).Append(' ')
                  .Append(safeSender).Append(' ')
                  .Append(safeText)
                  .Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }

        private static async Task<string> HandleHistoryAsync(ClientSession session)
        {
            if (session.UserName is null)
                return "ERR No_logueado";

            var list = await _tableRepository.GetHandHistoryForUserAsync(session.UserName, maxRows: 50)
                                             .ConfigureAwait(false);

            Console.WriteLine($"[History] HandleHistoryAsync user={session.UserName}, rows={list.Count}");

            // Formato nuevo: OK HISTORY|entry1|entry2|...
            // entry: <fechaISO>;<mesa>;<net>;<hole>;<board>
            var sb = new StringBuilder();
            sb.Append("OK HISTORY");

            foreach (var (createdAt, tableName, hole, board, net) in list)
            {
                // Escapar por si hubiera ';' o '|' (opcional, aquí asumimos que no)
                sb.Append('|')
                  .Append(createdAt.ToString("O")).Append(';')
                  .Append(tableName.Replace(";", "_").Replace("|", "_")).Append(';')
                  .Append(net.ToString(CultureInfo.InvariantCulture)).Append(';')
                  .Append(hole.Replace(";", ",")).Append(';')
                  .Append(board.Replace(";", ","));
            }

            return sb.ToString();
        }

        // Inicializa o reinicia una mano de poker en la mesa (manteniendo fichas actuales)
        private static async void InitPokerHand(string tableName)
        {
            Console.WriteLine($"[Poker] Iniciando mano para mesa '{tableName}'");

            var playerNames = GetPlayersForTable(tableName);
            if (playerNames.Count == 0)
            {
                Console.WriteLine($"[Poker] No hay jugadores en mesa '{tableName}' para iniciar mano.");
                return;
            }

            // Limitar a 6 jugadores
            if (playerNames.Count > 6)
            {
                playerNames = playerNames.GetRange(0, 6);
            }

            if (!_pokerTables.TryGetValue(tableName, out var state))
            {
                // Primera vez que se crea el estado para esta mesa
                state = new PokerTableState(tableName);

                state.PlayersInOrder.AddRange(playerNames);

                // Fichas iniciales: leer saldo global de Users
                foreach (var p in playerNames)
                {
                    var chips = await _userRepository.GetChipsAsync(p).ConfigureAwait(false);
                    if (chips <= 0) chips = 5000;
                    state.PlayerChips[p] = chips;
                }

                _pokerTables[tableName] = state;
            }
            else
            {
                // Mesa ya existente: actualizar lista de jugadores
                state.PlayersInOrder.Clear();
                state.PlayersInOrder.AddRange(playerNames);

                foreach (var p in playerNames)
                {
                    if (!state.PlayerChips.ContainsKey(p))
                    {
                        var chips = await _userRepository.GetChipsAsync(p).ConfigureAwait(false);
                        if (chips <= 0) chips = 5000;
                        state.PlayerChips[p] = chips;
                    }
                }
            }

            StartNewHand(state);
        }

        private static void StartNewHand(PokerTableState state)
        {
            var tableName = state.TableName;
            var playerNames = state.PlayersInOrder;
            if (playerNames.Count == 0)
                return;

            // Crear y barajar mazo
            var ranks = new[] { "2", "3", "4", "5", "6", "7", "8", "9", "T", "J", "Q", "K", "A" };
            var suits = new[] { "C", "D", "H", "S" };

            state.Deck.Clear();
            foreach (var r in ranks)
                foreach (var s in suits)
                    state.Deck.Add(r + s);

            for (int i = state.Deck.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (state.Deck[i], state.Deck[j]) = (state.Deck[j], state.Deck[i]);
            }

            state.HoleCards.Clear();
            state.CommunityCards.Clear();
            state.IsFolded.Clear();
            state.CurrentBets.Clear();
            state.Pot = 0;
            state.CurrentBetAmount = 0;
            state.Phase = 0;

            // Rotar dealer: si ya hubo mano, pasamos al siguiente jugador
            if (state.HasStartedAtLeastOneHand)
            {
                state.DealerIndex = (state.DealerIndex + 1) % playerNames.Count;
            }
            else
            {
                state.DealerIndex = 0;
                state.HasStartedAtLeastOneHand = true;
            }

            Console.WriteLine($"[Poker] Mesa '{tableName}': dealer en '{playerNames[state.DealerIndex]}'");

            // Resetear flags de fold y apuestas
            foreach (var p in playerNames)
            {
                state.IsFolded[p] = false;
                state.CurrentBets[p] = 0;

                // También garantizar que tienen al menos 0 fichas
                if (!state.PlayerChips.ContainsKey(p))
                    state.PlayerChips[p] = 5000;
            }

            // Repartir 2 cartas a cada jugador
            foreach (var player in playerNames)
            {
                var c1 = DrawCard(state);
                var c2 = DrawCard(state);
                state.HoleCards[player] = (c1, c2);
                Console.WriteLine($"[Poker] Mesa '{tableName}': {player} recibe {c1} {c2}");
            }

            // NUEVO: guardar snapshot de fichas iniciales
            foreach (var p in playerNames)
            {
                var chips = state.PlayerChips[p];
                state.StartingChips[p] = chips;
            }

            // Aplicar blinds y determinar orden de acción
            ApplyBlindsAndSetActionOrder(state);

            // El primer en hablar de la calle actual es el agresor inicial
            state.LastAggressorIndex = state.CurrentPlayerIndex;

            Console.WriteLine($"[Poker] Mesa '{tableName}': turno inicial para '{state.CurrentPlayer}' (fase Preflop)");
        }

        private static void ApplyBlindsAndSetActionOrder(PokerTableState state)
        {
            var tableName = state.TableName;
            var players = state.PlayersInOrder;
            var n = players.Count;

            if (n == 0)
                return;

            // Reset apuestas
            foreach (var p in players)
                state.CurrentBets[p] = 0;

            state.Pot = 0;
            state.CurrentBetAmount = 0;

            if (n == 1)
            {
                // Un solo jugador, edge case: no hay blinds reales ni acción
                state.CurrentPlayerIndex = state.DealerIndex;
                return;
            }

            // Indices relative al dealer
            int dealer = state.DealerIndex;
            int sbIndex;
            int bbIndex;
            int firstToActIndex;

            if (n == 2)
            {
                // Heads-up:
                // Dealer = SB, el otro = BB
                sbIndex = dealer;
                bbIndex = (dealer + 1) % n;

                var sbPlayer = players[sbIndex];
                var bbPlayer = players[bbIndex];

                // Aplicar ciegas 10/20
                int sbAmount = Math.Min(state.SmallBlind, state.PlayerChips[sbPlayer]);
                state.PlayerChips[sbPlayer] -= sbAmount;
                state.CurrentBets[sbPlayer] = sbAmount;
                state.Pot += sbAmount;

                int bbAmount = Math.Min(state.BigBlind, state.PlayerChips[bbPlayer]);
                state.PlayerChips[sbPlayer] -= bbAmount;
                state.CurrentBets[bbPlayer] = bbAmount;
                state.Pot += bbAmount;

                state.CurrentBetAmount = bbAmount;

                Console.WriteLine($"[Poker] Mesa '{tableName}' (HU): SB {sbPlayer} {sbAmount}, BB {bbPlayer} {bbAmount}");

                // Acción preflop: empieza SB (dealer) y luego BB
                firstToActIndex = sbIndex;
            }
            else
            {
                // 3-6 jugadores:
                // Dealer ya está; SB = dealer+1, BB = dealer+2
                sbIndex = (dealer + 1) % n;
                bbIndex = (dealer + 2) % n;

                var sbPlayer = players[sbIndex];
                var bbPlayer = players[bbIndex];

                int sbAmount = Math.Min(state.SmallBlind, state.PlayerChips[sbPlayer]);
                state.PlayerChips[sbPlayer] -= sbAmount;
                state.CurrentBets[sbPlayer] = sbAmount;
                state.Pot += sbAmount;

                int bbAmount = Math.Min(state.BigBlind, state.PlayerChips[bbPlayer]);
                state.PlayerChips[bbPlayer] -= bbAmount;
                state.CurrentBets[bbPlayer] = bbAmount;
                state.Pot += bbAmount;

                state.CurrentBetAmount = bbAmount;

                Console.WriteLine($"[Poker] Mesa '{tableName}': SB {sbPlayer} {sbAmount}, BB {bbPlayer} {bbAmount}");

                // Orden de acción que describes:
                // Jugador1..Jugador4 (los que no son blinds), luego SB y por último BB.
                // En términos de indices: el siguiente a BB (bbIndex+1) hasta dealer,
                // y luego SB y BB. Para simplificar, ponemos como primer en actuar
                // el que está tres posiciones a la izquierda del dealer (dealer+3),
                // que es el primero que "no es blind".
                firstToActIndex = (dealer + 3) % n;
            }

            state.CurrentPlayerIndex = firstToActIndex;
        }

        private static List<string> GetPlayersForTable(string tableName)
        {
            var players = new List<string>();

            try
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection(Casino.Data.DatabaseInitializer.ConnectionString);
                connection.Open();

                const string sql = @"
                    SELECT UserName
                    FROM TablePlayers
                    WHERE TableName = $table
                    ORDER BY Id ASC;";
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$table", tableName);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    players.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Poker] Error al obtener jugadores para mesa '{tableName}': {ex}");
            }

            return players;
        }

        private static string DrawCard(PokerTableState state)
        {
            if (state.Deck.Count == 0)
                throw new InvalidOperationException("No quedan cartas en el mazo.");

            var card = state.Deck[^1];
            state.Deck.RemoveAt(state.Deck.Count - 1);
            return card;
        }

        private static async Task<string> HandlePlayerActionAsync(string args, ClientSession session)
        {
            if (session.UserName is null)
                return "ERR No_logueado";

            if (string.IsNullOrWhiteSpace(session.CurrentTable))
                return "ERR No_estoy_en_mesa";

            var tableName = session.CurrentTable;

            if (!_pokerTables.TryGetValue(tableName, out var state))
                return "ERR Partida_no_inicializada";

            var player = session.UserName;

            // Validar que es su turno
            if (!string.Equals(state.CurrentPlayer, player, StringComparison.OrdinalIgnoreCase))
                return "ERR No_es_tu_turno";

            var actionText = args.Trim();
            if (string.IsNullOrWhiteSpace(actionText))
                return "ERR Accion_vacia";

            var parts = actionText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var action = parts[0].ToUpperInvariant();
            int amount = 0;

            if (parts.Length >= 2 && !int.TryParse(parts[1], out amount))
                return "ERR Cantidad_invalida";

            switch (action)
            {
                case "FOLD":
                    state.IsFolded[player] = true;
                    await _tableRepository.InsertChatMessageAsync(tableName, "Servidor",
                        $"{player} se retira (fold).").ConfigureAwait(false);
                    break;

                case "CHECK":
                    if (state.CurrentBetAmount > state.CurrentBets[player])
                        return "ERR No_puedes_hacer_check_debes_pagar";
                    await _tableRepository.InsertChatMessageAsync(tableName, "Servidor",
                        $"{player} hace check.").ConfigureAwait(false);
                    break;

                case "CALL":
                    {
                        var toPay = state.CurrentBetAmount - state.CurrentBets[player];
                        if (toPay < 0) toPay = 0;
                        if (toPay > state.PlayerChips[player])
                            return "ERR No_tienes_fichas_suficientes";

                        state.PlayerChips[player] -= toPay;
                        state.CurrentBets[player] += toPay;
                        state.Pot += toPay;

                        await _tableRepository.InsertChatMessageAsync(tableName, "Servidor",
                            $"{player} paga {toPay}.").ConfigureAwait(false);
                    }
                    break;

                case "BET":
                case "RAISE":
                    if (amount <= 0)
                        return "ERR Cantidad_invalida";

                    var needed = amount;
                    if (needed > state.PlayerChips[player])
                        return "ERR No_tienes_fichas_suficientes";

                    state.PlayerChips[player] -= needed;
                    state.CurrentBets[player] += needed;
                    state.Pot += needed;

                    state.CurrentBetAmount = state.CurrentBets[player];

                    // NUEVO: este jugador es el nuevo agresor; la calle debe
                    // continuar hasta que todos igualen o foldeen.
                    state.LastAggressorIndex = state.PlayersInOrder.FindIndex(p =>
                        string.Equals(p, player, StringComparison.OrdinalIgnoreCase));

                    await _tableRepository.InsertChatMessageAsync(tableName, "Servidor",
                        $"{player} {action.ToLowerInvariant()} {needed}.").ConfigureAwait(false);
                    break;

                default:
                    return "ERR Accion_desconocida";
            }

            // ¿Queda solo un jugador activo?
            if (CountActivePlayers(state) == 1)
            {
                var winner = GetSingleActivePlayer(state);
                if (!string.IsNullOrWhiteSpace(winner))
                {
                    state.PlayerChips[winner] += state.Pot;
                    await _tableRepository.InsertChatMessageAsync(tableName, "Servidor",
                        $"{winner} gana el bote de {state.Pot} puntos.").ConfigureAwait(false);

                    Console.WriteLine($"[Poker] Mesa '{tableName}': mano terminada, ganador {winner} (pot={state.Pot}).");

                    // NUEVO: guardar stacks resultantes en Users.Chips
                    await PersistChipsForTableAsync(state).ConfigureAwait(false);

                    // NUEVO: guardar historial de mano
                    await SaveHandHistoryAsync(state, tableName, winner).ConfigureAwait(false);

                    // Fin de partida: marcar tabla como no iniciada,
                    // resetear READY de todos y limpiar estado de poker en memoria
                    await _tableRepository.MarkGameFinishedAsync(tableName).ConfigureAwait(false);
                    await _tableRepository.ResetAllReadyAsync(tableName).ConfigureAwait(false);
                    _pokerTables.Remove(tableName);
                }
            }
            else
            {
                // Avanzar turno/fase
                AdvanceTurnOrPhase(state, tableName);
            }

            return "OK PLAYER_ACTION";
        }

        private static int CountActivePlayers(PokerTableState state)
        {
            var count = 0;
            foreach (var p in state.PlayersInOrder)
            {
                if (!state.IsFolded.TryGetValue(p, out var folded) || !folded)
                    count++;
            }
            return count;
        }

        private static string GetSingleActivePlayer(PokerTableState state)
        {
            string? last = null;
            var count = 0;
            foreach (var p in state.PlayersInOrder)
            {
                if (!state.IsFolded.TryGetValue(p, out var folded) || !folded)
                {
                    last = p;
                    count++;
                }
            }
            return count == 1 && last is not null ? last : "";
        }

        private static void AdvanceTurnOrPhase(PokerTableState state, string tableName)
        {
            if (state.PlayersInOrder.Count == 0)
                return;

            // Avanzar al siguiente jugador activo
            for (int i = 0; i < state.PlayersInOrder.Count; i++)
            {
                state.CurrentPlayerIndex = (state.CurrentPlayerIndex + 1) % state.PlayersInOrder.Count;
                var candidate = state.PlayersInOrder[state.CurrentPlayerIndex];

                if (!state.IsFolded.TryGetValue(candidate, out var folded) || !folded)
                {
                    Console.WriteLine($"[Poker] Mesa '{tableName}': turno pasa a '{state.CurrentPlayer}' (fase={state.Phase})");
                    break;
                }
            }

            // Cuando volvemos al último agresor, la calle ha terminado
            if (state.CurrentPlayerIndex == state.LastAggressorIndex)
            {
                // Resetear apuestas por calle
                state.CurrentBetAmount = 0;
                foreach (var p in state.PlayersInOrder)
                    state.CurrentBets[p] = 0;

                switch (state.Phase)
                {
                    case 0: // Preflop -> Flop
                        if (state.Deck.Count >= 3)
                        {
                            state.CommunityCards.Clear();
                            state.CommunityCards.Add(DrawCard(state));
                            state.CommunityCards.Add(DrawCard(state));
                            state.CommunityCards.Add(DrawCard(state));
                            Console.WriteLine($"[Poker] Mesa '{tableName}': FLOP -> {string.Join(' ', state.CommunityCards)}");
                        }
                        state.Phase = 1;
                        // Primer en hablar postflop = siguiente al dealer
                        state.CurrentPlayerIndex = (state.DealerIndex + 1) % state.PlayersInOrder.Count;
                        state.LastAggressorIndex = state.CurrentPlayerIndex;
                        break;

                    case 1: // Flop -> Turn
                        if (state.Deck.Count >= 1)
                        {
                            state.CommunityCards.Add(DrawCard(state));
                            Console.WriteLine($"[Poker] Mesa '{tableName}': TURN -> {state.CommunityCards[^1]}");
                        }
                        state.Phase = 2;
                        state.CurrentPlayerIndex = (state.DealerIndex + 1) % state.PlayersInOrder.Count;
                        state.LastAggressorIndex = state.CurrentPlayerIndex;
                        break;

                    case 2: // Turn -> River
                        if (state.Deck.Count >= 1)
                        {
                            state.CommunityCards.Add(DrawCard(state));
                            Console.WriteLine($"[Poker] Mesa '{tableName}': RIVER -> {state.CommunityCards[^1]}");
                        }
                        state.Phase = 3;
                        state.CurrentPlayerIndex = (state.DealerIndex + 1) % state.PlayersInOrder.Count;
                        state.LastAggressorIndex = state.CurrentPlayerIndex;
                        break;

                    case 3:
                        // Ronda de River completada -> showdown y fin de partida
                        Console.WriteLine($"[Poker] Mesa '{tableName}': fase River completada, hacemos showdown y terminamos partida.");
                        DoShowdownAndFinishGame(state, tableName);
                        break;
                }
            }
        }

        private enum HandRank
        {
            HighCard = 1,
            Pair,
            TwoPair,
            ThreeOfAKind,
            Straight,
            Flush,
            FullHouse,
            FourOfAKind,
            StraightFlush
        }

        private sealed class EvaluatedHand
        {
            public HandRank Rank { get; }
            public int[] Tiebreaker { get; }

            public EvaluatedHand(HandRank rank, int[] tiebreaker)
            {
                Rank = rank;
                Tiebreaker = tiebreaker;
            }
        }

        private static readonly Dictionary<char, int> _rankValue = new()
        {
            ['2'] = 2,
            ['3'] = 3,
            ['4'] = 4,
            ['5'] = 5,
            ['6'] = 6,
            ['7'] = 7,
            ['8'] = 8,
            ['9'] = 9,
            ['T'] = 10,
            ['J'] = 11,
            ['Q'] = 12,
            ['K'] = 13,
            ['A'] = 14
        };

        private static EvaluatedHand EvaluateBestHand(IEnumerable<string> cards)
        {
            // cards: 7 strings tipo "AS", "TD", etc.

            var list = new List<(int rank, char suit)>();
            foreach (var c in cards)
            {
                if (c.Length < 2) continue;
                var r = _rankValue[c[0]];
                var s = c[^1];
                list.Add((r, s));
            }

            list.Sort((a, b) => b.rank.CompareTo(a.rank)); // desc

            // Conteos por rango
            var byRank = new Dictionary<int, int>();
            foreach (var (r, _) in list)
                byRank[r] = byRank.TryGetValue(r, out var cnt) ? cnt + 1 : 1;

            // Conteos por palo
            var bySuit = new Dictionary<char, List<int>>();
            foreach (var (r, s) in list)
            {
                if (!bySuit.TryGetValue(s, out var l))
                {
                    l = new List<int>();
                    bySuit[s] = l;
                }
                l.Add(r);
            }

            // ¿Flush?
            List<int>? flushRanks = null;
            foreach (var kv in bySuit)
            {
                if (kv.Value.Count >= 5)
                {
                    kv.Value.Sort((a, b) => b.CompareTo(a));
                    flushRanks = kv.Value;
                    break;
                }
            }

            int HighestStraight(IReadOnlyList<int> ranks)
            {
                var distinct = new SortedSet<int>(ranks);
                var ordered = new List<int>(distinct);
                ordered.Sort();

                int best = 0;
                int run = 1;
                for (int i = 1; i < ordered.Count; i++)
                {
                    if (ordered[i] == ordered[i - 1] + 1)
                    {
                        run++;
                        if (run >= 5)
                            best = ordered[i];
                    }
                    else
                    {
                        run = 1;
                    }
                }

                // Wheel A2345
                if (distinct.Contains(14) &&
                    distinct.Contains(2) &&
                    distinct.Contains(3) &&
                    distinct.Contains(4) &&
                    distinct.Contains(5))
                {
                    best = Math.Max(best, 5);
                }

                return best;
            }

            // Straight Flush
            if (flushRanks is not null)
            {
                var sfHigh = HighestStraight(flushRanks);
                if (sfHigh > 0)
                {
                    return new EvaluatedHand(HandRank.StraightFlush, new[] { sfHigh });
                }
            }

            // Agrupar por rango
            var groups = new List<(int rank, int count)>();
            foreach (var kv in byRank)
                groups.Add((kv.Key, kv.Value));
            groups.Sort((a, b) =>
            {
                var c = b.count.CompareTo(a.count);
                return c != 0 ? c : b.rank.CompareTo(a.rank);
            });

            // Poker
            if (groups.Count > 0 && groups[0].count == 4)
            {
                var kicker = 0;
                foreach (var (r, _) in list)
                {
                    if (r != groups[0].rank)
                    {
                        kicker = r;
                        break;
                    }
                }
                return new EvaluatedHand(HandRank.FourOfAKind, new[] { groups[0].rank, kicker });
            }

            // Full house
            if (groups.Count > 1 && groups[0].count == 3 && groups[1].count >= 2)
            {
                return new EvaluatedHand(HandRank.FullHouse, new[] { groups[0].rank, groups[1].rank });
            }

            // Flush
            if (flushRanks is not null)
            {
                var top5 = flushRanks.GetRange(0, Math.Min(5, flushRanks.Count));
                return new EvaluatedHand(HandRank.Flush, top5.ToArray());
            }

            // Straight
            var allRanks = new List<int>();
            foreach (var (r, _) in list) allRanks.Add(r);
            var straightHigh = HighestStraight(allRanks);
            if (straightHigh > 0)
            {
                return new EvaluatedHand(HandRank.Straight, new[] { straightHigh });
            }

            // Trío
            if (groups.Count > 0 && groups[0].count == 3)
            {
                var kickers = new List<int>();
                foreach (var (r, _) in list)
                    if (r != groups[0].rank) kickers.Add(r);
                while (kickers.Count > 2) kickers.RemoveAt(kickers.Count - 1);
                var tb = new List<int> { groups[0].rank };
                tb.AddRange(kickers);
                return new EvaluatedHand(HandRank.ThreeOfAKind, tb.ToArray());
            }

            // Doble pareja
            if (groups.Count > 1 && groups[0].count == 2 && groups[1].count == 2)
            {
                int kicker = 0;
                foreach (var (r, _) in list)
                    if (r != groups[0].rank && r != groups[1].rank)
                    {
                        kicker = r;
                        break;
                    }
                return new EvaluatedHand(HandRank.TwoPair,
                    new[]
                    {
                        Math.Max(groups[0].rank, groups[1].rank),
                        Math.Min(groups[0].rank, groups[1].rank),
                        kicker
                    });
            }

            // Pareja
            if (groups.Count > 0 && groups[0].count == 2)
            {
                var kickers = new List<int>();
                foreach (var (r, _) in list)
                    if (r != groups[0].rank) kickers.Add(r);
                while (kickers.Count > 3) kickers.RemoveAt(kickers.Count - 1);
                var tb = new List<int> { groups[0].rank };
                tb.AddRange(kickers);
                return new EvaluatedHand(HandRank.Pair, tb.ToArray());
            }

            // Carta alta
            var hcVals = new List<int>();
            foreach (var (r, _) in list) hcVals.Add(r);
            while (hcVals.Count > 5) hcVals.RemoveAt(hcVals.Count - 1);
            return new EvaluatedHand(HandRank.HighCard, hcVals.ToArray());
        }

        private static int CompareHands(EvaluatedHand a, EvaluatedHand b)
        {
            if (a.Rank != b.Rank)
                return a.Rank.CompareTo(b.Rank);

            var len = Math.Min(a.Tiebreaker.Length, b.Tiebreaker.Length);
            for (int i = 0; i < len; i++)
            {
                if (a.Tiebreaker[i] != b.Tiebreaker[i])
                    return a.Tiebreaker[i].CompareTo(b.Tiebreaker[i]);
            }

            return 0;
        }

        private static async void DoShowdownAndFinishGame(PokerTableState state, string tableName)
        {
            try
            {
                var community = state.CommunityCards;
                if (community.Count < 5)
                {
                    Console.WriteLine($"[Poker] Mesa '{tableName}': showdown sin 5 comunitarias, ignorado.");
                    return;
                }

                string? bestPlayer = null;
                EvaluatedHand? bestHand = null;

                foreach (var player in state.PlayersInOrder)
                {
                    if (state.IsFolded.TryGetValue(player, out var folded) && folded)
                        continue;

                    if (!state.HoleCards.TryGetValue(player, out var hc))
                        continue;

                    var allCards = new List<string>
                    {
                        hc.Card1,
                        hc.Card2
                    };
                    allCards.AddRange(community);

                    var eval = EvaluateBestHand(allCards);
                    if (bestHand is null || CompareHands(eval, bestHand) > 0)
                    {
                        bestHand = eval;
                        bestPlayer = player;
                    }
                }

                if (bestPlayer is not null && bestHand is not null)
                {
                    state.PlayerChips[bestPlayer] += state.Pot;

                    var rankText = bestHand.Rank.ToString();
                    await _tableRepository.InsertChatMessageAsync(tableName, "Servidor",
                        $"{bestPlayer} gana el bote de {state.Pot} puntos con {rankText}.")
                        .ConfigureAwait(false);

                    Console.WriteLine($"[Poker] Mesa '{tableName}': showdown ganador {bestPlayer}, rank={rankText}, pot={state.Pot}");

                    // NUEVO: guardar stacks resultantes en Users.Chips
                    await PersistChipsForTableAsync(state).ConfigureAwait(false);

                    // NUEVO: guardar historial de mano
                    await SaveHandHistoryAsync(state, tableName, bestPlayer).ConfigureAwait(false);

                    // Fin de partida: marcar tabla como no iniciada,
                    // resetear READY de todos y limpiar estado de poker
                    await _tableRepository.MarkGameFinishedAsync(tableName).ConfigureAwait(false);
                    await _tableRepository.ResetAllReadyAsync(tableName).ConfigureAwait(false);
                    _pokerTables.Remove(tableName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Poker] Error en showdown en mesa '{tableName}': {ex}");
            }
        }

        private static async Task PersistChipsForTableAsync(PokerTableState state)
        {
            foreach (var player in state.PlayersInOrder)
            {
                if (state.PlayerChips.TryGetValue(player, out var chips))
                {
                    await _userRepository.UpdateChipsAsync(player, chips).ConfigureAwait(false);
                }
            }
        }

        private static async Task SaveHandHistoryAsync(PokerTableState state, string tableName, string winner)
        {
            // Board final (puede ser menos de 5 cartas si se terminó antes de river)
            var board = string.Join(' ', state.CommunityCards);

            foreach (var player in state.PlayersInOrder)
            {
                if (!state.HoleCards.TryGetValue(player, out var hc))
                    continue;

                var hole = $"{hc.Card1} {hc.Card2}";

                // Fichas antes de la mano (snapshot al empezar)
                var chipsBefore = state.StartingChips.TryGetValue(player, out var start)
                    ? start
                    : 0;

                // Fichas después de la mano (ya con pot repartido)
                var chipsAfter = state.PlayerChips.TryGetValue(player, out var end)
                    ? end
                    : chipsBefore;

                // Resultado simple
                var result = string.Equals(player, winner, StringComparison.OrdinalIgnoreCase)
                    ? "Win"
                    : (chipsAfter < chipsBefore ? "Loss" : "Other");

                Console.WriteLine($"[History] Guardando mano: user={player}, table={tableName}, hole={hole}, board='{board}', before={chipsBefore}, after={chipsAfter}, result={result}");

                await _tableRepository.InsertHandHistoryAsync(
                    player,
                    tableName,
                    hole,
                    board,
                    chipsBefore,
                    chipsAfter,
                    result
                ).ConfigureAwait(false);
            }
        }
    }
}