using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HungryGame;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HungryTests.TestInfrastructure;

internal sealed class SequenceRandomService : IRandomService
{
    private readonly Queue<int> _values;

    public SequenceRandomService(IEnumerable<int> values)
    {
        _values = new Queue<int>(values);
    }

    public int Next(int maxValue)
    {
        if (_values.Count == 0)
            throw new InvalidOperationException($"No more random values configured for maxValue {maxValue}.");

        var next = _values.Dequeue();
        if (next < 0 || next >= maxValue)
            throw new InvalidOperationException($"Configured random value {next} is outside the valid range [0, {maxValue}).");

        return next;
    }
}

internal sealed class ConstantRandomService : IRandomService
{
    private readonly int _value;

    public ConstantRandomService(int value = 0)
    {
        _value = value;
    }

    public int Next(int maxValue)
    {
        if (_value < 0 || _value >= maxValue)
            throw new InvalidOperationException($"Configured random value {_value} is outside the valid range [0, {maxValue}).");

        return _value;
    }
}

internal static class TestGameFactory
{
    public static GameLogic CreateGame(
        IRandomService? random = null,
        TimeProvider? timeProvider = null)
    {
        return new GameLogic(
            NullLogger<GameLogic>.Instance,
            random ?? new ConstantRandomService(),
            timeProvider ?? TimeProvider.System);
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _utcNow = start ?? new DateTimeOffset(2026, 4, 13, 12, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(by));

        DateTimeOffset target;
        lock (_gate)
        {
            target = _utcNow + by;
        }

        AdvanceTo(target);
    }

    private void AdvanceTo(DateTimeOffset target)
    {
        while (true)
        {
            ManualTimer? dueTimer;
            lock (_gate)
            {
                dueTimer = _timers
                    .Where(t => t.IsScheduled && !t.IsDisposed && t.NextDue <= target)
                    .OrderBy(t => t.NextDue)
                    .FirstOrDefault();

                if (dueTimer == null)
                {
                    _utcNow = target;
                    return;
                }

                _utcNow = dueTimer.NextDue;
                dueTimer.PrepareToFire();
            }

            dueTimer.Fire();
            CleanupDisposedTimers();
        }
    }

    private void CleanupDisposedTimers()
    {
        lock (_gate)
        {
            _timers.RemoveAll(t => t.IsDisposed);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _provider;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private bool _disposeAfterFire;

        public ManualTimer(ManualTimeProvider provider, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _provider = provider;
            _callback = callback;
            _state = state;
            Change(dueTime, period);
        }

        public DateTimeOffset NextDue { get; private set; }
        public TimeSpan Period { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool IsScheduled { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (IsDisposed)
                return false;

            Period = period;
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                IsScheduled = false;
                NextDue = DateTimeOffset.MaxValue;
                return true;
            }

            if (dueTime < TimeSpan.Zero)
                dueTime = TimeSpan.Zero;

            IsScheduled = true;
            NextDue = _provider.GetUtcNow() + dueTime;
            return true;
        }

        public void PrepareToFire()
        {
            if (!IsScheduled || IsDisposed)
                return;

            if (Period == Timeout.InfiniteTimeSpan)
            {
                IsScheduled = false;
                _disposeAfterFire = true;
                return;
            }

            NextDue += Period;
        }

        public void Fire()
        {
            if (!IsDisposed)
            {
                _callback(_state);
            }

            if (_disposeAfterFire && !IsDisposed && !IsScheduled)
            {
                Dispose();
            }

            _disposeAfterFire = false;
        }

        public void Dispose()
        {
            IsDisposed = true;
            IsScheduled = false;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal static class AsyncTestHelpers
{
    public static async Task FlushBackgroundWorkAsync()
    {
        await Task.Yield();
        await Task.Delay(1);
    }

    public static async Task WaitUntilAsync(Func<bool> predicate, int maxAttempts = 200)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (predicate())
                return;

            await FlushBackgroundWorkAsync();
        }

        throw new TimeoutException("Condition was not reached within the allotted attempts.");
    }
}

internal sealed class HungryGameWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SECRET_CODE"] = "swordfish",
                ["RateLimit:PermitLimit"] = "1000",
                ["RateLimit:WindowSeconds"] = "1",
                ["THROW_ERRORS"] = "false"
            });
        });
    }
}
