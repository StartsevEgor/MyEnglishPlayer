using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls; // Для Button, Slider
using System.Windows.Input;    // Для мыши
using System.Windows.Threading; // Для таймера
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace MyEnglishPlayer
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _timer;
        private bool _isPlaying = false;
        private bool _isFullscreen = false;
        private bool _isTimerWork = false;
        private bool _showRusSubs = false;
        private Process _translationProcess;
        private LocalDictionary _dictionary = new LocalDictionary();
        private AnkiManager _ankiManager = new AnkiManager();
        private DispatcherTimer _statusTimer;
        private DispatcherTimer _cursorTimer;
        private List<SubtitleItem> _engSubtitles = new List<SubtitleItem>();
        private List<SubtitleItem> _rusSubtitles = new List<SubtitleItem>();
        private SubtitleItem _currentEngItem = null;
        private SubtitleItem _currentRusItem = null;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTimers();

            // ЗАГРУЖАЕМ СЛОВАРЬ
            LoadDictionary();
        }

        private async void LoadDictionary()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dict.json");
            ShowStatus("Загрузка словаря...");

            await _dictionary.LoadAsync(path);

            ShowStatus("Словарь готов!");
        }

        private void InitializeTimers()
        {
            // Основной таймер видео (0.2 сек)
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Таймер скрытия курсора (3 секунды бездействия)
            _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _cursorTimer.Tick += OnCursorTimerTick; // Подписываемся на отдельный метод

            // Таймер скрытия статуса (сообщения "Сохранено" и т.д.)
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _statusTimer.Tick += (s, e) => { StatusBar.Visibility = Visibility.Collapsed; _statusTimer.Stop(); };
        }

        // Новый метод, который вызывается, когда прошло 3 секунды тишины
        private void OnCursorTimerTick(object sender, EventArgs e)
        {
            if (_isPlaying && !DictionaryPopup.IsOpen) // Не скрываем, если открыт словарь!
            {
                Mouse.OverrideCursor = Cursors.None;

                // Опционально: Можно здесь же скрыть и панель управления
                // ControlPanel.Visibility = Visibility.Collapsed; 
            }

            // Останавливаем таймер, чтобы он не срабатывал снова и снова
            _cursorTimer.Stop();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Убиваем процесс сервера, чтобы он не висел в диспетчере задач
            if (_translationProcess != null && !_translationProcess.HasExited)
            {
                try
                {
                    _translationProcess.Kill();
                    _translationProcess.WaitForExit(); // Ждем, пока точно умрет
                }
                catch { /* Игнорируем ошибки при закрытии */ }
            }
        }

        private void ShowStatus(string message)
        {
            // Если мы в полном экраре - не показываем (или можно показывать, но вы просили скрывать)
            // Но обычно уведомления полезны. 
            // Если вы хотите, чтобы при НАЖАТИИ на кнопку полного экрана он исчезал - это в другом методе.
            // А здесь мы просто показываем сообщение.

            StatusText.Text = message;
            StatusBar.Visibility = Visibility.Visible;

            // Перезапускаем таймер (чтобы продлить время, если сообщения идут подряд)
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "Video Files|*.mp4;*.avi;*.mkv|All Files|*.*";

            if (dialog.ShowDialog() == true)
            {
                string videoPath = dialog.FileName;

                _ankiManager.SetDeckName(videoPath);

                // 1. Запуск видео
                MyPlayer.Source = new Uri(videoPath);
                MyPlayer.Play();
                _isPlaying = true;

                // 2. Умный поиск субтитров (твой старый код)
                FindAndLoadSubtitles(videoPath);
            }
        }

        // НОВЫЙ МЕТОД: ЛОГИКА ПОИСКА
        private void FindAndLoadSubtitles(string videoPath)
        {
            // Очищаем старые
            _engSubtitles.Clear();
            _rusSubtitles.Clear();

            string folder = Path.GetDirectoryName(videoPath);
            if (string.IsNullOrEmpty(folder)) return;

            // Получаем все .srt файлы в папке с фильмом
            string[] srtFiles = Directory.GetFiles(folder, "*.srt");

            if (srtFiles.Length == 0)
            {
                // Сценарий 1: Субтитров нет вообще. Ничего не делаем.
                return;
            }

            // Пытаемся найти автоматически
            string autoEng = null;
            string autoRus = null;

            // Если файлов ровно 2, проверяем их имена
            if (srtFiles.Length == 2)
            {
                // Ищем файл, где в названии есть "english" или "eng" (без учета регистра)
                autoEng = srtFiles.FirstOrDefault(f => f.ToLower().Contains("english") || f.ToLower().Contains("eng"));
                // Ищем файл, где в названии есть "russian" или "rus"
                autoRus = srtFiles.FirstOrDefault(f => f.ToLower().Contains("russian") || f.ToLower().Contains("rus"));
            }

            // Сценарий 2: Файлов ровно 2 И мы успешно опознали оба языка
            if (autoEng != null && autoRus != null)
            {
                _engSubtitles = SrtParser.Parse(autoEng);
                _rusSubtitles = SrtParser.Parse(autoRus);

                string statusMsg = "";
                if (_engSubtitles.Count > 0) statusMsg += "EN ";
                if (_rusSubtitles.Count > 0) statusMsg += "RU";

                if (!string.IsNullOrEmpty(statusMsg))
                    ShowStatus($"Субтитры загружены: {statusMsg}");
                else
                    ShowStatus("Файлы найдены, но они пустые.");
            }
            else
            {
                MyPlayer.Pause();
                // Сценарий 3: Файлов куча ИЛИ имена непонятные -> Просим пользователя выбрать вручную

                // Спрашиваем про Английский
                var result = MessageBox.Show("Автопоиск не сработал. Хотите выбрать файл АНГЛИЙСКИХ субтитров вручную?",
                                             "Выбор субтитров", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    var openSubDlg = new Microsoft.Win32.OpenFileDialog();
                    openSubDlg.Filter = "SRT Subtitles|*.srt";
                    openSubDlg.Title = "Выберите АНГЛИЙСКИЕ субтитры";
                    openSubDlg.InitialDirectory = folder; // Открываем сразу ту же папку

                    if (openSubDlg.ShowDialog() == true)
                        _engSubtitles = SrtParser.Parse(openSubDlg.FileName);
                    ShowStatus("Английские субтитры" + (_engSubtitles.Count > 0 ? " " : " не ") + "загружены");
                }

                // Спрашиваем про Русский (только если пользователь не отказался на прошлом шаге, но для простоты спросим отдельно)
                result = MessageBox.Show("Хотите выбрать файл РУССКИХ субтитров вручную?",
                                         "Выбор субтитров", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    var openSubDlg = new Microsoft.Win32.OpenFileDialog();
                    openSubDlg.Filter = "SRT Subtitles|*.srt";
                    openSubDlg.Title = "Выберите РУССКИЕ субтитры";
                    openSubDlg.InitialDirectory = folder;

                    if (openSubDlg.ShowDialog() == true)
                        _rusSubtitles = SrtParser.Parse(openSubDlg.FileName);
                    ShowStatus("Русские субтитры" + (_rusSubtitles.Count > 0 ? " " : " не ") + "загружены");
                }
            }
        }

        // 2. Это событие срабатывает САМО, когда видео прогрузилось и готово играть
        // Именно здесь мы узнаем длину видео
        private void MyPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (MyPlayer.NaturalDuration.HasTimeSpan)
            {
                // Ставим максимум слайдера равным количеству секунд в видео
                TimeSlider.Maximum = MyPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                TimeSlider.Minimum = 0;
            }
        }

        // 3. Таймер: двигает ползунок вслед за видео
        private void Timer_Tick(object sender, EventArgs e)
        {
            // Если видео не загружено, ничего не делаем
            if (MyPlayer.Source == null || !MyPlayer.NaturalDuration.HasTimeSpan) return;

            // Поднимаем флаг: "Сейчас работает таймер, не реагируй на изменение Value"
            _isTimerWork = true;

            // Обновляем слайдер
            TimeSlider.Value = MyPlayer.Position.TotalSeconds;

            // Опускаем флаг
            _isTimerWork = false;


            var currentTime = MyPlayer.Position;

            // 1. Ищем подходящий АНГЛИЙСКИЙ субтитр
            // (Start <= Current <= End)
            var engItem = _engSubtitles.FirstOrDefault(s => s.Start <= currentTime && s.End >= currentTime);

            // Если субтитр изменился (появился новый или исчез старый)
            if (engItem != _currentEngItem)
            {
                _currentEngItem = engItem;
                UpdateEnglishText(engItem?.Text); // Метод отрисовки (см. ниже)
            }

            // 2. Ищем подходящий РУССКИЙ субтитр
            var rusItem = _rusSubtitles.FirstOrDefault(s => s.Start <= currentTime && s.End >= currentTime);

            if (rusItem != _currentRusItem)
            {
                _currentRusItem = rusItem;

                // Всегда обновляем текст (даже если он скрыт), чтобы он был актуальным при включении
                RusSubText.Text = rusItem?.Text ?? "";
            }

            // УПРАВЛЕНИЕ ВИДИМОСТЬЮ
            // Показываем только если:
            // 1. Пользователь включил их (клавиша W)
            // 2. Текст не пустой
            if (_showRusSubs && !string.IsNullOrEmpty(RusSubText.Text))
            {
                RusSubText.Visibility = Visibility.Visible;
            }
            else
            {
                RusSubText.Visibility = Visibility.Collapsed;
            }
        }

        // Вспомогательный метод для разбивки английского текста на слова
        private void UpdateEnglishText(string text)
        {
            EngSubText.Inlines.Clear();
            if (string.IsNullOrEmpty(text)) return;

            string[] parts = text.Split(new char[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var run = new System.Windows.Documents.Run(part);
                var link = new System.Windows.Documents.Hyperlink(run);

                link.Foreground = System.Windows.Media.Brushes.White;
                link.TextDecorations = null;
                link.Cursor = Cursors.Hand;

                // Передаем "грязное" слово (с запятыми и т.д.)
                link.Click += (s, e) => Word_Click(part);

                EngSubText.Inlines.Add(link);
                EngSubText.Inlines.Add(new System.Windows.Documents.Run(" "));
            }
        }

        private void Word_Click(string rawWord)
        {
            MyPlayer.Pause();
            _isPlaying = false;
            Mouse.OverrideCursor = null;

            ResetPopup();

            // 1. ОЧИСТКА:
            // Оставляем апостроф, чтобы слова типа "won't" или "'cause" дошли до поиска целыми
            string cleanWord = Regex.Replace(rawWord, "[^a-zA-Z0-9-']", "");

            // 2. УМНЫЙ ПОИСК
            var result = SmartFind(cleanWord);

            if (result != null)
            {
                // Показываем слово из словаря.
                // Если нашли "won't" — покажет "won't".
                // Если "won't" не было, и нашли "will" — покажет "will".
                PopupWord.Text = result.word;

                PopupTranscription.Text = result.transcription ?? "";

                if (result.translates != null && result.translates.Count > 0)
                {
                    PopupMainDef.Text = Capitalize(result.translates[0]);

                    if (result.translates.Count > 1)
                    {
                        var rest = result.translates.Skip(1);
                        PopupRestDefs.Text = string.Join(", ", rest);

                        SeparatorLine.Visibility = Visibility.Visible;
                        PopupRestDefs.Visibility = Visibility.Visible;
                        BtnExpand.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    PopupMainDef.Text = "Перевод не найден";
                }
            }
            else
            {
                // Если словарь вообще ничего не нашел (даже после обрезки)
                PopupWord.Text = cleanWord;
                PopupMainDef.Text = "Нет в словаре";
            }

            DictionaryPopup.IsOpen = true;
            AlignPopup();
        }

        // ОБНОВЛЕННЫЙ SMARTFIND
        private DictItem SmartFind(string word)
        {
            if (string.IsNullOrEmpty(word)) return null;

            // --- ЭТАП 1: ПРИОРИТЕТ ТОЧНОГО СОВПАДЕНИЯ ---
            // Ищем "won't", "trying", "'cause" как есть.
            // Если в json есть такая статья — возвращаем её и выходим.
            var directMatch = _dictionary.Find(word);
            if (directMatch != null) return directMatch;


            // --- ЭТАП 2: АПОСТРОФ В НАЧАЛЕ (для 'cause) ---
            // Если точного совпадения нет, а слово начинается с ', пробуем убрать его
            // 'cause -> cause
            if (word.StartsWith("'"))
            {
                string wordWithoutPrefix = word.TrimStart('\'');
                var prefixMatch = _dictionary.Find(wordWithoutPrefix);
                if (prefixMatch != null) return prefixMatch;
            }


            // --- ЭТАП 3: АПОСТРОФ В КОНЦЕ (СУФФИКСЫ) ---
            // Если слово содержит апостроф (но не в начале), пробуем найти основу
            if (word.Contains("'"))
            {
                // 1. Отрицания (n't)
                if (word.EndsWith("n't"))
                {
                    string lower = word.ToLower();
                    // Исключения (mapping)
                    if (lower == "won't") return _dictionary.Find("will");
                    if (lower == "can't") return _dictionary.Find("can");
                    if (lower == "shan't") return _dictionary.Find("shall");

                    // Общее правило: couldn't -> could
                    return _dictionary.Find(word.Substring(0, word.Length - 3));
                }



                if (word.EndsWith("'m"))
                {
                    return _dictionary.Find(word.Substring(0, word.Length - 2));
                }

                // 2. Притяжательный падеж или is ('s)
                if (word.EndsWith("'s"))
                {
                    return _dictionary.Find(word.Substring(0, word.Length - 2));
                }

                // 3. Are ('re)
                if (word.EndsWith("'re"))
                {
                    return _dictionary.Find(word.Substring(0, word.Length - 3));
                }

                // 4. Have ('ve)
                if (word.EndsWith("'ve"))
                {
                    return _dictionary.Find(word.Substring(0, word.Length - 3));
                }

                // 5. Will ('ll)
                if (word.EndsWith("'ll"))
                {
                    return _dictionary.Find(word.Substring(0, word.Length - 3));
                }

                // 6. Would/Had ('d)
                if (word.EndsWith("'d"))
                {
                    return _dictionary.Find(word.Substring(0, word.Length - 2));
                }
            }
            // --- ЭТАП 4: ОБРАБОТКА ОКОНЧАНИЙ (-s, -ed, -ing) ---

            // 1. Множественное число или глагол 3-го лица (-s)
            if (word.EndsWith("s") && word.Length > 3)
            {
                // tries -> try (меняем ies на y)
                if (word.EndsWith("ies"))
                {
                    var match = _dictionary.Find(word.Substring(0, word.Length - 3) + "y");
                    if (match != null) return match;
                }

                // watches -> watch (убираем es)
                if (word.EndsWith("es"))
                {
                    var match = _dictionary.Find(word.Substring(0, word.Length - 2));
                    if (match != null) return match;
                }

                // cats -> cat (убираем s)
                var simpleMatch = _dictionary.Find(word.Substring(0, word.Length - 1));
                if (simpleMatch != null) return simpleMatch;
            }

            // 2. Прошедшее время (-ed)
            if (word.EndsWith("ed") && word.Length > 4)
            {
                // tried -> try (ied -> y)
                if (word.EndsWith("ied"))
                {
                    var match = _dictionary.Find(word.Substring(0, word.Length - 3) + "y");
                    if (match != null) return match;
                }

                // liked -> like (d -> пусто, если слово заканчивается на e)
                // Это "слепая" проверка: пробуем убрать только 'd' (получится like)
                var matchD = _dictionary.Find(word.Substring(0, word.Length - 1));
                if (matchD != null) return matchD;

                // played -> play (ed -> пусто)
                var matchED = _dictionary.Find(word.Substring(0, word.Length - 2));
                if (matchED != null) return matchED;

                // stopped -> stop (ped -> p) - редкий кейс с удвоением, можно добавить позже
            }

            // 3. Продолженное время (-ing)
            if (word.EndsWith("ing") && word.Length > 5)
            {
                // making -> make (ing -> e)
                var matchE = _dictionary.Find(word.Substring(0, word.Length - 3) + "e");
                if (matchE != null) return matchE;

                // trying -> try (ing -> пусто)
                var matchIng = _dictionary.Find(word.Substring(0, word.Length - 3));
                if (matchIng != null) return matchIng;
            }

            return null;
        }

        private void ToggleDetails_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем текущий режим по углу стрелки или по Wrapping
            if (ArrowRotate.Angle == 0)
            {
                // РАСКРЫВАЕМ
                PopupRestDefs.TextWrapping = TextWrapping.Wrap; // Много строк

                ArrowRotate.Angle = 180; // Стрелка вверх (Пункт 4 требований)
            }
            else
            {
                // СВОРАЧИВАЕМ
                PopupRestDefs.TextWrapping = TextWrapping.NoWrap; // Одна строка

                ArrowRotate.Angle = 0; // Стрелка вниз
            }

            AlignPopup(); // Пересчитываем высоту окна
        }

        private void AddToDictionary_Click(object sender, RoutedEventArgs e)
        {
            string englishWord = PopupWord.Text;

            // Собираем перевод из главного и дополнительных (если развернуты)
            string russianDef = PopupMainDef.Text;
            if (!string.IsNullOrEmpty(PopupRestDefs.Text) && PopupRestDefs.Visibility == Visibility.Visible)
            {
                russianDef += ", " + PopupRestDefs.Text;
            }

            // Простая валидация
            if (string.IsNullOrWhiteSpace(englishWord) || englishWord == "Загрузка..." || russianDef == "Загрузка...")
            {
                return;
            }

            // ПИШЕМ В ФАЙЛ
            _ankiManager.AddWord(englishWord, russianDef);

            // Показываем уведомление
            ShowStatus($"Слово '{englishWord}' записано в файл!");

            // Закрываем попап
            ResetPopup();
        }

        // Вспомогательная функция: Первая буква заглавная
        private string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        // 3. Вспомогательный метод для выравнивания (чтобы хвостик был над мышкой)
        private void AlignPopup()
        {
            // Принудительно обновляем верстку, чтобы WPF пересчитал ActualHeight с новым текстом
            DictionaryPopup.UpdateLayout();

            if (DictionaryPopup.Child is FrameworkElement content)
            {
                // Сдвигаем окно вверх на его высоту
                DictionaryPopup.VerticalOffset = -content.ActualHeight;
            }
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Если это таймер двигает ползунок - выходим, видео мотать не надо
            if (_isTimerWork) return;

            // Если мы здесь - значит, это пользователь изменил слайдер (кликнул или потянул)
            // Мгновенно мотаем видео на новую позицию
            MyPlayer.Position = TimeSpan.FromSeconds(TimeSlider.Value);
        }

        // 4. Play / Pause
        private void PlayPause_Click(object sender, RoutedEventArgs e) // Привяжи это к кнопке вместо старого
        {
            if (DictionaryPopup.IsOpen)
            {
                ResetPopup();
            }

            if (_isPlaying)
            {
                MyPlayer.Pause();
                _isPlaying = false;

                // !!! ДОБАВИТЬ !!!
                Mouse.OverrideCursor = null; // Возвращаем курсор
                _cursorTimer.Stop();         // Останавливаем таймер исчезновения
            }
            else
            {
                MyPlayer.Play();
                _isPlaying = true;

                // !!! ДОБАВИТЬ !!!
                _cursorTimer.Start();        // Запускаем таймер исчезновения
            }
        }

        // 5. Перемотка кнопками (+- 5 сек)
        private void Seek_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            // Tag у нас теперь "-5" или "5" (секунд)
            double offset = double.Parse(button.Tag.ToString());

            MyPlayer.Position += TimeSpan.FromSeconds(offset);
        }

        // 6. Слайдер (начали тянуть)
        private void Slider_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Чтобы звук не "заикался" при перетаскивании, ставим на паузу
            if (DictionaryPopup.IsOpen)
            {
                ResetPopup();
            }

            MyPlayer.Pause();
        }

        // 7. Слайдер (отпустили)
        private void Slider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Мы НЕ мотаем видео здесь (это уже сделал ValueChanged).
            // Мы просто возвращаем воспроизведение, если оно было.

            // Логика: если до этого мы считали, что видео играет (_isPlaying), то снимаем с паузы
            if (_isPlaying)
            {
                MyPlayer.Play();
            }
        }

        private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
        {
            StatusBar.Visibility = Visibility.Collapsed;
            _statusTimer.Stop();

            if (_isFullscreen)
            {
                // ВЫХОДИМ из полного экрана
                this.WindowStyle = WindowStyle.SingleBorderWindow; // Возвращаем шапку
                this.ResizeMode = ResizeMode.CanResize;            // Разрешаем менять размер
                this.WindowState = WindowState.Normal;             // Возвращаем обычный размер

                // Делаем окно поверх всех только если нужно, сейчас возвращаем как было
                this.Topmost = false;

                _isFullscreen = false;
            }
            else
            {
                // ВХОДИМ в полный экран
                this.WindowStyle = WindowStyle.None;   // Убираем шапку
                this.ResizeMode = ResizeMode.NoResize; // Важно! Чтобы перекрыть панель задач
                this.WindowState = WindowState.Maximized; // Растягиваем

                // Опционально: окно поверх всех других окон
                // this.Topmost = true; 

                _isFullscreen = true;
            }
        }

        // Обработка нажатий клавиш на уровне всего окна
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Пробел - Пауза/Воспроизведение
            if (e.Key == Key.Space)
            {
                PlayPause_Click(null, null);
                e.Handled = true;
            }
            // Клавиша A (или стрелка влево) - Назад на 5 секунд
            else if (e.Key == Key.A || e.Key == Key.Left)
            {
                if (MyPlayer.Source != null && MyPlayer.NaturalDuration.HasTimeSpan)
                {
                    MyPlayer.Position -= TimeSpan.FromSeconds(5);
                }
                e.Handled = true;
            }
            // Клавиша D (или стрелка вправо) - Вперед на 5 секунд
            else if (e.Key == Key.D || e.Key == Key.Right)
            {
                if (MyPlayer.Source != null && MyPlayer.NaturalDuration.HasTimeSpan)
                {
                    MyPlayer.Position += TimeSpan.FromSeconds(5);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.W)
            {
                _showRusSubs = !_showRusSubs; // Меняем true на false и наоборот

                ShowStatus(_showRusSubs ? "Русские субтитры: ВКЛ" : "Русские субтитры: ВЫКЛ");

                // Чтобы изменение применилось мгновенно (не дожидаясь тика таймера),
                // можно принудительно обновить видимость прямо сейчас:
                if (_showRusSubs && !string.IsNullOrEmpty(RusSubText.Text))
                    RusSubText.Visibility = Visibility.Visible;
                else
                    RusSubText.Visibility = Visibility.Collapsed;

                e.Handled = true;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            // 1. Если курсор был скрыт — показываем его
            if (Mouse.OverrideCursor == Cursors.None)
            {
                Mouse.OverrideCursor = null; // null возвращает стандартную стрелку

                // Если вы скрывали панель управления, то здесь её надо вернуть:
                // ControlPanel.Visibility = Visibility.Visible;
            }

            // 2. Сбрасываем таймер бездействия
            _cursorTimer.Stop();

            // Запускаем таймер снова, ТОЛЬКО если видео играет
            if (_isPlaying)
            {
                _cursorTimer.Start();
            }

            // 3. (Ваш старый код) Закрытие словаря при уходе мыши вниз
            if (DictionaryPopup.IsOpen)
            {
                var mousePos = e.GetPosition(this);
                double windowHeight = MainGrid.ActualHeight;
                double bottomZoneHeight = ControlPanel.ActualHeight > 0 ? ControlPanel.ActualHeight : 60;

                if (windowHeight - mousePos.Y < bottomZoneHeight)
                {
                    ResetPopup();
                }
            }
        }

        private void ResetPopup()
        {
            DictionaryPopup.IsOpen = false;

            // 1. Сбрасываем тексты на дефолт
            PopupWord.Text = "";
            PopupTranscription.Text = "";
            PopupMainDef.Text = "Загрузка..."; // Пункт 1 требований
            PopupRestDefs.Text = "";

            // 2. Скрываем лишние элементы
            SeparatorLine.Visibility = Visibility.Collapsed;
            PopupRestDefs.Visibility = Visibility.Collapsed;
            BtnExpand.Visibility = Visibility.Collapsed;

            // 3. Сбрасываем состояние стрелки и списка (сворачиваем обратно)
            ArrowRotate.Angle = 0;
            PopupRestDefs.TextWrapping = TextWrapping.NoWrap; // В одну строку
        }

        // При наведении на область субтитров показываем "+"
        private void Subtitles_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_isPlaying || (!string.IsNullOrEmpty(EngSubText.Text))) // Показываем, только если есть текст
            {
                BtnAddPhrase.Visibility = Visibility.Visible;
            }
        }

        private void Subtitles_MouseLeave(object sender, MouseEventArgs e)
        {
            // Скрываем, если мышь ушла, НО не скрываем, если мышь на самой кнопке (WPF это сам обработает, т.к. кнопка внутри Grid)
            BtnAddPhrase.Visibility = Visibility.Collapsed;
        }

        private void BtnAddPhrase_Click(object sender, RoutedEventArgs e)
        {
            // 1. Ставим на паузу
            if (_isPlaying)
            {
                MyPlayer.Pause();
                _isPlaying = false;
                Mouse.OverrideCursor = null;
            }

            // --- АНГЛИЙСКИЙ (Оставляем как есть, или тоже можно искать ближайший, но пока берем текущий) ---
            string engText = "";
            if (_currentEngItem != null)
            {
                engText = _currentEngItem.Text.Replace("\n", " ");
            }
            // Если английский тоже "мигает", можно применить поиск, как для русского ниже.
            // Но пока предположим, что английские синхронны (как ты и писал).

            TbPhraseSourceEng.Text = engText;
            TbPhraseInputEng.Text = engText;

            // --- РУССКИЙ (НОВАЯ ЛОГИКА: ПОИСК БЛИЖАЙШЕГО) ---
            string rusContext = "";
            string currentRus = "";

            if (_rusSubtitles != null && _rusSubtitles.Count > 0)
            {
                TimeSpan currentPos = MyPlayer.Position;

                // Ищем индекс субтитра, который ближе всего к текущему времени
                int closestIndex = -1;
                double minDistance = double.MaxValue;

                for (int i = 0; i < _rusSubtitles.Count; i++)
                {
                    var item = _rusSubtitles[i];

                    // Вычисляем расстояние от курсора до интервала субтитра
                    double dist = 0;

                    if (currentPos < item.Start)
                        dist = (item.Start - currentPos).TotalSeconds; // Курсор левее
                    else if (currentPos > item.End)
                        dist = (currentPos - item.End).TotalSeconds;   // Курсор правее
                    else
                        dist = 0; // Курсор прямо внутри субтитра (идеальное совпадение)

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestIndex = i;
                    }
                }

                // Если нашли что-то (а список не пуст, значит найдем)
                if (closestIndex != -1)
                {
                    // 1. ПРЕДЫДУЩИЙ
                    if (closestIndex > 0)
                    {
                        rusContext += $"[Prev]: {_rusSubtitles[closestIndex - 1].Text.Replace("\n", " ")}\n";
                    }

                    // 2. ТЕКУЩИЙ (Ближайший)
                    currentRus = _rusSubtitles[closestIndex].Text.Replace("\n", " ");
                    rusContext += $"[Curr]: {currentRus}\n";

                    // 3. СЛЕДУЮЩИЙ
                    if (closestIndex < _rusSubtitles.Count - 1)
                    {
                        rusContext += $"[Next]: {_rusSubtitles[closestIndex + 1].Text.Replace("\n", " ")}";
                    }
                }
            }
            else
            {
                rusContext = "(Русские субтитры не загружены)";
            }

            // Заполняем поля
            TbPhraseSourceRus.Text = rusContext;
            TbPhraseInputRus.Text = currentRus;

            // Открываем окно
            PhraseDialogOverlay.Visibility = Visibility.Visible;
        }

        private void BtnSavePhrase_Click(object sender, RoutedEventArgs e)
        {
            string eng = TbPhraseInputEng.Text;
            string rus = TbPhraseInputRus.Text;

            if (string.IsNullOrWhiteSpace(eng) || string.IsNullOrWhiteSpace(rus))
            {
                MessageBox.Show("Поля не могут быть пустыми!");
                return;
            }

            // БЫЛО: _ankiManager.AddCard(eng, rus);
            // СТАЛО:
            _ankiManager.AddPhrase(eng, rus); // <--- Пишет в ...-phrases.txt

            ShowStatus("Фраза сохранена!");
            PhraseDialogOverlay.Visibility = Visibility.Collapsed;

            // Снимаем с паузы (по желанию)
            MyPlayer.Play();
            _isPlaying = true;
            Mouse.OverrideCursor = null; // Показываем курсор (он сам скроется таймером)
        }

        private void BtnCancelPhrase_Click(object sender, RoutedEventArgs e)
        {
            PhraseDialogOverlay.Visibility = Visibility.Collapsed;
        }
    }
}