using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/projects/{projectId:guid}/documents")]
    public class ProjectDocumentsController : ControllerBase
    {
        private readonly IProjectDocuments _docs;

        public ProjectDocumentsController(IProjectDocuments docs)
        {
            _docs = docs;
        }




        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetProjectDocs(Guid projectId)
        {
            var items = await _docs.GetProjectDocumentsAsync(projectId);
            return Ok(new { data = items });
        }

        [HttpDelete("{projectDocumentId:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteProjectDoc(Guid projectId, Guid projectDocumentId)
        {
            // projectId present in route for clarity; actual delete uses documentId
            var deletedBy = User?.Identity?.Name ?? "System";
            await _docs.DeleteProjectDocumentAsync(projectDocumentId, deletedBy);
            return Ok(new { message = "Project document deleted successfully." });
        }

        [HttpGet("{projectDocumentId:guid}/download")]
        [Authorize]
        public async Task<IActionResult> DownloadProjectDoc(Guid projectDocumentId, [FromServices] IProjectDocuments docs)
        {
            var result = await docs.DownloadProjectDocumentAsync(projectDocumentId);
            if (result == null)
                return NotFound();

            var (fileBytes, fileName, contentType) = result.Value;
            return File(fileBytes, contentType ?? "application/octet-stream", fileName ?? "document.pdf");
        }
    }
}