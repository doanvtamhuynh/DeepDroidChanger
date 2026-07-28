using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs
{
    [TestClass]
    public sealed class LoginViewModelTests
    {
        [TestMethod]
        public void SignInCommand_MissingFields_SetsLocalizedError()
        {
            var viewModel = CreateViewModel("vi");

            viewModel.SignInCommand.Execute(null);

            Assert.AreEqual(Expected("vi", "Login_ErrorMissingFields"), viewModel.ErrorMessage);
        }

        [TestMethod]
        public async Task SignInCommand_ConfigurationFailure_SetsServiceUnavailableError()
        {
            var authService = new FakeAccountAuthenticationService
            {
                Status = AccountAuthenticationStatus.ConfigurationError
            };
            var sessionService = new FakeDeviceSessionService();
            var viewModel = CreateViewModel(
                "en",
                new FakeAccountStoreService(),
                authService,
                sessionService);
            viewModel.Username = "user";
            viewModel.Password = "password";

            await viewModel.SignInCommand.ExecuteAsync(null);

            Assert.AreEqual(Expected("en", "Login_ErrorServiceUnavailable"), viewModel.ErrorMessage);
        }

        [TestMethod]
        public async Task SignInCommand_ServiceFailure_SetsServiceUnavailableError()
        {
            var authService = new FakeAccountAuthenticationService
            {
                Status = AccountAuthenticationStatus.ServiceUnavailable
            };
            var sessionService = new FakeDeviceSessionService();
            var viewModel = CreateViewModel(
                "en",
                new FakeAccountStoreService(),
                authService,
                sessionService);
            viewModel.Username = "user";
            viewModel.Password = "password";

            await viewModel.SignInCommand.ExecuteAsync(null);

            Assert.AreEqual(Expected("en", "Login_ErrorServiceUnavailable"), viewModel.ErrorMessage);
        }

        [TestMethod]
        public async Task SignInCommand_AuthenticationSuccess_SetsSessionAndCloses()
        {
            var accountStore = new FakeAccountStoreService();
            var authService = new FakeAccountAuthenticationService();
            var sessionService = new FakeDeviceSessionService();
            var viewModel = CreateViewModel("en", accountStore, authService, sessionService);
            var closeResult = false;
            viewModel.Username = "user@example.com";
            viewModel.Password = "secret-password";
            viewModel.RememberAccount = true;
            viewModel.CloseRequested += (_, result) => closeResult = result;

            await viewModel.SignInCommand.ExecuteAsync(null);

            Assert.IsTrue(closeResult);
            Assert.AreSame(authService.Session, sessionService.CurrentSession);
            Assert.AreEqual("user@example.com", accountStore.SavedRequest?.Username);
            Assert.AreEqual("secret-password", accountStore.SavedRequest?.Password);
        }

        [TestMethod]
        public async Task SignInCommand_AuthenticationFailure_SetsErrorAndDoesNotClose()
        {
            var authService = new FakeAccountAuthenticationService
            {
                Status = AccountAuthenticationStatus.AuthenticationFailed
            };
            var sessionService = new FakeDeviceSessionService();
            var viewModel = CreateViewModel(
                "en",
                new FakeAccountStoreService(),
                authService,
                sessionService);
            var closeRequested = false;
            viewModel.Username = "user@example.com";
            viewModel.Password = "wrong-password";
            viewModel.CloseRequested += (_, _) => closeRequested = true;

            await viewModel.SignInCommand.ExecuteAsync(null);

            Assert.IsFalse(closeRequested);
            Assert.IsTrue(sessionService.WasCleared);
            Assert.AreEqual(Expected("en", "Login_ErrorAuthenticationFailed"), viewModel.ErrorMessage);
        }

        [TestMethod]
        public async Task SignInCommand_Canceled_ClearsAnyExistingSession()
        {
            var authService = new FakeAccountAuthenticationService
            {
                ExceptionToThrow = new OperationCanceledException()
            };
            var sessionService = new FakeDeviceSessionService();
            sessionService.SetSession(new AccountSession("https://example.com", "authorization", "stale-token"));
            var viewModel = CreateViewModel(
                "en",
                new FakeAccountStoreService(),
                authService,
                sessionService);
            viewModel.Username = "user@example.com";
            viewModel.Password = "password";

            await viewModel.SignInCommand.ExecuteAsync(null);

            Assert.IsTrue(sessionService.WasCleared);
            Assert.IsNull(sessionService.CurrentSession);
            Assert.AreEqual(Expected("en", "Login_ErrorCanceled"), viewModel.ErrorMessage);
        }

        private static LoginViewModel CreateViewModel(string language)
        {
            return CreateViewModel(
                language,
                new FakeAccountStoreService(),
                new FakeAccountAuthenticationService(),
                new FakeDeviceSessionService());
        }

        private static LoginViewModel CreateViewModel(
            string language,
            IAccountStoreService accountStoreService,
            IAccountAuthenticationService authService,
            IDeviceSessionService sessionService)
        {
            ILocalizationService localization = Substitute.For<ILocalizationService>();
            localization.GetString(Arg.Any<string>())
                .Returns(callInfo => Expected(language, callInfo.Arg<string>()));
            return new LoginViewModel(
                accountStoreService,
                authService,
                sessionService,
                localization,
                NullLogger<LoginViewModel>.Instance);
        }

        private static string Expected(string language, string resourceKey)
        {
            return $"{language}:{resourceKey}";
        }
    }
}
