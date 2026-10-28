using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

// PoC: kies een taal op een webpagina, stuur het JSON-bestand naar eTranslation,
// ontvang het resultaat via callbacks en voeg het samen met het origineel.
//
// Configuratie (user secrets of omgevingsvariabelen, nooit in de broncode):
//   ETranslation:Username      applicatienaam van eTranslation
//   ETranslation:Password      wachtwoord van eTranslation
//   ETranslation:Institution   bijv. "NVWA"
//   ETranslation:Department    afdelingsnummer (zoals de documentatie vraagt)
//   ETranslation:CallerUser    jouw naam voor callerInformation
//   Poc:PublicBaseUrl          publieke URL van deze app (dev tunnel), zonder slash aan het eind
//   Poc:InputFile              pad naar je JSON, bijv. beslisboom-testboom-minimaal.json
//   Poc:DocumentFormat         standaard json

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var cfg = app.Configuration;

string Req(string key) => cfg[key] ?? throw new InvalidOperationException($"Ontbrekende configuratie: {key}");

var outDir = Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outDir);

var jobs = new ConcurrentDictionary<string, Job>();

// eTranslation-code, taalsleutel in de databeheer-JSON (AANNAME: controleer de echte sleutels!), eigen taalnaam.
// Nederlands is de brontaal en staat er dus niet in. Samen 25 talen (zie F01).
var languages = new Language[]
{
    new("BG", "bg-BG", "Български"), new("CS", "cs-CZ", "Čeština"), new("DA", "da-DK", "Dansk"),
    new("DE", "de-DE", "Deutsch"), new("EL", "el-GR", "Ελληνικά"), new("EN", "en-GB", "English"),
    new("ES", "es-ES", "Español"), new("ET", "et-EE", "Eesti"), new("FI", "fi-FI", "Suomi"),
    new("FR", "fr-FR", "Français"), new("GA", "ga-IE", "Gaeilge"), new("HR", "hr-HR", "Hrvatski"),
    new("HU", "hu-HU", "Magyar"), new("IT", "it-IT", "Italiano"), new("LT", "lt-LT", "Lietuvių"),
    new("LV", "lv-LV", "Latviešu"), new("MT", "mt-MT", "Malti"), new("PL", "pl-PL", "Polski"),
    new("PT", "pt-PT", "Português"), new("RO", "ro-RO", "Română"), new("SK", "sk-SK", "Slovenčina"),
    new("SL", "sl-SI", "Slovenščina"), new("SV", "sv-SE", "Svenska"), new("UK", "uk-UA", "Українська"),
    new("ZH", "zh-CN", "中文")
};

var options = string.Join("\n", languages.Select(l =>
    $"<option value=\"{l.Code}\" lang=\"{l.Code.ToLowerInvariant()}\">{WebUtility.HtmlEncode(l.Name)} ({l.Code})</option>"));

