using System.Diagnostics;
using System.Text;
using PaletteDictionary.Core;

Console.OutputEncoding = Encoding.UTF8;
try
{
    if (args.Length >= 2 && args[0] == "inspect")
    {
        var watch = Stopwatch.StartNew();
        var words = MdxHeadwordReader.Read(args[1]);
        Console.WriteLine(UiText.Format("CliReadHeadwords", words.Length, watch.Elapsed.TotalMilliseconds));
        foreach (var word in words.Take(30)) Console.WriteLine(word);
        return 0;
    }
    if (args.Length >= 2 && args[0] == "search")
    {
        var configPath = args[1];
        using var service = new DictionaryService(configPath, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "cache"));
        var watch = Stopwatch.StartNew();
        await service.ReloadAsync();
        var snapshot = service.Snapshot ?? throw new InvalidDataException(service.LastError ?? UiText.Get("IndexLoadFailed"));
        Console.WriteLine(UiText.Format("CliLoadIndex", watch.Elapsed.TotalMilliseconds));
        foreach (var warning in snapshot.Warnings) Console.Error.WriteLine(warning);
        var group = snapshot.Groups[snapshot.Configuration.DefaultGroup];
        var query = args.Length > 2 ? args[2] : "";
        if (query.Length == 0) { Console.Write(UiText.Get("QueryPrompt")); query = Console.ReadLine() ?? ""; }
        watch.Restart();
        var results = group.Index.Search(query, snapshot.Configuration.MaxResults, snapshot.Configuration.FuzzyMatching, snapshot.Configuration.MaxEditDistance);
        Console.WriteLine(UiText.Format("CliSearch", group.Index.Count, watch.Elapsed.TotalMilliseconds));
        foreach (var result in results) Console.WriteLine($"{result.Word}\t{result.Kind}\t{string.Join(" · ", result.Dictionaries)}");
        return 0;
    }
    if (args.Length >= 3 && args[0] == "open")
    {
        var config = PluginConfiguration.Load(args[1]);
        var group = config.Groups.Single(g => g.Name == config.DefaultGroup);
        GoldenDictLauncher.Open(config.GoldenDictPath, args[2], group.GoldenDictGroup);
        return 0;
    }
    Console.WriteLine("PaletteDictionary.Cli inspect <dictionary.mdx>\nPaletteDictionary.Cli search <config.json> [word]\nPaletteDictionary.Cli open <config.json> <word>");
    return args.Length == 0 ? 0 : 2;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
