namespace DeepDroidChanger.Models;

public sealed class PreparedIntegrityData
{
    public PreparedIntegrityData(
        IReadOnlyList<Integrity> integrityCandidates,
        string? keyboxXml)
    {
        ArgumentNullException.ThrowIfNull(integrityCandidates);
        IntegrityCandidates = integrityCandidates.ToArray();
        KeyboxXml = keyboxXml;
    }

    public IReadOnlyList<Integrity> IntegrityCandidates { get; }

    public string? KeyboxXml { get; }
}
