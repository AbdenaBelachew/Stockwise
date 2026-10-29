# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY StockWise.slnx .
COPY src/StockWise.Domain/StockWise.Domain.csproj src/StockWise.Domain/
COPY src/StockWise.Application/StockWise.Application.csproj src/StockWise.Application/
COPY src/StockWise.Infrastructure/StockWise.Infrastructure.csproj src/StockWise.Infrastructure/
COPY src/StockWise.Api/StockWise.Api.csproj src/StockWise.Api/
RUN dotnet restore src/StockWise.Api/StockWise.Api.csproj

COPY src/ src/
RUN dotnet publish src/StockWise.Api/StockWise.Api.csproj -c Release -o /app/publish --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "StockWise.Api.dll"]
