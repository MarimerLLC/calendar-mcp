using Microsoft.Extensions.Options;

namespace CalendarMcp.Tests.Helpers;

internal sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = currentValue;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
