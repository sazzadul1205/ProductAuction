using Microsoft.AspNetCore.Hosting;

namespace ProductAuction.API.Services
{
    public class ImageUploadService : IImageUploadService
    {
        private readonly IWebHostEnvironment _environment;

        public ImageUploadService(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<string> UploadImageAsync(IFormFile image)
        {
            if (image == null || image.Length == 0)
            {
                throw new ArgumentException("Please select an image.");
            }

            if (image.Length > 5 * 1024 * 1024)
            {
                throw new ArgumentException("Image size cannot exceed 5 MB.");
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                throw new ArgumentException("Only JPG, JPEG, PNG, and WEBP images are allowed.");
            }

            var webRoot = _environment.WebRootPath;

            var uploadFolder = Path.Combine(webRoot, "uploads");

            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await image.CopyToAsync(stream);
            }

            return $"/uploads/{fileName}";
        }

        // Deletes an image from disk based on its relative URL.
        public Task<bool> DeleteImageAsync(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
            {
                return Task.FromResult(false);
            }

            var fileName = Path.GetFileName(imageUrl);

            if (string.IsNullOrEmpty(fileName) ||
                fileName != imageUrl.Replace("\\", "/").Split('/').Last())
            {
                return Task.FromResult(false);
            }

            var webRoot = _environment.WebRootPath;
            var filePath = Path.Combine(webRoot, "uploads", fileName);
            if (!System.IO.File.Exists(filePath))
            {
                return Task.FromResult(false);
            }

            System.IO.File.Delete(filePath);
            return Task.FromResult(true);
        }









        // Deletes an image from disk based on its relative URL.
        //public bool DeleteImage(string imageUrl)
        //{
        //    if (string.IsNullOrEmpty(imageUrl))
        //    {
        //        return false;
        //    }

        //    var fileName = Path.GetFileName(imageUrl);

        //    if (string.IsNullOrEmpty(fileName))
        //    {
        //        return false;
        //    }

        //    var webRoot = _environment.WebRootPath;
        //    var filePath = Path.Combine(webRoot, "uploads", fileName);

        //    if (!System.IO.File.Exists(filePath))
        //    {
        //        return false;
        //    }

        //    System.IO.File.Delete(filePath);
        //    return true;
        //}
    }
}