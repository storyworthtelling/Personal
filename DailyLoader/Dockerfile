FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish DailyLoader.Worker/DailyLoader.Worker.csproj \
    -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "DailyLoader.Worker.dll"]
