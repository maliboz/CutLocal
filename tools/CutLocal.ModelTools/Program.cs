using System.Globalization;
using System.Text.Json;
using CutLocal.Domain;
using CutLocal.Inference;
using CutLocal.Persistence;

if (args.Length > 0 && args[0].Equals("providers", StringComparison.OrdinalIgnoreCase))
{
    IReadOnlyList<InferenceProviderDescriptor> providers =
        await new WindowsInferenceProviderCatalog().GetAllAsync(CancellationToken.None);
    foreach (InferenceProviderDescriptor provider in providers)
    {
        Console.WriteLine(
            $"{provider.Kind}\t{provider.Id}\tindex={provider.DeviceIndex?.ToString(CultureInfo.InvariantCulture) ?? "-"}"
            + $"\tdedicatedMiB={provider.DedicatedVideoMemoryBytes / 1024d / 1024d:F0}"
            + $"\t{provider.DisplayName}");
    }

    return 0;
}

if (args.Length > 0 && args[0].Equals("smoke", StringComparison.OrdinalIgnoreCase))
{
    return await ModelSmokeRunner.RunAsync(args[1..], CancellationToken.None);
}

string manifestRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets", "models", "manifests"));
bool commercialBuild = !args.Contains("--noncommercial", StringComparer.OrdinalIgnoreCase);

if (!Directory.Exists(manifestRoot))
{
    Console.Error.WriteLine($"Manifest directory does not exist: {manifestRoot}");
    return 2;
}

JsonSerializerOptions serializerOptions = new() { PropertyNameCaseInsensitive = true };
ModelManifestValidator validator = new();
int failures = 0;
string[] files = Directory.GetFiles(manifestRoot, "*.json", SearchOption.TopDirectoryOnly);
if (files.Length == 0)
{
    Console.Error.WriteLine("No model manifests were found.");
    return 3;
}

foreach (string file in files.Order(StringComparer.OrdinalIgnoreCase))
{
    try
    {
        await using FileStream stream = File.OpenRead(file);
        ModelDescriptor? descriptor = await JsonSerializer.DeserializeAsync<ModelDescriptor>(
            stream,
            serializerOptions);
        if (descriptor is null)
        {
            Console.Error.WriteLine($"FAIL {Path.GetFileName(file)}: empty manifest");
            failures++;
            continue;
        }

        IReadOnlyList<string> errors = validator.Validate(descriptor, commercialBuild);
        if (errors.Count == 0)
        {
            Console.WriteLine($"PASS {descriptor.Id} {descriptor.Version} {descriptor.License.Spdx}");
            continue;
        }

        Console.Error.WriteLine($"FAIL {Path.GetFileName(file)}: {string.Join("; ", errors)}");
        failures++;
    }
    catch (Exception exception) when (exception is JsonException or IOException)
    {
        Console.Error.WriteLine($"FAIL {Path.GetFileName(file)}: {exception.Message}");
        failures++;
    }
}

return failures == 0 ? 0 : 1;
