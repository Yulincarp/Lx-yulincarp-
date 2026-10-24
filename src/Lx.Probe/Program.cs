using System.Text.Json;
using Lx.Probe.Model;

string text = File.ReadAllText("samples/1.20.4.json");

var options = new JsonSerializerOptions();
options.PropertyNameCaseInsensitive = true;

VersionJson v = JsonSerializer.Deserialize<VersionJson>(text, options)!;

Console.WriteLine($"id = {v.Id}");
Console.WriteLine($"mainClass = {v.MainClass}");
Console.WriteLine($"assets = {v.Assets}");
Console.WriteLine($"java = {v.JavaVersion?.MajorVersion}");

// Step 3：只打印数量（1.20.4 应为 88）和前 3 个 name。
List<Library>? libraries = v.Libraries;
Console.WriteLine($"libraries = {libraries?.Count ?? 0}");
if (libraries is { Count: > 0 })
{
    foreach (Library lib in libraries.Take(3))
    {
        Console.WriteLine($"  name = {lib.Name}");
    }
}