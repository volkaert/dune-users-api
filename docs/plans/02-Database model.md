# Plan 02 — Modèle de données `users`

Source : [docs/specs/02-Database model.md](../specs/02-Database%20model.md)

## Objectif

Créer la table PostgreSQL qui stocke les users, avec l'entité EF Core, sa configuration et la première migration, pour que les futurs endpoints (liste des users onboardés du jour, stockage KYC…) puissent lire et écrire des users directement.

À la fin de ce plan :

- l'entité `User` (avec son adresse) existe dans `Infrastructure/Persistence` ;
- `createdAt` est calculé par l'application au moment de l'insertion ;
- la migration initiale crée la table `users` avec ses contraintes et index ;
- la migration est appliquée automatiquement en Development (`docker compose up`) et dans les tests (Testcontainers) ;
- des tests d'intégration vérifient le mapping contre une vraie base PostgreSQL.

## Hors périmètre

- Les endpoints (création, lecture, liste des onboardés du jour) : ils feront l'objet de specs et plans dédiés.
- La validation métier des données entrantes (format des noms, âge minimum…) : elle relèvera des validateurs FastEndpoints des futurs endpoints.
- Les données KYC autres que celles listées dans la spec.

## Horodatages

Les dates sont stockées avec le type natif `timestamptz`, et `DateTimeOffset` côté C#.

- `timestamptz` stocke un **instant**, toujours en UTC. Une valeur reçue avec un offset (`2026-11-20T10:00:00+02:00`) est convertie en UTC (`2026-11-20T08:00:00Z`) : l'instant est exact, mais **l'offset d'origine n'est pas conservé**. C'est un choix assumé : PostgreSQL n'a pas de type natif qui conserve l'offset (vérifié sur PostgreSQL 18), et l'offset n'est pas nécessaire au besoin actuel.
- Npgsql refuse d'écrire dans une colonne `timestamptz` un `DateTimeOffset` dont l'offset n'est pas nul (exception à l'exécution). L'entité convertit donc les valeurs en UTC dès leur affectation, pour que l'appelant puisse fournir une valeur avec n'importe quel offset :

  ```csharp
  private DateTimeOffset _onboardedAt;

  public required DateTimeOffset OnboardedAt
  {
      get => _onboardedAt;
      init => _onboardedAt = value.ToUniversalTime();
  }
  ```

  EF Core lit et écrit directement le champ `_onboardedAt` (convention de nommage des champs de stockage) ; les valeurs relues depuis la base sont déjà en UTC.

## Modèle cible

### Table `users`

| Champ (spec) | Propriété C# | Colonne | Type PostgreSQL | Null | Remarques |
|---|---|---|---|---|---|
| — | `Id` | `id` | `uuid` | non | Clé primaire technique, UUIDv7 générée côté client (comportement par défaut de Npgsql pour les clés `Guid`) |
| tenantId | `TenantId` | `tenant_id` | `varchar(100)` | non | |
| tenantUserId | `TenantUserId` | `tenant_user_id` | `varchar(100)` | non | Identifiant du user dans son tenant |
| createdAt | `CreatedAt` | `created_at` | `timestamptz` | non | Calculé par l'application à l'insertion (voir étape 4) |
| onboardedAt | `OnboardedAt` | `onboarded_at` | `timestamptz` | non | Instant de l'onboarding, fourni par l'appelant, converti en UTC |
| firstName | `FirstName` | `first_name` | `varchar(100)` | non | |
| lastName | `LastName` | `last_name` | `varchar(100)` | non | |
| birthDate | `BirthDate` | `birth_date` | `date` | non | `DateOnly` côté C# |
| address.street | `Address.Street` | `address_street` | `varchar(200)` | non | |
| address.postalCode | `Address.PostalCode` | `address_postal_code` | `varchar(20)` | non | Chaîne (codes postaux avec lettres ou zéros en tête) |
| address.city | `Address.City` | `address_city` | `varchar(100)` | non | |
| address.countryCode | `Address.CountryCode` | `address_country_code` | `char(2)` | non | ISO 3166-1 alpha-2, majuscules |

### Contraintes et index

- `pk_users` sur `id`.
- `ux_users_tenant_id_tenant_user_id` : index **unique** sur `(tenant_id, tenant_user_id)`. Un même user ne peut exister qu'une fois dans un tenant.
- `ix_users_onboarded_at` : index sur `onboarded_at`, en prévision de la requête « users onboardés du jour » (filtre par plage `[début du jour, début du lendemain)`).
- `ck_users_address_country_code` : `CHECK (address_country_code ~ '^[A-Z]{2}$')`.

### Choix de modélisation

