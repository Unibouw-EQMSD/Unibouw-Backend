using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Text.RegularExpressions;

namespace UnibouwAPI.Services
{
    public class SharePointQuoteStorage : ISharePointQuoteStorage
    {
        private readonly GraphServiceClient _graph;
        private readonly ILogger<SharePointQuoteStorage> _logger;

        public SharePointQuoteStorage(IConfiguration config, ILogger<SharePointQuoteStorage> logger)
        {
            _logger = logger;

            var tenantId = config["GraphEmail:TenantId"];
            var clientId = config["GraphEmail:ClientId"];
            var clientSecret = config["GraphEmail:ClientSecret"];

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _graph = new GraphServiceClient(credential);
        }

        public async Task EnsureProjectFolderAsync(string sharePointUrl, string projectFolderName, CancellationToken ct = default)
        {
            try
            {
                Console.WriteLine(">>> SharePointQuoteStorage.EnsureProjectFolderAsync CALLED");
                var (driveId, _) = await ResolveDriveAsync(sharePointUrl, ct);

                var loc = ParseSharePointUrl(sharePointUrl);
                var rootPath = string.IsNullOrWhiteSpace(loc.BaseFolderPath)
                    ? Sanitize(projectFolderName)
                    : $"{Sanitize(loc.BaseFolderPath)}/{Sanitize(projectFolderName)}";

                await EnsureFolderPathExistsAsync(driveId, rootPath, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine(">>> EnsureProjectFolderAsync GRAPH ERROR: " + ex.ToString());
                throw;
            }
        }

        public async Task UploadQuoteAsync(
      string sharePointUrl,
      string projectFolderName,
      string rfqNumber,
      string workItemName,
      string subcontractorName,
      string fileName,
      byte[] fileBytes,
      DateTime submittedOnUtc,
      CancellationToken ct = default)
        {
            Console.WriteLine(">>> SharePointQuoteStorage.UploadQuoteAsync CALLED");
            var (driveId, _) = await ResolveDriveAsync(sharePointUrl, ct);

            // This builds the nested folder path
            var folderPath =
                $"{Sanitize(projectFolderName)}/" +
                $"{Sanitize(rfqNumber)}/" +
                $"{Sanitize(workItemName)}/" +
                $"{Sanitize(subcontractorName)}";

            var targetFolder = await EnsureFolderPathExistsAsync(driveId, folderPath, ct);

            var ext = Path.GetExtension(fileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var stamp = submittedOnUtc.ToString("yyyyMMdd_HHmmss");
            var finalName = $"{Sanitize(baseName)}_{stamp}{ext}";

            using var ms = new MemoryStream(fileBytes);

            await _graph.Drives[driveId]
                .Items[targetFolder.Id]
                .ItemWithPath(finalName)
                .Content
                .PutAsync(ms, cancellationToken: ct);
        }
        private record SharePointLocation(string SiteHost, string SitePath, string LibraryName, string? BaseFolderPath);
        private async Task<(string DriveId, string DriveName)> ResolveDriveAsync(string sharePointUrl, CancellationToken ct)
        {
            var loc = ParseSharePointUrl(sharePointUrl);

            // v5 signature: GetAsync(Action<requestConfig>?, CancellationToken)
            var site = await _graph.Sites[$"{loc.SiteHost}:{loc.SitePath}"]
                .GetAsync(requestConfiguration: null, cancellationToken: ct);

            if (site?.Id == null)
                throw new Exception("Unable to resolve SharePoint site from URL.");

            var drives = await _graph.Sites[site.Id].Drives
                .GetAsync(requestConfiguration: null, cancellationToken: ct);

            var drive = drives?.Value?.FirstOrDefault(d =>
                  string.Equals(d.DriveType, "personal", StringComparison.OrdinalIgnoreCase))
              ?? drives?.Value?.FirstOrDefault();

            if (drive?.Id == null)
                throw new Exception($"Unable to resolve document library '{loc.LibraryName}' in SharePoint site.");

            return (drive.Id, drive.Name ?? loc.LibraryName);
        }

        private static SharePointLocation ParseSharePointUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new Exception("SharePoint URL is empty.");

            var uri = new Uri(url);
            var host = uri.Host;

            if (uri.AbsolutePath.Equals("/my", StringComparison.OrdinalIgnoreCase))
            {
                var query = uri.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");

                if (!query.TryGetValue("id", out var idEncoded) || string.IsNullOrWhiteSpace(idEncoded))
                    throw new Exception("Invalid OneDrive URL: missing 'id' parameter.");

                var decodedPath = Uri.UnescapeDataString(idEncoded);

                // normalize: /Documents/Documents/... -> /Documents/...
                decodedPath = decodedPath.Replace("/Documents/Documents/", "/Documents/");

                var parts = decodedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
                // expected: personal, user, Documents, (optional folders...)
                if (parts.Count < 3 || !parts[0].Equals("personal", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Invalid OneDrive 'id' path format.");

                var userSegment = parts[1];
                var library = parts[2]; // Documents
                var sitePath = $"/personal/{userSegment}";

                // ✅ everything after library is the base folder path, e.g. "QMSProjects" or "QMSProjects/SubFolder"
                string? baseFolder = null;
                if (parts.Count > 3)
                    baseFolder = string.Join("/", parts.Skip(3));

                return new SharePointLocation(host, sitePath, library, baseFolder);
            }

            // normal SharePoint links
            var absPath = Uri.UnescapeDataString(uri.AbsolutePath);
            var segs = absPath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

            var idx = segs.FindIndex(p =>
                p.Equals("sites", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("teams", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("personal", StringComparison.OrdinalIgnoreCase));

            if (idx < 0 || segs.Count < idx + 2)
                throw new Exception("Invalid SharePoint URL format. Expected /sites|teams|personal/{name}/...");

            var siteCollection = segs[idx];
            var siteName = segs[idx + 1];
            var sitePath2 = $"/{siteCollection}/{siteName}";

            var libraryName = segs.Count >= idx + 3 ? segs[idx + 2] : "Shared Documents";

            // base folder = any remaining segments after library
            string? baseFolder2 = null;
            if (segs.Count > idx + 3)
                baseFolder2 = string.Join("/", segs.Skip(idx + 3));

            return new SharePointLocation(host, sitePath2, libraryName, baseFolder2);
        }

        private async Task<DriveItem> EnsureFolderPathExistsAsync(string driveId, string folderPath, CancellationToken ct)
        {
            var segments = folderPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

            var parentId = "root";
            DriveItem? current = null;

            foreach (var segRaw in segments)
            {
                var seg = Sanitize(segRaw);

                // Get children
                var children = await _graph.Drives[driveId].Items[parentId].Children
                    .GetAsync(requestConfiguration: null, cancellationToken: ct);

                current = children?.Value?.FirstOrDefault(x =>
                    x.Folder != null && string.Equals(x.Name, seg, StringComparison.OrdinalIgnoreCase));

                if (current == null)
                {
                    try
                    {
                        current = await _graph.Drives[driveId].Items[parentId].Children.PostAsync(
                            new DriveItem
                            {
                                Name = seg,
                                Folder = new Folder(),
                                AdditionalData = new Dictionary<string, object>
                                {
                                    ["@microsoft.graph.conflictBehavior"] = "fail"
                                }
                            },
                            requestConfiguration: null,
                            cancellationToken: ct
                        );
                    }
                    catch
                    {
                        // If it failed (often 409), fetch again and use existing
                        var retryChildren = await _graph.Drives[driveId].Items[parentId].Children
                            .GetAsync(requestConfiguration: null, cancellationToken: ct);

                        current = retryChildren?.Value?.FirstOrDefault(x =>
                            x.Folder != null && string.Equals(x.Name, seg, StringComparison.OrdinalIgnoreCase));

                        if (current == null)
                            throw;
                    }
                }

                parentId = current!.Id!;
            }

            return current!;
        }

        private static string Sanitize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "NA";

            var cleaned = Regex.Replace(input, @"[~""#%&*:<>?/\\{|}]+", "_").Trim();
            cleaned = cleaned.TrimEnd('.', ' ');

            return string.IsNullOrWhiteSpace(cleaned) ? "NA" : cleaned;
        }
    }
}