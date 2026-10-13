using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services.NasbNiro.Contracts;

// Builds the API's services (Startup.ConfigureServices) against the CI database, which the smoke test
// has already migrated, and runs queries that cannot be reached through parameterless GET requests.
// On an empty database a query that translates fine ends with the method's own "not found" exception.
var host = WebApi.Program.CreateHostBuilder(args).Build();
using var scope = host.Services.CreateScope();
var failures = 0;

async Task RunAsync(string name, Func<Task> action, string expectedMessage)
{
    try
    {
        await action();
        Console.WriteLine($"OK    {name}");
    }
    catch (Exception ex)
    {
        var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
        if (expectedMessage != null && inner.Message.Contains(expectedMessage))
        {
            Console.WriteLine($"OK    {name} (ended with the expected message: {inner.Message})");
        }
        else
        {
            failures++;
            Console.WriteLine($"FAIL  {name}: {inner.GetType().FullName}: {inner.Message}");
            Console.WriteLine(inner.ToString());
        }
    }
}

var buyOrders = scope.ServiceProvider.GetRequiredService<IBuyOrderService>();
var addProgressReport = buyOrders.GetType().GetMethod("AddProgressReportAsync", BindingFlags.Instance | BindingFlags.NonPublic);
if (addProgressReport == null)
{
    Console.WriteLine("FAIL  BuyOrderService.AddProgressReportAsync not found");
    return 1;
}

await RunAsync(
    "BuyOrderService.AddProgressReportAsync (progress report query)",
    () => (Task)addProgressReport.Invoke(buyOrders, new object[] { 1, CancellationToken.None }),
    "فرآیندی برای گزارش یافت نشد");

Console.WriteLine(failures == 0 ? "query-check: all passed" : $"query-check: {failures} failed");
return failures == 0 ? 0 : 1;
