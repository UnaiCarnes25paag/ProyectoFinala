namespace Casino.Models
{
    public sealed class HandHistoryEntry
    {
        public DateTime CreatedAt { get; init; }
        public string TableName { get; init; } = "";
        public string HoleCards { get; init; } = "";
        public string BoardCards { get; init; } = "";
        public int Net { get; init; }   // positivo = ganó, negativo = perdió
    }
}