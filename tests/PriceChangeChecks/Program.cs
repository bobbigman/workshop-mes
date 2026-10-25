using System.Reflection;
using WorkshopMes.Tests;
using Xunit;
using Microsoft.Data.SqlClient;

if(args.Contains("--cleanup-owned-test-databases"))
{
    await using var connection=new SqlConnection("Server=localhost;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Connect Timeout=8");
    await connection.OpenAsync();
    await using var query=new SqlCommand("SELECT name FROM sys.databases WHERE name LIKE 'WorkshopMes418Checks[_]%' AND create_date >= CONVERT(date,GETDATE())",connection);
    var names=new List<string>();
    await using(var reader=await query.ExecuteReaderAsync())while(await reader.ReadAsync())names.Add(reader.GetString(0));
    foreach(var name in names)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^WorkshopMes418Checks_[0-9a-f]{32}$"))throw new InvalidOperationException("Unexpected test database name");
        await using var drop=new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];",connection);
        await drop.ExecuteNonQueryAsync();Console.WriteLine($"Removed owned test database: {name}");
    }
    return 0;
}

// 直接执行真实xUnit断言，避免此电脑VSTest宿主监视父进程时的Win32拒绝访问。
var passed = 0;
var failed = 0;
var skipped = 0;
foreach (var type in new[] { typeof(PriceChangeTests) })
{
    foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() != null))
    {
        if(method.Name.EndsWith("_OnSqlServer") && Environment.GetEnvironmentVariable("PRICE_CHANGE_SQLSERVER")!="1"){skipped++;continue;}
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
Console.WriteLine($"Checks: {passed} passed, {failed} failed, {skipped} skipped");
return failed == 0 ? 0 : 1;
