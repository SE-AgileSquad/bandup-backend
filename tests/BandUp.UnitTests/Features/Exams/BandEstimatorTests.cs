using BandUp.Domain.Exams;
using Shouldly;

namespace BandUp.UnitTests.Features.Exams;

/// <summary>
/// The approximate band: the raw score is scaled to a 40-question paper and read from the
/// published IELTS Academic Reading table. Attribute arguments cannot be decimals, so the
/// expected bands are written as doubles and converted; every band is a multiple of 0.5 and
/// exact in a double.
/// </summary>
public sealed class BandEstimatorTests
{
    // The first and last score of each band in the published table, on a full 40-question paper.
    [Theory]
    [InlineData(40, 9.0)]
    [InlineData(39, 9.0)]
    [InlineData(38, 8.5)]
    [InlineData(37, 8.5)]
    [InlineData(36, 8.0)]
    [InlineData(35, 8.0)]
    [InlineData(34, 7.5)]
    [InlineData(33, 7.5)]
    [InlineData(32, 7.0)]
    [InlineData(30, 7.0)]
    [InlineData(29, 6.5)]
    [InlineData(27, 6.5)]
    [InlineData(26, 6.0)]
    [InlineData(23, 6.0)]
    [InlineData(22, 5.5)]
    [InlineData(19, 5.5)]
    [InlineData(18, 5.0)]
    [InlineData(15, 5.0)]
    [InlineData(14, 4.5)]
    [InlineData(13, 4.5)]
    [InlineData(12, 4.0)]
    [InlineData(10, 4.0)]
    [InlineData(9, 3.5)]
    [InlineData(8, 3.5)]
    [InlineData(7, 3.0)]
    [InlineData(6, 3.0)]
    [InlineData(5, 2.5)]
    [InlineData(4, 2.5)]
    public void A_Full_Paper_Follows_The_Published_Table(int rawScore, double expectedBand)
    {
        BandEstimator.Estimate(rawScore, 40).ShouldBe((decimal)expectedBand);
    }

    // A ten-question practice passage is scaled by four before the lookup.
    [Theory]
    [InlineData(10, 9.0)]
    [InlineData(9, 8.0)]
    [InlineData(8, 7.0)]
    [InlineData(7, 6.5)]
    [InlineData(6, 6.0)]
    [InlineData(5, 5.5)]
    [InlineData(4, 5.0)]
    [InlineData(3, 4.0)]
    [InlineData(2, 3.5)]
    [InlineData(1, 2.5)]
    [InlineData(0, 0.0)]
    public void A_Ten_Question_Passage_Is_Scaled_To_Forty(int rawScore, double expectedBand)
    {
        BandEstimator.Estimate(rawScore, 10).ShouldBe((decimal)expectedBand);
    }

    [Theory]
    [InlineData(2, 2, 9.0)]
    [InlineData(1, 2, 5.5)]
    [InlineData(0, 2, 0.0)]
    [InlineData(2, 3, 6.5)]
    [InlineData(3, 3, 9.0)]
    public void A_Very_Short_Passage_Scales_Like_Any_Other(int rawScore, int total, double expectedBand)
    {
        BandEstimator.Estimate(rawScore, total).ShouldBe((decimal)expectedBand);
    }

    [Fact]
    public void The_Scaled_Score_Rounds_Half_Up()
    {
        // 29 of 80 is 14.5 of 40, which rounds up to 15 and so to band 5.0 rather than 4.5.
        BandEstimator.Estimate(29, 80).ShouldBe(5.0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void There_Is_No_Band_Without_Questions(int total)
    {
        BandEstimator.Estimate(0, total).ShouldBeNull();
    }

    [Fact]
    public void An_Out_Of_Range_Score_Is_Clamped_To_The_Questions_There_Are()
    {
        BandEstimator.Estimate(15, 10).ShouldBe(9.0m);
        BandEstimator.Estimate(-4, 10).ShouldBe(0.0m);
    }
}
