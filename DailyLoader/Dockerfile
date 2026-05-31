FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish DailyLoader.Worker/DailyLoader.Worker.csproj \
    -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
# PDFsharp has no embedded fonts; install a system sans-serif for PDF generation.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENTRYPOINT ["dotnet", "DailyLoader.Worker.dll"]
