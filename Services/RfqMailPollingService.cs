using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
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

        public RfqMailPollingService(
            IConfiguration config,
            IRfqEmailIngestionRepository repo,
            ILogger<RfqMailPollingService> logger)
        {
            _config = config;
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
            var cursorUtc = await _repo.GetOrCreateCursorUtcAsync(pmMailbox);

            var inboxMsgs = await GetFolderMessagesSinceAsync(graph, pmMailbox, "Inbox", cursorUtc, batchSize, ct);
            var sentMsgs = await GetFolderMessagesSinceAsync(graph, pmMailbox, "SentItems", cursorUtc, batchSize, ct);

            Console.WriteLine($"[PollMailboxAsync] pm={pmMailbox} cursorUtc={cursorUtc:o}");
            Console.WriteLine($"[PollMailboxAsync] inboxMsgs={inboxMsgs.Count} sentMsgs={sentMsgs.Count}");

            var all = inboxMsgs.Select(m => (Folder: "Inbox", Msg: m))
                .Concat(sentMsgs.Select(m => (Folder: "SentItems", Msg: m)))
                .OrderBy(x => x.Msg.ReceivedDateTime!.Value.UtcDateTime)
                .ToList();

            Console.WriteLine($"[PollMailboxAsync] all={all.Count}");

            if (all.Count == 0)
            {
                Console.WriteLine("[PollMailboxAsync] No messages after cursor filter");
                return;
            }

            // ✅ In-run de-dupe (prevents Inbox+SentItems copies inserting twice)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var maxUtc = cursorUtc;

            foreach (var item in all)
            {
                if (ct.IsCancellationRequested) break;

                var msg = item.Msg;
                var folder = item.Folder;

                Console.WriteLine(
                    $"[Mail] folder={folder} id={msg.Id} recv={msg.ReceivedDateTime?.UtcDateTime:o} " +
                    $"subj={(msg.Subject ?? "").Trim()} from={(msg.From?.EmailAddress?.Address ?? "").Trim()} " +
                    $"internetId={(msg.InternetMessageId ?? "").Trim()} convId={(msg.ConversationId ?? "").Trim()}");

                if (string.IsNullOrWhiteSpace(msg.Id) || !msg.ReceivedDateTime.HasValue)
                {
                    Console.WriteLine("[Mail] Skip: missing id or receivedDateTime");
                    continue;
                }

                var receivedUtc = msg.ReceivedDateTime.Value.UtcDateTime;
                if (receivedUtc > maxUtc) maxUtc = receivedUtc;

                // ✅ DB de-dupe (already processed in previous runs)
                if (await _repo.IsAlreadyIngestedAsync(msg.Id, folder))
                {
                    Console.WriteLine("[Mail] Skip: already ingested (DB)");
                    continue;
                }

                // ✅ In-run de-dupe key
                var internetId = (msg.InternetMessageId ?? "").Trim();
                var key = !string.IsNullOrWhiteSpace(internetId)
                    ? internetId
                    : $"{(msg.Subject ?? "").Trim()}|{receivedUtc:O}|{(msg.From?.EmailAddress?.Address ?? "").Trim()}";

                // If we already processed same email in this cycle, skip (but still mark processed)
                if (!seen.Add(key))
                {
                    Console.WriteLine($"[Mail] Skip: duplicate in-run key={key}");
                    await _repo.MarkIngestedAsync(pmMailbox, msg.Id, folder, receivedUtc);
                    continue;
                }

                try
                {
                    if (folder == "Inbox")
                    {
                        Console.WriteLine("[Mail] Calling TryIngestInboxAsync...");
                        await TryIngestInboxAsync(graph, pmMailbox, msg, ct);
                    }
                    else
                    {
                        Console.WriteLine("[Mail] Calling TryIngestSentAsync...");
                        await TryIngestSentAsync(graph, pmMailbox, msg, ct);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Mail] ERROR: {ex.Message}");
                    _logger.LogError(ex, "Ingest failed for mailbox={Mailbox}, folder={Folder}, msgId={MsgId}", pmMailbox, folder, msg.Id);
                }
                finally
                {
                    // ✅ Always mark processed (for now; later change to mark only on success)
                    await _repo.MarkIngestedAsync(pmMailbox, msg.Id, folder, receivedUtc);
                }
            }

            await _repo.UpdateCursorUtcAsync(pmMailbox, maxUtc);
        }
        private async Task TryIngestSentAsync(GraphServiceClient graph, string pmMailbox, Message msg, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(msg.ConversationId))
                return;

            // Token parse (subject first, then preview, then full body)
            var (projectId, subId, found) = ParseToken(msg.Subject);

            if (!found)
                (projectId, subId, found) = ParseToken(msg.BodyPreview);

            if (!found)
            {
                var full = await graph.Users[pmMailbox].Messages[msg.Id].GetAsync(r =>
                {
                    r.QueryParameters.Select = new[] { "id", "subject", "bodyPreview", "body", "receivedDateTime", "sentDateTime", "conversationId" };
                }, ct);

                (projectId, subId, found) = ParseToken(full?.Subject);

                if (!found) (projectId, subId, found) = ParseToken(full?.BodyPreview);
                if (!found) (projectId, subId, found) = ParseToken(full?.Body?.Content);
            }

            if (!found || !projectId.HasValue || !subId.HasValue)
                return;

            // Anchor check (your repo allows ConversationId IS NULL)
            var anchorOk = await _repo.AnchorExistsAsync(projectId.Value, subId.Value, pmMailbox, msg.ConversationId);
            if (!anchorOk)
                return;

            // Skip original RFQ invitation mail in SentItems (QMS already logs it)
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

            // Prefer SentDateTime for SentItems; fallback to ReceivedDateTime
            var sentUtc = (msg.SentDateTime ?? msg.ReceivedDateTime)!.Value.UtcDateTime;
            var sentAms = ToAmsterdamTime(sentUtc);
            // Save only the top reply (prevents repeated quoted RFQ content)
            var messageText = ExtractTopReply(msg.BodyPreview);

            await _repo.InsertPmSentToConversationAsync(
                projectId.Value,
                subId.Value,
                msg.Subject ?? "",
                messageText,
                sentAms,
                pmMailbox
            );
        }

        private static string ExtractTopReply(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var t = text.Trim();

            // Common Outlook separators where quoted content starts
            var idx = t.IndexOf("\r\nFrom:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = t.IndexOf("\nFrom:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = t.IndexOf("-----Original Message-----", StringComparison.OrdinalIgnoreCase);

            return idx > 0 ? t.Substring(0, idx).Trim() : t;
        }
        private async Task TryIngestInboxAsync(GraphServiceClient graph, string pmMailbox, Message msg, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(msg.ConversationId))
            {
                Console.WriteLine("[Inbox] Skip: ConversationId is null");
                return;
            }

            var (projectId, subId, found) = ParseToken(msg.Subject);
            if (!found) (projectId, subId, found) = ParseToken(msg.BodyPreview);

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

                (projectId, subId, found) = ParseToken(full?.Subject);
                if (!found) (projectId, subId, found) = ParseToken(full?.BodyPreview);
                if (!found) (projectId, subId, found) = ParseToken(full?.Body?.Content);

                Console.WriteLine($"[Inbox] Token after full fetch found={found}");
            }

            if (!found || !projectId.HasValue || !subId.HasValue)
            {
                Console.WriteLine("[Inbox] Skip: Token still not found");
                return;
            }

            var anchorOk = await _repo.AnchorExistsAsync(projectId.Value, subId.Value, pmMailbox, msg.ConversationId);

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
                .Where(m => m.ReceivedDateTime.HasValue && m.ReceivedDateTime.Value.UtcDateTime > sinceUtc)
                .ToList();

            list.Reverse(); // process oldest-first
            return list;
        }
        private static (Guid? ProjectId, Guid? SubId, bool Found) ParseToken(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (null, null, false);

            // Subject token: UBW:P=...;S=...;R=...
            var m1 = Regex.Match(
                text,
                @"UBW:P=(?<p>[0-9a-fA-F-]{36});S=(?<s>[0-9a-fA-F-]{36});R=(?<r>[0-9a-fA-F-]{36})",
                RegexOptions.IgnoreCase);

            if (m1.Success &&
                Guid.TryParse(m1.Groups["p"].Value, out var p1) &&
                Guid.TryParse(m1.Groups["s"].Value, out var s1) &&
                p1 != Guid.Empty && s1 != Guid.Empty)
            {
                return (p1, s1, true);
            }

            // Body token: UBW:project=...;sub=...;rfq=...
            var m2 = Regex.Match(
                text,
                @"UBW:project=(?<p>[0-9a-fA-F-]{36});sub=(?<s>[0-9a-fA-F-]{36});rfq=(?<r>[0-9a-fA-F-]{36})",
                RegexOptions.IgnoreCase);

            if (m2.Success &&
                Guid.TryParse(m2.Groups["p"].Value, out var p2) &&
                Guid.TryParse(m2.Groups["s"].Value, out var s2) &&
                p2 != Guid.Empty && s2 != Guid.Empty)
            {
                return (p2, s2, true);
            }

            return (null, null, false);
        }
    }
}