var page = """
<!doctype html>
<html lang="nl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Vertaal-PoC</title>
<style>
  body { font: 16px/1.5 system-ui, sans-serif; max-width: 32rem; margin: 3rem auto; padding: 0 1rem; color: #1b1f23; }
  h1 { font-size: 1.4rem; }
  label { display: block; font-weight: 600; margin-bottom: .4rem; }
  select, button { font: inherit; padding: .5rem .75rem; }
  select { width: 100%; }
  button { margin-top: 1rem; background: #154273; color: #fff; border: 0; border-radius: 4px; cursor: pointer; }
  button:disabled { opacity: .6; cursor: default; }
  :focus-visible { outline: 3px solid #f5a300; outline-offset: 2px; }
  #status { margin-top: 1.5rem; min-height: 3rem; }
</style>
</head>
<body>
<h1>Beslisboom vertalen</h1>
<label for="taal">Taal</label>
<select id="taal">
{{OPTIONS}}
</select>
<button id="start" type="button">Vertalen</button>
<div id="status" role="status" aria-live="polite"></div>
<script>
  const btn = document.getElementById('start');
  const sel = document.getElementById('taal');
  const out = document.getElementById('status');

  btn.addEventListener('click', async () => {
    btn.disabled = true;
    out.textContent = 'Aanvraag versturen...';
    try {
      const r = await fetch('/api/translate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ language: sel.value })
      });
      if (!r.ok) throw new Error(await r.text());
      const { id } = await r.json();
      poll(id);
    } catch (e) {
      out.textContent = 'Mislukt: ' + e.message;
      btn.disabled = false;
    }
  });

  async function poll(id) {
    const r = await fetch('/api/status/' + id);
    if (!r.ok) { out.textContent = 'Status niet gevonden. Start opnieuw.'; btn.disabled = false; return; }
    const s = await r.json();
    if (s.status === 'Klaar') {
      const a = document.createElement('a');
      a.href = s.downloadUrl;
      a.textContent = 'Download ' + s.fileName;
      out.textContent = 'Klaar. ';
      out.append(a);
      btn.disabled = false;
      return;
    }
    if (s.status === 'Mislukt') {
      out.textContent = 'Mislukt: ' + (s.message || 'onbekende fout');
      btn.disabled = false;
      return;
    }
    out.textContent = 'Bezig met vertalen... (' + s.status + ')';
    setTimeout(() => poll(id), 2000);
  }
</script>
</body>
</html>
""".Replace("{{OPTIONS}}", options);

// --- Pagina -----------------------------------------------------------------------------------

app.MapGet("/", () => Results.Content(page, "text/html; charset=utf-8"));

// --- Trigger: stuur het bestand naar eTranslation ---------------------------------------------

app.MapPost("/api/translate", async (TranslateRequest body) =>
{
    var lang = languages.FirstOrDefault(l => l.Code == body.Language);
    if (lang is null) return Results.BadRequest("Onbekende taal.");

    var inputFile = Req("Poc:InputFile");
    var baseUrl = Req("Poc:PublicBaseUrl").TrimEnd('/');

    var job = new Job { Id = Guid.NewGuid().ToString(), Code = lang.Code, Key = lang.Key, Status = "Versturen" };
    jobs[job.Id] = job;

    var payload = new
    {
        callerInformation = new
        {
            externalReference = job.Id,
            institution = cfg["ETranslation:Institution"] ?? "NVWA",
            departmentNumber = cfg["ETranslation:Department"] ?? "",
            username = cfg["ETranslation:CallerUser"] ?? ""
        },
        documentToTranslate = new
        {
            document = new
            {
                content = Convert.ToBase64String(await File.ReadAllBytesAsync(inputFile)),
                format = cfg["Poc:DocumentFormat"] ?? "json",
                filename = Path.GetFileNameWithoutExtension(inputFile)
            }
        },
        sourceLanguage = "NL",
        targetLanguages = new[] { lang.Code },
        domain = "GEN",
        notifications = new
        {
            success = new { http = $"{baseUrl}/callback/success" },
            failure = new { http = $"{baseUrl}/callback/failure" }
        },
        deliveries = new { http = $"{baseUrl}/callback/delivery" }
    };

    var handler = new HttpClientHandler
    {
        Credentials = new NetworkCredential(Req("ETranslation:Username"), Req("ETranslation:Password"))
    };
    using var http = new HttpClient(handler);
    var request = new HttpRequestMessage(HttpMethod.Post,
        "https://language-tools.ec.europa.eu/etranslation/api/askTranslate")
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"askTranslate ({lang.Code}) -> {(int)response.StatusCode}: {text}");

    if (!response.IsSuccessStatusCode)
    {
        job.Status = "Mislukt";
        job.Message = $"eTranslation gaf {(int)response.StatusCode}: {text}";
        return Results.Ok(new { id = job.Id });
    }

    try
    {
        job.RequestId = JsonDocument.Parse(text).RootElement.GetProperty("requestId").GetInt64();
        job.Status = "Wachten op eTranslation";
    }
    catch (Exception ex)
    {
        job.Status = "Mislukt";
        job.Message = $"Onverwacht antwoord van eTranslation: {text} ({ex.Message})";
    }
    return Results.Ok(new { id = job.Id });
});