| Sujet | Choix | Justification |
|---|---|---|
| Clé primaire | `Guid` technique (UUIDv7) + unicité sur `(tenant_id, tenant_user_id)` | Clé stable et courte pour les futures tables liées (KYC) ; l'unicité métier est garantie par l'index. UUIDv7 est ordonné dans le temps, ce qui limite la fragmentation de l'index |
| Horodatages | `DateTimeOffset` ↔ `timestamptz` natif, valeurs converties en UTC | Type natif PostgreSQL, requêtes et index directs ; l'offset d'origine n'est pas conservé (voir section « Horodatages ») |
| `createdAt` | Calculé par l'application via `TimeProvider`, dans un intercepteur EF Core | Valeur renseignée de façon centralisée pour toute insertion, et horloge remplaçable dans les tests (`FakeTimeProvider`) |
| Date de naissance | `DateOnly` ↔ `date` | Pas d'heure ni de fuseau pour une date de naissance |
| Adresse | Type complexe EF Core (`ComplexProperty`), colonnes préfixées `address_` dans la table `users` | Une adresse n'a pas d'identité propre : un type complexe (sémantique de valeur) est plus adapté qu'une entité possédée ou qu'une table séparée |
| Nommage | snake_case (tables, colonnes, contraintes) via `EFCore.NamingConventions` | Convention PostgreSQL, évite les identifiants entre guillemets dans le SQL écrit à la main |

## Arborescence cible

```
src/DuneUsersApi/Infrastructure/Persistence/
├── AppDbContext.cs                      # + DbSet<User> Users
├── CreatedAtInterceptor.cs              # SaveChangesInterceptor : renseigne CreatedAt à l'insertion
├── Users/
│   ├── User.cs
│   ├── Address.cs
│   └── UserConfiguration.cs             # IEntityTypeConfiguration<User>
└── Migrations/
    ├── <horodatage>_CreateUsersTable.cs
    ├── <horodatage>_CreateUsersTable.Designer.cs
    └── AppDbContextModelSnapshot.cs

tests/DuneUsersApi.Tests/
├── Fixtures/ApiFixture.cs               # + FakeTimeProvider
└── Infrastructure/Persistence/
    └── UserPersistenceTests.cs
```

## Étapes

### Étape 1 — Convention de nommage snake_case

1. Ajouter le package `EFCore.NamingConventions` dans `Directory.Packages.props`, en choisissant une version compatible avec EF Core 10.0.x, et le référencer (sans version) dans `DuneUsersApi.csproj`.
2. Dans `Program.cs`, appeler `.UseSnakeCaseNamingConvention()` après `UseNpgsql(...)`.

