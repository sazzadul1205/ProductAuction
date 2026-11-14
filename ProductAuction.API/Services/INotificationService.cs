namespace ProductAuction.API.Services
{
    public interface INotificationService
    {
        Task CreateNotificationAsync(string userId, string title, string message, string? linkUrl = null);
    }
}