// --- Status en download voor de pagina --------------------------------------------------------

app.MapGet("/api/status/{id}", (string id) =>
    jobs.TryGetValue(id, out var job)
        ? Results.Ok(new
        {
            status = job.Status,
            message = job.Message,
            fileName = job.FilePath is null ? null : Path.GetFileName(job.FilePath),
            downloadUrl = job.FilePath is null ? null : $"/api/result/{job.Id}"
        })
        : Results.NotFound());

app.MapGet("/api/result/{id}", (string id) =>
    jobs.TryGetValue(id, out var job) && job.FilePath is not null && File.Exists(job.FilePath)
        ? Results.File(job.FilePath, "application/json", Path.GetFileName(job.FilePath))
        : Results.NotFound());

// --- Callbacks van eTranslation ---------------------------------------------------------------

async Task<byte[]> ReadBody(HttpRequest req)
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    return ms.ToArray();
}

Job? FindJob(JsonElement root)
{
    if (root.TryGetProperty("requestId", out var rid) && rid.TryGetInt64(out var id))
        return jobs.Values.FirstOrDefault(j => j.RequestId == id);
    if (root.TryGetProperty("externalReference", out var er) && er.GetString() is { } extRef)
        return jobs.GetValueOrDefault(extRef);
    return null;
}

app.MapPost("/callback/success", async (HttpRequest req) =>
{
    var bytes = await ReadBody(req);
    Console.WriteLine($"success: {Encoding.UTF8.GetString(bytes)}");
    try
    {
        var job = FindJob(JsonDocument.Parse(bytes).RootElement);
        if (job is not null && job.Status != "Klaar") job.Status = "Vertaald, bestand onderweg";
    }
    catch (JsonException) { }
    return Results.Ok();
});

app.MapPost("/callback/failure", async (HttpRequest req) =>
{
    var bytes = await ReadBody(req);
    var raw = Encoding.UTF8.GetString(bytes);
    Console.WriteLine($"failure: {raw}");
    try
    {
        var job = FindJob(JsonDocument.Parse(bytes).RootElement);
        if (job is not null)
        {
            job.Status = "Mislukt";
            job.Message = raw.Length > 500 ? raw[..500] : raw;
        }
    }
    catch (JsonException) { }
    return Results.Ok();
});

app.MapPost("/callback/delivery", async (HttpRequest req) =>
{
    var bytes = await ReadBody(req);
    // Bewaar altijd het ruwe antwoord, handig om te debuggen.
    await File.WriteAllBytesAsync(Path.Combine(outDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-delivery.bin"), bytes);

    var doc = JsonDocument.Parse(bytes);
    var job = FindJob(doc.RootElement);
    if (job is null)
    {
        Console.WriteLine("delivery ontvangen, maar geen bijbehorende aanvraag gevonden.");
        return Results.Ok();
    }

    try
    {
        var translatedBytes = Convert.FromBase64String(doc.RootElement.GetProperty("result").GetString()!);
        var original = JsonNode.Parse(await File.ReadAllTextAsync(Req("Poc:InputFile")))!;
        var translated = JsonNode.Parse(Encoding.UTF8.GetString(translatedBytes))!;
        var merged = TranslationMerger.Merge(original, translated, "nl-NL", job.Key);

        var path = Path.Combine(outDir, $"beslisboom-{job.Key}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, merged.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));

        job.FilePath = path;
        job.Status = "Klaar";
        Console.WriteLine($"samengevoegd: {path}");
    }
    catch (Exception ex)
    {
        job.Status = "Mislukt";
        job.Message = $"Verwerken van het resultaat mislukte: {ex.Message}";
        Console.WriteLine(job.Message);
    }
    return Results.Ok();
});

app.Run();

record Language(string Code, string Key, string Name);
record TranslateRequest(string Language);

class Job
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";     // eTranslation-code, bijv. DE
    public string Key { get; set; } = "";      // taalsleutel in de JSON, bijv. de-DE
    public long? RequestId { get; set; }
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public string? FilePath { get; set; }
}