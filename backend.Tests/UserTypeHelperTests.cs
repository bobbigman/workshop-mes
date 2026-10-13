using ahu.MicrosoftMes.Common;
using Xunit;

namespace WorkshopMes.Tests;

public class UserTypeHelperTests
{
    [Fact]
    public void EnsureCanLogin_External_ThrowsBizWithMessage()
    {
        var ex = Assert.Throws<BusinessException>(() => UserTypeHelper.EnsureCanLogin(UserTypeHelper.External));
        Assert.Equal(UserTypeHelper.ExternalLoginDeniedMsg, ex.Message);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void EnsureCanLogin_InternalOrTemp_Ok(byte userType)
    {
        UserTypeHelper.EnsureCanLogin(userType);
    }

    [Theory]
    [InlineData(null, (byte)0)]
    [InlineData("", (byte)0)]
    [InlineData("内部", (byte)0)]
    [InlineData("临时", (byte)1)]
    [InlineData("外部", (byte)2)]
    [InlineData("外部供应商", (byte)2)]
    public void ParseImport_Known_Ok(string? raw, byte expected)
    {
        Assert.Equal(expected, UserTypeHelper.ParseImport(raw));
    }

    [Fact]
    public void ParseImport_Unknown_ThrowsWithColumnHint()
    {
        var ex = Assert.Throws<BusinessException>(() => UserTypeHelper.ParseImport("外委"));
        Assert.Contains("人员类型", ex.Message);
    }
}
