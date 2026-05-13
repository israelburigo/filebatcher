# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY FileBatcher.Api/FileBatcher.Api.csproj FileBatcher.Api/
RUN dotnet restore FileBatcher.Api/FileBatcher.Api.csproj
COPY FileBatcher.Api/ FileBatcher.Api/
WORKDIR /src/FileBatcher.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FileBatcher.Api.dll"]
