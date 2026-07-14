namespace DeepDroidChanger.Services
{
    public interface ILoginDialogService
    {
        Task<bool> ShowLoginAsync(CancellationToken cancellationToken);
    }
}
