# Dockerfile generico parametrico: pubblica il progetto indicato da PROJECT
# e lo avvia col DLL indicato da DLL. Usato per i tre servizi in docker-compose.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet restore "$PROJECT"
RUN dotnet publish "$PROJECT" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ARG DLL
ENV DLL_NAME=$DLL
ENTRYPOINT ["sh", "-c", "dotnet $DLL_NAME"]
