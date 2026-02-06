namespace Casino.Models
{
    public enum Suit { Clubs, Diamonds, Hearts, Spades }

    public sealed record Card(string Rank, Suit Suit);
}