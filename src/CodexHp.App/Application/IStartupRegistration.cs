namespace CodexHp.App.Application;

public interface IStartupRegistration
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
