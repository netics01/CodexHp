namespace CodexHp.App.Application;

public enum DiagnosticLevel
{
    Information,
    Warning,
    Error,
}

public interface IDiagnosticLogger
{
    void Log(DiagnosticLevel level, string component, string message, Exception? exception = null);
}
