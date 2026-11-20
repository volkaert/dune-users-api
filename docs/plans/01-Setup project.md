# Plan 01 — Setup du projet `dune-users-api`

Source : [docs/specs/01-Setup project.md](../specs/01-Setup%20project.md)

## Objectif

Mettre en place le squelette technique de l'API `dune-users-api` pour que les futures fonctionnalités (liste des users onboardés du jour, stockage des données KYC, etc.) puissent être développées directement, sans travail d'infrastructure supplémentaire.

À la fin de ce plan :

- l'API C# .NET 10 basée sur FastEndpoints compile et démarre ;
- elle est connectée à une base PostgreSQL ;
- `docker compose up` lance ensemble l'API et la base ;
- une suite de tests d'intégration (xUnit v3 + FastEndpoints.Testing + Testcontainers + Shouldly) tourne contre une vraie instance PostgreSQL ;
- un endpoint technique (`GET /health`) valide toute la chaîne de bout en bout.

## Hors périmètre

- Les endpoints métier (users onboardés, KYC) : ils feront l'objet de specs et de plans dédiés.
- Authentification / autorisation.
- CI/CD, déploiement, observabilité avancée.

## Choix techniques

| Sujet | Choix | Justification |
|---|---|---|
| SDK | .NET 10 (`10.0.300`, figé via `global.json`) | Version installée en local, imposée par la spec |
| Solution | Format `.slnx` | Format par défaut de .NET 10, plus lisible que `.sln` |
| Gestion des packages | Central Package Management (`Directory.Packages.props`) | Versions centralisées entre projet API et projet de tests |
| Librairie d'API | FastEndpoints | Imposé par la spec |
| Base de données | PostgreSQL 18 (`postgres:18-alpine`) | Imposé par la spec ; même image en Docker Compose et en tests |
| Accès aux données | EF Core + `Npgsql.EntityFrameworkCore.PostgreSQL` | Migrations versionnées, standard .NET ; à confirmer (voir « Décisions à valider ») |
| Runner de tests | xUnit v3 | Recommandé par la spec, supporté par FastEndpoints.Testing |
| Tests d'intégration | FastEndpoints.Testing (`AppFixture<Program>`) | Imposé par la spec |
| Assertions | Shouldly | Utilisé dans la doc FastEndpoints, syntaxe simple |
| Infra de test | Testcontainers (`Testcontainers.PostgreSql`) | Vraie instance PostgreSQL pendant les tests |

## Arborescence cible

```
dune-users-api/
├── global.json
├── Directory.Build.props          # TargetFramework, Nullable, ImplicitUsings, TreatWarningsAsErrors
├── Directory.Packages.props       # Versions centralisées des packages NuGet
├── DuneUsersApi.slnx
├── docker-compose.yml
├── .env.example                   # Variables pour docker compose (identifiants Postgres)
├── .dockerignore
├── dotnet-tools.json              # Outil local dotnet-ef (à la racine, emplacement par défaut du SDK .NET 10)
├── src/
│   └── DuneUsersApi/
│       ├── DuneUsersApi.csproj
│       ├── Dockerfile
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Features/
│       │   └── Health/
│       │       └── HealthEndpoint.cs
│       └── Infrastructure/
│           └── Persistence/
│               ├── AppDbContext.cs
│               └── Migrations/
└── tests/
    └── DuneUsersApi.Tests/
        ├── DuneUsersApi.Tests.csproj
        ├── xunit.runner.json
        ├── Fixtures/
        │   └── ApiFixture.cs
        └── Features/
            └── Health/
                └── HealthEndpointTests.cs
```

Organisation par **feature** (`Features/<Feature>/...`) : chaque endpoint FastEndpoints regroupe sa requête, sa réponse, son validateur et son handler au même endroit (approche REPR / vertical slices).

## Étapes

### Étape 1 — Fondations de la solution

1. Créer `global.json` en figeant le SDK `10.0.300` (`rollForward: latestFeature`).
2. Créer `Directory.Build.props` :
   - `TargetFramework` = `net10.0`
   - `Nullable` = `enable`, `ImplicitUsings` = `enable`
   - `TreatWarningsAsErrors` = `true`
