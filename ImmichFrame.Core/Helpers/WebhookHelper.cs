using System.Text.Json;
using ImmichFrame.Core.Interfaces;

public static class WebhookHelper
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task SendWebhookNotification(IWebhookNotification notification, string? webhookUrl)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl)) return;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        string json = JsonSerializer.Serialize(notification, notification.GetType(), options);
        var data = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync(webhookUrl, data);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Webhook successfully sent.");
            }
            else
            {
                Console.WriteLine($"Failed to send notification to webhook: {Convert.ToInt32(response.StatusCode)} {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send notification to webhook: {ex.Message}");
        }
    }
}