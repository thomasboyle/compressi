using System.Text.Json.Serialization;
using Compressi.Core.Models;

namespace Compressi_App.Services;

/// <summary>
/// Source-generated metadata for settings I/O. Reflection-based serialization cost ~30 ms of
/// cold start because it runs before the first frame.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
