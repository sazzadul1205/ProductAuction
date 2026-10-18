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

        // Saves the uploaded image to disk and returns its relative URL.
        public async Task<string> UploadImageAsync(IFormFile image)
        {
            // Make sure a file was actually sent.
            if (image == null || image.Length == 0)
            {
                throw new ArgumentException("Please select an image.");
            }

            // Reject files larger than 5 MB.
            if (image.Length > 5 * 1024 * 1024)
            {
                throw new ArgumentException("Image size cannot exceed 5 MB.");
            }

            // List of extensions we accept.
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            // Get the file extension in lowercase.
            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();

            // Reject anything that is not an allowed image type.
            if (!allowedExtensions.Contains(extension))
            {
                throw new ArgumentException("Only JPG, JPEG, PNG, and WEBP images are allowed.");
            }

            // Build the path to the uploads folder.
            var uploadFolder = Path.Combine(
                _environment.WebRootPath ?? _environment.ContentRootPath,
                "wwwroot",
                "uploads");

            // Create the uploads folder if it does not exist yet.
            Directory.CreateDirectory(uploadFolder);

            // Give the file a unique name so nothing gets overwritten.
            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadFolder, fileName);

            // Copy the uploaded stream to disk.
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await image.CopyToAsync(stream);
            }

            // Return the relative URL to save in the database.
            return $"/uploads/{fileName}";
        }

        // Deletes an image from disk based on its relative URL.
        public Task<bool> DeleteImageAsync(string imageUrl)
        {
            // Nothing to delete if the URL is empty.
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return Task.FromResult(false);
            }

            // Extract just the file name from the URL.
            var fileName = Path.GetFileName(imageUrl);

            // Only allow URLs that actually point to a file in our uploads folder.
            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName != imageUrl.Replace("\\", "/").Split('/').Last())
            {
                return Task.FromResult(false);
            }

            // Build the full path to the file on disk.
            var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");

            var filePath = Path.Combine(webRoot, "uploads", fileName);

            // If the file is not there, nothing to delete.
            if (!System.IO.File.Exists(filePath))
            {
                return Task.FromResult(false);
            }

            // Delete the file.
            System.IO.File.Delete(filePath);

            // Report success.
            return Task.FromResult(true);
        }
    }
}