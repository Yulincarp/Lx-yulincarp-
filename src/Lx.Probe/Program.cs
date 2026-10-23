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
Console.WriteLine($"libraries = {v.Libraries?.Count}");