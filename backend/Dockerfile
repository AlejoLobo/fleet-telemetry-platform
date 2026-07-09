FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FleetTelemetry.sln ./
COPY FleetTelemetry.Domain/ FleetTelemetry.Domain/
COPY FleetTelemetry.Application/ FleetTelemetry.Application/
COPY FleetTelemetry.Infrastructure/ FleetTelemetry.Infrastructure/
COPY FleetTelemetry.Api/ FleetTelemetry.Api/
RUN dotnet restore FleetTelemetry.Api/FleetTelemetry.Api.csproj
RUN dotnet publish FleetTelemetry.Api/FleetTelemetry.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "FleetTelemetry.Api.dll"]
