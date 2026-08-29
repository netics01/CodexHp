namespace CodexHp.Core.Settings;

public sealed class SettingsEditSession
{
    private AppSettings baseline;

    public SettingsEditSession(AppSettings baseline)
    {
        this.baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        this.Working = baseline;
    }

    public AppSettings Working { get; private set; }

    public void Preview(AppSettings settings)
    {
        this.Working = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public AppSettings Confirm()
    {
        var validated = SettingsValidator.Validate(this.Working).Settings;
        this.baseline = validated;
        this.Working = validated;
        return validated;
    }

    public AppSettings Cancel()
    {
        this.Working = this.baseline;
        return this.baseline;
    }
}
