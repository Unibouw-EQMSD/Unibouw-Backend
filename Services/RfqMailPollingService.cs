using Azure.Identity;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Data;
using System.Text.RegularExpressions;
using UnibouwAPI.Repositories.Interfaces;
using UnibouwAPI.Services.Interfaces;

namespace UnibouwAPI.Services
{
    public class RfqMailPollingService : IRfqMailPollingService
    {
        private readonly IConfiguration _config;
        private readonly IRfqEmailIngestionRepository _repo;
        private readonly ILogger<RfqMailPollingService> _logger;

        private readonly string _connectionString;

        public RfqMailPollingService(
            IConfiguration config,
            IRfqEmailIngestionRepository repo,
            ILogger<RfqMailPollingService> logger)
        {
            _config = config;
            _connectionString = config.GetConnectionString("UnibouwDbConnection")
               ?? throw new InvalidOperationException("Connection string not configured.");

            _repo = repo;
            _logger = logger;
        }

        private GraphServiceClient CreateGraphClient()
        {
            var tenantId = _config["GraphEmail:TenantId"];
            var clientId = _config["GraphEmail:ClientId"];
            var clientSecret = _config["GraphEmail:ClientSecret"];

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            return new GraphServiceClient(credential);
        }

        public async Task PollOnceAsync(CancellationToken ct)
        {
            Console.WriteLine($"[PollOnceAsync] started UTC={DateTime.UtcNow:o}");
            _logger.LogWarning("PollOnceAsync started");
            var graph = CreateGraphClient();
            var batchSize = int.TryParse(_config["MailPolling:BatchSize"], out var b) ? b : 25;

            var mailboxes = await _repo.GetAllPersonMailboxesAsync();

            foreach (var pmMailbox in mailboxes)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await PollMailboxAsync(graph, pmMailbox, batchSize, ct);
                    await _repo.UpdateCursorRunAsync(pmMailbox, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Polling failed for {Mailbox}", pmMailbox);
                    await _repo.UpdateCursorRunAsync(pmMailbox, ex.Message);
                }
            }
        }

