# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Кеш залежностей
COPY TelegramOlxBot.csproj ./
RUN dotnet restore TelegramOlxBot.csproj

# Копіюємо код і публікуємо
COPY . .
RUN dotnet publish TelegramOlxBot.csproj -c Release -o /out \
    /p:PublishReadyToRun=true \
    /p:UseAppHost=false

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

# (Опційно) таймзона у логах + безпека + менше метаданих
ENV DOTNET_EnableDiagnostics=0 \
    COMPlus_EnableDiagnostics=0 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Встановимо tzdata лише якщо потрібна локальна таймзона (і приберемо кеші)
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && \
    rm -rf /var/lib/apt/lists/*

# Стандартна таймзона для логів (можеш змінити на UTC, якщо не критично)
ENV TZ=Europe/Kyiv

# Додаємо unprivileged user
RUN useradd -r -u 10001 -m appuser && chown -R appuser:appuser /app
USER appuser

# Копіюємо артефакти публікації
COPY --from=build /out ./

# HEALTHCHECK: перевіряємо, що процес запущений
HEALTHCHECK --interval=30s --timeout=5s --retries=5 CMD sh -c 'pgrep -f "TelegramOlxBot.dll" >/dev/null || exit 1'

# Запуск
ENTRYPOINT ["dotnet", "TelegramOlxBot.dll"]
