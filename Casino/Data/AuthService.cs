using System;
using System.Threading.Tasks;

namespace Casino.Data
{
    public sealed class AuthService : IAuthService
    {
        private readonly UserRepository _userRepository = new();

        public async Task<bool> ValidateUserAsync(string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
                return false;

            var user = await _userRepository.ValidateUserAsync(userName, password)
                                            .ConfigureAwait(false);
            return user is not null;
        }

        public Task<bool> IsAdminAsync(string userName)
        {
            // Para el ejercicio: consideramos "admin" como admin
            var isAdmin = string.Equals(userName, "admin", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(isAdmin);
        }
    }
}