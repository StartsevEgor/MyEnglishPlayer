using System;
using System.IO;
using System.Text;
using System.Windows;

namespace MyEnglishPlayer
{
    public class AnkiManager
    {
        private string _storageFolder;
        private string _currentDeckName;

        public AnkiManager()
        {
            // Папка: Мои Документы / Anki
            _storageFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Anki");

            if (!Directory.Exists(_storageFolder))
                Directory.CreateDirectory(_storageFolder);
        }

        public void SetDeckName(string videoFilename)
        {
            string rawName = Path.GetFileNameWithoutExtension(videoFilename);
            _currentDeckName = string.Join("_", rawName.Split(Path.GetInvalidFileNameChars()));
        }

        // Публичный метод для СЛОВ
        public void AddWord(string english, string russian)
        {
            WriteToFile(english, russian, "-words.txt");
        }

        // Публичный метод для ФРАЗ
        public void AddPhrase(string english, string russian)
        {
            WriteToFile(english, russian, "-phrases.txt");
        }

        // Приватный метод с общей логикой записи
        private void WriteToFile(string english, string russian, string fileSuffix)
        {
            if (string.IsNullOrEmpty(_currentDeckName))
            {
                MessageBox.Show("Сначала откройте видео, чтобы создать файл!");
                return;
            }

            try
            {
                // Формируем имя файла: "Matrix-words.txt" или "Matrix-phrases.txt"
                string fileName = _currentDeckName + fileSuffix;
                string filePath = Path.Combine(_storageFolder, fileName);

                // Чистим данные для TXT формата Anki
                string safeEng = english.Replace(";", ",");
                string safeRus = russian.Replace(";", ",");

                // Заменяем переносы строк на HTML тег <br>, чтобы Anki отображал их красиво
                safeEng = safeEng.Replace("\n", "<br>").Replace("\r", "");
                safeRus = safeRus.Replace("\n", "<br>").Replace("\r", "");

                // Формируем строку: Русский (вопрос) ; Английский (ответ)
                string line = $"{safeRus};{safeEng}{Environment.NewLine}";

                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка записи в файл: {ex.Message}");
            }
        }
    }
}