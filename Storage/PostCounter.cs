using System.IO;
using System;
using System.Text.Json;

namespace Storage;

public static class PostCounter
{
	private const string FilePath = "data/post_count.json";
	private static readonly object LockObj = new();

	private class CountData
	{
		public DateOnly Date { get; set; }
		public int Count { get; set; }
	}

	private static CountData Data = Load();

	public static bool TryIncrement()
	{
		lock (LockObj)
		{
			var today = DateOnly.FromDateTime(DateTime.UtcNow);

			if (Data.Date != today)
			{
				Data = new CountData { Date = today, Count = 0 };
			}

			if (Data.Count >= 100)
			{
				return false;
			}

			Data.Count++;
			Save();
			return true;
		}
	}

	public static int GetCurrentCount()
	{
		lock (LockObj)
		{
			var today = DateOnly.FromDateTime(DateTime.UtcNow);
			return Data.Date == today ? Data.Count : 0;
		}
	}

	private static CountData Load()
	{
		try
		{
			if (!File.Exists(FilePath))
				return new CountData { Date = DateOnly.FromDateTime(DateTime.UtcNow), Count = 0 };

			var json = File.ReadAllText(FilePath);
			return JsonSerializer.Deserialize<CountData>(json) ?? new CountData();
		}
		catch
		{
			return new CountData();
		}
	}

	private static void Save()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(FilePath, json);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"❌ Не вдалося зберегти лічильник постів: {ex.Message}");
		}
	}
}
