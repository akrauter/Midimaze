# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies first (separate layer so `dotnet restore` only
# re-runs when a .csproj actually changes, not on every source edit).
COPY ["src/MidiMaze.Server/MidiMaze.Server.csproj", "src/MidiMaze.Server/"]
RUN dotnet restore "src/MidiMaze.Server/MidiMaze.Server.csproj"

COPY src/MidiMaze.Server/ src/MidiMaze.Server/
WORKDIR "/src/src/MidiMaze.Server"
RUN dotnet publish "MidiMaze.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

RUN useradd -m appuser
COPY --from=build /app/publish .
USER appuser

# The aspnet image ships neither curl nor wget, so the app probes its own /health endpoint.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["dotnet", "MidiMaze.Server.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "MidiMaze.Server.dll"]