3. Créer `Directory.Packages.props` avec `ManagePackageVersionsCentrally` = `true`.
4. Créer la solution `DuneUsersApi.slnx`.
5. Créer le manifeste d'outils local et installer `dotnet-ef`.

**Validation** : `dotnet --version` dans le repo renvoie `10.0.300`.

### Étape 2 — Projet API minimal avec FastEndpoints

1. Créer `src/DuneUsersApi` (`dotnet new web`) et l'ajouter à la solution.
2. Ajouter le package `FastEndpoints`.
3. Dans `Program.cs` : `AddFastEndpoints()` / `UseFastEndpoints()`, et terminer le fichier par `public partial class Program;` (nécessaire pour `AppFixture<Program>` dans les tests).
4. Créer un premier endpoint `GET /health` (sans dépendance à la base pour l'instant) qui renvoie `200 OK`.

**Validation** : `dotnet run --project src/DuneUsersApi` puis `curl http://localhost:<port>/health` → `200`.

### Étape 3 — Intégration PostgreSQL (EF Core)

1. Ajouter les packages `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`.
2. Créer `AppDbContext` (vide pour l'instant, sans entité métier).
3. Enregistrer le `DbContext` avec la chaîne de connexion `ConnectionStrings:Postgres`.
4. Renseigner `appsettings.Development.json` avec une chaîne pointant sur `localhost:5432` (base locale lancée via Docker Compose). Aucun secret de production dans les fichiers versionnés.
5. Appliquer les migrations au démarrage **uniquement en environnement Development** (`Database.MigrateAsync()`), pour simplifier le dev local.
6. Faire évoluer `GET /health` pour vérifier la connexion à la base (`Database.CanConnectAsync()`) :
   - `200 OK` avec `{ "status": "Healthy", "database": "Up" }` si la base répond ;
   - `503 Service Unavailable` sinon.

**Validation** : avec Postgres démarré, `/health` → `200` ; Postgres arrêté, `/health` → `503`.

### Étape 4 — Conteneurisation et Docker Compose

1. Créer `src/DuneUsersApi/Dockerfile` multi-stage :
   - build : `mcr.microsoft.com/dotnet/sdk:10.0` (restore → publish en Release) ;
   - runtime : `mcr.microsoft.com/dotnet/aspnet:10.0`, exécution en utilisateur non-root, port `8080`.
2. Créer `.dockerignore` (`bin/`, `obj/`, `tests/`, `.git/`, …).
3. Créer `docker-compose.yml` avec deux services :
   - `postgres` : image `postgres:18-alpine`, volume nommé monté sur `/var/lib/postgresql` (chemin attendu à partir de Postgres 18), `healthcheck` avec `pg_isready`, port `5432` exposé pour le dev local ;
   - `api` : build depuis le Dockerfile, `depends_on: postgres` avec `condition: service_healthy`, chaîne de connexion injectée par variable d'environnement (`ConnectionStrings__Postgres`, host = `postgres`), port `8080` exposé.
4. Créer `.env.example` (utilisateur, mot de passe, nom de base) et ajouter `.env` au `.gitignore`.

**Validation** : `docker compose up --build` puis `curl http://localhost:8080/health` → `200` avec `"database": "Up"`.

### Étape 5 — Projet de tests d'intégration

1. Créer `tests/DuneUsersApi.Tests` (projet xUnit v3) et l'ajouter à la solution, avec une référence vers `src/DuneUsersApi`.
2. Ajouter les packages : `xunit.v3`, `FastEndpoints.Testing`, `Shouldly`, `Testcontainers.PostgreSql`.
3. Créer `Fixtures/ApiFixture.cs` dérivant de `AppFixture<Program>` :
   - `PreSetupAsync` : démarrer un conteneur `postgres:18-alpine` via Testcontainers ;
   - `ConfigureApp` : injecter la chaîne de connexion du conteneur (`ConnectionStrings:Postgres`) et l'environnement `Testing` ;
   - `SetupAsync` : appliquer les migrations EF Core ;
   - `TearDownAsync` : arrêter et supprimer le conteneur.
4. Écrire `HealthEndpointTests` : `GET /health` → `200` et `database` = `Up`, en utilisant `App.Client.GETAsync<HealthEndpoint, HealthResponse>()` et des assertions Shouldly.
5. Ajouter `xunit.runner.json` si un réglage de parallélisation est nécessaire (un conteneur par fixture partagée entre les tests d'une même classe).

**Validation** : `dotnet test` passe (Docker doit tourner sur la machine).

### Étape 6 — Documentation

1. Compléter `README.md` :
   - prérequis (SDK .NET 10, Docker) ;
   - lancer en local : `docker compose up -d postgres` + `dotnet run`, ou tout via `docker compose up --build` ;
   - lancer les tests : `dotnet test` ;
   - créer une migration : `dotnet ef migrations add <Nom> --project src/DuneUsersApi --output-dir Infrastructure/Persistence/Migrations`.
2. Ajouter un `CLAUDE.md` (optionnel) résumant les conventions : organisation par feature, un endpoint = une classe FastEndpoints, tests d'intégration obligatoires par endpoint.

## Critères d'acceptation globaux

- [x] `dotnet build` passe sans warning.
- [x] `dotnet test` passe, avec au moins un test d'intégration exécuté contre un PostgreSQL lancé par Testcontainers.
- [x] `docker compose up --build` démarre l'API et PostgreSQL ; `GET http://localhost:8080/health` renvoie `200` avec `"database": "Up"`.
- [x] Aucun secret réel n'est versionné (`.env` ignoré, seul `.env.example` est commité).
- [x] Le README permet à un nouveau développeur de lancer l'API et les tests sans autre aide.

## Notes d'implémentation

Écarts et ajustements constatés pendant l'implémentation (2026-11-20) :

- **Versions retenues** : FastEndpoints / FastEndpoints.Testing 9.0.0, EF Core 10.0.15, Npgsql EF 10.0.3, xUnit v3 4.0.3, Shouldly 4.3.0, Testcontainers.PostgreSql 4.17.0. EF Core 11 est disponible mais pas encore supporté par le provider Npgsql 10.x, on reste donc en 10.x.
- **`Microsoft.EntityFrameworkCore.Relational` référencé explicitement** : Npgsql 10.0.3 tire la version 10.0.4, ce qui provoquait un conflit (warning MSB3277, bloquant avec `TreatWarningsAsErrors`).
- **Microsoft Testing Platform** : le SDK .NET 10 refuse VSTest pour les projets xUnit v3 sous `dotnet test`. `global.json` déclare donc `"test": { "runner": "Microsoft.Testing.Platform" }`. Les packages propres à VSTest (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) ont été retirés : ils ne servent plus, ni pour `dotnet test`, ni pour l'explorateur de tests de VS Code (C# Dev Kit gère nativement Microsoft Testing Platform).
- **`dotnet-tools.json` à la racine** au lieu de `.config/` : c'est l'emplacement par défaut de `dotnet new tool-manifest` avec le SDK .NET 10.
- **Pas de migration initiale** : `AppDbContext` n'a encore aucune entité. La première migration sera créée avec la première feature métier.
- **Port local** : `dotnet run` écoute sur `http://localhost:5080` (`launchSettings.json`), et sur `8080` via Docker Compose.
- **Cas `503` vérifié manuellement** (Postgres arrêté via `docker compose stop postgres`) : il n'est pas couvert par un test automatisé.

## Décisions à valider

1. **Accès aux données : EF Core ou Dapper ?** Le plan part sur EF Core (migrations intégrées, productivité). Dapper + un outil de migration (DbUp, FluentMigrator) serait une alternative plus légère si on préfère écrire le SQL à la main.
2. **Shouldly ou AwesomeAssertions ?** Le plan retient Shouldly (utilisé dans la documentation FastEndpoints). Les deux conviennent.
3. **Application des migrations** : au démarrage en Development uniquement (choix du plan), ou via une étape dédiée (job/conteneur de migration) dès maintenant ?
4. **Version de PostgreSQL** : 18 (dernière majeure) retenue ; à aligner si un environnement cible impose une autre version.
