// /Bot/KeyboardFactory.cs
using Telegram.Bot.Types.ReplyMarkups;

namespace Bot;

public static class KeyboardFactory
{
    public static ReplyKeyboardMarkup MainButtons()
    {
        return new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "📤 Опублікувати оголошення" },
            new KeyboardButton[] { "📢 Перейти на канал" }
        })
        {
            ResizeKeyboard = true
        };
    }

    public static InlineKeyboardMarkup ConfirmButtons()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] {
                InlineKeyboardButton.WithCallbackData("✅ Підтвердити", "confirm_publish"),
                InlineKeyboardButton.WithCallbackData("❌ Скасувати", "cancel")
            }
        });
    }

    public static InlineKeyboardMarkup DeleteButtonByMessageId(int channelMessageId)
    {
        return new InlineKeyboardMarkup(new[]
        {
        InlineKeyboardButton.WithCallbackData("🗑 Видалити", $"delete_post_msg_{channelMessageId}")
    });
    }

}
