using System;
using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Tsumari.Bot.Configuration;
using Tsumari.Bot.Services;

namespace Tsumari.Bot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--validate-deployment")
            {
                try
                {
                    if (args.Length != 3) throw new ArgumentException("Usage: --validate-deployment <release-directory> <report-path>");
                    DeploymentConfigurationValidator.ValidateArtifact(args[1], args[2]);
                    Console.WriteLine("Deployment configuration validation passed. No bot or database was started.");
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Deployment configuration validation failed: {error.Message}");
                    for (Exception? detail = error; detail is not null; detail = detail.InnerException)
                        if (detail is System.Text.Json.JsonException json)
                            Console.Error.WriteLine($"Invalid JSON syntax: line {json.LineNumber + 1}, byte position {json.BytePositionInLine}. Configuration values are not printed.");
                    Environment.ExitCode = 2;
                }
                return;
            }

            // Prefer UTF-8 console I/O so Unicode log output has the best chance of rendering.
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            var builder = Host.CreateApplicationBuilder(args);

            // Build output contains the merged local configuration. Keep the source content
            // root (and therefore database paths), while preserving normal override priority.
            builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false);
            builder.Configuration.AddJsonFile(Path.Combine(builder.Environment.ContentRootPath, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: false);
            builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: false);
            if (builder.Environment.IsDevelopment())
                builder.Configuration.AddUserSecrets<Program>(optional: true);
            builder.Configuration.AddEnvironmentVariables();
            builder.Configuration.AddCommandLine(args);


            // Load Discord REST guardrail options up front so they can be applied to the
            // underlying Discord.Net HTTP client before the host is built.
            builder.Services.Configure<DiscordRestOptions>(builder.Configuration.GetSection("Discord:Rest"));
            var discordRestOptions = new DiscordRestOptions();
            builder.Configuration.GetSection("Discord:Rest").Bind(discordRestOptions);

            // Configure Console Logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options =>
            {
                options.FormatterName = TsumariConsoleFormatter.FormatterName;
            });
            builder.Logging.AddConsoleFormatter<TsumariConsoleFormatter, TsumariConsoleFormatterOptions>(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
                options.UseUtcTimestamp = true;
                options.IncludeScopes = false;
                options.ColorBehavior = LoggerColorBehavior.Enabled;
            });
            builder.Logging.AddDebug();

            // Discord Client Setup
            // Enforce a per-request timeout on every REST call Discord.Net makes. The value here is
            // the longest operation we expect (file uploads). Per-operation timeouts are set via
            // RequestOptions, so this is only a safety net for calls that do not supply one.
            var socketConfig = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMessageReactions | GatewayIntents.MessageContent,
                AlwaysDownloadUsers = false,
                MessageCacheSize = 50,
                LogLevel = LogSeverity.Info,
                RestClientProvider = TimeoutRestClientProvider.Create(discordRestOptions.FileUploadTimeout)
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
            builder.Services.AddHttpClient(HttpClientNames.DiscordCdn)
                .ConfigureHttpClient(client => client.Timeout = discordRestOptions.FileUploadTimeout);
            builder.Services.AddHttpClient(HttpClientNames.DeepLLanguageMetadata);
            builder.Services.AddHttpClient(HttpClientNames.OllamaTranslation);
            builder.Services.AddHttpClient(HttpClientNames.OpenAITranslation);

            builder.Services.AddSingleton<IDiscordRestRetryPolicy, DiscordRestRetryPolicy>();
            builder.Services.AddSingleton<IGatewayReadinessCoordinator, GatewayReadinessCoordinator>();
            builder.Services.AddSingleton<IDiscordMessageService, DiscordMessageService>();
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<DiscordMessagePublisherService>();
            builder.Services.AddSingleton<DeepLLanguageService>();
            builder.Services.AddSingleton<DeepLTranslationProvider>();
            builder.Services.AddSingleton<Tsumari.Bot.TranslationProviders.Models.LlmModelFactory>();
            builder.Services.AddSingleton<OllamaTranslationProvider>();
            builder.Services.AddSingleton<OpenAITranslationProvider>();
            builder.Services.AddSingleton<TranslationProviderResolver>();
            builder.Services.AddSingleton<ITranslationProvider>(provider =>
                provider.GetRequiredService<TranslationProviderResolver>().Resolve());
            builder.Services.AddSingleton<TranslationService>();
            builder.Services.Configure<TranslationRetryOptions>(options => options.BindConfiguration(builder.Configuration.GetSection("Translation:Retry")));
            builder.Services.AddSingleton<MessageMutationCoordinator>();
            builder.Services.AddSingleton<TranslationRetryService>();
            builder.Services.AddSingleton<MirroredMessageRoutingService>();
            builder.Services.AddSingleton<IMirroredMessageRoutingService>(provider =>
                provider.GetRequiredService<MirroredMessageRoutingService>());
            builder.Services.AddSingleton<IHistoricalMessageSyncService, HistoricalMessageSyncService>();
            builder.Services.AddSingleton<IStartupMessageSyncService, StartupMessageSyncService>();
            builder.Services.AddSingleton<EditedMessageSyncService>();
            builder.Services.AddSingleton<LinkedMessageDeletionService>();
            builder.Services.AddSingleton<ReplyMirroringService>();
            builder.Services.AddSingleton<ReactionMirroringService>();
            builder.Services.AddSingleton<IGatewayEventGroupResolver, GatewayEventGroupResolver>();
            builder.Services.AddSingleton<IDiscordGatewayEventProcessor, DiscordGatewayEventProcessorService>();
            builder.Services.AddSingleton<DiscordGatewayEventDispatcherService>();
            builder.Services.AddSingleton<IDiscordGatewayEventDispatcher>(provider =>
                provider.GetRequiredService<DiscordGatewayEventDispatcherService>());

            // Background services
            builder.Services.AddHostedService(provider =>
                provider.GetRequiredService<DiscordGatewayEventDispatcherService>());
            builder.Services.AddHostedService<DiscordGatewayHostedService>();
            builder.Services.AddHostedService<StartupMessageSyncHostedService>();

            var host = builder.Build();
            host.Services.GetRequiredService<Tsumari.Bot.TranslationProviders.Models.LlmModelFactory>().ValidateConfiguration();
            host.Services.GetRequiredService<TranslationService>().LogProviderConfiguration();
            host.Run();
        }
    }
}
