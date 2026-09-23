namespace DeepDroidChanger.Models
{
    public enum UpdateIntegrityDialogAction
    {
        Update,
        ClearIntegrity,
        ClearKeybox
    }

    public sealed class UpdateIntegrityDialogResult
    {
        public bool UpdateIntegrityFromServer { get; }
        public bool UpdateIntegrityEnabled { get; }
        public bool UpdateKeyboxEnabled { get; }
        public bool FakeDroidGuardSdkEnabled { get; }
        public string UpdateIntegrityFile { get; }
        public UpdateIntegrityDialogAction Action { get; }
        public string UpdateKeyboxFile { get; }
        public UpdateIntegrityDialogResult(
            bool updateIntegrityFromServer,
            bool updateIntegrityEnabled,
            bool updateKeyboxEnabled,
            string updateIntegrityFile,
            string updateKeyboxFile,
            bool fakeDroidGuardSdkEnabled = false,
            UpdateIntegrityDialogAction action = UpdateIntegrityDialogAction.Update)
        {
            Action = action;
            UpdateIntegrityFromServer = updateIntegrityFromServer;
            UpdateIntegrityEnabled = updateIntegrityEnabled;
            UpdateKeyboxEnabled = updateKeyboxEnabled;
            FakeDroidGuardSdkEnabled = updateIntegrityEnabled && fakeDroidGuardSdkEnabled;
            UpdateIntegrityFile = updateIntegrityFile;
            UpdateKeyboxFile = updateKeyboxFile;
        }
    }
}
