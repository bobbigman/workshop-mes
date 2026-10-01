using ahu.MicrosoftMes.Common;
using Xunit;

namespace WorkshopMes.Tests;

public class ThrowHelperTests
{
    [Fact]
    public void Biz_ReturnsOnlyBusinessMessage_AndPreservesDefaultCode()
    {
        const string message = "字段“辣度”已有业务数据，不能删除";
        var exception = ThrowHelper.Biz("DeleteAsync", message);
        Assert.Equal(message, exception.Message);
        Assert.Equal(1, exception.Code);
        Assert.Equal(message, ThrowHelper.BizUser(message).Message);
        Assert.Equal(1, ThrowHelper.BizUser(message).Code);
    }
}
