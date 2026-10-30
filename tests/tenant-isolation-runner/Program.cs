using System.Reflection;
using Xunit;

// 受限Windows环境无法让testhost监视父进程；在当前进程执行同一批Fact/InlineData断言。
var assembly = typeof(WorkshopMes.Tests.TenantIsolation426Tests).Assembly;
var passed = 0; var failed = 0;
foreach (var type in assembly.GetTypes().Where(t => t.IsPublic && t.Namespace == "WorkshopMes.Tests"))
foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() != null))
{
    var rows = method.GetCustomAttributes<InlineDataAttribute>().Select(a => a.GetData(method).Single()).ToList();
    if (rows.Count == 0) rows.Add(Array.Empty<object>());
    foreach (var row in rows)
    {
        var name = type.Name + "." + method.Name;
        try
        {
            var argsForMethod = row.Select((v, i) => v == null ? null : Convert.ChangeType(v,
                Nullable.GetUnderlyingType(method.GetParameters()[i].ParameterType) ?? method.GetParameters()[i].ParameterType)).ToArray();
            var instance = Activator.CreateInstance(type)!;
            var result = method.Invoke(instance, argsForMethod);
            if (result is Task task) await task;
            if (instance is IDisposable disposable) disposable.Dispose();
            passed++; Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            failed++; var error = ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException : ex;
            Console.WriteLine("FAIL " + name + " " + error);
        }
    }
}
Console.WriteLine($"RESULT passed={passed} failed={failed}");
return failed == 0 ? 0 : 1;
