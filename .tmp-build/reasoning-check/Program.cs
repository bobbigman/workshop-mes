using WorkshopMes.Tests;
var count = 0;
foreach (var method in typeof(AiSessionLifecycleTests).GetMethods().Where(m => m.GetCustomAttributes(typeof(Xunit.FactAttribute), true).Length > 0 && m.GetParameters().Length == 0))
{
    if (method.Invoke(new AiSessionLifecycleTests(), null) is Task task) await task;
    count++;
}
foreach (var text in new string[] { "  思考\n原文  ", "", null })
{
    await new DeepSeekReasoningTests().ResponseReasoning_RoundTripsInActualHttpPayload(text);
    count++;
}
Console.WriteLine($"PASS: {count} regression cases (direct assertion runner)");
