using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace MyEnglishPlayer
{
    // Структура одной записи в JSON
    public class DictItem
    {
        public string word { get; set; }
        public string transcription { get; set; }
        public List<string> translates { get; set; }
    }

    public class LocalDictionary
    {
        // Словарь для мгновенного поиска: Ключ (слово) -> Данные
        private Dictionary<string, DictItem> _cache;

        public bool IsLoaded => _cache != null;

        public async Task LoadAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"Файл словаря не найден: {filePath}");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    string json = File.ReadAllText(filePath);
                    // Десериализуем массив
                    var list = JsonSerializer.Deserialize<List<DictItem>>(json);

                    // Превращаем в Dictionary для быстрого поиска (O(1))
                    // Ключи делаем маленькими буквами (Lower), чтобы поиск был нечувствителен к регистру
                    _cache = new Dictionary<string, DictItem>();

                    foreach (var item in list)
                    {
                        if (!string.IsNullOrWhiteSpace(item.word))
                        {
                            string key = item.word.ToLower().Trim();
                            if (!_cache.ContainsKey(key))
                            {
                                _cache[key] = item;
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки словаря: {ex.Message}");
            }
        }

        public DictItem Find(string word)
        {
            if (_cache == null || string.IsNullOrEmpty(word)) return null;

            // Приводим искомое слово к нижнему регистру
            string key = word.ToLower().Trim();

            if (_cache.TryGetValue(key, out var item))
            {
                return item;
            }

            // Если прямого совпадения нет, можно попробовать убрать окончание 's' или 'ed' (простая лемматизация)
            // Но для начала хватит и точного поиска
            return null;
        }
    }
}