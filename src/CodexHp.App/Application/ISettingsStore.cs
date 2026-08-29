using CodexHp.Core.Settings;

namespace CodexHp.App.Application;

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
