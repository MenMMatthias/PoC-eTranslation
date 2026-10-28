# PoC-eTranslation

Proof of concept voor het vertalen van een JSON-beslisboom met eTranslation.

## Projectstructuur

- `PoC-eTranslation.Api/` bevat de ASP.NET Core API, callback-afhandeling en het samenvoegen van vertalingen.
- `data/` bevat het JSON-invoerbestand voor de proef.

De oude console-app is verwijderd; de API is het enige applicatieproject.

## Starten

Start de API met:

```powershell
dotnet run --project PoC-eTranslation.Api\PoC-eTranslation.Api.csproj
```

Configureer de benodigde `ETranslation:*`- en `Poc:*`-instellingen via user secrets of omgevingsvariabelen. De vereiste instellingen staan als toelichting bovenaan `PoC-eTranslation.Api/Program.cs`.
