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
- Entités et configurations EF Core dans `Infrastructure/Persistence`. Migrations générées avec `dotnet ef` dans `Infrastructure/Persistence/Migrations`.
- Specs dans `docs/specs/`, plans d'implémentation dans `docs/plans/`.
