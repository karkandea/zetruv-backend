FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Zetruv.Api/Zetruv.Api.csproj src/Zetruv.Api/
RUN dotnet restore src/Zetruv.Api/Zetruv.Api.csproj

COPY . .
RUN dotnet publish src/Zetruv.Api/Zetruv.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Zetruv.Api.dll"]
