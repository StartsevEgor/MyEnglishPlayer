using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MyEnglishPlayer
{
    public class SubtitleItem
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string Text { get; set; }
    }

    public static class SrtParser
    {
        public static List<SubtitleItem> Parse(string filePath)
        {
            var items = new List<SubtitleItem>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return items;

            var lines = File.ReadAllLines(filePath);

            // Регулярка для времени
            var timeRegex = new Regex(@"(\d{2}:\d{2}:\d{2},\d{3})\s-->\s(\d{2}:\d{2}:\d{2},\d{3})");

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                var match = timeRegex.Match(line);

                if (match.Success)
                {
                    var item = new SubtitleItem();
                    string start = match.Groups[1].Value.Replace(',', '.');
                    string end = match.Groups[2].Value.Replace(',', '.');

                    if (TimeSpan.TryParse(start, out TimeSpan s) && TimeSpan.TryParse(end, out TimeSpan e))
                    {
                        item.Start = s;
                        item.End = e;
                    }

                    var textLines = new List<string>();
                    int j = i + 1;
                    while (j < lines.Length && !string.IsNullOrWhiteSpace(lines[j]))
                    {
                        textLines.Add(lines[j]);
                        j++;
                    }

                    // Собираем текст
                    string rawText = string.Join("\n", textLines);

                    // !!! ГЛАВНОЕ ИЗМЕНЕНИЕ ЗДЕСЬ !!!
                    // Мы вызываем функцию очистки перед присваиванием
                    item.Text = StripHtmlTags(rawText);

                    items.Add(item);
                    i = j;
                }
            }
            return items;
        }

        // Вспомогательный метод для удаления HTML-тегов
        private static string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // <.*?> означает: найти любой символ между < и >
            // Заменяем найденное на пустоту
            return Regex.Replace(input, "<.*?>", string.Empty);
        }
    }
}