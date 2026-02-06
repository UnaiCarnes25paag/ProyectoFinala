using System.Windows;
using System.Windows.Controls;
using Casino.ViewModels;

namespace Casino.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;

        public LoginWindow()
        {
            InitializeComponent();

            _viewModel = new LoginViewModel();
            _viewModel.LoginSucceeded += OnLoginSucceeded;
            DataContext = _viewModel;
        }

        private void OnLoginSucceeded(string userName)
        {
            // Crear el viewmodel del poker usando el nombre del usuario logueado
            var pokerVm = new PokerViewModel(userName, _viewModel.Password);

            var main = new MainWindow
            {
                DataContext = pokerVm
            };
            main.Show();
            Close();
        }

        private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm && sender is PasswordBox pb)
            {
                vm.Password = pb.Password;
            }
        }

        private void ConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel vm && sender is PasswordBox pb)
            {
                vm.ConfirmPassword = pb.Password;
            }
        }

        private async void SubmitButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SubmitCommand.CanExecute(null))
            {
                await System.Threading.Tasks.Task.Run(() => _viewModel.SubmitCommand.Execute(null));
            }
        }

        private void ToggleModeButton_OnClick(object sender, RoutedEventArgs e)
        {
            _viewModel.ToggleModeCommand.Execute(null);
            ToggleModeButton.Content = _viewModel.IsRegisterMode ? "Ya tengo cuenta" : "Crear cuenta";
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}