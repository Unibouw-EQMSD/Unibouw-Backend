using Microsoft.AspNetCore.Mvc;
using UnibouwAPI.Helpers;
using UnibouwAPI.Repositories.Interfaces;
using UnibouwAPI.Services;

namespace UnibouwAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DwhTransferController : ControllerBase
    {

        private readonly DwhTransferService _transferService;
        private readonly IMsTeamsNotification _teamsService;
        private readonly ISubcontractors _subcontractorRepo;
        private readonly ILogger<DwhTransferController> _logger;

        public DwhTransferController(DwhTransferService transferService,IMsTeamsNotification teamsService, ISubcontractors subcontractorRepo, ILogger<DwhTransferController> logger)
        {
            _transferService = transferService;
            _teamsService = teamsService;
            _subcontractorRepo = subcontractorRepo;
            _logger = logger;
        }

        [HttpPost("dwh/sync-all")]
        public async Task<IActionResult> DwhSyncAll()
        {
            try
            {
                _logger.LogInformation("DWH Sync started at {Time}", DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow));
                var result = await _transferService.SyncAllAsync();

                // ✅ Success logger
                _logger.LogInformation(
                    "✅ DWH Sync Completed Successfully at {Time}.\n" +
                    "Categories: Inserted={CI}, Updated={CU}, Skipped={CS}\n" +
                    "WorkItems:  Inserted={WI}, Updated={WU}, Skipped={WS}\n" +
                    "Customers:  Inserted={CUI}, Updated={CUU}, Skipped={CUS}\n" +
                    "Projects:   Inserted={PI}, Updated={PU}, Skipped={PS}",
                    DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow),

                    // Categories
                    result.Categories.Inserted.Count,
                    result.Categories.Updated.Count,
                    result.Categories.Skipped.Count,

                    // WorkItems
                    result.WorkItems.Inserted.Count,
                    result.WorkItems.Updated.Count,
                    result.WorkItems.Skipped.Count,

                    // Customers
                    result.Customers.Inserted.Count,
                    result.Customers.Updated.Count,
                    result.Customers.Skipped.Count,

                    // Projects
                    result.Projects.Inserted.Count,
                    result.Projects.Updated.Count,
                    result.Projects.Skipped.Count
                );

                return Ok(new
                {
                    Message = "Full data transfer complete.",

                    Categories = new
                    {
                        Inserted = result.Categories.Inserted.Count,
                        Updated = result.Categories.Updated.Count,
                        Skipped = result.Categories.Skipped.Count
                    },

                    WorkItems = new
                    {
                        Inserted = result.WorkItems.Inserted.Count,
                        Updated = result.WorkItems.Updated.Count,
                        Skipped = result.WorkItems.Skipped.Count
                    },

                    Customers = new
                    {
                        Inserted = result.Customers.Inserted.Count,
                        Updated = result.Customers.Updated.Count,
                        Skipped = result.Customers.Skipped.Count
                    },

                    Projects = new
                    {
                        Inserted = result.Projects.Inserted.Count,
                        Updated = result.Projects.Updated.Count,
                        Skipped = result.Projects.Skipped.Count
                    }
                });

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Full DWH sync transfer failed.");
                return StatusCode(500, ex.Message);
            }
        }


        [HttpPost("CompareSubcontractorIDs1")]
        public async Task<IActionResult> CompareSubcontractorIDs1()
        {
            try
            {
                var result = await _transferService.GetMissingSubcontractorDetailsAsync1();
                var sentMessages = new List<string>();

                if (result != null && result.Any())
                {

                    foreach (var subcontractor in result)
                    {
                        // 1️⃣ Get subcontractor reminder data
                        var subcontractorData = await _subcontractorRepo.GetSubcontractorRemindersSent(subcontractor.SubcontractorID);
                        if (subcontractorData == null)
                            continue;
                        int reminderCount = subcontractor?.RemindersSent ?? 0;

                        // 2️⃣ Build reminder text
                        string reminderText = reminderCount switch
                        {
                            0 => "Reminder 1: Subcontractor Missing in DWH \n After First Sync",
                            1 => "Reminder 2: Subcontractor Missing in DWH After Second Sync",
                            2 => "Reminder 3: Final Warning Alert Subcontractor Missing in DWH After Third Sync",
                            3 => "Reminder 4: Subcontractor Still Missing in DWH",
                            _ => $"Reminder {reminderCount + 1}: Subcontractor Still Missing in DWH"
                        };

                        // Escalation message
                        string additionalAction = reminderCount switch
                        {
                            3 => "<b>Final Notice:</b> The subcontractor data has been removed from the DWH as no action was taken after multiple reminders.<br><br>",
                            2 => "<b>Warning:</b> If no action is taken, the subcontractor data will be removed in the next sync cycle.<br><br>",
                            _ => ""
                        };

                        // Action required only if data is NOT removed
                        string actionRequired = reminderCount >= 3
                            ? ""
                            : "<b>Action Required:</b> Please verify and ensure the subcontractor data is added to the DWH at the earliest.";
                                                
                        string message = $@"
<b>{reminderText}</b><br>
This is a follow-up regarding the subcontractor data missing from the DWH after today’s sync at 07:30 AM.<br><br>

<b>Subcontractor Details:</b><br>
• Subcontractor Name: {subcontractor.Name}<br>
• Email: {subcontractor.Email}<br>
• Country: {subcontractor.Country}<br>
• Last Sync Date: {subcontractor.Country}<br>
• Country: {subcontractor.Country}<br>
• Country: {subcontractor.Country}<br>
• Country: {subcontractor.Country}<br>
• Country: {subcontractor.Country}<br>

 <br>

{additionalAction}
{actionRequired}
";

                        // 3️⃣ Send Teams notification
                        await _teamsService.SendTeamsMessageAsync(message);

                        reminderCount = reminderCount + 1;

                        await _subcontractorRepo.UpdateSubcontractorRemindersSent(subcontractor.SubcontractorID, reminderCount);

                        // Delete only after 4th reminder sent
                        if (reminderCount >= 4)
                        {
                            await _subcontractorRepo.DeleteSubcontractor(subcontractor.SubcontractorID);
                        }

                        sentMessages.Add(message);
                        _logger.LogInformation(
                            "📨 Reminder {ReminderNumber} sent for Subcontractor Email {Email} at {Time}",
                            reminderCount + 1,
                            subcontractor.Email,
                            DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow)
                          );
                    }
                }

                return Ok(new
                {
                    Message = sentMessages.Any()
                    ? "Teams notifications sent successfully."
                    : "No reminders to send.",
                    Count = sentMessages.Count,
                    Messages = sentMessages
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to compare Subcontractor IDs.");
                return StatusCode(500, "An error occurred while comparing Subcontractor IDs.");
            }
        }


        [HttpPost("CompareSubcontractorIDs")]
        public async Task<IActionResult> CompareSubcontractorIDs()
        {
            try
            {
                var result = await _transferService.GetMissingSubcontractorDetailsAsync();
                var sentMessages = new List<string>();

                if (result == null || result.MissingSubcontractorsDetails == null || !result.MissingSubcontractorsDetails.Any())
                {
                    return Ok(new
                    {
                        Message = "No missing subcontractors found.",
                        Count = 0,
                        Messages = sentMessages
                    });
                }

                foreach (var subcontractor in result.MissingSubcontractorsDetails)
                {
                    // 1️⃣ Get subcontractor reminder data
                    var subcontractorData = await _subcontractorRepo.GetSubcontractorRemindersSent(subcontractor.SubcontractorID);
                    if (subcontractorData == null)
                        continue;

                    int reminderCount = subcontractorData.RemindersSent ?? 0;

                    // 2️⃣ WorkItems mapped list
                    string workItemsMapped = (subcontractor.WorkItemName != null && subcontractor.WorkItemName.Any())
                        ? string.Join(", ", subcontractor.WorkItemName)
                        : "N/A";

                    // 3️⃣ Get RFQ Details related to this subcontractor
                    var subcontractorRfqIds = result.QuoteDetails
                        .Where(q => q.SubcontractorID == subcontractor.SubcontractorID)
                        .Select(q => q.RfqID)
                        .Distinct()
                        .ToList();

                    // 4️⃣ Get RFQ details for those RFQ IDs
                    var subcontractorRfqs = result.RfqDetails
                        .Where(r => subcontractorRfqIds.Contains(r.RfqID))
                        .ToList();

                    // 5️⃣ Build RFQ Info Section
                    string rfqInfo = "";

                    if (subcontractorRfqs.Any())
                    {
                        rfqInfo = string.Join("\n\n", subcontractorRfqs.Select(rfq =>
                        {
                            var quote = result.QuoteDetails.FirstOrDefault(q =>
                                q.RfqID == rfq.RfqID &&
                                q.SubcontractorID == subcontractor.SubcontractorID);

                            string quoteAmount = quote?.TotalQuoteAmount != null
                                ? quote.TotalQuoteAmount.Value.ToString("C", new System.Globalization.CultureInfo("nl-NL"))
                                : "N/A";

                            string createdOnDate = subcontractor.CreatedOn.HasValue
                                ? subcontractor.CreatedOn.Value.ToString("dd-MM-yyyy")
                                : "N/A";


                            return $@"
• **Project Name:** {rfq.ProjectName}<br>
• **Project Code:** {rfq.ProjectCode} <br>
• **RFQ Number:** {rfq.RfqNumber}<br>
• **Quote Amount:** {quoteAmount}<br>
• **Submitted On:** {createdOnDate} <br>
• **WorkItem:** {workItemsMapped ?? "N/A"} <br>
";
                        }));
                    }
                    else
                    {
                        rfqInfo = "**Project:** N/A\n**RFQ Number:** N/A\n**Quote Amount:** N/A";
                    }


                    // 4️⃣ Reminder Titles
                    string reminderTitle = reminderCount switch
                    {
                        0 => "⏰ Reminder 1: Subcontractor Missing in DWH After First Sync",
                        1 => "⏰ Reminder 2: Subcontractor Missing in DWH After Second Sync",
                        2 => "⏰ Reminder 3: Final Warning, Subcontractor Missing in DWH After Third Sync",
                        3 => "⏰ Reminder 4: Subcontractor Still Missing in DWH",
                        _ => $"Reminder {reminderCount + 1}: Subcontractor Still Missing in DWH"
                    };

                    // 5️⃣ Warning / Final Notice Section
                    string escalationMessage = reminderCount switch
                    {
                        2 => @"⚠️ <b>Warning:</b> If no action is taken before tomorrow at 7:30 AM CET, the subcontractor data will be removed from the system during the next sync cycle, even if RFQs are sent.<br><br>",
                        3 => @"🚨 <b>Final Notice:</b> The subcontractor data has been removed from the DWH as no action was taken after multiple reminders.<br><br>",
                        _ => ""
                    };

                    // 6️⃣ Action Required Section
                    string actionRequired = reminderCount >= 3
                        ? ""
                        : "⏳ <b>Action Required:</b> Please verify and ensure the subcontractor data is added to the DWH at the earliest.<br><br>";

                    // 7️⃣ Build Message
                    string message = $@"
<b>{reminderTitle}</b><br><br>

This is a follow-up regarding the subcontractor data missing from the DWH after today’s sync at 07:30 AM.<br><br>

<b>Subcontractor Details:</b><br>
• <b>Subcontractor Name:</b> {subcontractor.Name}<br>
• <b>Email:</b> {subcontractor.Email}<br>
• <b>Location:</b> {subcontractor.Location}<br>
• <b>Country:</b> {subcontractor.Country}<br><br>

<b>Primary Contact:</b><br>
• <b>Name:</b> {subcontractor?.ContactName ?? "N/A"}<br>
• <b>Email:</b> {subcontractor?.ContactEmail ?? "N/A"}<br>
• <b>Phone:</b> {subcontractor?.ContactPhone ?? "N/A"}<br><br>

• <b>Office Address:</b> {subcontractor?.OfficeAddress ?? "N/A"}<br>
• <b>Billing Address:</b> {subcontractor?.BillingAddress ?? "N/A"}<br><br>

<b>Work Items Mapped:</b><br>
{workItemsMapped}<br><br>

<b>RFQ Details:</b><br>
{rfqInfo}<br>

{escalationMessage}
{actionRequired}
";

                    // 8️⃣ Send Teams notification
                    await _teamsService.SendTeamsMessageAsync(message);

                    // 9️⃣ Increment reminder count
                    reminderCount++;

                    await _subcontractorRepo.UpdateSubcontractorRemindersSent(subcontractor.SubcontractorID, reminderCount);

                    // 🔟 Delete subcontractor after Reminder 4
                    if (reminderCount >= 4)
                    {
                        await _subcontractorRepo.DeleteSubcontractor(subcontractor.SubcontractorID);
                    }

                    sentMessages.Add(message);

                    _logger.LogInformation(
                        "📨 Reminder {ReminderNumber} sent for Subcontractor Email {Email} at {Time}",
                        reminderCount,
                        subcontractor.Email,
                        DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow)
                    );
                }

                return Ok(new
                {
                    Message = sentMessages.Any()
                        ? "Teams notifications sent successfully."
                        : "No reminders to send.",
                    Count = sentMessages.Count,
                    Messages = sentMessages
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to compare Subcontractor IDs.");
                return StatusCode(500, "An error occurred while comparing Subcontractor IDs.");
            }
        }


    }
}
