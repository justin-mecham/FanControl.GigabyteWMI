using System;
using Xunit;
using FluentAssertions;
using FanControl.GigabyteWMI.Hardware;

namespace FanControl.GigabyteWMI.Tests
{
    public class DutyTableTests
    {
        [Fact]
        public void DutyTable_ShouldHaveExactly16Steps()
        {
            DutyTable.Table.Should().HaveCount(16);
            DutyTable.Table[0].Should().Be(57);
            DutyTable.Table[15].Should().Be(229);
        }

        [Theory]
        [InlineData(0f, 57)]      // 0% -> Step 0 (57)
        [InlineData(50f, 148)]    // 50% -> Step 8 (148)
        [InlineData(100f, 229)]   // 100% -> Step 15 (229)
        [InlineData(-10f, 57)]    // Clamped below 0
        [InlineData(150f, 229)]   // Clamped above 100
        public void ToRegisterSpeed_ShouldMapPercentagesCorrectly(float percentage, byte expectedRegSpeed)
        {
            byte actual = DutyTable.ToRegisterSpeed(percentage);
            actual.Should().Be(expectedRegSpeed);
        }

        [Fact]
        public void DutyTable_ShouldBeStrictlyMonotonicallyIncreasing()
        {
            for (int i = 1; i < DutyTable.Table.Length; i++)
            {
                DutyTable.Table[i].Should().BeGreaterThan(DutyTable.Table[i - 1]);
            }
        }
    }
}
