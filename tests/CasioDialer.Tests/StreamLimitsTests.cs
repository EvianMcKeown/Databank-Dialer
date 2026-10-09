public class StreamLimitsTests
{
    [Fact]
    public void Admission_RefusesBeyondPerIpLimit()
    {
        var admission = new StreamAdmission();
        Assert.True(admission.TryAdmit("1.1.1.1", 10, 2));
        Assert.True(admission.TryAdmit("1.1.1.1", 10, 2));
        Assert.False(admission.TryAdmit("1.1.1.1", 10, 2));
        Assert.True(admission.TryAdmit("2.2.2.2", 10, 2));
    }

    [Fact]
    public void Admission_RefusesBeyondTotalLimit()
    {
        var admission = new StreamAdmission();
        Assert.True(admission.TryAdmit("1.1.1.1", 2, 5));
        Assert.True(admission.TryAdmit("2.2.2.2", 2, 5));
        Assert.False(admission.TryAdmit("3.3.3.3", 2, 5));
        Assert.Equal(2, admission.Active);
    }

    [Fact]
    public void Admission_ReleaseFreesASlot()
    {
        var admission = new StreamAdmission();
        Assert.True(admission.TryAdmit("1.1.1.1", 1, 1));
        Assert.False(admission.TryAdmit("2.2.2.2", 1, 1));
        admission.Release("1.1.1.1");
        Assert.Equal(0, admission.Active);
        Assert.True(admission.TryAdmit("2.2.2.2", 1, 1));
    }

    [Fact]
    public void Admission_ReleaseOfUnknownIpIsIgnored()
    {
        var admission = new StreamAdmission();
        admission.Release("9.9.9.9");
        Assert.Equal(0, admission.Active);
    }

    [Fact]
    public void Budget_AllowsRealtimeStreaming()
    {
        var start = DateTime.UtcNow;
        var budget = new SessionBudget(new AudioLimits(), start);
        for (int i = 1; i <= 625; i++)
        {
            Assert.True(budget.Allow(128, start.AddMilliseconds(i * 16)));
        }
    }

    [Fact]
    public void Budget_StopsAtMaxSessionLength()
    {
        var start = DateTime.UtcNow;
        var limits = new AudioLimits { MaxSessionSeconds = 10 };
        var budget = new SessionBudget(limits, start);
        Assert.True(budget.Allow(128, start.AddSeconds(9)));
        Assert.False(budget.Allow(128, start.AddSeconds(11)));
    }

    [Fact]
    public void Budget_RejectsFasterThanRealtimeFlood()
    {
        var start = DateTime.UtcNow;
        var budget = new SessionBudget(new AudioLimits(), start);
        bool refused = false;
        for (int i = 0; i < 2000 && !refused; i++)
        {
            refused = !budget.Allow(2048, start.AddMilliseconds(100));
        }
        Assert.True(refused);
    }

    [Fact]
    public void Budget_RejectsTinyChunkFlood()
    {
        var start = DateTime.UtcNow;
        var budget = new SessionBudget(new AudioLimits(), start);
        bool refused = false;
        for (int i = 0; i < 5000 && !refused; i++)
        {
            refused = !budget.Allow(1, start.AddMilliseconds(100));
        }
        Assert.True(refused);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2049)]
    public void Budget_RejectsBadChunkSizes(int length)
    {
        var start = DateTime.UtcNow;
        var budget = new SessionBudget(new AudioLimits(), start);
        Assert.False(budget.Allow(length, start));
    }
}
