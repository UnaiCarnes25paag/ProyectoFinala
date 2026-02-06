using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Casino.Data;
using Casino.Models;
using System.Linq;

namespace Casino.ViewModels
{
    public sealed class PokerViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _readyPollTimer;

        // Info jugador / mesa
        private string _localPlayerName;
        private string _password;
        private string _tableName = "";
        private string _currentTableName = "";
        private bool _isInTable;
        private long _lastChatMessageId = 0;
        private readonly ServerClient _serverClient;

        public string LocalPlayerName
        {
            get => _localPlayerName;
            set { _localPlayerName = value; OnPropertyChanged(); }
        }

        public string TableName
        {
            get => _tableName;
            set
            {
                _tableName = value;
                OnPropertyChanged();
                RaiseTableCommandsCanExecuteChanged();
            }
        }

        public string CurrentTableName
        {
            get => _currentTableName;
            private set { _currentTableName = value; OnPropertyChanged(); }
        }

        public bool IsInTable
        {
            get => _isInTable;
            private set
            {
                _isInTable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWaitingForReady));
                RaiseTableCommandsCanExecuteChanged();
                UpdateReadyPolling();
            }
        }

        // Estado de la mesa
        private int _pot;
        private string _phaseText = "Pre-Flop";
        private int _smallBlind = 50;
        private int _bigBlind = 100;
        private int _handNumber = 1;

        public int Pot { get => _pot; set { _pot = value; OnPropertyChanged(); } }
        public string PhaseText { get => _phaseText; set { _phaseText = value; OnPropertyChanged(); } }
        public int SmallBlind { get => _smallBlind; set { _smallBlind = value; OnPropertyChanged(); } }
        public int BigBlind { get => _bigBlind; set { _bigBlind = value; OnPropertyChanged(); } }
        public int HandNumber { get => _handNumber; set { _handNumber = value; OnPropertyChanged(); } }

        // Acciones / apuestas
        private bool _isLocalTurn;
        private int _turnCountdown = 30;
        private int _minBet = 100;
        private int _maxBet = 1000;
        private int _betAmount = 100;

        public bool IsLocalTurn
        {
            get => _isLocalTurn;
            set
            {
                _isLocalTurn = value;
                OnPropertyChanged();
                RaiseTableCommandsCanExecuteChanged();
            }
        }
        public int TurnCountdown { get => _turnCountdown; set { _turnCountdown = value; OnPropertyChanged(); } }
        public int MinBet { get => _minBet; set { _minBet = value; OnPropertyChanged(); } }
        public int MaxBet { get => _maxBet; set { _maxBet = value; OnPropertyChanged(); } }
        public int BetAmount { get => _betAmount; set { _betAmount = value; OnPropertyChanged(); UpdateActionTexts(); } }

        private string _checkOrCallText = "Check";
        private string _betOrRaiseText = "Bet";
        public string CheckOrCallText { get => _checkOrCallText; set { _checkOrCallText = value; OnPropertyChanged(); } }
        public string BetOrRaiseText { get => _betOrRaiseText; set { _betOrRaiseText = value; OnPropertyChanged(); } }

        public ObservableCollection<Card> CommunityCards { get; } = new();
        public ObservableCollection<PlayerSeat> PlayerSeats { get; } = new();
        public ObservableCollection<PlayerSeat> PlayerSummary { get; } = new();
        public ObservableCollection<string> TableLog { get; } = new();

        private PlayerSeat? _localSeat;
        public PlayerSeat? LocalSeat
        {
            get => _localSeat;
            private set { _localSeat = value; OnPropertyChanged(); }
        }

        // Chat
        private string _chatInput = "";
        public ObservableCollection<ChatMessage> ChatMessages { get; } = new();
        public string ChatInput
        {
            get => _chatInput;
            set
            {
                _chatInput = value;
                OnPropertyChanged();
                (SendChatCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        // Status
        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // Estado del juego
        private bool _isGameStarted;
        public bool IsGameStarted
        {
            get => _isGameStarted;
            set
            {
                _isGameStarted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWaitingForReady));
                UpdateReadyPolling();
                RaiseTableCommandsCanExecuteChanged();
            }
        }

        private bool _isLocalReady;
        public bool IsLocalReady
        {
            get => _isLocalReady;
            set
            {
                _isLocalReady = value;
                OnPropertyChanged();
                RaiseTableCommandsCanExecuteChanged();
            }
        }

        private int _totalPlayers;
        public int TotalPlayers
        {
            get => _totalPlayers;
            set { _totalPlayers = value; OnPropertyChanged(); }
        }

        private int _readyPlayers;
        public int ReadyPlayers
        {
            get => _readyPlayers;
            set { _readyPlayers = value; OnPropertyChanged(); }
        }

        public bool IsWaitingForReady => IsInTable && !IsGameStarted;

        // Modo estadísticas
        private bool _isShowingStats;
        public bool IsShowingStats
        {
            get => _isShowingStats;
            private set
            {
                _isShowingStats = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatsButtonText));
                RaiseTableCommandsCanExecuteChanged();
            }
        }

        public string StatsButtonText => IsShowingStats ? "Salir estadísticas" : "Estadísticas";

        // Commands
        public ICommand CreateTableCommand { get; }
        public ICommand JoinTableCommand { get; }
        public ICommand LeaveTableCommand { get; }
        public ICommand ToggleReadyCommand { get; }
        public ICommand FoldCommand { get; }
        public ICommand CheckOrCallCommand { get; }
        public ICommand BetOrRaiseCommand { get; }
        public ICommand SetMaxBetCommand { get; }
        public ICommand SendChatCommand { get; }
        public ICommand ShowHistoryCommand { get; }

        private int _localChips;
        public int LocalChips
        {
            get => _localChips;
            set { _localChips = value; OnPropertyChanged(); }
        }

        public ObservableCollection<HandHistoryEntry> HandHistory { get; } = new();

        // Textos de estadísticas para mostrar en la UI
        private string _statsSummary = "Sin datos de estadísticas.";
        public string StatsSummary
        {
            get => _statsSummary;
            private set { _statsSummary = value; OnPropertyChanged(); }
        }

        private string _winRateText = "";
        public string WinRateText
        {
            get => _winRateText;
            private set { _winRateText = value; OnPropertyChanged(); }
        }

        private string _mostPlayedRanksText = "";
        public string MostPlayedRanksText
        {
            get => _mostPlayedRanksText;
            private set { _mostPlayedRanksText = value; OnPropertyChanged(); }
        }

        private string _mostPlayedHandsText = "";
        public string MostPlayedHandsText
        {
            get => _mostPlayedHandsText;
            private set { _mostPlayedHandsText = value; OnPropertyChanged(); }
        }

        public PokerViewModel(string userName, string password)
        {
            _localPlayerName = userName;
            _password = password;

            try
            {
                _serverClient = new ServerClient("127.0.0.1", 5000);
                _serverClient.LineReceived += OnServerLineReceived;
                _ = _serverClient.SendAsync($"LOGIN {LocalPlayerName} {_password}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo conectar al servidor: {ex.Message}";
            }

            CreateTableCommand = new RelayCommand(
                _ => CreateTable(),
                _ => !string.IsNullOrWhiteSpace(TableName) && !IsInTable && !IsShowingStats);

            JoinTableCommand = new RelayCommand(
                _ => JoinTable(),
                _ => !string.IsNullOrWhiteSpace(TableName) && !IsInTable && !IsShowingStats);

            LeaveTableCommand = new RelayCommand(_ => LeaveTable(), _ => IsInTable);

            ToggleReadyCommand = new RelayCommand(_ => ToggleReady(), _ => IsInTable && !IsGameStarted && !IsLocalReady);

            FoldCommand = new RelayCommand(_ => DoAction("Fold"), _ => IsInTable && IsLocalTurn);
            CheckOrCallCommand = new RelayCommand(_ => DoAction(CheckOrCallText), _ => IsInTable && IsLocalTurn);
            BetOrRaiseCommand = new RelayCommand(_ => DoAction($"{BetOrRaiseText} {BetAmount}"), _ => IsInTable && IsLocalTurn && BetAmount >= MinBet);
            SetMaxBetCommand = new RelayCommand(_ => BetAmount = MaxBet);
            SendChatCommand = new RelayCommand(_ => SendChat(), _ => !string.IsNullOrWhiteSpace(ChatInput));

            // Botón de estadísticas (mismo comando para entrar/salir)
            ShowHistoryCommand = new RelayCommand(_ => ToggleStats(), _ => _serverClient is not null && _serverClient.IsConnected);

            _readyPollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _readyPollTimer.Tick += async (_, _) => await PollAsync();

            ClearTableState();
            UpdateActionTexts();
        }

        private async void CreateTable()
        {
            StatusMessage = "";

            if (_serverClient is null || !_serverClient.IsConnected)
            {
                StatusMessage = "Servidor no disponible.";
                return;
            }

            var table = TableName.Trim();
            if (string.IsNullOrWhiteSpace(table))
            {
                StatusMessage = "El nombre de la mesa no puede estar vacio.";
                return;
            }

            string response;
            try
            {
                response = await _serverClient.SendAndWaitAsync(
                    $"CREATE_TABLE {table}",
                    "OK CREATE_TABLE");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error al crear mesa: {ex.Message}";
                return;
            }

            if (!response.StartsWith("OK CREATE_TABLE", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = response;
                return;
            }

            CurrentTableName = table;
            IsInTable = true;
            IsGameStarted = false;
            IsLocalReady = false;
            TotalPlayers = 0;
            ReadyPlayers = 0;

            Log($"Mesa creada: {CurrentTableName}");

            InitDemoTable();
            await PollAsync();
        }

        private async void JoinTable()
        {
            StatusMessage = "";

            if (_serverClient is null || !_serverClient.IsConnected)
            {
                StatusMessage = "Servidor no disponible.";
                return;
            }

            var table = TableName.Trim();
            if (string.IsNullOrWhiteSpace(table))
            {
                StatusMessage = "El nombre de la mesa no puede estar vacio.";
                return;
            }

            string response;
            try
            {
                response = await _serverClient.SendAndWaitAsync(
                    $"JOIN_TABLE {table}",
                    "OK JOIN_TABLE");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error al unirse a la mesa: {ex.Message}";
                return;
            }

            if (!response.StartsWith("OK JOIN_TABLE", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = response;
                CurrentTableName = "";
                IsInTable = false;
                IsGameStarted = false;
                IsLocalReady = false;
                TotalPlayers = 0;
                ReadyPlayers = 0;
                ClearTableState();
                return;
            }

            CurrentTableName = table;
            IsInTable = true;
            IsGameStarted = false;
            IsLocalReady = false;
            TotalPlayers = 0;
            ReadyPlayers = 0;

            Log($"Te has unido a la mesa: {CurrentTableName}");

            InitDemoTable();
            await PollAsync();
        }

        private void InitDemoTable()
        {
            PlayerSeats.Clear();
            PlayerSummary.Clear();
            CommunityCards.Clear();
            TableLog.Clear();

            var localSeat = new PlayerSeat
            {
                SeatIndex = 1,
                DisplayName = LocalPlayerName,
                Chips = 5000,
                CurrentBet = 0,
                StatusText = IsLocalReady ? "Listo" : "No listo",
                IsDealer = true,
                IsLocal = true,
                IsReady = IsLocalReady,
                LastAction = "",
            };
            PlayerSeats.Add(localSeat);
            PlayerSummary.Add(localSeat);

            LocalSeat = localSeat;

            Pot = 0;
            PhaseText = IsGameStarted ? "Pre-Flop" : "";
            IsLocalTurn = false;
        }

        private void ClearTableState()
        {
            PlayerSeats.Clear();
            PlayerSummary.Clear();
            CommunityCards.Clear();
            TableLog.Clear();
            LocalSeat = null;
            Pot = 0;
            PhaseText = "";
            IsLocalTurn = false;
        }

        private async void DoAction(string action)
        {
            if (!IsInTable || string.IsNullOrWhiteSpace(CurrentTableName))
                return;

            if (_serverClient is null || !_serverClient.IsConnected)
            {
                StatusMessage = "Servidor no disponible.";
                return;
            }

            Log($"{LocalPlayerName}: {action}");

            try
            {
                var response = await _serverClient.SendAndWaitAsync(
                    $"PLAYER_ACTION {action}",
                    "OK PLAYER_ACTION");

                if (!response.StartsWith("OK PLAYER_ACTION", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = response;
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error al enviar acción: {ex.Message}";
                return;
            }

            IsLocalTurn = false;
        }

        private PlayerSeat? GetLocal()
        {
            foreach (var p in PlayerSeats)
                if (p.IsLocal)
                    return p;
            return null;
        }

        private void UpdateActionTexts()
        {
            CheckOrCallText = BetAmount == 0 ? "Check" : "Call";
            BetOrRaiseText = BetAmount <= BigBlind ? "Bet" : "Raise";
        }

        private async void SendChat()
        {
            var text = ChatInput.Trim();
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(CurrentTableName))
                return;

            if (_serverClient is null || !_serverClient.IsConnected)
            {
                StatusMessage = "Servidor no disponible.";
                return;
            }

            await _serverClient.SendAsync($"SEND_CHAT {text}");
            ChatInput = "";
        }

        private void Log(string msg) => TableLog.Add(msg);

        private void RaiseTableCommandsCanExecuteChanged()
        {
            (CreateTableCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (JoinTableCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LeaveTableCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FoldCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CheckOrCallCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BetOrRaiseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleReadyCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async void ToggleReady()
        {
            if (!IsInTable || string.IsNullOrWhiteSpace(CurrentTableName))
                return;

            if (IsLocalReady)
                return;

            if (_serverClient is null || !_serverClient.IsConnected)
            {
                StatusMessage = "Servidor no disponible.";
                return;
            }

            IsLocalReady = true;

            var local = GetLocal();
            if (local is not null)
            {
                local.IsReady = true;
                local.StatusText = "Listo";
                OnPropertyChanged(nameof(PlayerSeats));
            }

            Log($"{LocalPlayerName} esta listo.");

            await _serverClient.SendAsync("SET_READY");
            RaiseTableCommandsCanExecuteChanged();
        }

        private async void LeaveTable()
        {
            StatusMessage = "";

            if (_serverClient is not null && _serverClient.IsConnected && IsInTable && !string.IsNullOrWhiteSpace(CurrentTableName))
            {
                await _serverClient.SendAsync("LEAVE_TABLE");
            }

            CurrentTableName = "";
            IsInTable = false;
            IsGameStarted = false;
            IsLocalReady = false;
            TotalPlayers = 0;
            ReadyPlayers = 0;
            _lastChatMessageId = 0;

            ClearTableState();
        }

        private void UpdateReadyPolling()
        {
            Console.WriteLine($"[Client] UpdateReadyPolling: IsInTable={IsInTable}, CurrentTableName='{CurrentTableName}', TimerEnabled={_readyPollTimer.IsEnabled}");

            if (IsInTable && !string.IsNullOrWhiteSpace(CurrentTableName))
            {
                if (!_readyPollTimer.IsEnabled)
                {
                    Console.WriteLine("[Client] UpdateReadyPolling: START timer");
                    _readyPollTimer.Start();
                }
            }
            else
            {
                if (_readyPollTimer.IsEnabled)
                {
                    Console.WriteLine("[Client] UpdateReadyPolling: STOP timer");
                    _readyPollTimer.Stop();
                }
            }
        }

        private async Task PollAsync()
        {
            Console.WriteLine($"[Client] PollAsync: IsInTable={IsInTable}, CurrentTableName='{CurrentTableName}'");

            if (!IsInTable || string.IsNullOrWhiteSpace(CurrentTableName))
            {
                Console.WriteLine("[Client] PollAsync: no envia POLL_STATE (fuera de mesa)");
                return;
            }

            if (_serverClient is null || !_serverClient.IsConnected)
            {
                Console.WriteLine("[Client] PollAsync: servidor no disponible");
                return;
            }

            Console.WriteLine("[Client] PollAsync: envia POLL_STATE");
            await _serverClient.SendAsync("POLL_STATE");
        }

        private void OnServerLineReceived(string line)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (line.StartsWith("OK POLL_STATE", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 &&
                        int.TryParse(parts[2], out var total) &&
                        int.TryParse(parts[3], out var ready))
                    {
                        TotalPlayers = total;
                        ReadyPlayers = ready;

                        var startedFlag = parts[4];
                        var started = startedFlag == "1";

                        if (started && !IsGameStarted)
                        {
                            IsGameStarted = true;
                            PhaseText = "Pre-Flop";
                            Log($"La partida ha comenzado en {CurrentTableName} (desde servidor).");
                            InitDemoTable();
                        }
                        else if (!started && IsGameStarted)
                        {
                            Log($"La partida ha terminado en {CurrentTableName}. Esperando nuevo Ready.");

                            IsGameStarted = false;
                            IsLocalReady = false;

                            ClearTableState();
                            Pot = 0;
                            PhaseText = "";
                            CommunityCards.Clear();
                        }
                    }
                }
                else if (line.StartsWith("PLAYER_CARDS ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        var c1 = ParseServerCard(parts[1]);
                        var c2 = ParseServerCard(parts[2]);

                        var local = LocalSeat ?? GetLocal();
                        if (local is not null)
                        {
                            local.HoleCards.Clear();
                            local.HoleCards.Add(c1);
                            local.HoleCards.Add(c2);
                            OnPropertyChanged(nameof(LocalSeat));
                            OnPropertyChanged(nameof(PlayerSeats));
                            OnPropertyChanged(nameof(PlayerSummary));
                        }
                    }
                }
                else if (line.StartsWith("COMMUNITY", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    CommunityCards.Clear();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var card = ParseServerCard(parts[i]);
                        CommunityCards.Add(card);
                    }
                }
                else if (line.StartsWith("CURRENT_TURN ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var current = parts[1];
                        IsLocalTurn = string.Equals(current, LocalPlayerName, StringComparison.OrdinalIgnoreCase);
                    }
                }
                else if (line.StartsWith("PHASE ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        PhaseText = parts[1];
                    }
                }
                else if (line.StartsWith("POT ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out var pot))
                    {
                        Pot = pot;
                    }
                }
                else if (line.StartsWith("PLAYER_STATE ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 &&
                        int.TryParse(parts[2], out var chips) &&
                        int.TryParse(parts[3], out var bet) &&
                        int.TryParse(parts[4], out var foldedInt))
                    {
                        var user = parts[1];
                        var folded = foldedInt != 0;

                        var seat = FindOrCreateSeat(user);
                        seat.Chips = chips;
                        seat.CurrentBet = bet;
                        seat.IsFolded = folded;
                        seat.StatusText = folded ? "Fold" : "";

                        if (string.Equals(user, LocalPlayerName, StringComparison.OrdinalIgnoreCase))
                        {
                            LocalChips = chips;
                        }

                        OnPropertyChanged(nameof(PlayerSeats));
                        OnPropertyChanged(nameof(PlayerSummary));
                    }
                }
                else if (line.StartsWith("CHAT ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        var sender = parts[2].Replace("\\|", "|");
                        var text = parts[3].Replace("\\|", "|");
                        ChatMessages.Add(new ChatMessage(sender, text));
                    }
                }
                else if (line.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = line;
                }
            });
        }

        private PlayerSeat FindOrCreateSeat(string userName)
        {
            foreach (var p in PlayerSeats)
            {
                if (string.Equals(p.DisplayName, userName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            var seat = new PlayerSeat
            {
                SeatIndex = PlayerSeats.Count + 1,
                DisplayName = userName,
                Chips = 0,
                CurrentBet = 0,
                StatusText = "",
                IsDealer = false,
                IsLocal = string.Equals(userName, LocalPlayerName, StringComparison.OrdinalIgnoreCase),
                IsReady = true,
                LastAction = ""
            };

            PlayerSeats.Add(seat);
            PlayerSummary.Add(seat);

            if (seat.IsLocal)
                LocalSeat = seat;

            return seat;
        }

        private static Card ParseServerCard(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length < 2)
                return new Card("?", Suit.Clubs);

            var rankChar = code[0];
            var rank = rankChar switch
            {
                'T' => "10",
                _ => rankChar.ToString()
            };

            var suitChar = code[^1];
            var suit = suitChar switch
            {
                'C' => Suit.Clubs,
                'D' => Suit.Diamonds,
                'H' => Suit.Hearts,
                'S' => Suit.Spades,
                _ => Suit.Clubs
            };

            return new Card(rank, suit);
        }

        private async void ToggleStats()
        {
            if (IsShowingStats)
            {
                IsShowingStats = false;
                StatusMessage = "Has salido del modo estadísticas.";
                return;
            }

            if (_serverClient is null || !_serverClient.IsConnected)
            {
                StatusMessage = "Servidor no disponible.";
                return;
            }

            HandHistory.Clear();

            string response;
            try
            {
                response = await _serverClient.SendAndWaitAsync("HISTORY", "OK HISTORY");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error al pedir historial: {ex.Message}";
                return;
            }

            if (!response.StartsWith("OK HISTORY", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = response;
                return;
            }

            var parts = response.Split('|', StringSplitOptions.RemoveEmptyEntries);
            int historyEntries = 0;

            for (int i = 1; i < parts.Length; i++)
            {
                var entry = parts[i];
                var fields = entry.Split(';');
                if (fields.Length < 5)
                    continue;

                var dateStr = fields[0];
                var tableName = fields[1];
                var netStr = fields[2];
                var hole = fields[3];
                var board = fields[4];

                if (!DateTime.TryParse(
                        dateStr,
                        null,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var createdAt))
                {
                    createdAt = DateTime.MinValue;
                }

                if (!int.TryParse(netStr, out var net))
                    net = 0;

                HandHistory.Add(new HandHistoryEntry
                {
                    CreatedAt = createdAt,
                    TableName = tableName,
                    HoleCards = hole,
                    BoardCards = board,
                    Net = net
                });

                historyEntries++;
            }

            RecomputeStatistics();
            StatusMessage = $"Estadísticas cargadas: {HandHistory.Count} manos.";
            IsShowingStats = true;
        }

        private void RecomputeStatistics()
        {
            if (HandHistory.Count == 0)
            {
                StatsSummary = "Sin datos de estadísticas.";
                WinRateText = "";
                MostPlayedRanksText = "";
                MostPlayedHandsText = "";
                return;
            }

            var total = HandHistory.Count;
            var positive = 0;
            var negative = 0;

            var rankCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var handCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var h in HandHistory)
            {
                if (h.Net > 0) positive++;
                if (h.Net < 0) negative++;

                if (!string.IsNullOrWhiteSpace(h.HoleCards))
                {
                    var tokens = h.HoleCards.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var t in tokens)
                    {
                        if (t.Length >= 1)
                        {
                            var rank = t[0].ToString();
                            rankCounts[rank] = rankCounts.TryGetValue(rank, out var c) ? c + 1 : 1;
                        }
                    }

                    var handKey = h.HoleCards.Trim();
                    handCounts[handKey] = handCounts.TryGetValue(handKey, out var hc) ? hc + 1 : 1;
                }
            }

            var posRate = total > 0 ? (double)positive / total * 100.0 : 0.0;
            StatsSummary = $"Total manos: {total}  |  Ganadoras (Net>0): {positive}  |  Perdedoras (Net<0): {negative}";
            WinRateText = $"Porcentaje de manos con Net positivo: {posRate:0.0}%";

            if (rankCounts.Count > 0)
            {
                var maxRank = rankCounts
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key)
                    .First();
                var rankPct = (double)maxRank.Value / rankCounts.Values.Sum() * 100.0;
                MostPlayedRanksText = $"Rango más jugado: {maxRank.Key} ({rankPct:0.0}% de las cartas propias)";
            }
            else
            {
                MostPlayedRanksText = "Sin datos de rangos más jugados.";
            }

            if (handCounts.Count > 0)
            {
                var maxHand = handCounts
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key)
                    .First();
                var handPct = (double)maxHand.Value / total * 100.0;
                MostPlayedHandsText = $"Mano inicial más jugada: {maxHand.Key} ({handPct:0.0}% de las manos)";
            }
            else
            {
                MostPlayedHandsText = "Sin datos de manos iniciales.";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed record ChatMessage(string Sender, string Message);
}