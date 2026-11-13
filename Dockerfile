# Multi-stage Dockerfile for TripDistribution .NET 9 Web API
# Optimized for Render, Railway, Fly.io, Azure, and Docker deployments

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first for optimal Docker layer caching
COPY ["TripDistribution.Model/TripDistribution.Model.csproj", "TripDistribution.Model/"]
COPY ["TripDistribution.Data/TripDistribution.Data.csproj", "TripDistribution.Data/"]
COPY ["TripDistribution.Api/TripDistribution.Api.csproj", "TripDistribution.Api/"]
COPY ["TripDistribution.sln", "./"]

RUN dotnet restore "TripDistribution.sln"

# Copy remaining source code and publish release build
COPY . .
WORKDIR "/src/TripDistribution.Api"
RUN dotnet publish "TripDistribution.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TripDistribution.Api.dll"]

