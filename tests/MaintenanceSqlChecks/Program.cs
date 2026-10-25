using System.Reflection;
using WorkshopMes.Tests;
using Xunit;

// 直接执行真实xUnit断言，避免此电脑VSTest宿主监视父进程时的Win32拒绝访问。
var passed = 0;
var failed = 0;
foreach (var type in new[] { typeof(MaintenanceSqlTests), typeof(DiagSqlServiceTests), typeof(McpDataAccessTests) })
{
    foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() != null))
    {
        var data = method.GetCustomAttributes<InlineDataAttribute>().Select(a => a.GetData(method).Single()).ToList();
        if (method.GetCustomAttribute<TheoryAttribute>() == null) data.Add(Array.Empty<object>());
        foreach (var argsForTest in data)
        {
            try
            {
                var instance = Activator.CreateInstance(type);
                var parameters = method.GetParameters();
                var converted = argsForTest.Select((value, index) => value == null ? null :
                    Convert.ChangeType(value, Nullable.GetUnderlyingType(parameters[index].ParameterType) ?? parameters[index].ParameterType)).ToArray();
                var result = method.Invoke(instance, converted);
                if (result is Task task) await task;
                if (instance is IDisposable disposable) disposable.Dispose();
                passed++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {type.Name}.{method.Name}: {(ex.InnerException ?? ex)}");
            }
        }
    }
}
Console.WriteLine($"Checks: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
