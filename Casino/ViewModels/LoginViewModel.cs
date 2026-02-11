using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Casino.Data;

namespace Casino.ViewModels
{
    public sealed class LoginViewModel : INotifyPropertyChanged
    {
        private string _userName = "";
        private string _password = "";
        private string _confirmPassword = "";
        private bool _isRegisterMode;
        private string _statusMessage = "";
        private string _submitText = "Saioa hasi";
        private string _toggleText = "Kontua sortu";

        private readonly UserRepository _repository = new();
        private readonly IAuthService _authService;

        private bool _isAuthenticated;
        private bool _isAdmin;

        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set { _confirmPassword = value; OnPropertyChanged(); }
        }

        public bool IsRegisterMode
        {
            get => _isRegisterMode;
            set
            {
                _isRegisterMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLoginMode));
                UpdateTexts();
            }
        }

        public bool IsLoginMode => !IsRegisterMode;

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string SubmitText
        {
            get => _submitText;
            private set { _submitText = value; OnPropertyChanged(); }
        }

        public string ToggleText
        {
            get => _toggleText;
            private set { _toggleText = value; OnPropertyChanged(); }
        }

        public bool IsAuthenticated
        {
            get => _isAuthenticated;
            private set { _isAuthenticated = value; OnPropertyChanged(); }
        }

        public bool IsAdmin
        {
            get => _isAdmin;
            private set { _isAdmin = value; OnPropertyChanged(); }
        }

        public ICommand ToggleModeCommand { get; }
        public ICommand SubmitCommand { get; }

        public event Action<string>? LoginSucceeded;

        // Constructor por defecto para la UI real
        public LoginViewModel()
            : this(new AuthService())
        {
        }

        // Constructor que se usará desde los tests (inyectando IAuthService)
        public LoginViewModel(IAuthService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));

            ToggleModeCommand = new RelayCommand(_ => ToggleMode());
            SubmitCommand = new RelayCommand(async _ => await SubmitAsync(), _ => CanSubmit());
            UpdateTexts();
        }

        private void ToggleMode()
        {
            IsRegisterMode = !IsRegisterMode;
            StatusMessage = "";
            Password = "";
            ConfirmPassword = "";
        }

        private void UpdateTexts()
        {
            if (IsRegisterMode)
            {
                SubmitText = "Erregistratu";
                ToggleText = "Dagoeneko kontua dut";
            }
            else
            {
                SubmitText = "Saioa hasi";
                ToggleText = "Kontua sortu";
            }
        }

        private bool CanSubmit()
        {
            if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
                return false;

            if (IsRegisterMode && string.IsNullOrWhiteSpace(ConfirmPassword))
                return false;

            return true;
        }

        private async Task SubmitAsync()
        {
            StatusMessage = "";
            IsAuthenticated = false;
            IsAdmin = false;

            if (IsRegisterMode)
            {
                await RegisterAsync();
            }
            else
            {
                await LoginInternalAsync();
            }
        }

        private async Task RegisterAsync()
        {
            if (Password != ConfirmPassword)
            {
                StatusMessage = "Pasahitzak ez datoz bat.";
                return;
            }

            if (await _repository.UserExistsAsync(UserName).ConfigureAwait(false))
            {
                StatusMessage = "Erabiltzaile izena jada erabilita dago.";
                return;
            }

            await _repository.CreateUserAsync(UserName, Password).ConfigureAwait(false);

            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = "Erregistroa osatu da. Orain kontu berriarekin saioa has dezakezu.";
                UserName = "";
                Password = "";
                ConfirmPassword = "";
                IsRegisterMode = false;
            });
        }

        // Método público que usan los tests directamente
        public Task LoginAsync() => LoginInternalAsync();

        private async Task LoginInternalAsync()
        {
            var user = UserName?.Trim() ?? "";
            var pass = Password?.Trim() ?? "";

            // Entrada vacía
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                StatusMessage = "Erabiltzailea edo pasahitza hutsik";
                IsAuthenticated = false;
                return;
            }

            try
            {
                var valid = await _authService.ValidateUserAsync(user, pass).ConfigureAwait(false);
                if (!valid)
                {
                    StatusMessage = "Kredentzial okerrak";
                    IsAuthenticated = false;
                    return;
                }

                var isAdmin = await _authService.IsAdminAsync(user).ConfigureAwait(false);

                IsAuthenticated = true;
                IsAdmin = isAdmin;
                StatusMessage = "";

                // Lanzar el evento de forma segura: en WPF con Dispatcher, en tests directamente
                var app = Application.Current;
                if (app is not null)
                {
                    app.Dispatcher.Invoke(() =>
                    {
                        LoginSucceeded?.Invoke(user);
                    });
                }
                else
                {
                    // Entorno sin Application (tests unitarios)
                    LoginSucceeded?.Invoke(user);
                }
            }
            catch (InvalidOperationException)
            {
                StatusMessage = "Konexio errorea";
                IsAuthenticated = false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore ezezaguna: {ex.Message}";
                IsAuthenticated = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}