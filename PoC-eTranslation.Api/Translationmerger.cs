using System.Text.Json.Nodes;

/// <summary>
/// Voegt vertalingen uit het door eTranslation vertaalde bestand samen met het ORIGINELE bestand.
/// Alleen waarden op plekken waar het origineel een brontaal-sleutel (bijv. "nl-NL") heeft worden
/// overgenomen, onder een nieuwe taalsleutel (bijv. "de-DE"). Alles wat eTranslation verder
/// aangepast heeft (baseLanguage, code-velden, bestaande en-GB-teksten) wordt genegeerd.
/// </summary>
public static class TranslationMerger
{
    public static JsonNode Merge(JsonNode original, JsonNode translated, string sourceKey, string targetKey)
    {
        var result = original.DeepClone();
        MergeNode(result, translated, sourceKey, targetKey);

        // Voeg de nieuwe taal toe aan project.extraLanguages (als die lijst bestaat en de taal er nog niet in staat).
        if (result["project"]?["extraLanguages"] is JsonArray langs &&
            !langs.Any(l => l?.GetValue<string>() == targetKey))
        {
            langs.Add(targetKey);
        }

        return result;
    }

    private static void MergeNode(JsonNode? original, JsonNode? translated, string sourceKey, string targetKey)
    {
        if (original is JsonObject o && translated is JsonObject t)
        {
            // Dit object is een "taalveld": heeft het een niet-lege brontekst, neem dan de vertaling over.
            if (o[sourceKey] is JsonValue sv && sv.TryGetValue<string>(out var source) &&
                !string.IsNullOrWhiteSpace(source) &&
                t[sourceKey] is JsonValue tv && tv.TryGetValue<string>(out var translatedText))
            {
                o[targetKey] = translatedText;
            }

            // Ga door naar de onderliggende velden (kopie van de sleutels, want we passen o aan).
            foreach (var key in o.Select(p => p.Key).ToList())
            {
                if (key == sourceKey || key == targetKey) continue;
                MergeNode(o[key], t[key], sourceKey, targetKey);
            }
        }
        else if (original is JsonArray oa && translated is JsonArray ta)
        {
            for (var i = 0; i < Math.Min(oa.Count, ta.Count); i++)
                MergeNode(oa[i], ta[i], sourceKey, targetKey);
        }
    }
}