namespace Tsumari.Bot.Models
{
    public sealed class MediaAsset
    {
        public string Filename { get; init; } = string.Empty;

        public byte[] Bytes { get; init; } = [];
    }
}
