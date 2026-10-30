# --- Stádium 1: Build & Publish ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Cachování NuGet restore vrstvy (kopírují se pouze .csproj soubory)
COPY ["Directory.Build.props", "./"]
COPY ["src/UAI.ZeroVP.DTO/UAI.ZeroVP.DTO.csproj", "src/UAI.ZeroVP.DTO/"]
COPY ["src/UAI.ZeroVP.Data/UAI.ZeroVP.Data.csproj", "src/UAI.ZeroVP.Data/"]
COPY ["src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj", "src/UAI.ZeroVP.API/"]
RUN dotnet restore "src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj"

# Kopírování zdrojových kódů a publikace API
COPY src/ src/
WORKDIR "/src/src/UAI.ZeroVP.API"
RUN dotnet publish "UAI.ZeroVP.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Kompilace ultralehkého HealthCheck nástroje (vyhne se instalaci curl/wget v runtime)
WORKDIR /src/HealthCheck
RUN dotnet new console --framework net10.0 --no-restore && \
    echo 'using var client = new HttpClient(); var res = await client.GetAsync(args.Length > 0 ? args[0] : "http://localhost:8080/health"); Environment.Exit(res.IsSuccessStatusCode ? 0 : 1);' > Program.cs && \
    dotnet publish -c Release -o /app/publish/healthcheck /p:UseAppHost=false

# --- Stádium 2: Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Složka pro souborové logy musí existovat a patřit ne-rootovskému uživateli
# (jinak do ní aplikace nemůže zapisovat). Vytváří se před přepnutím na USER.
RUN mkdir -p /app/logs && chown $APP_UID /app/logs

# Běh pod ne-rootovským uživatelem ($APP_UID = 1654)
USER $APP_UID

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "UAI.ZeroVP.API.dll"]