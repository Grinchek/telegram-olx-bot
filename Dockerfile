# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# окремо копіюємо .csproj для кешу залежностей
COPY TelegramOlxBot.csproj ./
RUN dotnet restore TelegramOlxBot.csproj

# копіюємо решту коду і публішуємо
COPY . .
RUN dotnet publish TelegramOlxBot.csproj -c Release -o /out \
    /p:PublishReadyToRun=true \
    /p:UseAppHost=false

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

# (опціонально) зручний таймзон у логах
ENV TZ=Europe/Kyiv
ENV DOTNET_EnableDiagnostics=0

# копіюємо артефакти публікації
COPY --from=build /out ./

# запускаємо саме наш DLL (консольний long-polling бот)
ENTRYPOINT ["dotnet", "TelegramOlxBot.dll"]
