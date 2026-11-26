using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SeaHunter.Game;

// One line on the high score list
public record HighScoreEntry(string Name, int Score);

// Saves and loads the top scores as JSON in the user's AppData folder
public static class HighScoreStore
{
    public const int MaxEntries = 15;
    public const int MaxNameLength = 12;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SeaHunter-PeriscopeUp",
        "highscores.json");

    public static List<HighScoreEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            string json = File.ReadAllText(FilePath);

            // New format: names and scores
            try
            {
                var entries = JsonSerializer.Deserialize<List<HighScoreEntry>>(json);
                if (entries != null) return Sorted(entries);
            }
            catch (JsonException)
            {
                // Not the new format, try the old one below
            }

            // Old format: just a list of numbers. Keep them, without names.
            var oldScores = JsonSerializer.Deserialize<List<int>>(json) ?? new();
            return Sorted(oldScores.Select(s => new HighScoreEntry("---", s)));
        }
        catch
        {
            return new(); // Corrupt or unreadable file: start fresh
        }
    }

    // True if this score is good enough for the list
    public static bool Qualifies(int score)
    {
        if (score <= 0) return false;
        var top = Load();
        return top.Count < MaxEntries || score > top[^1].Score;
    }

    // Adds a score with a name, keeps only the best ones, saves and returns the new list
    public static List<HighScoreEntry> Add(string name, int score)
    {
        name = CleanName(name);
        var top = Sorted(Load().Append(new HighScoreEntry(name, score)));
        Save(top);
        return top;
    }

    // Upper case, max 12 letters, never empty
    public static string CleanName(string? name)
    {
        name = (name ?? "").Trim().ToUpperInvariant();
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];
        return name.Length == 0 ? "PLAYER" : name;
    }

    // Always returns MaxEntries lines, with "-" for empty slots: " 1. RONALD       001200"
    public static List<string> Format(List<HighScoreEntry> entries)
    {
        var lines = new List<string>();
        for (int i = 0; i < MaxEntries; i++)
        {
            if (i < entries.Count)
                lines.Add($"{i + 1,2}. {entries[i].Name,-MaxNameLength} {entries[i].Score:D6}");
            else
                lines.Add($"{i + 1,2}. {"",-MaxNameLength} ------");
        }
        return lines;
    }

    private static List<HighScoreEntry> Sorted(IEnumerable<HighScoreEntry> entries) =>
        entries.OrderByDescending(e => e.Score).Take(MaxEntries).ToList();

    private static void Save(List<HighScoreEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
        }
        catch
        {
            // Saving failed (e.g. no write access): the game keeps running anyway
        }
    }
}
