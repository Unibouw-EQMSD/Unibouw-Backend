using System;
using System.Threading;
using System.Threading.Tasks;

namespace UnibouwAPI.Services
{
    public interface ISharePointQuoteStorage
    {
        Task EnsureProjectFolderAsync(
            string sharePointUrl,
            string projectFolderName,
            CancellationToken ct = default);

        Task UploadQuoteAsync(
            string sharePointUrl,
            string projectFolderName,
            string rfqNumber,
            string workItemName,
            string subcontractorName,
            string fileName,
            byte[] fileBytes,
            DateTime submittedOnUtc,
            CancellationToken ct = default);
    }
}