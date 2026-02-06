using System.Collections.ObjectModel;

namespace Casino.Models
{
    public sealed class PlayerSeat
    {
        public int SeatIndex { get; init; }
        public string DisplayName { get; init; } = "";
        public int Chips { get; set; }
        public int CurrentBet { get; set; }
        public string StatusText { get; set; } = "";
        public bool IsDealer { get; set; }
        public bool IsLocal { get; set; }
        public bool IsReady { get; set; }        
        public string LastAction { get; set; } = "";
        public ObservableCollection<Card> HoleCards { get; } = new();
        public bool IsFolded { get; set; }
    }
}