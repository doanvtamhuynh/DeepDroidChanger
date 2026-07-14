namespace DeepDroidChanger.Models
{
    public sealed class AccountSession
    {
        public AccountSession(string endpoint, string authenticationHeaderName, string idToken)
        {
            Endpoint = endpoint;
            AuthenticationHeaderName = authenticationHeaderName;
            IdToken = idToken;
        }

        public string Endpoint { get; }
        public string AuthenticationHeaderName { get; }
        public string IdToken { get; }
    }
}
