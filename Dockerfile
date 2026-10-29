# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["UrubuApi.csproj", "./"]
RUN dotnet restore "UrubuApi.csproj"

COPY . .
RUN dotnet publish "UrubuApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Instalar pacotes para suporte e otimização
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Criar pasta para persistência do banco SQLite
RUN mkdir -p /app/data

# Configurações do Kestrel
ENV ASPNETCORE_URLS=http://+:5033
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/urubu.db"

EXPOSE 5033

ENTRYPOINT ["dotnet", "UrubuApi.dll"]
