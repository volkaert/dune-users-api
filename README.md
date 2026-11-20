# dune-users-api

API C# .NET 10 (FastEndpoints + PostgreSQL) exposant les opérations sur les users onboardés : récupération des users onboardés du jour, stockage des données KYC, etc.

## Stack

- .NET 10, [FastEndpoints](https://fast-endpoints.com)
- PostgreSQL 18, EF Core (Npgsql)
- Docker Compose (API + base)
- Tests : xUnit v3, FastEndpoints.Testing, Shouldly, Testcontainers

## Prérequis

- SDK .NET 10 (version figée dans `global.json`)
- Docker (Docker Desktop ou équivalent), **démarré** : nécessaire pour Docker Compose et pour les tests

## Lancer l'API

### Tout en Docker

```bash
cp .env.example .env   # optionnel : valeurs par défaut utilisées sinon
docker compose up --build
curl http://localhost:8080/health
# {"status":"Healthy","database":"Up"}
```

### API en local, base en Docker

```bash
docker compose up -d postgres
dotnet run --project src/DuneUsersApi
curl http://localhost:5080/health
```

En environnement `Development`, les migrations EF Core sont appliquées automatiquement au démarrage.

## Lancer les tests

```bash
dotnet test
```

Les tests d'intégration démarrent l'API en mémoire (`AppFixture` de FastEndpoints) et une vraie instance PostgreSQL via Testcontainers. Docker doit tourner.

## Migrations EF Core

`dotnet-ef` est déclaré comme outil local (`dotnet-tools.json`) :

```bash
dotnet tool restore
dotnet ef migrations add <NomDeLaMigration> \
  --project src/DuneUsersApi \
  --output-dir Infrastructure/Persistence/Migrations
```

Les tables, colonnes, index et contraintes sont nommés en snake_case (`EFCore.NamingConventions`).

## Modèle de données

Table `users` : un user par couple `(tenant_id, tenant_user_id)`, avec nom, prénom, date de naissance, adresse (colonnes `address_*`), `created_at` et `onboarded_at`. Détail dans [docs/plans/02-Database model.md](docs/plans/02-Database%20model.md).

Les dates sont stockées en `timestamptz` : l'instant est conservé, mais pas l'offset d'origine (relu en UTC). `created_at` est calculé par l'application (`TimeProvider`) à l'insertion.

## Structure

```
src/DuneUsersApi/
  Features/<Feature>/              # un dossier par feature : endpoints, requêtes, réponses, validateurs
  Infrastructure/Persistence/      # AppDbContext, intercepteurs EF Core, migrations
    <Entité>/                      # entité, types complexes et configuration EF Core (ex. Users/)
tests/DuneUsersApi.Tests/
  Fixtures/ApiFixture.cs           # API + PostgreSQL (Testcontainers) + horloge de test
  Features/<Feature>/              # tests d'intégration par feature
  Infrastructure/Persistence/      # tests du mapping EF Core
```

## Endpoints

| Méthode | Route | Description |
|---|---|---|
| GET | `/health` | `200` si l'API et la base sont disponibles, `503` si la base est injoignable |
