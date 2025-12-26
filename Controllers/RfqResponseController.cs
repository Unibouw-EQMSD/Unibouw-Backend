using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RfqResponseController : ControllerBase
    {
        private readonly IRfqResponse _repository;
        private readonly ILogger<RfqResponseController> _logger;

        public RfqResponseController(IRfqResponse repository, ILogger<RfqResponseController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        //---------RfqResponseDocuments
        [HttpGet("GetAllRfqResponseDocuments")]
        [Authorize]
        public async Task<IActionResult> GetAllRfqResponseDocuments()
        {
            try
            {
                var items = await _repository.GetAllRfqResponseDocuments();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No RFQ Response Documents found.",
                        data = Array.Empty<RfqResponseDocument>()
                    });
                }

                return Ok(new
                {
                    count = items.Count(),
                    data = items
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all RFQ Response Documents.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }


        [HttpGet("GetRfqResponseDocumentsById/{id}")]
        [Authorize]
        public async Task<IActionResult> GetRfqResponseDocumentsById(Guid id)
        {
            try
            {
                var item = await _repository.GetRfqResponseDocumentsById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No RFQ Response Document found for ID: {id}.",
                        data = (RfqResponseDocument?)null
                    });
                }

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching RFQ Response Document with ID {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }


        //--------RfqSubcontractorResponse
        [HttpGet("GetAllRfqSubcontractorResponse")]
        [Authorize]
        public async Task<IActionResult> GetAllRfqSubcontractorResponse()
        {
            try
            {
                var items = await _repository.GetAllRfqSubcontractorResponse();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No RFQ Subcontractor Responses found.",
                        data = Array.Empty<RfqSubcontractorResponse>()
                    });
                }

                return Ok(new
                {
                    count = items.Count(),
                    data = items
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all RFQ Subcontractor Responses.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("GetRfqSubcontractorResponseById/{id}")]
        [Authorize]
        public async Task<IActionResult> GetRfqSubcontractorResponseById(Guid id)
        {
            try
            {
                var item = await _repository.GetRfqSubcontractorResponseById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No RFQ Subcontractor Response found for ID: {id}.",
                        data = (RfqSubcontractorResponse?)null
                    });
                }

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching RFQ Subcontractor Response with ID {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }


        //-----------RfqSubcontractorWorkItemResponse
        [HttpGet("GetAllRfqSubcontractorWorkItemResponse")]
        [Authorize]
        public async Task<IActionResult> GetAllRfqSubcontractorWorkItemResponse()
        {
            try
            {
                var items = await _repository.GetAllRfqSubcontractorWorkItemResponse();

                if (items == null || !items.Any())
                {
                    return NotFound(new
                    {
                        message = "No RFQ Subcontractor Work Item Responses found.",
                        data = Array.Empty<RfqSubcontractorWorkItemResponse>()
                    });
                }

                return Ok(new
                {
                    count = items.Count(),
                    data = items
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all RFQ Subcontractor Work Item Responses.");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet("GetRfqSubcontractorWorkItemResponseById/{id}")]
        [Authorize]
        public async Task<IActionResult> GetRfqSubcontractorWorkItemResponseById(Guid id)
        {
            try
            {
                var item = await _repository.GetRfqSubcontractorWorkItemResponseById(id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = $"No RFQ Subcontractor Work Item Response found for ID: {id}.",
                        data = (RfqSubcontractorWorkItemResponse?)null
                    });
                }

                return Ok(new { data = item });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching RFQ Subcontractor Work Item Response with ID {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }


        //---------------------------------------------------------
        [HttpGet("GetProjectSummary")]
        public async Task<IActionResult> GetProjectSummary([FromQuery] Guid rfqId,[FromQuery] Guid subId,[FromQuery] List<Guid>? workItemIds)
        {
            try
            {
                var result = await _repository.GetProjectSummaryAsync(rfqId, subId, workItemIds);

                if (result == null)
                    return NotFound("No data found for the given RFQ.");

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in GetProjectSummary.");
                return StatusCode(500, new
                {
                    message = "An unexpected error occurred. Try again later."
                });
            }
        }


        [HttpGet]
        [Route("")]
        [Route("respond")]
        public async Task<IActionResult> RespondToRfq([FromQuery(Name = "rfqId")] Guid rfqId, [FromQuery(Name = "subIds")] Guid subId, [FromQuery(Name = "workItemId")] Guid workItemId, [FromQuery] string status)
        {
            try
            {
                bool success = await _repository.SaveResponseAsync(rfqId, subId, workItemId, status);

                if (success)
                {
                    string htmlContent = $@"
                    <html>
                        <body style='font-family:Arial;text-align:center;padding:40px'>
                            <h2>Thank you for your response!</h2>
                            <p>Your status has been recorded as: <strong>{status}</strong>.</p>
                            <p>You can now close this tab.</p>
                        </body>
                    </html>";

                    return Content(htmlContent, "text/html");
                }

                return BadRequest("Unable to save your response. Please try again later.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ An unexpected error occurred while processing your request.");
                return StatusCode(500, new
                {
                    message = "An unexpected error occurred while processing your request.",
                    details = ex.Message
                });
            }
        }

        // POST endpoint for file/form submissions
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitResponse([FromForm] Guid rfqId, [FromForm] Guid subcontractorId, [FromForm] Guid workItemId, [FromForm] string status)
        {
            try
            {
                bool success = await _repository.SaveResponseAsync(rfqId, subcontractorId, workItemId, status);

                if (success)
                    return Ok(new { success = true, message = "Response saved successfully." });

                return BadRequest(new { success = false, message = "Failed to save response." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving RFQ response");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error saving RFQ response",
                    error = ex.Message,
                    innerException = ex.InnerException?.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        [HttpPost("UploadQuote")]
        public async Task<IActionResult> UploadQuote([FromQuery] Guid rfqId,[FromQuery] Guid subcontractorId, [FromQuery] Guid workItemId, [FromForm] decimal totalAmount,[FromForm] string comment,IFormFile file)
        {
            if (rfqId == Guid.Empty || subcontractorId == Guid.Empty)
                return BadRequest("Invalid RFQ or Subcontractor ID.");

            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var success = await _repository.UploadQuoteAsync(
                rfqId,
                subcontractorId,
                workItemId,
                file,
                totalAmount,
                comment
            );

            if (!success)
                return BadRequest("Upload failed.");

            return Ok(new { message = "Quote uploaded successfully", totalAmount, comment });
        }

        private string ExtractQuoteAmount(byte[] fileBytes, string extension)
        {
            try
            {
                // ---------- Normalize helper ----------
                string Normalize(string s)
                {
                    if (s == null) return "";
                    return s.Replace("\u00A0", " ").Trim().ToLower();
                }

                // ---------- Euro Regex ----------
                string euroPattern = @"€?\s?\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})?\s?€?";
                var regex = new Regex(euroPattern, RegexOptions.IgnoreCase);

                // 1️ PDF EXTRACTION (iText7)
                if (extension == ".pdf")
                {
                    try
                    {
                        using var ms = new MemoryStream(fileBytes);
                        using var reader = new PdfReader(ms);
                        using var pdf = new PdfDocument(reader);

                        StringBuilder sb = new StringBuilder();

                        for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
                            sb.AppendLine(PdfTextExtractor.GetTextFromPage(pdf.GetPage(i)));

                        var matches = regex.Matches(sb.ToString());

                        if (matches.Count == 0)
                            return "Amount Not Found";

                        return matches
                            .Select(m => m.Value)
                            .OrderByDescending(v => ParseEuro(v))
                            .First();
                    }
                    catch
                    {
                        return "Error reading PDF";
                    }
                }

                // 2️ EXCEL EXTRACTION (EPPlus)
                if (extension == ".xlsx")
                {
                    try
                    {
                        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                        using var ms = new MemoryStream(fileBytes);
                        using var package = new ExcelPackage(ms);

                        var sheet = package.Workbook.Worksheets[0];

                        int rows = sheet.Dimension.Rows;
                        int cols = sheet.Dimension.Columns;

                        int quoteAmountColumn = -1;

                        // FIND CORRECT QUOTE AMOUNT COLUMN
                        for (int col = 1; col <= cols; col++)
                        {
                            string header = Normalize(sheet.Cells[1, col].Text);

                            if (header == "quote amount" ||
                                (header.Contains("quote") && header.Contains("amount")))
                            {
                                quoteAmountColumn = col;
                                break;
                            }
                        }

                        if (quoteAmountColumn == -1)
                            return "Quote Amount Column Not Found";

                        // READ values in this column
                        List<string> euroValues = new List<string>();

                        for (int row = 2; row <= rows; row++)
                        {
                            string val = sheet.Cells[row, quoteAmountColumn].Text.Trim();

                            if (string.IsNullOrWhiteSpace(val)) continue;

                            var match = regex.Match(val);
                            if (match.Success)
                                euroValues.Add(match.Value);
                        }

                        if (!euroValues.Any())
                            return "Amount Not Found";

                        return euroValues
                            .OrderByDescending(v => ParseEuro(v))
                            .First();
                    }
                    catch
                    {
                        return "Error reading Excel";
                    }
                }

                return "Amount Not Found";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting quote amount");

                return "Error processing file";
            }
        }

        private decimal ParseEuro(string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return 0;

                string cleaned = value.Replace("€", "")
                                      .Replace(",", ".")
                                      .Trim();

                decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result);

                return result;
            }
            catch
            {
                // If parsing fails for any reason, safely return 0.
                return 0;
            }
        }
        [HttpGet("GetQuoteAmount")]
        [Authorize]
        public async Task<IActionResult> GetQuoteAmount(Guid rfqId, Guid subcontractorId, Guid workItemId)
        {
            try
            {
                var totalAmount = await _repository.GetTotalQuoteAmountAsync(rfqId, subcontractorId, workItemId);

                if (totalAmount == null)
                    return Ok(new { quoteAmount = "-" }); // no quote submitted yet

                return Ok(new { quoteAmount = totalAmount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving total quote amount.");
                return StatusCode(500, new
                {
                    message = "An error occurred while retrieving the quote amount.",
                    details = ex.Message
                });
            }
        }

        [HttpGet("PreviousSubmissions")]
        public async Task<IActionResult> PreviousSubmissions([FromQuery] Guid rfqId, [FromQuery] Guid subcontractorId)
        {
            var submissions = await _repository.GetPreviousSubmissionsAsync(rfqId, subcontractorId);
            return Ok(submissions);
        }

        [HttpGet("DownloadQuote")]

        [Authorize]

        public async Task<IActionResult> DownloadQuote([FromQuery] Guid documentId)

        {

            if (documentId == Guid.Empty)

                return BadRequest("Invalid document ID.");

            var document = await _repository.GetRfqResponseDocumentsById(documentId);

            if (document == null)

                return NotFound("No quote found for this document.");

            // Return the PDF file to the client

            return File(document.FileData, "application/pdf", document.FileName ?? "Quote.pdf");

        }

        // Static helper method to extract and save the document locally

        static void ExtractDocument(string connectionString, Guid documentId)

        {

            using (SqlConnection conn = new SqlConnection(connectionString))

            {

                conn.Open();

                string sql = @"
                        SELECT RfqResponseDocumentID, RfqID, SubcontractorID, FileName, FileData, UploadedOn, IsDeleted, IsDeletedBy, DeletedOn
                        FROM RfqResponseDocument
                        RfqResponseDocumentID = @DocId";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@DocId", SqlDbType.UniqueIdentifier).Value = documentId;
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string fileName = reader["FileName"].ToString();
                            byte[] fileData = (byte[])reader["FileData"];
                            // Save the file locally
                            string savePath = Path.Combine(Environment.CurrentDirectory, fileName);
                            System.IO.File.WriteAllBytes(savePath, fileData);
                            Console.WriteLine($"Document saved successfully: {savePath}");
                        }
                        else
                        {
                            Console.WriteLine("Document not found.");
                        }
                    }
                }
            }
        }


        [HttpGet("responses/project/{projectId}")]
        [Authorize]
        public async Task<IActionResult> GetResponsesByProject(Guid projectId)
        {
            try
            {
                var result = await _repository.GetRfqResponsesByProjectAsync(projectId) as IEnumerable<object>;
                if (result == null || !result.Any())
                    return NotFound("No responses found for this project.");

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetResponsesByProject");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while fetching project responses.",
                    details = ex.Message
                });
            }
        }

        [HttpGet("responses/project/{projectId}/subcontractors")]
        [Authorize]
        public async Task<IActionResult> GetResponsesByProjectSubcontractors(Guid projectId)
        {
            try
            {
                var result = await _repository.GetRfqResponsesByProjectSubcontractorAsync(projectId);

                if (result == null)
                    return NotFound("No subcontractor responses found for this project.");

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ An error occurred while fetching project responses");

                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while fetching project responses.",
                    details = ex.Message
                });
            }
        }

        [HttpPost("mark-viewed")]
        public async Task<IActionResult> MarkViewed([FromQuery] Guid rfqId, [FromQuery] Guid subcontractorId, [FromQuery] Guid workItemId)
        {
            try
            {
                if (rfqId == Guid.Empty || subcontractorId == Guid.Empty || workItemId == Guid.Empty)
                    return BadRequest("Invalid RFQ/Subcontractor/WorkItem.");

                var success = await _repository.MarkRfqViewedAsync(rfqId, subcontractorId, workItemId);

                if (!success)
                    return BadRequest("Failed to update viewed status.");

                return Ok(new { message = "RFQ marked as viewed successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in MarkViewed: {ex}");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }


        [HttpDelete("DeleteQuoteFile")]
        public async Task<IActionResult> DeleteQuoteFile(Guid rfqId, Guid subcontractorId)
        {
            var result = await _repository.DeleteQuoteFile(rfqId, subcontractorId);
            return result ? Ok() : NotFound();
        }
    }
}
