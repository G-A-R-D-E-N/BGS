using System;
using System.Collections.Generic;
using System.Linq;

namespace BehaviourStudio.App;

public static class AssistantModelPrefs
{
    public const int MaximumRecents = 6;
    private const string FavoritesKey = "assistant.model_favorites";
    private const string RecentsKey = "assistant.model_recents";
    private const char Separator = '|';

    public static IReadOnlyList<string> Favorites() => Read(FavoritesKey);

    public static bool IsFavorite(string model) =>
        model.Length > 0 && Favorites().Contains(model, StringComparer.Ordinal);

    public static void ToggleFavorite(string model)
    {
        if (model.Length == 0) return;
        List<string> favorites = Favorites().ToList();
        int index = favorites.IndexOf(model);
        if (index >= 0) favorites.RemoveAt(index);
        else favorites.Add(model);
        Write(FavoritesKey, favorites);
    }

    public static IReadOnlyList<string> Recents() => Read(RecentsKey);

    public static void Remember(string model)
    {
        if (model.Length == 0) return;
        List<string> recents = Recents().ToList();
        recents.RemoveAll(item => string.Equals(item, model, StringComparison.Ordinal));
        recents.Insert(0, model);
        if (recents.Count > MaximumRecents) recents.RemoveRange(MaximumRecents, recents.Count - MaximumRecents);
        Write(RecentsKey, recents);
    }

    internal static IReadOnlyList<string> Read(string key)
    {
        string stored = Settings.Get(key);
        if (stored.Length == 0) return Array.Empty<string>();
        return stored
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static void Write(string key, IReadOnlyList<string> values) =>
        Settings.TrySet(key, string.Join(Separator, values), out _);
}
