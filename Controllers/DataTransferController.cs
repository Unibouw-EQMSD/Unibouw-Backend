using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class DataTransferController : ControllerBase
{
    private readonly DataTransferService _transferService;

    public DataTransferController(DataTransferService transferService)
    {
        _transferService = transferService;
    }

    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer()
    {
        try
        {
            var result = await _transferService.TransferDataAsync();

            if (result.Inserted.Count == 0 && result.Updated.Count == 0)
            {
                return Ok("No updates performed, data is already in sync.");
            }

            return Ok(new
            {
                Message = "Data transfer complete.",
                InsertedCount = result.Inserted.Count,
                UpdatedCount = result.Updated.Count,
                SkippedCount = result.Skipped.Count,
                InsertedIds = result.Inserted,
                UpdatedIds = result.Updated,
                SkippedIds = result.Skipped
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

}

