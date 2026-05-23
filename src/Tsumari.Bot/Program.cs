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
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
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
            builder.Services.AddSingleton<InteractionService>();

            // Register Custom services
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<TranslationService>();
            builder.Services.AddSingleton<HttpClient>();

            // Main Background Bot Service
            builder.Services.AddHostedService<Worker>();

            var host = builder.Build();
            host.Run();
        }
    }
}
