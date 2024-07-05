using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

class Program
{
    private static readonly HttpClient _httpClient = new HttpClient();

    public static async Task Main(string[] args)
    {
        var config = new DiscordSocketConfig
        {
            TotalShards = 1,
            GatewayIntents = (GatewayIntents.AllUnprivileged & (~GatewayIntents.GuildScheduledEvents) & (~GatewayIntents.GuildInvites)) | GatewayIntents.MessageContent
        };
        await using var services = ConfigureServices(config);
        var discordClient = services.GetRequiredService<DiscordShardedClient>();
        discordClient.ShardReady += ReadyAsync;
        discordClient.Log += LogAsync;
        discordClient.MessageReceived += MessageReceivedAsync;
        await discordClient.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("Discord_Bot_Token", EnvironmentVariableTarget.User));
        await discordClient.StartAsync();

        await Task.Delay(Timeout.Infinite);
    }

    private static ServiceProvider ConfigureServices(DiscordSocketConfig config)
        => new ServiceCollection()
            .AddSingleton(new DiscordShardedClient(config))
            .BuildServiceProvider();


    private static Task ReadyAsync(DiscordSocketClient shard)
    {
        Console.WriteLine($"Shard Number {shard.ShardId} is connected and ready!");
        return Task.CompletedTask;
    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private static async Task MessageReceivedAsync(SocketMessage prompt)
    {
        _ = Task.Run(async () =>
        {
            // ignore system messages, or messages from other bots
            if (!(prompt is SocketUserMessage message))
                return;
            if (message.Source != MessageSource.User)
                return;

            string url = Environment.GetEnvironmentVariable("Image_Generator_Api", EnvironmentVariableTarget.User)!;
            url = url.Replace("[PROMPT]", prompt.Content);
            url = url.Replace("[NEGATIVE]", "Bad anatomy, Bad hands, Amputee, Missing fingers, Missing hands, Missing limbs, Missing arms, Extra fingers, Extra hands, Extra limbs, Mutated hands, Mutated, Mutation, Multiple heads, Malformed limbs, Disfigured, Poorly drawn hands, Poorly drawn face, Long neck, Fused fingers, Fused hands, Dismembered, Duplicate, Improper scale, Ugly body, Cloned face, Cloned body, Gross proportions, Body horror, Too many fingers");
            url = url.Replace("[SCALE]", "7");
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            string body = await response.Content.ReadAsStringAsync();
            JsonDocument jsonDoc = JsonDocument.Parse(body);
            JsonElement root = jsonDoc.RootElement;
            if (root.TryGetProperty("imageId", out JsonElement imageIdElement))
            {
                string imageId = imageIdElement.GetString()!;
                url = Environment.GetEnvironmentVariable("Image_Download_Api", EnvironmentVariableTarget.User)!;
                response = await _httpClient.GetAsync(url + imageId);
                if (response.IsSuccessStatusCode)
                {
                    // Read the content as a byte array
                    byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();

                    using (MemoryStream imageStream = new MemoryStream(imageBytes))
                    {
                        // Send the image as a message
                        await ((ITextChannel)prompt.Channel).SendFileAsync(imageStream, "image.jpg");
                    }
                }
                else
                {
                    await ((ITextChannel)prompt.Channel).SendMessageAsync("Failed to download image.");
                }
            }
            else
            {
                await ((ITextChannel)prompt.Channel).SendMessageAsync("Failed to generate image.");
            }
            jsonDoc.Dispose();
        });
    }
}