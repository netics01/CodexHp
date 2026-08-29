using CodexHp.Core.Settings;

namespace CodexHp.App.Application;

public sealed class SettingsCommitService
{
    private readonly ISettingsStore settingsStore;
    private readonly IStartupRegistration startupRegistration;

    public SettingsCommitService(ISettingsStore settingsStore, IStartupRegistration startupRegistration)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.startupRegistration = startupRegistration ?? throw new ArgumentNullException(nameof(startupRegistration));
    }

    public AppSettings Commit(AppSettings desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        var validated = SettingsValidator.Validate(desired).Settings;
        var previousStartupState = this.startupRegistration.IsEnabled();
        this.startupRegistration.SetEnabled(validated.StartWithWindows);

        try
        {
            this.settingsStore.Save(validated);
        }
        catch (Exception saveException)
        {
            try
            {
                this.startupRegistration.SetEnabled(previousStartupState);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Settings save and startup registration rollback both failed.",
                    saveException,
                    rollbackException);
            }

            throw;
        }

        return validated;
    }
}
