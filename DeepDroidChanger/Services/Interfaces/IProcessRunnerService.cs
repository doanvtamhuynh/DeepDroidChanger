using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IProcessRunnerService
    {
        Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken);

        Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            string standardInput,
            CancellationToken cancellationToken);
    }
}
