using System;
using System.Linq;
using System.Reflection;
using Xunit;
using WorkshopMes.Tests;
var suite = new SupportDocumentSearchTests();
int count = 0;
foreach (var m in typeof(SupportDocumentSearchTests).GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() != null || m.GetCustomAttribute<TheoryAttribute>() != null)) {
 var cases = m.GetCustomAttributes<InlineDataAttribute>().Select(a => a.GetData(m).Single()).ToList();
 if (cases.Count == 0) cases.Add(Array.Empty<object>());
 foreach (var data in cases) { try { m.Invoke(suite, data); count++; } catch (TargetInvocationException ex) { throw ex.InnerException!; } }
}
Console.WriteLine($"PASS: {count} real SupportDocumentSearchTests cases (direct invocation)");
