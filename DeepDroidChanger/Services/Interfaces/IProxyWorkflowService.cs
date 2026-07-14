using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IProxyWorkflowService
{
    Task<ProxyWorkflowResult> ApplyAsync(
        string serial,
        FakeProxyDialogResult configuration,
        CancellationToken cancellationToken);
}
