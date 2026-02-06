namespace Casino.Models
{
    public sealed class User
    {
        public int Id { get; set; }               // PK autoincrement
        public string UserName { get; set; } = ""; // único
        public string PasswordHash { get; set; } = ""; // Hash simple por ahora

        // NUEVO: saldo global de fichas del usuario
        public int Chips { get; set; }
    }
}