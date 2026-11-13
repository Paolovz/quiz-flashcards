using System.IO;
using System.Text.Json;
using Recall.Models;

namespace Recall.Services;

public sealed class Store
{
    private static readonly Lazy<Store> _instance = new(() => new Store());
    public static Store Instance => _instance.Value;

    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public AppData Data { get; private set; } = new();

    private Store()
    {
        // RECALL_DATA_DIR keeps a second profile (or a test run) away from the real one.
        var dir = Environment.GetEnvironmentVariable("RECALL_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Recall");

        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "data.json");
        Load();
    }

    public string DataPath => _path;

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var text = File.ReadAllText(_path);
                Data = JsonSerializer.Deserialize<AppData>(text, _json) ?? new AppData();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable store: start clean rather than crash on launch.
            var backup = _path + ".broken";
            try { File.Copy(_path, backup, true); } catch { /* best effort */ }
            Data = new AppData();
        }

        if (Data.Decks.Count == 0)
            Data.Decks.AddRange(Seed.Build());
    }

    public void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, _json));
        File.Move(tmp, _path, true);
    }

    private static string Key(DateTime day) => day.ToString("yyyy-MM-dd");

    public void LogReview(int count = 1)
    {
        var k = Key(DateTime.Today);
        Data.ReviewsByDay[k] = Data.ReviewsByDay.GetValueOrDefault(k) + count;
        Save();
    }

    public int ReviewsToday => Data.ReviewsByDay.GetValueOrDefault(Key(DateTime.Today));

    public int Streak()
    {
        var day = DateTime.Today;
        if (Data.ReviewsByDay.GetValueOrDefault(Key(day)) == 0) day = day.AddDays(-1);
        var n = 0;
        while (Data.ReviewsByDay.GetValueOrDefault(Key(day)) > 0)
        {
            n++;
            day = day.AddDays(-1);
        }
        return n;
    }

    public int DueTotal => Data.Decks.Sum(d => d.DueCount);

    public IEnumerable<Card> AllDue()
        => Data.Decks.SelectMany(d => d.Cards).Where(c => c.IsDue(DateTime.Now));

    public IEnumerable<Card> AllCards() => Data.Decks.SelectMany(d => d.Cards);
}
