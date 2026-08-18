using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;

// Deletes this project's own RabbitMQ queues/exchanges and its OrderSagaDb Mongo database.
// Only names matching the project's known prefixes are touched, so it's safe to run
// against a shared broker/vhost. Run with --yes to actually delete; otherwise it only
// lists what would be removed.

var managementUrl = "http://localhost:15672";
var username = "admin";
var password = "Asdf1234";
var vhost = "/";
var execute = args.Contains("--yes");

var mongoConnectionString = "mongodb://localhost:27017";
var mongoDatabaseName = "OrderSagaDb";

// Queue/exchange names this solution owns. MassTransit auto-names exchanges after the
// fully-qualified message type (e.g. "Application.Messaging.Command:CheckInventory")
// and the saga receive endpoint after the kebab-cased state type ("order-saga-state"),
// so we match by substring rather than hardcoding every generated name.
string[] queuePatterns =
[
    "inventory-queue",
    "payment-queue",
    "notification-queue",
    "OrderSagaState",
];

string[] exchangePatterns =
[
    "Application.Messaging",
    "OrderSagaState",
    "OrderSaga",
    "inventory-queue",
    "payment-queue",
    "notification-queue",
    "MassTransit:Fault"
];

using var http = new HttpClient { BaseAddress = new Uri(managementUrl) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
    "Basic",
    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}")));

var encodedVhost = Uri.EscapeDataString(vhost);

var queues = await GetMatching($"/api/queues/{encodedVhost}", queuePatterns);
var exchanges = await GetMatching($"/api/exchanges/{encodedVhost}", exchangePatterns);

Console.WriteLine($"Matched {queues.Count} queue(s):");
foreach (var q in queues) Console.WriteLine($"  - {q}");

Console.WriteLine($"Matched {exchanges.Count} exchange(s):");
foreach (var e in exchanges) Console.WriteLine($"  - {e}");

Console.WriteLine($"Mongo database to drop: {mongoDatabaseName}");

if (!execute)
{
    Console.WriteLine("\nDry run only. Re-run with --yes to delete the items listed above.");
    return;
}

foreach (var q in queues)
{
    var resp = await http.DeleteAsync($"/api/queues/{encodedVhost}/{Uri.EscapeDataString(q)}");
    Console.WriteLine($"DELETE queue {q}: {(int)resp.StatusCode}");
}

foreach (var e in exchanges)
{
    var resp = await http.DeleteAsync($"/api/exchanges/{encodedVhost}/{Uri.EscapeDataString(e)}");
    Console.WriteLine($"DELETE exchange {e}: {(int)resp.StatusCode}");
}

var mongoClient = new MongoClient(mongoConnectionString);
Console.WriteLine($"DROP database {mongoDatabaseName}");
await mongoClient.DropDatabaseAsync(mongoDatabaseName);

async Task<List<string>> GetMatching(string path, string[] patterns)
{
    var resp = await http.GetAsync(path);
    resp.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    var names = new List<string>();

    foreach (var item in doc.RootElement.EnumerateArray())
    {
        var name = item.GetProperty("name").GetString();
        if (name is null || name.StartsWith("amq.")) continue;

        if (patterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
            names.Add(name);
    }

    return names;
}
