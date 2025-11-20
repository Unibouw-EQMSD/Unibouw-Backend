using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories;
using UnibouwAPI.Repositories.Interfaces;
using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using OfficeOpenXml;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;


namespace UnibouwAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RfqResponseController : ControllerBase
    {
        private readonly IRfqResponseRepository _responseRepo;
        private readonly string _connectionString;
        private readonly ILogger<RfqResponseController> _logger;



        public RfqResponseController(IRfqResponseRepository responseRepo, IConfiguration configuration, ILogger<RfqResponseController> logger)
        {
            _responseRepo = responseRepo;
            _connectionString = configuration.GetConnectionString("UnibouwDbConnection")
                               ?? throw new InvalidOperationException("Connection string missing");
            _logger = logger;

        }

        [HttpGet("GetProjectSummary")]
        public async Task<IActionResult> GetProjectSummary(Guid rfqId)
        {
            try
            {
                var result = await _responseRepo.GetProjectSummaryAsync(rfqId);
                if (result == null)
                    return NotFound("No data found for the given RFQ.");

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in GetProjectSummary: {ex}");
                return StatusCode(500, new { message = "An unexpected error occurred. Try again later." });
            }
        }

        [HttpGet]
        [Route("")]
        [Route("respond")]
        public async Task<IActionResult> RespondToRfq(
     [FromQuery(Name = "rfqId")] Guid rfqId,
     [FromQuery(Name = "subIds")] Guid subId,
     [FromQuery(Name = "workItemId")] Guid workItemId,
     [FromQuery] string status)
        {
            bool success = await _responseRepo.SaveResponseAsync(rfqId, subId, workItemId, status);


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


        // ✅ POST endpoint for file/form submissions
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitResponse(
     [FromForm] Guid rfqId,
     [FromForm] Guid subcontractorId,
     [FromForm] Guid workItemId,
     [FromForm] string status)
        {
            try
            {
                bool success = await _responseRepo.SaveResponseAsync(rfqId, subcontractorId, workItemId, status);

                if (success)
                    return Ok(new { success = true, message = "Response saved successfully." });

                return BadRequest(new { success = false, message = "Failed to save response." });
            }
            catch (Exception ex)
            {
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
        public async Task<IActionResult> UploadQuote(
     [FromQuery] Guid rfqId,
     [FromQuery] Guid subcontractorId,
     IFormFile file)
        {
            if (rfqId == Guid.Empty || subcontractorId == Guid.Empty)
                return BadRequest("Invalid RFQ ID or Subcontractor ID.");

            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            try
            {
                // --------------------------
                // 1️⃣ Read file bytes and get extension
                // --------------------------
                byte[] fileBytes;
                using (var ms = new MemoryStream())
                {
                    await file.CopyToAsync(ms);
                    fileBytes = ms.ToArray();
                }
                string extension = Path.GetExtension(file.FileName).ToLower();

                // --------------------------
                // 2️⃣ Extract quote amount from bytes
                // --------------------------
                string extractedAmount = ExtractQuoteAmount(fileBytes, extension);

                // --------------------------
                // 3️⃣ Save file to DB via repository
                // --------------------------
                var success = await _responseRepo.UploadQuoteAsync(rfqId, subcontractorId, file);

                if (!success)
                    return BadRequest("Upload failed");

                // --------------------------
                // 4️⃣ Return response with amount
                // --------------------------
                return Ok(new
                {
                    message = "Quote uploaded successfully",
                    quoteAmount = extractedAmount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"UploadQuote Error: {ex}");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        private string ExtractQuoteAmount(byte[] fileBytes, string extension)
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

            // =====================================================================
            // 1️⃣ PDF EXTRACTION (iText7)
            // =====================================================================
            if (extension == ".pdf")
            {
                using var ms = new MemoryStream(fileBytes);
                using var reader = new PdfReader(ms);
                using var pdf = new PdfDocument(reader);

                StringBuilder sb = new StringBuilder();

                for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
                    sb.AppendLine(PdfTextExtractor.GetTextFromPage(pdf.GetPage(i)));

                // Get the *largest* euro amount (usually quote)
                var matches = regex.Matches(sb.ToString());

                if (matches.Count == 0)
                    return "Amount Not Found";

                return matches
                    .Select(m => m.Value)
                    .OrderByDescending(v => ParseEuro(v))
                    .First();
            }

            // =====================================================================
            // 2️⃣ EXCEL EXTRACTION (EPPlus)
            // =====================================================================
            if (extension == ".xlsx")
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                using var ms = new MemoryStream(fileBytes);
                using var package = new ExcelPackage(ms);

                var sheet = package.Workbook.Worksheets[0];

                int rows = sheet.Dimension.Rows;
                int cols = sheet.Dimension.Columns;

                int quoteAmountColumn = -1;

                // -----------------------------
                // FIND CORRECT QUOTE AMOUNT COLUMN
                // -----------------------------
                for (int col = 1; col <= cols; col++)
                {
                    string header = Normalize(sheet.Cells[1, col].Text);

                    if (header == "quote amount" ||
                        header.Contains("quote") && header.Contains("amount"))
                    {
                        quoteAmountColumn = col;
                        break;
                    }
                }

                if (quoteAmountColumn == -1)
                    return "Quote Amount Column Not Found";

                // -----------------------------
                // READ values in this column
                // -----------------------------
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

                // Return the biggest EUR value (quote amount)
                return euroValues
                    .OrderByDescending(v => ParseEuro(v))
                    .First();
            }

            return "Amount Not Found";
        }


   
        private decimal ParseEuro(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;

            string cleaned = value.Replace("€", "")
                                  .Replace(",", ".")
                                  .Trim();

            decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result);

            return result;
        }





        [HttpGet("GetQuoteAmount")]
        public async Task<IActionResult> GetQuoteAmount(Guid rfqId, Guid subcontractorId)
        {
            var quote = await _responseRepo.GetQuoteAsync(rfqId, subcontractorId);

            if (quote == null)
                return Ok(new { quoteAmount = "-" }); // no file uploaded

            // Retrieve file extension (you might need to store it in DB when uploading)
            string extension = ".pdf"; // <-- or quote.Value.FileName extension

            // Extract amount from saved PDF/Excel bytes
            string amount = ExtractQuoteAmount(quote.Value.FileBytes, extension);

            return Ok(new { quoteAmount = amount });
        }

        [HttpGet("DownloadQuote")]
        public async Task<IActionResult> DownloadQuote(
     [FromQuery] Guid rfqId,
     [FromQuery] Guid subcontractorId)
        {
            if (rfqId == Guid.Empty || subcontractorId == Guid.Empty)
                return BadRequest("Invalid RFQ ID or Subcontractor ID.");

            var fileData = await _responseRepo.GetQuoteAsync(rfqId, subcontractorId);

            if (fileData == null)
                return NotFound("No quote uploaded for this RFQ.");

            // Deconstruct tuple
            var (bytes, fileName) = fileData.Value;

            return File(bytes, "application/octet-stream", fileName);
        }


        [HttpGet("responses/project/{projectId}")]
        public async Task<IActionResult> GetResponsesByProject(Guid projectId)
        {
            var result = await _responseRepo.GetRfqResponsesByProjectAsync(projectId) as IEnumerable<object>;
            if (result == null || !result.Any())
                return NotFound("No responses found for this project.");

            return Ok(result);
        }

        [HttpGet("responses/project/{projectId}/subcontractors")]
        public async Task<IActionResult> GetResponsesByProjectSubcontractors(Guid projectId)
        {
            var result = await _responseRepo.GetRfqResponsesByProjectSubcontractorAsync(projectId);

            if (result == null)
                return NotFound("No subcontractor responses found for this project.");

            return Ok(result);
        }

        [HttpPost("mark-viewed")]
        public async Task<IActionResult> MarkViewed(
    [FromQuery] Guid rfqId,
    [FromQuery] Guid subcontractorId,
    [FromQuery] Guid workItemId)
        {
            if (rfqId == Guid.Empty || subcontractorId == Guid.Empty || workItemId == Guid.Empty)
                return BadRequest("Invalid RFQ/Subcontractor/WorkItem.");

            try
            {
                var success = await _responseRepo.MarkRfqViewedAsync(rfqId, subcontractorId, workItemId);

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


    }
}
