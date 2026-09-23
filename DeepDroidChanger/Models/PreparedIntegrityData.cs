namespace DeepDroidChanger.Models;

public sealed class PreparedIntegrityData
{
    public PreparedIntegrityData(
        IReadOnlyList<Integrity> integrityCandidates,
        string? keyboxXml,
        bool fakeDroidGuardSdkEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(integrityCandidates);
        IntegrityCandidates = integrityCandidates.ToArray();
        KeyboxXml = keyboxXml;
        FakeDroidGuardSdkEnabled = fakeDroidGuardSdkEnabled;
    }

    public IReadOnlyList<Integrity> IntegrityCandidates { get; }

    public string? KeyboxXml { get; }

    public bool FakeDroidGuardSdkEnabled { get; }
}
