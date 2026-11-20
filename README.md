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

Table `users` : un user par couple `(tenant_id, tenant_user_id)`, avec nom, prénom, date de naissance, adresse (colonnes `address_*`), `inserted_at` et `onboarded_at`. Détail dans [docs/plans/02-Database model.md](docs/plans/02-Database%20model.md).

Les dates sont stockées en `timestamptz` : l'instant est conservé, mais pas l'offset d'origine (relu en UTC). `inserted_at` (insertion en base) est renseigné par PostgreSQL (`DEFAULT now()`) et ignoré par EF Core à l'écriture.

## Structure

```
src/DuneUsersApi/
  Features/<Feature>/              # un dossier par feature : endpoints, requêtes, réponses, validateurs
  Infrastructure/Persistence/      # AppDbContext, intercepteurs EF Core, migrations
    <Entité>/                      # entité, types complexes et configuration EF Core (ex. Users/)
tests/DuneUsersApi.Tests/
  Fixtures/ApiFixture.cs           # API + PostgreSQL (Testcontainers) + horloge de test
  Fixtures/TestUsers.cs            # fabrique et insertion de users de test
  Features/<Feature>/              # tests d'intégration par feature
  Infrastructure/Persistence/      # tests du mapping EF Core
```

## Endpoints

Guide d'utilisation complet, avec exemples et enchaînement horaire des appels à `/missings` : [HOW-TO-USE-THE-API.md](HOW-TO-USE-THE-API.md) (en anglais).

| Méthode | Route | Description |
|---|---|---|
| GET | `/health` | `200` si l'API et la base sont disponibles, `503` si la base est injoignable |
| GET | `/v1/reconciliations/user-onboardings` | Users d'un tenant onboardés sur une plage de dates (paginé) |
| GET | `/v1/reconciliations/user-onboardings/missings` | Users de la même plage insérés en base après un parcours précédent (paginé) |

### `GET /v1/reconciliations/user-onboardings`

Renvoie le `tenantUserId` et la date d'onboarding (en UTC) des users d'un tenant dont `onboardedAt` est dans `[fromOnboardedAt, toOnboardedAt)` (début inclus, fin exclue), triés par `onboardedAt` puis `tenantUserId` (simple départage des ex æquo, sans ordre métier).

| Paramètre | Obligatoire | Défaut | Règles |
|---|---|---|---|
| `tenantId` | oui | — | 100 caractères max |
| `fromOnboardedAt` | oui | — | Date ISO 8601 avec offset, borne incluse |
| `toOnboardedAt` | oui | — | Date ISO 8601 avec offset, borne exclue, après `fromOnboardedAt` |
| `page` | non | `1` | À partir de 1 |
| `pageSize` | non | `1000` | Entre 1 et 1000 |
| `asOf` | oui | — | Date ISO 8601 avec offset, fournie par l'appelant, borne **exclue** : seuls les users insérés en base avant cet instant sont renvoyés |

```bash
curl "http://localhost:8080/v1/reconciliations/user-onboardings?tenantId=acme&fromOnboardedAt=2026-11-20T00:00:00Z&toOnboardedAt=2026-11-21T00:00:00Z&asOf=2026-11-21T06:00:00Z&page=1&pageSize=1000"
# {"userOnboardings":[{"tenantUserId":"u-001","onboardedAt":"2026-11-20T08:00:00+00:00"}]}
```

- Une page de moins de `pageSize` éléments (éventuellement vide) est la dernière.
- Parcours stable : fixer `asOf` au début du parcours et passer la même valeur pour toutes les pages ; les users insérés entre-temps ne décalent alors pas les pages. Choisir `asOf` légèrement dans le passé (ex. maintenant moins une minute) pour absorber un décalage d'horloge avec la base et les insertions en cours.
- Dans une query string, `+` est décodé en espace : encoder les offsets positifs (`2026-11-20T02:00:00%2B02:00`) ou utiliser `Z`. Une date non encodée est rejetée (`400`).
- Une date sans offset est interprétée dans le fuseau du serveur : toujours préciser l'offset.
- Paramètre manquant ou invalide : `400` avec le détail des erreurs par paramètre.

### `GET /v1/reconciliations/user-onboardings/missings`

Renvoie les onboardings qu'un parcours de `/v1/reconciliations/user-onboardings` n'a pas pu voir : les users de la plage `[fromOnboardedAt, toOnboardedAt)` insérés en base à partir du `asOf` de ce parcours. Même format de réponse, même tri, même pagination.

Mêmes paramètres que `/v1/reconciliations/user-onboardings`, avec les mêmes règles, plus :

| Paramètre | Obligatoire | Défaut | Règles |
|---|---|---|---|
| `missingsSince` | oui | — | Date ISO 8601 avec offset, borne **incluse** : seuls les users insérés en base à partir de cet instant sont renvoyés. Normalement le `asOf` du parcours précédent |
| `asOf` | oui | — | Borne **exclue**, comme pour `/user-onboardings`, et strictement après `missingsSince` |

```bash
# 1. Parcours initial
curl "http://localhost:8080/v1/reconciliations/user-onboardings?tenantId=acme&fromOnboardedAt=2026-11-20T00:00:00Z&toOnboardedAt=2026-11-21T00:00:00Z&asOf=2026-11-21T06:00:00Z"
# 2. Plus tard : users de la même plage insérés depuis
curl "http://localhost:8080/v1/reconciliations/user-onboardings/missings?tenantId=acme&fromOnboardedAt=2026-11-20T00:00:00Z&toOnboardedAt=2026-11-21T00:00:00Z&missingsSince=2026-11-21T06:00:00Z&asOf=2026-11-22T06:00:00Z"
```

- Enchaîner les appels en passant à chaque fois le `asOf` précédent comme `missingsSince` : l'ensemble des réponses contient tous les users de la plage, sans doublon. Toutes les plages de dates suivent la même convention, borne basse incluse et borne haute exclue : la fenêtre d'insertion est `[missingsSince, asOf)`, comme la plage d'onboarding `[fromOnboardedAt, toOnboardedAt)`.
- Comme `inserted_at` est l'heure de **début** de la transaction d'insertion, un user dont la transaction a commencé avant le `asOf` du parcours initial mais a été validée après n'est vu ni par ce parcours ni par `/missings`. La marge recommandée sur `asOf` (maintenant moins une minute) couvre ce cas tant qu'aucune insertion ne dure plus longtemps.
