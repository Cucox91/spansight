# SpanSight API — build context is the repo root:
#   docker build -f src/SpanSight.Api/Dockerfile -t spansight-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SpanSight.Core/SpanSight.Core.csproj src/SpanSight.Core/
COPY src/SpanSight.Api/SpanSight.Api.csproj src/SpanSight.Api/
RUN dotnet restore src/SpanSight.Api/SpanSight.Api.csproj
COPY src/ src/
RUN dotnet publish src/SpanSight.Api/SpanSight.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SpanSight.Api.dll"]
