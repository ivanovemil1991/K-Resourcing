FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY Claims/Claims.csproj Claims/
RUN dotnet restore Claims/Claims.csproj
COPY Claims/ Claims/
RUN dotnet publish Claims/Claims.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER 1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "Claims.dll"]
