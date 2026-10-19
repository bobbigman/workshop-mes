using System.Text.Json;
using System.Reflection;
using ahu.MicrosoftMes.Mcp;
using Microsoft.AspNetCore.Http;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

public static class MultiFactoryIdentityChecks
{
    public static async Task Run(Action<bool,string> check, Func<Func<Task>,string,Task> reject)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clock = new Clock();
        var tickets = new ReportIdentityTickets(cache, clock);
        var directory = new Directory();
        const string password = "test-web-only";
        directory.Users.Add(new SysUser { Id=20, FactoryId=2, Name="唯一员工", Phone="13812345678", Role=2, Password=ahu.MicrosoftMes.Common.PasswordHelper.Hash(password) });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Mcp:AuthBaseUrl"]="https://example.invalid" }).Build();
        var service = new ReportIdentityService(directory,tickets,config);
        static JsonElement Obj(object value) => JsonSerializer.SerializeToElement(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        async Task<JsonElement> Find(string? name=null,string? phone=null) => Obj(await service.IdentifyAsync(1,name,phone));
        var unique = await Find("唯一员工");
        var lookup = unique.GetProperty("lookupToken").GetString()!;
        check(tickets.Read(lookup).FactoryId==2 && tickets.Read(lookup).OriginFactoryId==1,"姓名唯一跨厂定位，不采用入口默认厂");
        await reject(async()=>await service.InspectAsync(lookup,1,20),"入口厂登录不能冒充目标厂本人");
        var code = await service.ApproveAsync(lookup,2,20,password);
        await reject(async()=>await service.VerifyAsync(2,lookup,code),"即使目标厂相同，也不能换MCP入口兑换");
        var grant=Obj(await service.VerifyAsync(1,lookup,code)).GetProperty("reportAccessToken").GetString()!;
        var resolved=await service.ResolveAsync(1,grant);
        check(resolved.User.FactoryId==2 && resolved.Grant.FactoryId==2 && resolved.Grant.OriginFactoryId==1,"授权在原入口解析为目标厂本人");
        await reject(async()=>await service.ResolveAsync(2,grant),"目标厂凭证不能移到其他入口使用");
        config["Mcp:WecomReport:Enabled"]="true";
        var binding=DispatchProxy.Create<IMcpWxBindService,Stub>();
        ((Stub)(object)binding).Call=(_,_)=>throw new Exception("不应使用企微绑定");
        var tasks=DispatchProxy.Create<IWecomReportTaskService,Stub>();
        ((Stub)(object)tasks).Call=(_,args)=> { check(args![0] is long f && f==2,"候选查询传入授权目标厂"); return Task.FromResult(new ReportTaskPage()); };
        var context=new McpRequestContext { CurrentFactoryId=1 };
        var fake=new FakeWecom();
        var tools=new WecomReportMcpTools(fake,context,binding,new HttpContextAccessor { HttpContext=new DefaultHttpContext() },config,cache,tasks,identity:service);
        await tools.QueryTasks(reportAccessToken:grant);
        var preview=JsonDocument.Parse(await tools.Preview(orderNo:"GD1",operationName:"包装",goodQty:1,reportAccessToken:grant)).RootElement;
        check(fake.LastFactoryId==2 && fake.LastPreview?.VerifiedLoginUserId==20,"预览按目标厂本人办理");
        await tools.Confirm(preview.GetProperty("data").GetProperty("confirmToken").GetString()!,reportAccessToken:grant);
        check(fake.LastFactoryId==2 && context.CurrentFactoryId==1,"确认按目标厂办理且不改变全局MCP工厂");
        directory.Users.Add(new SysUser { Id=30,FactoryId=3,Name="唯一员工",Phone="13812345678",Role=2 });
        check((await Find("唯一员工")).GetProperty("state").GetString()=="need_phone_suffix","跨厂同名先问手机尾号");
        check((await Find(phone:"345678")).GetProperty("state").GetString()=="need_full_phone","跨厂尾号不唯一再问完整号");
        var choices=await Find(phone:"13812345678");
        check(choices.GetProperty("state").GetString()=="select_factory" && choices.GetProperty("factories").GetArrayLength()==2,"完整号多厂返回实际匹配列表");
        check(choices.GetProperty("factories")[0].GetProperty("factoryCode").GetString()=="F2" && !choices.GetRawText().Contains("13812345678"),"同名工厂使用实际代码区分且不泄露手机号");
        var token=choices.GetProperty("factorySelectionToken").GetString()!;
        await reject(async()=>await service.IdentifyAsync(9,null,null,token,1),"工厂选择不能跨入口");
        await reject(async()=>await service.IdentifyAsync(1,null,null,token,99),"工厂选择序号不能越界");
        var selected=Obj(await service.IdentifyAsync(1,null,null,token,2));
        check(tickets.Read(selected.GetProperty("lookupToken").GetString()!).FactoryId==3,"按实际列表序号定位目标厂");
        await reject(async()=>await service.IdentifyAsync(1,null,null,token,2),"工厂选择凭证单次使用");
        var expired=(await Find(phone:"13812345678")).GetProperty("factorySelectionToken").GetString()!;
        clock.Advance(TimeSpan.FromMinutes(11));
        await reject(async()=>await service.IdentifyAsync(1,null,null,expired,1),"工厂选择凭证十分钟失效");
        directory.Users.RemoveAt(1);
        directory.Users.Add(new SysUser { Id=21,FactoryId=2,Name="另一账号",Phone="13812345678",Role=2 });
        check((await Find(phone:"13812345678")).GetProperty("state").GetString()=="contact_admin","厂内重复手机号不能随意挑选账号");
    }
    private sealed class Clock:TimeProvider { private DateTimeOffset now=DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow()=>now; public void Advance(TimeSpan time)=>now+=time; }
    private sealed class Directory:IReportIdentityDirectory
    {
        public List<SysUser> Users {get;}=new();
        public Task<List<SysUser>> FindAsync(long? factoryId,string? name,string? phone)=>Task.FromResult(Users.Where(u=>(factoryId==null || u.FactoryId==factoryId) && u.Status==1).Where(u=>phone==null?u.Name==name:phone.Length==6?u.Phone?.EndsWith(phone)==true:u.Phone==phone).ToList());
        public Task<SysUser?> GetAsync(long factoryId,long userId)=>Task.FromResult(Users.FirstOrDefault(u=>u.FactoryId==factoryId && u.Id==userId));
        public Task<string> FactoryNameAsync(long factoryId)=>Task.FromResult("同名测试厂");
        public Task<string> FactoryCodeAsync(long factoryId)=>Task.FromResult("F"+factoryId);
    }
}
