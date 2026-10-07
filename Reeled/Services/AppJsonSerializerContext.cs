using System.Collections.Generic;
using System.Text.Json.Serialization;
using Reeled.Models;

namespace Reeled.Services;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<CachedClipMetadata>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
