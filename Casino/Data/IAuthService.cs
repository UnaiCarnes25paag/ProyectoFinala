using System.Threading.Tasks;

namespace Casino.Data
{
    public interface IAuthService
    {
        Task<bool> ValidateUserAsync(string userName, string password);
        Task<bool> IsAdminAsync(string userName);
    }
}