using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Xunit;

namespace WorkshopMes.Tests;

public class ReportPriceSnapshotTests
{
    [Theory]
    [InlineData(1, 24)]
    [InlineData(2, 6)]
    [InlineData(3, 3)]
    public void RuleMutation_PreservesTypePriceAndDeduction(byte type, decimal amount)
    {
        var rule = new BasePriceRule { Id = 9, PriceType = type, UnitPrice = 3, DeductPrice = .25m };
        var report = new ProdReport { Id = 12, GoodQty = 10, DefectQty = 2, DurationMinutes = 150,
            PriceSnapshot = ReportPriceSnapshot.Capture(rule) };
        rule.UnitPrice = 99; rule.PriceType = 3; rule.DeductPrice = 10;
        var calc = ReportPriceSnapshot.Calculate(report, .8m);
        Assert.Equal(3m, calc.UnitPrice); Assert.Equal(type, calc.PriceType);
        Assert.Equal(amount, calc.Amount); Assert.Equal(.5m, calc.DeductAmount);
        report.GoodQty = 20; report.DurationMinutes = 300;
        var edited = ReportPriceSnapshot.Calculate(report, .8m);
        Assert.Equal(type == 3 ? amount : amount * 2, edited.Amount);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("null")] [InlineData("{}")] [InlineData("broken")]
    public void MissingOrCorruptSnapshot_ExplicitlyRejects(string? snapshot)
    {
        var report = new ProdReport { Id = 123, UnitPrice = 10, WageAmount = 100, PriceSnapshot = snapshot };
        Assert.Contains("123", Assert.Throws<BusinessException>(() => ReportPriceSnapshot.Calculate(report, 1)).Message);
    }

    [Fact]
    public void ZeroPrice_IsValid()
    {
        var report = new ProdReport { GoodQty = 10, PriceSnapshot = ReportPriceSnapshot.Capture(
            new BasePriceRule { Id = 1, PriceType = 1, UnitPrice = 0 }) };
        Assert.Equal(0, ReportPriceSnapshot.Calculate(report, 1).WageAmount);
    }
}
