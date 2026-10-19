using System.Reflection;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

public static class ReportEntryChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Mcp:AuthBaseUrl"]="https://example.invalid", ["Mcp:DemoSkipAuth"]="true", ["Mcp:WecomReport:Enabled"]="false" }).Build();
        var context=new McpRequestContext { CurrentFactoryId=1 };
        var http=new HttpContextAccessor { HttpContext=new DefaultHttpContext() };
        var users=DispatchProxy.Create<IUserService,Stub>();
        var binding=DispatchProxy.Create<IMcpWxBindService,Stub>();
        var generated=0; var resolved=0;
        var person=new SysUser { Id=7, FactoryId=1, Name="测试本人", Account="worker7", Phone="13800000001", Role=2 };
        ((Stub)(object)users).Call=(method,args)=> {
            if(method.Name!="GetOrCreateLoginQrAsync") throw new Exception("不应调用其他用户操作");
            check((long)args![0]! == 7 && (long)args[1]! == 1,"免密入口只按已核验本人的ID和工厂取码");
            generated++;
            return Task.FromResult(ApiResult<UserLoginQrDto>.Ok(new UserLoginQrDto { UserId=7,Code="fake-test-secret" }));
        };
        ((Stub)(object)binding).Call=(_,_)=> { resolved++; return Task.FromResult<SysUser?>(person); };
        var service=new McpReportLinkService(binding,http,context,users,config,Options.Create(new InstanceOptions()));
        static JsonElement Obj(string text)=>JsonDocument.Parse(text).RootElement.Clone();
        var noIdentity=Obj(await service.GetReportLinkByIdentityAsync("测试本人"));
        check(noIdentity.GetProperty("code").GetInt32()!=0 && noIdentity.GetProperty("publicEntryUrl").GetString()=="https://example.invalid/#/h5/home" && !noIdentity.TryGetProperty("link",out _),"仅姓名不给个人链接，提供不含凭证的H5首页");
        check(generated==0 && resolved==0,"没有平台身份时不查绑或生成个人码，演示模式不能绕过");
        http.HttpContext.Request.Headers["X-Mcp-Session-Id"]="trusted-current-user";
        var unverified=Obj(await service.GetReportLinkByIdentityAsync("13800000001"));
        check(unverified.GetProperty("state").GetString()=="use_existing_login_or_personal_code" && generated==0 && resolved==0,"自报手机号不能代替管理员核验");
        var key=$"Mcp:WecomReport:VerifiedUsers:1:{McpWxBindService.HashSession("trusted-current-user")}";
        config[key]="8";
        check(Obj(await service.GetReportLinkByIdentityAsync(null)).GetProperty("code").GetInt32()!=0 && generated==0,"绑定员工与管理员映射不一致拒绝发码");
        config[key]="7";
        check(Obj(await service.GetReportLinkByIdentityAsync("另一员工")).GetProperty("code").GetInt32()!=0 && generated==0,"已认证用户不能凭另一个名字获取他人链接");
        person.Status=0;
        check(Obj(await service.GetReportLinkByIdentityAsync(null)).GetProperty("code").GetInt32()!=0 && generated==0,"停用账号拒绝发码");
        person.Status=1;person.UserType=UserTypeHelper.External;
        check(Obj(await service.GetReportLinkByIdentityAsync(null)).GetProperty("code").GetInt32()!=0 && generated==0,"外部人员不能获取登录链接");
        person.UserType=UserTypeHelper.Internal;person.FactoryId=2;
        check(Obj(await service.GetReportLinkByIdentityAsync(null)).GetProperty("code").GetInt32()!=0 && generated==0,"跨厂绑定不能获取链接");
        person.FactoryId=1;
        var success=Obj(await service.GetReportLinkByIdentityAsync(null));
        check(success.GetProperty("code").GetInt32()==0 && success.GetProperty("link").GetString()=="https://example.invalid/#/login?pq=fake-test-secret" && generated==1,"已关联本人无需密码或验证码可获取免密链接，不依赖聊天报工启用开关");
        config[key]=null;
        check(Obj(await service.GetReportLinkByIdentityAsync(null)).GetProperty("code").GetInt32()!=0 && generated==1,"管理员撤销核验后立即停止新获取个人链接");
        config["Mcp:AuthBaseUrl"]="http://example.invalid";
        check(!Obj(await service.GetReportLinkByIdentityAsync(null)).TryGetProperty("link",out _) && generated==1,"拒绝HTTP非安全地址且不生成个人码");
    }
}
