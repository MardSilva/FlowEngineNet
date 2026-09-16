using System.Text.Json;

var arguments = args.ToList();
var mode = arguments.Count > 0 ? arguments[0] : "success";
arguments.RemoveAt(0);

if (arguments.Contains("--version", StringComparer.Ordinal))
{
    Console.WriteLine(mode == "incompatible" ? "EPUBCheck v4.2.6" : "EPUBCheck v5.4.0");
    return 0;
}

if (mode is "timeout" or "cancel")
{
    await Task.Delay(TimeSpan.FromMinutes(5));
    return 0;
}

if (mode == "excessive")
{
    Console.Write(new string('X', 64 * 1024));
    return 0;
}

if (mode == "unrecognized")
{
    Console.WriteLine("not-json");
    return 0;
}

var epubPath = arguments.LastOrDefault() ?? string.Empty;
if (mode == "require-spaces" && !epubPath.Contains(' '))
{
    Console.Error.WriteLine("The EPUB argument did not preserve spaces.");
    return 2;
}

var failed = mode == "failure";
var report = new
{
    messages = failed
        ? new[]
        {
            new
            {
                ID = "RSC-001",
                severity = "ERROR",
                message = "A declared resource is missing.",
                locations = new[] { new { path = "EPUB/missing.xhtml", line = -1, column = -1 } },
            },
        }
        : [],
    checker = new
    {
        path = epubPath,
        checkerVersion = "5.4.0",
        checkDate = "not-deterministic",
        elapsedTime = 123,
        nFatal = 0,
        nError = failed ? 1 : 0,
        nWarning = 0,
        nUsage = 0,
    },
    publication = new { },
    items = Array.Empty<object>(),
};

Console.Write(JsonSerializer.Serialize(report));
Console.Error.Write($"checked: {epubPath}");
return failed ? 1 : 0;
