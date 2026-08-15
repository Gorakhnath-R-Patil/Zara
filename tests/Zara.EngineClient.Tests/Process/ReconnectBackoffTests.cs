using Zara.EngineClient.Process;

namespace Zara.EngineClient.Tests.Process;

public class ReconnectBackoffTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordCrashAndGetNextDelay_FirstCrash_Returns1Second()
    {
        var sut = new ReconnectBackoff();

        var delay = sut.RecordCrashAndGetNextDelay(T0);

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public void RecordCrashAndGetNextDelay_FollowsTheDocumentedSchedule_1_4_16_60()
    {
        var sut = new ReconnectBackoff();

        var delays = new[]
        {
            sut.RecordCrashAndGetNextDelay(T0),
            sut.RecordCrashAndGetNextDelay(T0.AddSeconds(1)),
            sut.RecordCrashAndGetNextDelay(T0.AddSeconds(5)),
            sut.RecordCrashAndGetNextDelay(T0.AddSeconds(21)),
        };

        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(60)],
            delays);
    }

    [Fact]
    public void RecordCrashAndGetNextDelay_BeyondTheSchedule_StaysAtTheLongestDelay()
    {
        var sut = new ReconnectBackoff();

        for (int i = 0; i < 4; i++)
        {
            sut.RecordCrashAndGetNextDelay(T0.AddMinutes(i));
        }
        var fifthDelay = sut.RecordCrashAndGetNextDelay(T0.AddMinutes(4));

        Assert.Equal(TimeSpan.FromSeconds(60), fifthDelay);
    }

    [Fact]
    public void Reset_RestartsTheScheduleFromTheBeginning()
    {
        var sut = new ReconnectBackoff();
        sut.RecordCrashAndGetNextDelay(T0);
        sut.RecordCrashAndGetNextDelay(T0.AddSeconds(1));

        sut.Reset();
        var delay = sut.RecordCrashAndGetNextDelay(T0.AddMinutes(10));

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public void ShouldGiveUp_FewerThanThreeCrashesInFiveMinutes_ReturnsFalse()
    {
        var sut = new ReconnectBackoff();
        sut.RecordCrashAndGetNextDelay(T0);
        sut.RecordCrashAndGetNextDelay(T0.AddMinutes(1));

        Assert.False(sut.ShouldGiveUp(T0.AddMinutes(2)));
    }

    [Fact]
    public void ShouldGiveUp_ThreeCrashesWithinFiveMinutes_ReturnsTrue()
    {
        var sut = new ReconnectBackoff();
        sut.RecordCrashAndGetNextDelay(T0);
        sut.RecordCrashAndGetNextDelay(T0.AddMinutes(1));
        sut.RecordCrashAndGetNextDelay(T0.AddMinutes(2));

        Assert.True(sut.ShouldGiveUp(T0.AddMinutes(2)));
    }

    [Fact]
    public void ShouldGiveUp_ThreeCrashesButSpreadOverMoreThanFiveMinutes_ReturnsFalse()
    {
        var sut = new ReconnectBackoff();
        sut.RecordCrashAndGetNextDelay(T0);
        sut.RecordCrashAndGetNextDelay(T0.AddMinutes(3));
        // By now the first crash (at T0) has aged out of the trailing 5-minute window.
        var now = T0.AddMinutes(6);

        sut.RecordCrashAndGetNextDelay(now);

        Assert.False(sut.ShouldGiveUp(now));
    }

    [Fact]
    public void ShouldGiveUp_OldCrashesOutsideTheWindow_DoNotCount()
    {
        var sut = new ReconnectBackoff();
        sut.RecordCrashAndGetNextDelay(T0);
        sut.RecordCrashAndGetNextDelay(T0.AddSeconds(30));

        // Ten minutes later: both earlier crashes are outside the 5-minute
        // window, so this lone recent one shouldn't trigger give-up.
        var recentTime = T0.AddMinutes(10);
        sut.RecordCrashAndGetNextDelay(recentTime);

        Assert.False(sut.ShouldGiveUp(recentTime));
    }
}
