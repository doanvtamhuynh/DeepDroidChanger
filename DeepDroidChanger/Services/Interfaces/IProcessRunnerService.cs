using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IProcessRunnerService
    {
        Task<CommandResult> RunStreamingAsync(string fileName, string arguments,
            Action<ProcessUpdate> output, CancellationToken cancellationToken);

        Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken);

        Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            string standardInput,
            CancellationToken cancellationToken);
    }
}
