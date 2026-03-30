using System.Security.Cryptography;

namespace UnibouwAPI.Helpers
{
    public static class DocumentStorage
    {
        public static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static async Task<string> SaveToDiskAsync(string baseFolder, Guid projectId, Guid documentId, string originalFileName, byte[] bytes)
        {
            Directory.CreateDirectory(baseFolder);
            var safeName = Path.GetFileName(originalFileName);
            var projectFolder = Path.Combine(baseFolder, projectId.ToString("N"));
            Directory.CreateDirectory(projectFolder);

            var ext = Path.GetExtension(safeName);
            var fileName = $"{documentId:N}{ext}";
            var fullPath = Path.Combine(projectFolder, fileName);

            await File.WriteAllBytesAsync(fullPath, bytes);
            return fullPath;
        }
    }
}