# PoC-eTranslation

Proof of concept voor het vertalen van een JSON-beslisboom met eTranslation.

## Projectstructuur

- `PoC-eTranslation.Api/` bevat de ASP.NET Core API, callback-afhandeling en het samenvoegen van vertalingen.

## Starten

Start de API met:

```powershell
dotnet run --project PoC-eTranslation.Api\PoC-eTranslation.Api.csproj
```

Configureer de benodigde `ETranslation:*`- en `Poc:*`-instellingen via user secrets of omgevingsvariabelen. De vereiste instellingen staan als toelichting bovenaan `PoC-eTranslation.Api/Program.cs`.

## callback api 
- dev tunnel opzetten om te zorgen dat de callback via localhost kan worden opgehaald:
1. In VS ga naar view, other windows en dev tunnel.
2. Maak een nieuwe tunnel aan en login met Github
3. Geef de tunnel een naam
4. Tunnel type op persistent
5. Acces op public zodat de api toegang heeft.
6. Run de code en selecteer de tunnel door op het groene pijltje naast runnen en ga naar dev tunnel en selecteer de gemaakte tunnel 
