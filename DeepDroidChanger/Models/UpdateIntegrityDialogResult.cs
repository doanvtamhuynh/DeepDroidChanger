namespace DeepDroidChanger.Models
{
    public sealed class UpdateIntegrityDialogResult
    {
        public bool UpdateIntegrityFromServer { get; }
        public bool UpdateIntegrityEnabled { get; }
        public bool UpdateKeyboxEnabled { get; }
        public string UpdateIntegrityFile { get; }
        public string UpdateKeyboxFile { get; }

        public UpdateIntegrityDialogResult(
            bool updateIntegrityFromServer,
            bool updateIntegrityEnabled,
            bool updateKeyboxEnabled,
            string updateIntegrityFile,
            string updateKeyboxFile)
        {
            UpdateIntegrityFromServer = updateIntegrityFromServer;
            UpdateIntegrityEnabled = updateIntegrityEnabled;
            UpdateKeyboxEnabled = updateKeyboxEnabled;
            UpdateIntegrityFile = updateIntegrityFile;
            UpdateKeyboxFile = updateKeyboxFile;
        }
    }
}
