
namespace ProductAuction.API.Services
{
    public interface IImageUploadService
    {
        Task<string> UploadImageAsync(IFormFile image);

        Task<bool> DeleteImageAsync(string imageUrl);
    }
}
