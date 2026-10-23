using System.Reflection;
using Xunit;
using WorkshopMes.Tests;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
if (args.Contains("real")) {
 var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("backend")).AddJsonFile("appsettings.json").AddEnvironmentVariables().Build();
 var opt = config.GetSection("Ai").Get<AiOptions>()!;
 opt.SupportDocs = new() { Enabled = true, RootPath = Path.GetFullPath("SupportDocs") };
 var options = Options.Create(opt);
 var services = new ServiceCollection().AddLogging().AddOptions();
 services.Configure<ErrorLoggingOptions>(_ => {});
 services.AddHttpClient(DeepSeekClient.HttpClientName, c => c.Timeout=TimeSpan.FromSeconds(90));
 using var provider=services.BuildServiceProvider();
 var client=new DeepSeekClient(provider.GetRequiredService<IHttpClientFactory>(),options,NullLogger<DeepSeekClient>.Instance, new OutboundHttpErrorLogger(NullLogger<OutboundHttpErrorLogger>.Instance,provider.GetRequiredService<IOptionsMonitor<ErrorLoggingOptions>>()));
 var docs=new SupportDocumentService(options,NullLogger<SupportDocumentService>.Instance);
 var tools=new AiToolDispatcher(null!,null!,null!,docs,null!,options,NullLogger<AiToolDispatcher>.Instance);
 var service=new AiAssistantService(options,client,new AiSessionStore(options),tools,null!,NullLogger<AiAssistantService>.Instance);
 foreach(var q in new[]{"账上多少钱","材料涨价按新价算","销售报价的时候，AI 能帮着算算料钱吗？"}) {
  var result=await service.SendMessageAsync(new(){Mode="support",Message=q},1,1,CancellationToken.None);
  Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{question=q,result.Data!.Answer,Sources=result.Data.Sources.Select(s=>new{s.SourceId,s.Title,s.Chapter}),result.Data.Warnings},new System.Text.Json.JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
 }
 return;
}
int count=0;
foreach(var type in new[]{typeof(AiSessionLifecycleTests),typeof(SupportDocumentSearchTests)}) {
 var suite=Activator.CreateInstance(type);
 foreach(var m in type.GetMethods().Where(m=>m.GetCustomAttribute<FactAttribute>()!=null||m.GetCustomAttribute<TheoryAttribute>()!=null)) {
  var cases=m.GetCustomAttributes<InlineDataAttribute>().Select(a=>a.GetData(m).Single()).ToList();
  if(cases.Count==0)cases.Add(Array.Empty<object>());
  foreach(var data in cases){try{var value=m.Invoke(suite,data);if(value is Task task)await task;count++;}catch(TargetInvocationException ex){throw ex.InnerException!;}}
 }
}
Console.WriteLine($"PASS: {count} real test cases via direct invocation");
