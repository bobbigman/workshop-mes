using WorkshopMes.Tests;
var unit = new ReportPriceSnapshotTests();
unit.RuleMutation_PreservesTypePriceAndDeduction(1,24);
unit.RuleMutation_PreservesTypePriceAndDeduction(2,6);
unit.RuleMutation_PreservesTypePriceAndDeduction(3,3);
foreach (var value in new string[] { null, "", "null", "{}", "broken" }) unit.MissingOrCorruptSnapshot_ExplicitlyRejects(value);
unit.ZeroPrice_IsValid();
Console.WriteLine("PASS: 9 snapshot unit cases");
var methods = typeof(TimedWageTests).GetMethods().Where(m => m.GetCustomAttributes(typeof(Xunit.FactAttribute),true).Length > 0 && m.GetParameters().Length == 0).ToList();
var failures = 0;
foreach(var method in methods) {
 var test = new TimedWageTests();
 try { await test.InitializeAsync(); await (Task)method.Invoke(test,null); Console.WriteLine("PASS: " + method.Name); }
 catch(Exception ex) { failures++; Console.WriteLine("FAIL: " + method.Name + " " + (ex.InnerException ?? ex)); }
 finally { await test.DisposeAsync(); }
}
Console.WriteLine($"SQL Server cases: {methods.Count}, failed: {failures}");
return failures == 0 ? 0 : 1;
