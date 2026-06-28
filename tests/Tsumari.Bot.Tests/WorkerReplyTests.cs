using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tsumari.Bot;
using Tsumari.Bot.Services;
using Xunit;

namespace Tsumari.Bot.Tests
{
    public class WorkerReplyTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly DatabaseService _dbService;

        public WorkerReplyTests()
        {
            _testDbPath = $"test_tsumari_worker_reply_{Guid.NewGuid():N}.db";

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["Database:FilePath"]).Returns(_testDbPath);

            _dbService = new DatabaseService(configMock.Object, NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }

                var walFile = $"{_testDbPath}-wal";
                if (File.Exists(walFile))
                {
                    File.Delete(walFile);
                }

                var shmFile = $"{_testDbPath}-shm";
                if (File.Exists(shmFile))
                {
                    File.Delete(shmFile);
                }
            }
            catch
            {
            }
        }

        [Fact]
        public async Task SendMessageWithFilesAsync_PassesReplyReference_ForTextOnlyMessages()
        {
            var worker = CreateWorker();
            var channelMock = new Mock<IMessageChannel>();
            var userMessageMock = new Mock<IUserMessage>();
            MessageReference? capturedReference = null;

            channelMock
                .Setup(channel => channel.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .Callback<string, bool, Embed?, RequestOptions?, AllowedMentions?, MessageReference?, MessageComponent?, ISticker[]?, Embed[]?, MessageFlags, PollProperties?>(
                    (_, _, _, _, _, messageReference, _, _, _, _, _) => capturedReference = messageReference)
                .ReturnsAsync(userMessageMock.Object);

            var replyReference = new MessageReference(12345UL, 54321UL, null, false, default);

            var result = await InvokeSendMessageWithFilesAsync(worker, channelMock.Object, "hello", [], null, replyReference);

            Assert.Same(userMessageMock.Object, result);
            Assert.NotNull(capturedReference);
            Assert.Equal(12345UL, capturedReference!.MessageId.Value);
            Assert.Equal(54321UL, capturedReference.ChannelId);
        }

        [Fact]
        public async Task SendMessageWithFilesAsync_PassesReplyReference_ForFileMessages()
        {
            var worker = CreateWorker();
            var channelMock = new Mock<IMessageChannel>();
            var userMessageMock = new Mock<IUserMessage>();
            MessageReference? capturedReference = null;

            channelMock
                .Setup(channel => channel.SendFilesAsync(
                    It.IsAny<IEnumerable<FileAttachment>>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .Callback<IEnumerable<FileAttachment>, string, bool, Embed?, RequestOptions?, AllowedMentions?, MessageReference?, MessageComponent?, ISticker[]?, Embed[]?, MessageFlags, PollProperties?>(
                    (_, _, _, _, _, _, messageReference, _, _, _, _, _) => capturedReference = messageReference)
                .ReturnsAsync(userMessageMock.Object);

            var replyReference = new MessageReference(22222UL, 33333UL, null, false, default);
            var mediaAssets = new List<(string Filename, byte[] Bytes)>
            {
                ("file.txt", [1, 2, 3])
            };

            var result = await InvokeSendMessageWithFilesAsync(worker, channelMock.Object, "hello", mediaAssets, null, replyReference);

            Assert.Same(userMessageMock.Object, result);
            Assert.NotNull(capturedReference);
            Assert.Equal(22222UL, capturedReference!.MessageId.Value);
            Assert.Equal(33333UL, capturedReference.ChannelId);
        }

        [Fact]
        public async Task SendMessageWithFilesAsync_PreservesReplyReference_WhenFallingBackToText()
        {
            var worker = CreateWorker();
            var channelMock = new Mock<IMessageChannel>();
            var userMessageMock = new Mock<IUserMessage>();
            MessageReference? capturedFallbackReference = null;

            channelMock
                .Setup(channel => channel.SendFilesAsync(
                    It.IsAny<IEnumerable<FileAttachment>>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .ThrowsAsync(new InvalidOperationException("upload failed"));

            channelMock
                .Setup(channel => channel.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed?>(),
                    It.IsAny<RequestOptions?>(),
                    It.IsAny<AllowedMentions?>(),
                    It.IsAny<MessageReference?>(),
                    It.IsAny<MessageComponent?>(),
                    It.IsAny<ISticker[]?>(),
                    It.IsAny<Embed[]?>(),
                    It.IsAny<MessageFlags>(),
                    It.IsAny<PollProperties?>()))
                .Callback<string, bool, Embed?, RequestOptions?, AllowedMentions?, MessageReference?, MessageComponent?, ISticker[]?, Embed[]?, MessageFlags, PollProperties?>(
                    (_, _, _, _, _, messageReference, _, _, _, _, _) => capturedFallbackReference = messageReference)
                .ReturnsAsync(userMessageMock.Object);

            var replyReference = new MessageReference(77777UL, 88888UL, null, false, default);
            var mediaAssets = new List<(string Filename, byte[] Bytes)>
            {
                ("file.txt", [1, 2, 3])
            };

            var result = await InvokeSendMessageWithFilesAsync(worker, channelMock.Object, "hello", mediaAssets, null, replyReference);

            Assert.Same(userMessageMock.Object, result);
            Assert.NotNull(capturedFallbackReference);
            Assert.Equal(77777UL, capturedFallbackReference!.MessageId.Value);
            Assert.Equal(88888UL, capturedFallbackReference.ChannelId);
        }

        private Worker CreateWorker()
        {
            var configMock = new Mock<IConfiguration>();

            return new Worker(
                null!,
                null!,
                _dbService,
                null!,
                null!,
                new ReplyMirroringService(_dbService),
                null!,
                null!,
                null!,
                null!,
                configMock.Object,
                NullLogger<Worker>.Instance);
        }

        private static async Task<IUserMessage?> InvokeSendMessageWithFilesAsync(
            Worker worker,
            IMessageChannel channel,
            string text,
            List<(string Filename, byte[] Bytes)> mediaAssets,
            MessageComponent? components,
            MessageReference? replyReference)
        {
            var method = typeof(Worker).GetMethod("SendMessageWithFilesAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find SendMessageWithFilesAsync.");

            var task = (Task<IUserMessage?>?)method.Invoke(worker, [channel, text, mediaAssets, components, replyReference])
                ?? throw new InvalidOperationException("SendMessageWithFilesAsync did not return the expected task.");

            return await task;
        }
    }
}
