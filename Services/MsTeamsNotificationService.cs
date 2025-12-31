using System.Text.Json;
using UnibouwAPI.Repositories.Interfaces;
using System.Text;

namespace UnibouwAPI.Service
{
    public class MsTeamsNotificationService : IMsTeamsNotification
    {
        private readonly HttpClient _httpClient;
        private readonly string _webhookUrl;

        public MsTeamsNotificationService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _webhookUrl = configuration["Teams:WebhookUrl"]
                          ?? throw new ArgumentNullException("Teams Webhook URL is missing");
        }

        public async Task SendTeamsMessageAsync(string message)
        {
            var payload = new
            {
                text = message
            };

            await PostAsync(payload);
        }

        public async Task SendRfqTeamsNotificationAsync(string rfqId, string client, string status)
        {
            var payload = new
            {
                @type = "MessageCard",
                @context = "http://schema.org/extensions",
                summary = "RFQ Notification",
                themeColor = "0076D7",
                title = "📩 RFQ Update",
                sections = new[]
                {
                new
                {
                    facts = new[]
                    {
                        new { name = "RFQ ID", value = rfqId },
                        new { name = "Client", value = client },
                        new { name = "Status", value = status }
                    },
                    markdown = true
                }
            }
            };

            await PostAsync(payload);
        }

        private async Task PostAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_webhookUrl, content);
            response.EnsureSuccessStatusCode();
        }
    }

}


