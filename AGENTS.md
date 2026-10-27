# UrubuApi - C# ASP.NET Core Web API

## Visão Geral
Backend de alta performance para o ecossistema **Urubu Hunter / Personal Hunter**, responsável por:
- Gerenciamento de peças garimpadas, histórico de preços e marcas monitoradas.
- Orquestração de crawlers / scrapers em background (`IHostedService` / BackgroundService).
- Avaliação e scoring de estilo (integração multimodal com IA).
- Fornecimento de endpoints REST tipados (OpenAPI / Scalar / Swagger) consumidos pelo frontend Next.js.

## Regras de Desenvolvimento .NET & C#
- **Versão:** .NET 10 / C# 13+
- **Estilo de API:** ASP.NET Core Minimal APIs / Controllers com OpenAPI tipado.
- **Tipagem e Nulabilidade:** `<Nullable>enable</Nullable>`, sem uso de `dynamic` ou `object` soltos.
- **Assincronismo:** Todos os métodos de I/O devem ser `async Task<T>` com `CancellationToken`.
- **Injeção de Dependência:** Registre serviços e repositórios em `Program.cs` com lifecycles adequados (`AddScoped`, `AddSingleton`).

## Skills Disponíveis (`.agents/skills/`)
Este projeto possui o conjunto oficial de skills da Microsoft (.NET Skills):
- `dotnet-webapi`: Boas práticas para endpoints, filtros e DTOs.
- `code-testing` / `run-tests`: Padrões para testes unitários com MSTest/xUnit.
- `dotnet-data`: Boas práticas para EF Core e persistência.
- `dotnet-ai`: Integração com modelos de inteligência artificial.