        private static DateTime ToAmsterdamTime(DateTime utc)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Amsterdam"
            );

            // utc must be DateTimeKind.Utc to convert correctly
            var utcKind = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(utcKind, tz);
        }

        private async Task PollMailboxAsync(GraphServiceClient graph, string pmMailbox, int batchSize, CancellationToken ct)
        {
            var (cursorUtc, processedIdsAtCursor) = await _repo.GetOrCreateCursorUtcAsync(pmMailbox);

            var inboxMsgs = await GetFolderMessagesSinceAsync(graph, pmMailbox, "Inbox", cursorUtc, batchSize, ct);
            var sentMsgs = await GetFolderMessagesSinceAsync(graph, pmMailbox, "SentItems", cursorUtc, batchSize, ct);

            var all = inboxMsgs.Select(m => (Folder: "Inbox", Msg: m))
                .Concat(sentMsgs.Select(m => (Folder: "SentItems", Msg: m)))
                .OrderBy(x => x.Msg.ReceivedDateTime!.Value.UtcDateTime)
                .ToList();

            if (all.Count == 0)
            {
                Console.WriteLine("[PollMailboxAsync] No messages after cursor filter");
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var maxUtc = cursorUtc;
            var newProcessedIdsAtCursor = new HashSet<string>();

            foreach (var item in all)
            {
                if (ct.IsCancellationRequested) break;
                var msg = item.Msg;
                var folder = item.Folder;

                if (string.IsNullOrWhiteSpace(msg.Id) || !msg.ReceivedDateTime.HasValue)
                    continue;

                var receivedUtc = msg.ReceivedDateTime.Value.UtcDateTime;

                // NEW LOGIC: Allow same-timestamp messages if not already processed at this timestamp
                bool shouldProcess =
                    (receivedUtc > cursorUtc) ||
                    (receivedUtc == cursorUtc && !processedIdsAtCursor.Contains(msg.Id));

                if (!shouldProcess)
                    continue;

                if (await _repo.IsAlreadyIngestedAsync(msg.Id, folder))
                    continue;

                // In-run de-dupe (optional, for double mailbox scan protection)
                var key = msg.Id;
                if (!seen.Add(key))
                    continue;

                try
                {
                    if (folder == "Inbox")
                        await TryIngestInboxAsync(graph, pmMailbox, msg, ct);
                    else
                        await TryIngestSentAsync(graph, pmMailbox, msg, ct);
                }
                catch (Exception ex)
                {
                    // log error
                }
                finally
                {
                    await _repo.MarkIngestedAsync(pmMailbox, msg.Id, folder, receivedUtc);
                }

                // Update cursor logic:
                if (receivedUtc > maxUtc)
                {
                    maxUtc = receivedUtc;
                    newProcessedIdsAtCursor.Clear();
                    newProcessedIdsAtCursor.Add(msg.Id);
                }
                else if (receivedUtc == maxUtc)
                {
                    newProcessedIdsAtCursor.Add(msg.Id);
                }
            }

            // Save new cursor and IDs at cursor
            await _repo.UpdateCursorUtcAsync(pmMailbox, maxUtc, string.Join(",", newProcessedIdsAtCursor));
        }



        private async Task TryIngestSentAsync(GraphServiceClient graph, string pmMailbox, Message msg, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(msg.ConversationId))
                return;

            // Parse token as usual (projectId, subId, rfqId, found)...
            var (projectId, subId, rfqId, found) = ParseToken(msg.Subject);
            if (!found) (projectId, subId, rfqId, found) = ParseToken(msg.BodyPreview);
            if (!found)
            {
                var full = await graph.Users[pmMailbox].Messages[msg.Id].GetAsync(r =>
                {
                    r.QueryParameters.Select = new[] { "id", "subject", "bodyPreview", "body", "receivedDateTime", "sentDateTime", "conversationId" };
                }, ct);
                (projectId, subId, rfqId, found) = ParseToken(full?.Subject);
                if (!found) (projectId, subId, rfqId, found) = ParseToken(full?.BodyPreview);
                if (!found) (projectId, subId, rfqId, found) = ParseToken(full?.Body?.Content);
            }
            if (!found || !projectId.HasValue || !subId.HasValue || !rfqId.HasValue)
                return;

            var anchorOk = await _repo.AnchorExistsAsync(projectId.Value, subId.Value, rfqId.Value, pmMailbox, msg.ConversationId);
            if (!anchorOk)
                return;

            var subj = (msg.Subject ?? "").Trim();
            if (subj.StartsWith("RFQ –", StringComparison.OrdinalIgnoreCase) &&
                !subj.StartsWith("RE:", StringComparison.OrdinalIgnoreCase) &&
                !subj.StartsWith("FW:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var fromEmail = msg.From?.EmailAddress?.Address ?? "";
            if (fromEmail.Equals(pmMailbox, StringComparison.OrdinalIgnoreCase))
                return;

            // Use full body content, or fallback to BodyPreview
            var rawText = msg.Body?.Content ?? msg.BodyPreview ?? "";
            // Convert <br> tags to newlines before stripping HTML
            rawText = rawText.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                             .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                             .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);
            var plainText = StripHtml(rawText);
            var replyText = ExtractTopReply(plainText);

            // Fetch official details from DB
            using var conn = new SqlConnection(_connectionString);
            var details = await conn.QuerySingleAsync<dynamic>(
                @"SELECT 
            p.Number AS ProjectCode,
            p.Name AS ProjectName,
            r.RfqNumber,
            rsm.DueDate
        FROM Projects p
        INNER JOIN Rfq r ON r.ProjectID = p.ProjectID
        INNER JOIN RfqSubcontractorMapping rsm ON rsm.RfqID = r.RfqID
        WHERE p.ProjectID = @ProjectID AND r.RfqID = @RfqID AND rsm.SubcontractorID = @SubID",
                new { ProjectID = projectId.Value, RfqID = rfqId.Value, SubID = subId.Value }
            );
            string projectLine = $"Project: {details.ProjectCode} - {details.ProjectName}";
            string rfqLine = $"RFQ No: {details.RfqNumber}";
            string dueLine = $"Due Date: {((DateTime)details.DueDate):dd-MMM-yyyy}";
            string officialDetails = $"{projectLine}\n{rfqLine}\n{dueLine}";

            // Combine reply and official details
            string messageToLog = $"{replyText}\n\n---\n{officialDetails}";

            var sentUtc = (msg.SentDateTime ?? msg.ReceivedDateTime)!.Value.UtcDateTime;
            var sentAms = ToAmsterdamTime(sentUtc);

            await _repo.InsertPmSentToConversationAsync(
                projectId.Value,
                subId.Value,
                msg.Subject ?? "",
                messageToLog,
                sentAms,
                pmMailbox
            );
        }
        private string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            html = html.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                       .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                       .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);
            html = Regex.Replace(html, @"<\s*p\s*>", "\n\n", RegexOptions.IgnoreCase);
            return Regex.Replace(html, "<.*?>", string.Empty).Trim();
        }

        private string ExtractTopReply(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var t = text.Trim();
            string[] separators = new[]
            {
        "\r\nOn ", "\nOn ",
        "-----Original Message-----",
        "\r\nFrom:", "\nFrom:",
        "<div class=3D'gmail_quote'>",
        "From: "
    };
            int idx = -1;
            foreach (var sep in separators)
            {
                idx = t.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) break;
            }
            return idx > 0 ? t.Substring(0, idx).Trim() : t;
        }
        private async Task TryIngestInboxAsync(GraphServiceClient graph, string pmMailbox, Message msg, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(msg.ConversationId))
            {
                Console.WriteLine("[Inbox] Skip: ConversationId is null");
                return;
            }

            var (projectId, subId, rfqId, found) = ParseToken(msg.Subject);
            if (!found) (projectId, subId, rfqId, found) = ParseToken(msg.BodyPreview);

            if (!found)
            {
                Console.WriteLine("[Inbox] Token not found in subject/preview. Fetching full body...");

                var full = await graph.Users[pmMailbox].Messages[msg.Id].GetAsync(r =>
                {
                    r.QueryParameters.Select = new[]
                    {
            "id", "subject", "bodyPreview", "body", "receivedDateTime", "conversationId", "from"
        };
                }, ct);

                (projectId, subId, rfqId, found) = ParseToken(full?.Subject);
                if (!found) (projectId, subId, rfqId, found) = ParseToken(full?.BodyPreview);
                if (!found) (projectId, subId, rfqId, found) = ParseToken(full?.Body?.Content);

                Console.WriteLine($"[Inbox] Token after full fetch found={found}");
            }

            if (!found || !projectId.HasValue || !subId.HasValue)
            {
                Console.WriteLine("[Inbox] Skip: Token still not found");
                return;
            }

            var anchorOk = await _repo.AnchorExistsAsync(projectId.Value, subId.Value, rfqId.Value, pmMailbox, msg.ConversationId);
            if (!anchorOk)
            {
                Console.WriteLine($"[Inbox] Skip: Anchor not found. pm={pmMailbox}, conv={msg.ConversationId}, p={projectId}, s={subId}");
                return;
            }

            var fromEmail = msg.From?.EmailAddress?.Address ?? "";
            if (!string.IsNullOrWhiteSpace(fromEmail) &&
                fromEmail.Equals(pmMailbox, StringComparison.OrdinalIgnoreCase))
            {
                return; // ignore self-sent copy in Inbox
            }
            var subject = msg.Subject ?? "";
            var receivedUtc = msg.ReceivedDateTime!.Value.UtcDateTime;
            var receivedAms = ToAmsterdamTime(receivedUtc);
            var messageText = (msg.BodyPreview ?? "").Trim();

            await _repo.InsertInboundToConversationAsync(projectId.Value, subId.Value, subject, messageText, receivedAms, fromEmail);
        }

        private async Task<List<Message>> GetFolderMessagesSinceAsync(
     GraphServiceClient graph,
     string pmMailbox,
     string folderName,
     DateTime sinceUtc,
     int top,
     CancellationToken ct)
        {
            var resp = await graph.Users[pmMailbox].MailFolders[folderName].Messages.GetAsync(r =>
            {
                r.QueryParameters.Top = top;

                r.QueryParameters.Orderby = folderName.Equals("SentItems", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "sentDateTime desc" }
                    : new[] { "receivedDateTime desc" };

                r.QueryParameters.Select = new[]
                {
            "id",
            "internetMessageId",
            "subject",
            "receivedDateTime",
            "sentDateTime",
            "conversationId",
            "from",
            "bodyPreview"
        };
            }, ct);

            var list = (resp?.Value ?? new List<Message>())
                .Where(m => m.ReceivedDateTime.HasValue && m.ReceivedDateTime.Value.UtcDateTime >= sinceUtc)
                .ToList();

            list.Reverse(); // process oldest-first
            return list;
        }
        private static (Guid? ProjectId, Guid? SubId, Guid? RfqId, bool Found) ParseToken(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (null, null, null, false);

            // Subject token: UBW:P=...;S=...;R=...
            var m1 = Regex.Match(
                text,
                @"UBW:P=(?<p>[0-9a-fA-F-]{36});S=(?<s>[0-9a-fA-F-]{36});R=(?<r>[0-9a-fA-F-]{36})",
                RegexOptions.IgnoreCase);
            if (m1.Success
                && Guid.TryParse(m1.Groups["p"].Value, out var p1)
                && Guid.TryParse(m1.Groups["s"].Value, out var s1)
                && Guid.TryParse(m1.Groups["r"].Value, out var r1)
                && p1 != Guid.Empty && s1 != Guid.Empty && r1 != Guid.Empty)
            {
                return (p1, s1, r1, true);
            }

            // Body token: UBW:project=...;sub=...;rfq=...
            var m2 = Regex.Match(
                text,
                @"UBW:project=(?<p>[0-9a-fA-F-]{36});sub=(?<s>[0-9a-fA-F-]{36});rfq=(?<r>[0-9a-fA-F-]{36})",
                RegexOptions.IgnoreCase);
            if (m2.Success
                && Guid.TryParse(m2.Groups["p"].Value, out var p2)
                && Guid.TryParse(m2.Groups["s"].Value, out var s2)
                && Guid.TryParse(m2.Groups["r"].Value, out var r2)
                && p2 != Guid.Empty && s2 != Guid.Empty && r2 != Guid.Empty)
            {
                return (p2, s2, r2, true);
            }
            return (null, null, null, false);
        }
    }
}