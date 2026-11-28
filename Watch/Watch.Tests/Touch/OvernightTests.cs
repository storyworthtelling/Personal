using Watch.Domain.Touch;

namespace Watch.Tests.Touch;

/// <summary>
/// The night is the owner's own Do Not Disturb or Bedtime mode on the watch (P14 B8, A12): no hours are kept on the watch
/// of its own, so the night follows the schedule the owner set there.
/// </summary>
public class OvernightTests
{
    [Theory]
    [InlineData(Interruptions.Priority)]
    [InlineData(Interruptions.Alarms)]
    [InlineData(Interruptions.None)]
    public void TheWatchsOwnQuietIsTheNight(Interruptions interruptions) => Assert.True(Overnight.During(interruptions));

    [Fact]
    public void EveryInterruptionIsTheDay() => Assert.False(Overnight.During(Interruptions.All));

    [Fact]
    public void WhatThePlatformWontSayIsTheDay() => Assert.False(Overnight.During(Interruptions.Unknown));
}
