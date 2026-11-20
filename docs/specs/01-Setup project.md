Le projet `dune-users-api` est une API en C# .NET 10 qui propose des opérations permettant par exemple de récupérer la liste des users onboardés du jour ou de stocker les données de KYC d'un user onboardé.


Ce projet doit utiliser:
- C# .NET 10
- FastEndpoints comme librairie d'API
- PostgreSQL comme base de données
- DockerCompose pour gérer ensemble l'API et la base de données

Pour les tests des endpoints de l'API:
- Runner : xUnit (ou xUnit.v3 pour les nouveautés .NET récentes)
- Intégration API : FastEndpoints.Testing
- Assertions : Shouldly ou AwesomeAssertions (pour une syntaxe fluide et lisible)
- Base de données / Infrastructure : Testcontainers (pour faire tourner une vraie instance PostgreSQL, Redis ou RabbitMQ en Docker pendant les tests, parfaitement supporté par l'AppFixture de FastEndpoints).