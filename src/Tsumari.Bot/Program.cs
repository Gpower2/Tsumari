using System;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tsumari.Bot.Services;

namespace Tsumari.Bot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Configure Console Logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // Discord Client Setup
            var socketConfig = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMessageReactions | GatewayIntents.MessageContent,
                AlwaysDownloadUsers = false,
                MessageCacheSize = 50,
                LogLevel = LogSeverity.Info
            };
            builder.Services.AddSingleton(socketConfig);
            builder.Services.AddSingleton<DiscordSocketClient>();

            // Interaction Service (Slash Commands) Setup
            var interactionConfig = new InteractionServiceConfig
            {
                LogLevel = LogSeverity.Info,
                DefaultRunMode = RunMode.Async
            };
            builder.Services.AddSingleton(interactionConfig);
            // Explicitly construct it using the registered DiscordSocketClient instance
            builder.Services.AddSingleton<InteractionService>(provider =>
                new InteractionService(
                    provider.GetRequiredService<DiscordSocketClient>(),
                    provider.GetRequiredService<InteractionServiceConfig>()
                ));

            // Register Custom services
            builder.Services.AddHttpClient(HttpClientNames.DiscordCdn);
            builder.Services.AddHttpClient(HttpClientNames.DeepLLanguageMetadata);
            builder.Services.AddHttpClient(HttpClientNames.OllamaTranslation);
            builder.Services.AddHttpClient(HttpClientNames.OpenAITranslation);

            builder.Services.AddSingleton<IDiscordMessageService, DiscordMessageService>();
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<DiscordMessagePublisherService>();
            builder.Services.AddSingleton<DeepLLanguageService>();
            builder.Services.AddSingleton<DeepLTranslationProvider>();
            builder.Services.AddSingleton<OllamaTranslationProvider>();
            builder.Services.AddSingleton<OpenAITranslationProvider>();
            builder.Services.AddSingleton<TranslationProviderResolver>();
            builder.Services.AddSingleton<ITranslationProvider>(provider =>
                provider.GetRequiredService<TranslationProviderResolver>().Resolve());
            builder.Services.AddSingleton<TranslationService>();
            builder.Services.AddSingleton<MirroredMessageRoutingService>();
            builder.Services.AddSingleton<EditedMessageSyncService>();
            builder.Services.AddSingleton<LinkedMessageDeletionService>();
            builder.Services.AddSingleton<ReplyMirroringService>();
            builder.Services.AddSingleton<ReactionMirroringService>();

            // Main Background Bot Service
            builder.Services.AddHostedService<DiscordGatewayHostedService>();

            var host = builder.Build();
            host.Run();
        }
    }
}