**Validation** : `dotnet build` passe sans warning (attention aux conflits de versions transitives d'EF Core, déjà rencontrés au plan 01 avec `Relational`).

### Étape 2 — Entité `User` et type complexe `Address`

1. Créer `Infrastructure/Persistence/Users/Address.cs` : `sealed record` avec `Street`, `PostalCode`, `City`, `CountryCode`, toutes `required` et `init`.
2. Créer `Infrastructure/Persistence/Users/User.cs` :
   - `Guid Id { get; private set; }` ;
   - `required string TenantId`, `required string TenantUserId`, `required string FirstName`, `required string LastName` ;
   - `required DateOnly BirthDate`, `required Address Address` ;
   - `required DateTimeOffset OnboardedAt`, convertie en UTC dans son `init` (voir section « Horodatages ») ;
   - `DateTimeOffset CreatedAt { get; private set; }` : pas d'affectation possible par l'appelant, la valeur est posée par l'intercepteur.
3. Ajouter `public DbSet<User> Users => Set<User>();` dans `AppDbContext`.

### Étape 3 — Configuration EF Core

Créer `Infrastructure/Persistence/Users/UserConfiguration.cs` (récupérée automatiquement par `ApplyConfigurationsFromAssembly`) :

1. `ToTable("users", t => t.HasCheckConstraint(...))` pour la contrainte sur le code pays.
2. `HasKey(u => u.Id)`.
3. Longueurs maximales (`HasMaxLength`) conformes au tableau du modèle cible.
4. `ComplexProperty(u => u.Address, a => { ... })` : longueurs maximales et `CountryCode` en `char(2)` (`IsFixedLength()`).
5. Index unique `(TenantId, TenantUserId)` et index sur `OnboardedAt`, avec les noms définis plus haut.

### Étape 4 — Calcul de `createdAt` par l'application

1. Enregistrer l'horloge dans `Program.cs` : `builder.Services.AddSingleton(TimeProvider.System);`.
2. Créer `Infrastructure/Persistence/CreatedAtInterceptor.cs`, un `SaveChangesInterceptor` qui reçoit `TimeProvider` par injection. Dans `SavingChanges` et `SavingChangesAsync`, pour chaque entrée `User` à l'état `Added`, il affecte `entry.Property(u => u.CreatedAt).CurrentValue = timeProvider.GetUtcNow()` (valeur déjà en UTC). La valeur est toujours écrasée : un appelant ne peut pas imposer sa propre date de création.
3. Enregistrer l'intercepteur en singleton et le brancher sur le `DbContext` :
   ```csharp
   builder.Services.AddSingleton<CreatedAtInterceptor>();
   builder.Services.AddDbContext<AppDbContext>((sp, options) => options
       .UseNpgsql(connectionString)
       .UseSnakeCaseNamingConvention()
       .AddInterceptors(sp.GetRequiredService<CreatedAtInterceptor>()));
   ```
4. Si d'autres entités ont besoin d'un `createdAt` plus tard, généraliser avec une interface (`IHasCreatedAt`). Ce n'est pas nécessaire tant qu'il n'y a que `User`.

### Étape 5 — Migration initiale

1. Générer la migration :
   ```bash
   dotnet ef migrations add CreateUsersTable \
     --project src/DuneUsersApi \
     --output-dir Infrastructure/Persistence/Migrations
   ```
2. Relire le code généré et le SQL produit (`dotnet ef migrations script --project src/DuneUsersApi`) : types de colonnes (`timestamptz` pour les deux dates, pas de valeur par défaut sur `created_at`), nom et définition des index et de la contrainte `CHECK`.
3. Vérifier que le build passe toujours : les fichiers de migration générés ne doivent pas déclencher de warnings (sinon, les exclure de l'analyse via un `.editorconfig` ciblé sur `Migrations/`).

**Validation** : `docker compose up --build`, puis `docker compose exec postgres psql -U dune -d dune_users -c '\d users'` affiche la table attendue.

### Étape 6 — Tests d'intégration du mapping

1. Ajouter le package `Microsoft.Extensions.TimeProvider.Testing` (groupe « Tests » de `Directory.Packages.props`) et le référencer dans le projet de tests.
2. Dans `ApiFixture`, substituer un `FakeTimeProvider` (initialisé à une date fixe) à `TimeProvider.System` dans `ConfigureServices`, et l'exposer via une propriété `Time`. L'horloge est partagée par toutes les classes de tests (voir « Notes d'implémentation ») : un test qui en dépend la positionne lui-même avec `SetUtcNow`.
3. Créer `tests/DuneUsersApi.Tests/Infrastructure/Persistence/UserPersistenceTests.cs`, basé sur `TestBase<ApiFixture>` (le `AppDbContext` est récupéré via un scope de `app.Services`). Chaque test utilise un `TenantId` unique (`Guid.NewGuid().ToString()`) pour ne pas dépendre des autres tests qui partagent le même conteneur.

Cas à couvrir :

1. **Aller-retour complet** : insérer un user, le relire avec un nouveau `DbContext`, et vérifier que tous les champs, y compris l'adresse, sont identiques.
2. **Date avec offset acceptée** : un `OnboardedAt` à `2026-11-20T10:00:00+02:00` est enregistré sans erreur et relu comme `2026-11-20T08:00:00+00:00` (même instant, offset nul). Même vérification avec un offset négatif.
3. **`CreatedAt` calculé par l'application** : après `SaveChangesAsync`, `CreatedAt` vaut exactement l'heure du `FakeTimeProvider`, avec un offset nul.
4. **`CreatedAt` non modifié par une mise à jour** : avancer l'horloge, modifier le user, sauvegarder, et vérifier que `CreatedAt` n'a pas changé.
5. **Requête par plage de dates** : un user onboardé à `2026-11-20T23:30:00-02:00` est trouvé par un filtre `OnboardedAt` sur la plage du 21/11 en UTC.
6. **Unicité `(tenant_id, tenant_user_id)`** : insérer deux users avec le même couple lève une `DbUpdateException` dont l'exception interne est une `PostgresException` avec `SqlState` = `23505` (`PostgresErrorCodes.UniqueViolation`).
7. **Même `tenantUserId` dans deux tenants différents** : accepté.
8. **Contrainte sur le code pays** : `"fr"` est rejeté (`SqlState` `23514`, `CheckViolation`) ; `"FRA"` est rejeté pour longueur (`22001`).
9. **Pas de dérive modèle/migrations** : `db.Database.HasPendingModelChanges()` renvoie `false`. Ce test échoue si quelqu'un modifie le modèle sans générer de migration.

**Validation** : `dotnet test` passe.

### Étape 7 — Documentation

1. Mettre à jour la section « Structure » du `README.md` et y décrire brièvement le modèle de données.
2. Ajouter dans `CLAUDE.md` les règles sur les horodatages (`DateTimeOffset` ↔ `timestamptz`, valeurs converties en UTC avant écriture, heure courante via `TimeProvider`) et sur la fixture de tests partagée.
3. Compléter ce plan avec une section « Notes d'implémentation » (versions retenues, écarts éventuels), comme pour le plan 01.

## Points d'attention

- **Offset non conservé** : une API qui renvoie `onboardedAt` le renverra en UTC, même s'il a été reçu avec un autre offset. Si l'offset d'origine devient nécessaire plus tard, il faudra ajouter une colonne dédiée (`smallint`, offset en minutes), PostgreSQL n'ayant pas de type natif pour ça.
- **Contournement de l'intercepteur** : une insertion en SQL brut (`ExecuteSqlRaw`, `COPY`, script d'import) ne passe pas par l'intercepteur et doit renseigner `created_at` elle-même. La colonne étant `NOT NULL` sans valeur par défaut, un oubli est rejeté par la base.
- **« Users onboardés du jour »** : le « jour » dépend d'un fuseau horaire (UTC ou fuseau du tenant). Ce choix appartient à la spec de l'endpoint concerné. L'index sur `onboarded_at` sert dans les deux cas.
- **Données personnelles** : nom, prénom, date de naissance et adresse sont des données personnelles (RGPD). Le plan ne prévoit ni chiffrement ni pseudonymisation ; à traiter dans une spec dédiée si besoin.

## Critères d'acceptation

- [x] `dotnet build` passe sans warning.
- [x] La migration `CreateUsersTable` crée la table `users` avec les colonnes, types, contraintes et index décrits dans ce plan.
- [x] Une date fournie avec un offset non nul est enregistrée sans erreur, au bon instant.
- [x] `createdAt` est calculé par l'application, via `TimeProvider`.
- [x] `docker compose up --build` applique la migration (environnement Development) et la table est présente dans la base.
- [x] `dotnet test` passe, y compris les tests de `UserPersistenceTests`.
- [x] Aucun changement de modèle en attente (`HasPendingModelChanges()` = `false`).

## Notes d'implémentation

Écarts et ajustements constatés pendant l'implémentation (2026-11-20) :

- **Versions retenues** : `EFCore.NamingConventions` 10.0.1, `Microsoft.Extensions.TimeProvider.Testing` 10.11.1.
- **Décisions à valider** : elles restent ouvertes ; l'implémentation applique les choix par défaut du plan (clé `Guid`, tous les champs obligatoires, longueurs proposées, `EFCore.NamingConventions`).
- **Fixture de tests partagée** : FastEndpoints met en cache l'application (WAF) **par type de fixture**, et toutes les classes de tests utilisant `ApiFixture` la partagent. `PreSetupAsync`, `ConfigureApp` et `ConfigureServices` ne s'exécutent qu'une fois, mais `TearDownAsync` s'exécute à la fin de chaque classe. Avec deux classes de tests, la première terminée arrêtait le conteneur PostgreSQL encore utilisé par l'autre (échecs de connexion aléatoires). Corrections :
  - le conteneur est un champ statique, arrêté dans `OnCachedWafDisposedAsync`, ce qui demande `[assembly: EnableAdvancedTesting]` ;
  - `ApiFixture.Time` renvoie l'horloge enregistrée dans l'injection de dépendances (et non une instance propre à la fixture) ;
  - l'horloge étant partagée, le test de `CreatedAt` la positionne avec `SetUtcNow` au lieu de supposer sa valeur.
- **Propriétés modifiables** : `FirstName`, `LastName`, `BirthDate`, `Address` et `OnboardedAt` ont un `set` (et non `init`), pour permettre les mises à jour. `TenantId` et `TenantUserId` restent en `init`.
- **Historique des migrations renommé** : la convention snake_case s'applique aussi à la table `__EFMigrationsHistory` (colonnes `migration_id`, `product_version`). Une base locale créée avant ce plan contient la table avec les anciennes colonnes (`MigrationId`, `ProductVersion`) et doit être réinitialisée (`docker compose down -v`) ou la table supprimée si elle est vide.
- **Validation Docker Compose** : faite sur une stack isolée (`docker compose -p <autre nom>` avec d'autres ports et un volume neuf) : migration appliquée, `/health` → `200`, table `users` conforme.

## Décisions à valider

1. **Clé primaire** : `Guid` technique + unicité `(tenant_id, tenant_user_id)` (choix du plan), ou clé primaire composite `(tenant_id, tenant_user_id)` sans identifiant technique ?
2. **Champs obligatoires** : le plan rend tous les champs `NOT NULL`, adresse comprise. Peut-on stocker un user pas encore onboardé (`onboarded_at` nullable) ou sans adresse ?
3. **Longueurs maximales** : les valeurs proposées (100 pour les identifiants de tenant et les noms, 200 pour la rue, 20 pour le code postal) sont-elles adaptées ? Quel est le format réel de `tenantId` et `tenantUserId` (UUID, slug, identifiant externe) ?
4. **Nommage snake_case** via `EFCore.NamingConventions` (choix du plan), ou noms de colonnes explicites dans la configuration (une dépendance de moins) ?
