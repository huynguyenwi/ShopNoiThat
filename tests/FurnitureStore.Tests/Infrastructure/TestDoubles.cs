using FurnitureStore.Application.Common.Interfaces;

namespace FurnitureStore.Tests.Infrastructure;

public sealed class TestCurrentUser(string? userName = "tester", string? userId = "test-user-id", params string[] roles) : ICurrentUserService
{
    public string? UserId { get; } = userId;
    public string? UserName { get; } = userName;
    public bool IsAuthenticated => UserId is not null;
    public bool IsInRole(string role) => roles.Contains(role);
    public string? IpAddress => "127.0.0.1";
}

/// <summary>TimeProvider whose clock only moves when the test says so.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public static FixedTimeProvider At(int year, int month, int day, int hour = 9) =>
        new(new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
