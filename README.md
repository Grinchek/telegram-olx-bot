# 🤖 Telegram OLX Bot

Бот для автоматичної публікації оголошень з OLX у Telegram-каналі з перевіркою оплати через Monobank.

---

## 🧩 Можливості

- 📤 Публікація оголошень з OLX за посиланням
- 💳 Платіж через банку Monobank (15 грн)
- ⏱ Автоматична перевірка оплати кожні 1–5 хвилин
- 🗑 Видалення оголошення через кнопку під постом:
  - Адмін може видалити будь-яке
  - Користувач — лише своє
- 🔐 Адмін-режим
- 🌐 Підтримка `.env` для чутливих даних

---

## ⚙️ Встановлення

### 1. Клонувати репозиторій

bash:
git clone https://github.com/your-username/telegram-olx-bot.git
cd telegram-olx-bot
2. Створити .env файл
Заповни змінні у .env:

env

BOT_TOKEN=your_telegram_bot_token
MONOBANK_TOKEN=your_monobank_api_token
MONOBANK_ACCOUNT_ID=your_monobank_account_id
CHANNEL_USERNAME=@your_channel_username
BOT_USERNAME=@your_bot_username
ADMIN_CHAT_ID=123456789
MONO_JAR_URL=https://send.monobank.ua/jar/your_jar_id
MONOBANK_ACCOUNT_ID=Account id of the jar
❗ Не публікуй цей файл у відкритий доступ!
3. Встановити залежності та запустити
bash:
dotnet restore
dotnet run
