using System.Text.Json.Serialization;
using CutLocal.Domain;

namespace CutLocal.Persistence;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ApplicationSettings))]
[JsonSerializable(typeof(ProcessingJob))]
internal sealed partial class PersistenceJsonContext : JsonSerializerContext;
