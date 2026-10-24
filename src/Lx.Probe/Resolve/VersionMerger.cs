using System.Text.Json.Nodes;

namespace Lx.Probe.Resolve;

// 把一个"子版本"（Fabric 的 profile）合并进原版 version.json：
// mainClass 用子版本的，libraries 和 arguments 两边拼起来。
public static class VersionMerger
{
    public static string Merge(string parentJson, string childJson)
    {
        JsonObject parent = JsonNode.Parse(parentJson)!.AsObject();
        JsonObject child = JsonNode.Parse(childJson)!.AsObject();

        string parentId = parent["id"]!.GetValue<string>();
        string childId = child["id"]!.GetValue<string>();

        parent["id"] = childId;
        parent["inheritsFrom"] = parentId;

        if (child["mainClass"] is JsonNode mainClass)
        {
            parent["mainClass"] = mainClass.DeepClone();
        }

        var libraries = new JsonArray();
        foreach (JsonNode? library in AsArray(parent["libraries"]))
        {
            libraries.Add(library?.DeepClone());
        }

        foreach (JsonNode? library in AsArray(child["libraries"]))
        {
            libraries.Add(library?.DeepClone());
        }

        parent["libraries"] = libraries;

        JsonObject arguments = parent["arguments"] as JsonObject ?? new JsonObject();
        foreach (string key in new[] { "jvm", "game" })
        {
            var merged = new JsonArray();
            foreach (JsonNode? argument in AsArray(arguments[key]))
            {
                merged.Add(argument?.DeepClone());
            }

            foreach (JsonNode? argument in AsArray(child["arguments"]?[key]))
            {
                merged.Add(argument?.DeepClone());
            }

            arguments[key] = merged;
        }

        parent["arguments"] = arguments;

        return parent.ToJsonString();
    }

    private static JsonArray AsArray(JsonNode? node) => node as JsonArray ?? new JsonArray();
}
