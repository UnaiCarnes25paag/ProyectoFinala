using System;
using System.Threading.Tasks;
using Casino.Data;
using Casino.ViewModels;
using Moq;
using Xunit;

namespace Test
{
    public sealed class LoginViewModelTests
    {
        private static LoginViewModel CreateVm(Mock<IAuthService> mock)
            => new LoginViewModel(mock.Object);

        [Fact]
        public async Task Login_EmptyUserOrPassword_SetsErrorAndNotAuthenticated()
        {
            var mock = new Mock<IAuthService>(MockBehavior.Strict);
            var vm = CreateVm(mock);

            vm.UserName = "";
            vm.Password = "";

            await vm.LoginAsync();

            Assert.False(vm.IsAuthenticated);
            Assert.Equal("Erabiltzailea edo pasahitza hutsik", vm.StatusMessage);

            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Login_WrongCredentials_SetsErrorAndNotAuthenticated()
        {
            var mock = new Mock<IAuthService>();
            mock.Setup(s => s.ValidateUserAsync("user", "bad"))
                .ReturnsAsync(false);

            var vm = CreateVm(mock);
            vm.UserName = "user";
            vm.Password = "bad";

            await vm.LoginAsync();

            Assert.False(vm.IsAuthenticated);
            Assert.Equal("Kredentzial okerrak", vm.StatusMessage);

            mock.Verify(s => s.ValidateUserAsync("user", "bad"), Times.Once);
            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Login_NormalUser_SucceedsAndIsAdminFalse()
        {
            var mock = new Mock<IAuthService>();
            mock.Setup(s => s.ValidateUserAsync("user", "1234"))
                .ReturnsAsync(true);
            mock.Setup(s => s.IsAdminAsync("user"))
                .ReturnsAsync(false);

            var vm = CreateVm(mock);
            vm.UserName = "user";
            vm.Password = "1234";

            await vm.LoginAsync();

            Assert.True(vm.IsAuthenticated);
            Assert.False(vm.IsAdmin);
            Assert.Equal(string.Empty, vm.StatusMessage);

            mock.VerifyAll();
        }

        [Fact]
        public async Task Login_AdminUser_SucceedsAndIsAdminTrue()
        {
            var mock = new Mock<IAuthService>();
            mock.Setup(s => s.ValidateUserAsync("admin", "admin1234"))
                .ReturnsAsync(true);
            mock.Setup(s => s.IsAdminAsync("admin"))
                .ReturnsAsync(true);

            var vm = CreateVm(mock);
            vm.UserName = "admin";
            vm.Password = "admin1234";

            await vm.LoginAsync();

            Assert.True(vm.IsAuthenticated);
            Assert.True(vm.IsAdmin);
            Assert.Equal(string.Empty, vm.StatusMessage);

            mock.VerifyAll();
        }

        [Fact]
        public async Task Login_ConnectionException_SetsKonexioErrorea()
        {
            var mock = new Mock<IAuthService>();
            mock.Setup(s => s.ValidateUserAsync("user", "1234"))
                .ThrowsAsync(new InvalidOperationException("DB down"));

            var vm = CreateVm(mock);
            vm.UserName = "user";
            vm.Password = "1234";

            await vm.LoginAsync();

            Assert.False(vm.IsAuthenticated);
            Assert.Equal("Konexio errorea", vm.StatusMessage);

            mock.Verify(s => s.ValidateUserAsync("user", "1234"), Times.Once);
            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Debug_NormalUser_Flow()
        {
            var mock = new Mock<IAuthService>();
            mock.Setup(s => s.ValidateUserAsync("user", "1234"))
                .ReturnsAsync(true);
            mock.Setup(s => s.IsAdminAsync("user"))
                .ReturnsAsync(false);

            var vm = new LoginViewModel(mock.Object);
            vm.UserName = "user";
            vm.Password = "1234";

            await vm.LoginAsync();

            // Logs para ver qué ha pasado
            Console.WriteLine($"StatusMessage='{vm.StatusMessage}' IsAuthenticated={vm.IsAuthenticated} IsAdmin={vm.IsAdmin}");

            Assert.True(mock.Invocations.Count >= 1); // al menos se ha llamado a algo del mock
        }
    }
}
