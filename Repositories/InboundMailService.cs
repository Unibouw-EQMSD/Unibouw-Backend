namespace UnibouwAPI.Repositories;
using Azure.Identity;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnibouwAPI.Repositories.Interfaces;

public class InboundMailService : IInboundMailService
{
    private readonly IConfiguration _config;
    private readonly string _connectionString;

    public InboundMailService(IConfiguration config)
    {
        _config = config;
        _connectionString = _config.GetConnectionString("UnibouwDbConnection");
    }

    public async Task ProcessNotificationAsync(string notificationJson)
    {
        if (string.IsNullOrWhiteSpace(notificationJson))
            return; // ✅ no JSON, nothing to process

        using var doc = JsonDocument.Parse(notificationJson);

        if (!doc.RootElement.TryGetProperty("value", out var valueArr))
            return;

        if (valueArr.ValueKind != JsonValueKind.Array || valueArr.GetArrayLength() == 0)
            return;

        foreach (var item in valueArr.EnumerateArray())
        {
            if (!item.TryGetProperty("resource", out var resourceProp)) continue;
            var resource = resourceProp.GetString();
            var messageId = resource?.Split("/messages/").LastOrDefault();
            if (string.IsNullOrWhiteSpace(messageId)) continue;

            await ProcessSingleMessageAsync(messageId);
        }
    }

    private async Task ProcessSingleMessageAsync(string messageId)
    {
        var tenantId = _config["GraphEmail:TenantId"];
        var clientId = _config["GraphEmail:ClientId"];
        var clientSecret = _config["GraphEmail:ClientSecret"];
        var senderUser = _config["GraphEmail:SenderUser"]; // nitish mailbox

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var graphClient = new GraphServiceClient(credential);

        // 2) Read message
        var msg = await graphClient.Users[senderUser].Messages[messageId].GetAsync(request =>
        {
            request.QueryParameters.Select = new[]
            {
                "id","subject","body","from","receivedDateTime"
            };
        });

        if (msg == null) return;

        var bodyHtml = msg.Body?.Content ?? "";
        var subject = msg.Subject ?? "";
        var received = msg.ReceivedDateTime?.DateTime ?? DateTime.UtcNow;
        var fromEmail = msg.From?.EmailAddress?.Address ?? "";

        // 3) Extract tracking token: TRACK:UBW|{projectId}|{subId}
        var tracking = ExtractTracking(bodyHtml);
        if (tracking == null) return;

        var (projectId, subcontractorId, rfqId) = tracking.Value;
        // 4) Insert into RFQConversationMessage as Subcontractor message
        var conversationMessageId = Guid.NewGuid();

        using var con = new SqlConnection(_connectionString);
        await con.OpenAsync();

        const string insertMsgSql = @"
INSERT INTO RFQConversationMessage
(
  ConversationMessageID, ProjectID, SubcontractorID, SenderType,
  MessageText, MessageDateTime, Status, CreatedBy, CreatedOn, Subject, Tag
)
VALUES
(
  @ConversationMessageID, @ProjectID, @SubcontractorID, @SenderType,
  @MessageText, @MessageDateTime, @Status, @CreatedBy, @CreatedOn, @Subject, @Tag
);";

        await con.ExecuteAsync(insertMsgSql, new
        {
            ConversationMessageID = conversationMessageId,
            ProjectID = projectId,
            SubcontractorID = subcontractorId,
            SenderType = "Subcontractor",
            MessageText = StripHtml(bodyHtml),
            MessageDateTime = received,
            Status = "Active",
            CreatedBy = fromEmail,
            CreatedOn = received,
            Subject = subject,
            Tag = "Inbound Email Reply"
        });

        // 5) Attachments (optional)
        var atts = await graphClient.Users[senderUser].Messages[messageId].Attachments.GetAsync();
        if (atts?.Value == null || atts.Value.Count == 0) return;

        var uploadsFolder = Path.Combine("Uploads", "RFQ");
        Directory.CreateDirectory(uploadsFolder);

        foreach (var a in atts.Value)
        {
            if (a is FileAttachment fa && fa.ContentBytes != null)
            {
                var originalFileName = fa.Name ?? "attachment";
                var storedFileName = $"{Guid.NewGuid()}_{originalFileName}";
                var filePath = Path.Combine(uploadsFolder, storedFileName);

                await File.WriteAllBytesAsync(filePath, fa.ContentBytes);

                const string insertAttSql = @"
INSERT INTO dbo.RfqConversationMessageAttachment
(
  AttachmentID, ConversationMessageID, FileName, FileExtension, FileSize,
  FilePath, IsActive, UploadedBy, UploadedOn
)
VALUES
(
  @AttachmentID, @ConversationMessageID, @FileName, @FileExtension, @FileSize,
  @FilePath, 1, NULL, @UploadedOn
);";

                await con.ExecuteAsync(insertAttSql, new
                {
                    AttachmentID = Guid.NewGuid(),
                    ConversationMessageID = conversationMessageId,
                    FileName = originalFileName,
                    FileExtension = Path.GetExtension(originalFileName),
                    FileSize = fa.ContentBytes.Length,
                    FilePath = filePath,
                    UploadedOn = received
                });
            }
        }
    }

    private (Guid projectId, Guid subcontractorId, Guid rfqId)? ExtractTracking(string html)
    {
        var m = Regex.Match(html ?? "",
            @"UBW:project=(?<p>[0-9a-fA-F-]{36});sub=(?<s>[0-9a-fA-F-]{36});rfq=(?<r>[0-9a-fA-F-]{36})",
            RegexOptions.IgnoreCase);

        if (!m.Success) return null;

        return (Guid.Parse(m.Groups["p"].Value),
                Guid.Parse(m.Groups["s"].Value),
                Guid.Parse(m.Groups["r"].Value));
    }
    private string StripHtml(string html)
    {
        return Regex.Replace(html ?? "", "<.*?>", "").Trim();
    }
}
