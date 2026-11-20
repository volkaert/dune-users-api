# CLAUDE.md

API C# .NET 10 avec FastEndpoints, PostgreSQL (EF Core / Npgsql) et Docker Compose. Voir `README.md` pour les commandes.

## Commandes

- Build : `dotnet build` (les warnings sont traités comme des erreurs)
- Tests : `dotnet test` (Microsoft Testing Platform ; Docker doit tourner pour Testcontainers)
- Stack complète : `docker compose up --build`

## Conventions

- Organisation par feature : `src/DuneUsersApi/Features/<Feature>/`. Un endpoint = une classe FastEndpoints, avec sa requête, sa réponse et son validateur dans le même dossier.
- Chaque endpoint a des tests d'intégration dans `tests/DuneUsersApi.Tests/Features/<Feature>/`, basés sur `ApiFixture` (`TestBase<ApiFixture>`) et des assertions Shouldly.
- Versions des packages NuGet uniquement dans `Directory.Packages.props` (gestion centralisée) ; les `.csproj` ne portent pas de version.
- Entités et configurations EF Core dans `Infrastructure/Persistence/<Entité>/`. Migrations générées avec `dotnet ef` dans `Infrastructure/Persistence/Migrations`. Nommage snake_case automatique (`UseSnakeCaseNamingConvention`).
- Dates : `DateTimeOffset` ↔ `timestamptz`. Npgsql refuse un offset non nul : convertir en UTC (`ToUniversalTime()`) avant écriture. L'offset d'origine n'est pas conservé.
- Heure courante : toujours via `TimeProvider` (injecté), jamais `DateTime.UtcNow` / `DateTimeOffset.UtcNow`.
- Plages de dates de l'API : borne basse incluse, borne haute exclue (`[fromOnboardedAt, toOnboardedAt)`, `[missingsSince, asOf)`, `inserted_at < asOf`). Deux plages consécutives s'enchaînent ainsi sans chevauchement ni trou.
- `users.inserted_at` (insertion en base) est posé par PostgreSQL (`DEFAULT now()`), jamais par l'application : EF Core l'ignore à l'écriture et le relit après l'insertion.
- Tests : `ApiFixture` est partagée par toutes les classes de tests (application, base et horloge `FakeTimeProvider` communes). Isoler les données par un `TenantId` unique, et positionner l'horloge (`SetUtcNow`) dans les tests qui en dépendent.
- Specs dans `docs/specs/`, plans d'implémentation dans `docs/plans/`.
- Erreurs de validation : les messages citent les paramètres sous leur nom d'API en camelCase (réglage global dans `Program.cs` : `DisplayNameResolver` de FluentValidation et `Binding.FailureMessage` de FastEndpoints). Dans un `WithMessage` personnalisé, utiliser `{PropertyName}` / `{ComparisonProperty}` plutôt que des noms en dur.
- FluentValidation : un `.When(...)` dans une chaîne s'applique par défaut à tous les validateurs qui le précèdent (dont `NotNull`). Pour ne conditionner qu'une comparaison, passer `ApplyConditionTo.CurrentValidator`.
