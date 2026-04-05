using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.SecurityAdministration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var arguments = args.ToList();
if (arguments.Count == 0 || string.Equals(arguments[0], "--help", StringComparison.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

if (!string.Equals(arguments[0], "populate-fake-data", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Unknown command '{arguments[0]}'.");
    PrintUsage();
    return 1;
}

var dataRoot = GetOption(arguments, "--data-root")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrassLedger", "App_Data");

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Storage:DataRoot"] = dataRoot
    })
    .Build();

var services = new ServiceCollection();
services.AddBrassLedgerInfrastructure(configuration, AppContext.BaseDirectory, seedSampleData: false);

await using var serviceProvider = services.BuildServiceProvider();
await serviceProvider.InitializeBrassLedgerAsync();

var seeder = serviceProvider.GetRequiredService<IFakeDataPopulationService>();
var result = await seeder.PopulateAsync();

Console.WriteLine($"Fake data loaded into: {dataRoot}");
Console.WriteLine($"Customers added: {result.CustomersAdded}");
Console.WriteLine($"Invoices added: {result.InvoicesAdded}");
Console.WriteLine($"Vendors added: {result.VendorsAdded}");
Console.WriteLine($"Bills added: {result.BillsAdded}");
Console.WriteLine($"Inventory items added: {result.ItemsAdded}");
Console.WriteLine($"Employees added: {result.EmployeesAdded}");
Console.WriteLine($"Sales orders added: {result.OrdersAdded}");
Console.WriteLine($"Purchase orders added: {result.PurchaseOrdersAdded}");
Console.WriteLine($"Projects added: {result.ProjectsAdded}");
Console.WriteLine($"Journal entries added: {result.JournalEntriesAdded}");
Console.WriteLine($"Operator accounts added: {result.UsersAdded}");
return 0;

static string? GetOption(IReadOnlyList<string> arguments, string optionName)
{
    for (var index = 0; index < arguments.Count; index++)
    {
        var argument = arguments[index];
        if (string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase))
        {
            return index + 1 < arguments.Count ? arguments[index + 1] : null;
        }

        if (argument.StartsWith($"{optionName}=", StringComparison.OrdinalIgnoreCase))
        {
            return argument[(optionName.Length + 1)..];
        }
    }

    return null;
}

static void PrintUsage()
{
    Console.WriteLine("BrassLedger Tools");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  populate-fake-data [--data-root <path>]");
}
