# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копіюємо .csproj окремо для кешу залежностей
COPY *.csproj ./
RUN dotnet restore

# Копіюємо решту коду
COPY . .
RUN dotnet publish -c Release -o /app /p:PublishReadyToRun=true

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

# Таймзона для логів
ENV TZ=Europe/Kyiv
ENV DOTNET_EnableDiagnostics=0

# Копіюємо збірку з build stage
COPY --from=build /app ./

# У тебе бот — консольний long polling, тому порт не потрібен
ENTRYPOINT ["dotnet","OlxTelegramBot.dll"]
