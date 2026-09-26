namespace Tsumari.Bot.Configuration;

public sealed class TranslationRetryOptions
{
    public double[] Temperatures { get; set; } = [0.15, 0.25, 0.35, 0.45, 0.55, 0.60, 0.60, 0.60, 0.60, 0.60];
    public double TopP { get; set; } = 0.80;
    public int CooldownSeconds { get; set; } = 30;
    public int QueueCapacity { get; set; } = 8;
    public ulong RoleId { get; set; }

    public void Validate()
    {
        if (Temperatures.Length is < 1 or > 10 || Temperatures.Any(t => !double.IsFinite(t) || t < 0 || t > 0.60)
            || Temperatures.Zip(Temperatures.Skip(1)).Any(p => p.First > p.Second)
            || !double.IsFinite(TopP) || TopP <= 0 || TopP > 1 || CooldownSeconds < 0 || QueueCapacity is < 1 or > 100)
            throw new ArgumentException("Invalid Translation:Retry settings.");
    }
}
