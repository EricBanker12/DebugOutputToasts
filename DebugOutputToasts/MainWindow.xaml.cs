using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DebugOutputToasts
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string ConfigPath = null;
        private Configuration Config = null;
        private DebugOutputMonitor Monitor = null;
        private Queue<(string uid, string text)> MessageHistory = null;
        private DateTime NotificationWait = default;
        
        private Task FilterTask = null;
        private Task NotifyTask = null;
        
        private CancellationTokenSource FilterCancel = null;
        private CancellationTokenSource NotifyCancel = null;
        
        private System.Windows.Forms.NotifyIcon NotifyIcon = null;

        public MainWindow()
        {
            InitializeComponent();

            string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DebugOutputToasts");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            ConfigPath = Path.Combine(configDir, "config.ini");

            try
            {
                Config = Nett.Toml.ReadFile<Configuration>(ConfigPath);
            }
            catch (Exception)
            {
                try
                {
                    using(var reader = new FileStream(ConfigPath, FileMode.Open))
                        using(var writer = new FileStream(ConfigPath.Replace(".ini", ".backup.ini"), FileMode.Create))
                            reader.CopyTo(writer);
                }
                catch (Exception) { }

                Config = new Configuration();
                Nett.Toml.WriteFile(Config, ConfigPath);
            }

            Closed += new EventHandler(MainWindow_Closed);

            // Create tray icon and context menu
            NotifyIcon = new System.Windows.Forms.NotifyIcon();
            NotifyIcon.Icon = Properties.Resources.DebugOutputToasts;
            NotifyIcon.Visible = false;
            NotifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(Icon_MouseClick);

            NotifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu();
            EventHandler showEventHandler = (sender, e) => Icon_MouseClick(sender, new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 0, 0, 0, 0));
            EventHandler closeEventHandler = (sender, e) => this.Close();
            NotifyIcon.ContextMenu.MenuItems.Add("Show", showEventHandler);
            NotifyIcon.ContextMenu.MenuItems.Add("Exit", closeEventHandler);
        }

        #region Event Handlers
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Nett.Toml.WriteFile(Config, ConfigPath);
            
            ToastNotificationManagerCompat.Uninstall();

            if (Monitor != null) Monitor.Dispose();
            if (NotifyIcon != null) NotifyIcon.Dispose();
        }

        private void Icon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                NotifyIcon.Visible = false;
            }
        }

        private void StackPanel_Loaded_MessagePanel(object sender, RoutedEventArgs e)
        {
            MessageHistory = new Queue<(string uid, string text)>(Config.MaxDebugMessageHistory);

            // load config
            chkShowNotifications.IsChecked = Config.ShowNotifications;
            chkPlaySound.IsChecked = Config.PlaySound;
            chkThrottle.IsChecked = Config.Throttle;
            chkDebounce.IsChecked = Config.Debounce;
            chkMinimizeToTray.IsChecked = Config.MinimizeToTrayIcon;
            txtThrottle.Text = Config.ThrottleTime.ToString();
            txtDebounce.Text = Config.DebounceTime.ToString();

            foreach (var filter in Config.InclusionFilters)
                AddGridFilterRow(InclusionGrid, filter);

            foreach (var filter in Config.ExclusionFilters)
                AddGridFilterRow(ExclusionGrid, filter);

            foreach (var filter in Config.ReplacementFilters)
                AddGridFilterRow(ReplacementGrid, filter);

            // create monitor
            Monitor = new DebugOutputMonitor(DebugOutputHandler);
        }

        private void StackPanel_Unloaded_MessagePanel(object sender, RoutedEventArgs e)
        {
            Monitor.Dispose();
            Monitor = null;
            MessageHistory.Clear();
            MessageHistory = null;
        }

        // Notification Settings
        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var checkbox = (CheckBox)sender;
            switch (checkbox.Name)
            {
                case "chkShowNotifications":
                    Config.ShowNotifications = true;
                    break;
                case "chkPlaySound":
                    Config.PlaySound = true;
                    break;
                case "chkThrottle":
                    Config.Throttle = true;
                    break;
                case "chkDebounce":
                    Config.Debounce = true;
                    break;
                case "chkMinimizeToTray":
                    Config.MinimizeToTrayIcon = true;
                    break;
            }
            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        private void CheckBox_Unhecked(object sender, RoutedEventArgs e)
        {
            var checkbox = (CheckBox)sender;
            switch (checkbox.Name)
            {
                case "chkShowNotifications":
                    Config.ShowNotifications = false;
                    break;
                case "chkPlaySound":
                    Config.PlaySound = false;
                    break;
                case "chkThrottle":
                    Config.Throttle = false;
                    break;
                case "chkDebounce":
                    Config.Debounce = false;
                    break;
                case "chkMinimizeToTray":
                    Config.MinimizeToTrayIcon = false;
                    break;
            }
            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        private void TextBox_TextChanged_WaitTime(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            if (int.TryParse(textBox.Text, out int waitTime))
            {
                if (textBox.Background == Brushes.Red)
                    textBox.Background = Brushes.Transparent;

                if (waitTime < 0)
                {
                    waitTime = 0;
                    textBox.Text = "0";
                }

                switch (textBox.Name)
                {
                    case "txtThrottle":
                        Config.ThrottleTime = waitTime;
                        break;
                    case "txtDebounce":
                        Config.DebounceTime = waitTime;
                        break;
                }
                Nett.Toml.WriteFile(Config, ConfigPath);
            }
            else
            {
                textBox.Background = Brushes.Red;
            }
        }

        // Filter Match Case
        private void CheckBox_Checked_Aa(object sender, RoutedEventArgs e)
        {
            var label = (UIElement)((CheckBox)sender).Parent;
            var rowIndex = Grid.GetRow(label);
            
            if (InclusionGrid.Children.Contains(label))
                Config.InclusionFilters[rowIndex].IsMatchCase = true;
            
            else if (ExclusionGrid.Children.Contains(label))
                Config.ExclusionFilters[rowIndex].IsMatchCase = true;
            
            else if (ReplacementGrid.Children.Contains(label))
                Config.ReplacementFilters[rowIndex].IsMatchCase = true;

            if (FilterTask != null && !FilterTask.IsCompleted)
                if (FilterCancel != null && !FilterCancel.IsCancellationRequested)
                    FilterCancel.Cancel();

            FilterCancel = new CancellationTokenSource();
            FilterTask = ReapplyFilters(FilterCancel.Token).ContinueWith(t => { try { FilterCancel.Dispose(); } catch (Exception) { } });
            
            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        private void CheckBox_Unchecked_Aa(object sender, RoutedEventArgs e)
        {
            var label = (UIElement)((CheckBox)sender).Parent;
            var rowIndex = Grid.GetRow(label);
            
            if (InclusionGrid.Children.Contains(label))
                Config.InclusionFilters[rowIndex].IsMatchCase = false;
            
            else if (ExclusionGrid.Children.Contains(label))
                Config.ExclusionFilters[rowIndex].IsMatchCase = false;
            
            else if (ReplacementGrid.Children.Contains(label))
                Config.ReplacementFilters[rowIndex].IsMatchCase = false;

            if (FilterTask != null && !FilterTask.IsCompleted)
                if (FilterCancel != null && !FilterCancel.IsCancellationRequested)
                    FilterCancel.Cancel();

            FilterCancel = new CancellationTokenSource();
            FilterTask = ReapplyFilters(FilterCancel.Token).ContinueWith(t => { try { FilterCancel.Dispose(); } catch (Exception) { } });

            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        // Filter Regular Expression
        private void CheckBox_Checked_Rx(object sender, RoutedEventArgs e)
        {
            var label = (UIElement)((CheckBox)sender).Parent;
            var rowIndex = Grid.GetRow(label);
            
            if (InclusionGrid.Children.Contains(label))
                Config.InclusionFilters[rowIndex].IsUseRegex = true;
            
            else if (ExclusionGrid.Children.Contains(label))
                Config.ExclusionFilters[rowIndex].IsUseRegex = true;
            
            else if (ReplacementGrid.Children.Contains(label))
                Config.ReplacementFilters[rowIndex].IsUseRegex = true;

            if (FilterTask != null && !FilterTask.IsCompleted)
                if (FilterCancel != null && !FilterCancel.IsCancellationRequested)
                    FilterCancel.Cancel();

            FilterCancel = new CancellationTokenSource();
            FilterTask = ReapplyFilters(FilterCancel.Token).ContinueWith(t => { try { FilterCancel.Dispose(); } catch (Exception) { } });

            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        private void CheckBox_Unchecked_Rx(object sender, RoutedEventArgs e)
        {
            var label = (UIElement)((CheckBox)sender).Parent;
            var rowIndex = Grid.GetRow(label);
            
            if (InclusionGrid.Children.Contains(label))
                Config.InclusionFilters[rowIndex].IsUseRegex = false;
            
            else if (ExclusionGrid.Children.Contains(label))
                Config.ExclusionFilters[rowIndex].IsUseRegex = false;
            
            else if (ReplacementGrid.Children.Contains(label))
                Config.ReplacementFilters[rowIndex].IsUseRegex = false;

            if (FilterTask != null && !FilterTask.IsCompleted)
                if (FilterCancel != null && !FilterCancel.IsCancellationRequested)
                    FilterCancel.Cancel();

            FilterCancel = new CancellationTokenSource();
            FilterTask = ReapplyFilters(FilterCancel.Token).ContinueWith(t => { try { FilterCancel.Dispose(); } catch (Exception) { } });

            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        // Filter Up
        private void Button_Click_Up(object sender, RoutedEventArgs e)
        {
            var button = (UIElement)sender;
            var index = Grid.GetRow(button);
            if (InclusionGrid.Children.Contains(button))
            {
                SwapAdjacentFilters(Config.InclusionFilters, index, Direction.Up);
                SwapAdjacentGridRows(InclusionGrid, index, Direction.Up);
            }
            else if (ExclusionGrid.Children.Contains(button))
            {
                SwapAdjacentFilters(Config.ExclusionFilters, index, Direction.Up);
                SwapAdjacentGridRows(ExclusionGrid, index, Direction.Up);
            }
            else if (ReplacementGrid.Children.Contains(button))
            {
                SwapAdjacentFilters(Config.ReplacementFilters, index, Direction.Up);
                SwapAdjacentGridRows(ReplacementGrid, index, Direction.Up);
            }

            if (FilterTask != null && !FilterTask.IsCompleted)
                if (FilterCancel != null && !FilterCancel.IsCancellationRequested)
                    FilterCancel.Cancel();

            FilterCancel = new CancellationTokenSource();
            FilterTask = ReapplyFilters(FilterCancel.Token).ContinueWith(t => { try { FilterCancel.Dispose(); } catch (Exception) { } });


            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        // Filter Down
        private void Button_Click_Down(object sender, RoutedEventArgs e)
        {
            var button = (UIElement)sender;
            var index = Grid.GetRow(button);
            if (InclusionGrid.Children.Contains(button))
            {
                SwapAdjacentFilters(Config.InclusionFilters, index, Direction.Down);
                SwapAdjacentGridRows(InclusionGrid, index, Direction.Down);
            }
            else if (ExclusionGrid.Children.Contains(button))
            {
                SwapAdjacentFilters(Config.ExclusionFilters, index, Direction.Down);
                SwapAdjacentGridRows(ExclusionGrid, index, Direction.Down);
            }
            else if (ReplacementGrid.Children.Contains(button))
            {
                SwapAdjacentFilters(Config.ReplacementFilters, index, Direction.Down);
                SwapAdjacentGridRows(ReplacementGrid, index, Direction.Down);
            }

            if (FilterTask != null && !FilterTask.IsCompleted)
                if (FilterCancel != null && !FilterCancel.IsCancellationRequested)
                    FilterCancel.Cancel();

            FilterCancel = new CancellationTokenSource();
            FilterTask = ReapplyFilters(FilterCancel.Token).ContinueWith(t => { try { FilterCancel.Dispose(); } catch (Exception) { } });

            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        // Filter Delete
        private void Button_Click_Delete(object sender, RoutedEventArgs e)
        {
            UIElement button = (UIElement)sender;
            int rowIndex = Grid.GetRow(button);

            if (InclusionGrid.Children.Contains(button))
            {
                RemoveConfigFilter(Config, rowIndex, FilterType.Inclusion);
                RemoveGridFilterRow(InclusionGrid, rowIndex);
            }
            else if (ExclusionGrid.Children.Contains(button))
            {
                RemoveConfigFilter(Config, rowIndex, FilterType.Exclusion);
                RemoveGridFilterRow(ExclusionGrid, rowIndex);
            }
            else if (ReplacementGrid.Children.Contains(button))
            {
                RemoveConfigFilter(Config, rowIndex, FilterType.Replacement);
                RemoveGridFilterRow(ReplacementGrid, rowIndex);
            }

            if (FilterTask != null && !FilterTask.IsCompleted)
                if (FilterCancel != null && !FilterCancel.IsCancellationRequested)
                    FilterCancel.Cancel();

            FilterCancel = new CancellationTokenSource();
            FilterTask = ReapplyFilters(FilterCancel.Token).ContinueWith(t => { try { FilterCancel.Dispose(); } catch (Exception) { } });

            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        // Filter Add
        private void Button_Click_Add(object sender, RoutedEventArgs e)
        {
            UIElement button = (UIElement)sender;

            if (InclusionGrid.Children.Contains(button))
                AddGridFilterRow(InclusionGrid, AddConfigFilter(Config, FilterType.Inclusion));

            else if (ExclusionGrid.Children.Contains(button))
                AddGridFilterRow(ExclusionGrid, AddConfigFilter(Config, FilterType.Exclusion));

            else if (ReplacementGrid.Children.Contains(button))
                AddGridFilterRow(ReplacementGrid, AddConfigFilter(Config, FilterType.Replacement));


            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        // Find Text
        private void TextBox_TextChanged_Find(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            int rowIndex = Grid.GetRow(textBox);
            
            if (InclusionGrid.Children.Contains(textBox))
                Config.InclusionFilters[rowIndex].Find = textBox.Text;
            
            else if (ExclusionGrid.Children.Contains(textBox))
                Config.ExclusionFilters[rowIndex].Find = textBox.Text;
            
            else if (ReplacementGrid.Children.Contains(textBox))
                Config.ReplacementFilters[rowIndex].Find = textBox.Text;

            if (FilterTask != null && !FilterTask.IsCompleted)
                if (FilterCancel != null && !FilterCancel.IsCancellationRequested)
                    FilterCancel.Cancel();

            FilterCancel = new CancellationTokenSource();
            FilterTask = ReapplyFilters(FilterCancel.Token).ContinueWith(t => { try { FilterCancel.Dispose(); } catch (Exception) { } });

            Nett.Toml.WriteFile(Config, ConfigPath);
        }

        // Replace Text
        private void TextBox_TextChanged_Replace(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            int rowIndex = Grid.GetRow(textBox);

            if (ReplacementGrid.Children.Contains(textBox))
                Config.ReplacementFilters[rowIndex].Replace = textBox.Text;

            if (FilterTask != null && !FilterTask.IsCompleted)
                if (FilterCancel != null && !FilterCancel.IsCancellationRequested)
                    FilterCancel.Cancel();

            FilterCancel = new CancellationTokenSource();
            FilterTask = ReapplyFilters(FilterCancel.Token).ContinueWith(t => { try { FilterCancel.Dispose(); } catch (Exception) { } });

            Nett.Toml.WriteFile(Config, ConfigPath);
        }
        #endregion

        #region Event Helpers
        public enum FilterType
        {
            Inclusion = 1,
            Exclusion = 2,
            Replacement = 3,
        }

        public enum Direction
        {
            Up = 1,
            Down = 2,
        }

        private void DebugOutputHandler(DebugOutputMonitor.DebugOutput debugOutput)
        {
            // get process name
            string processName = null;
            try { using (var p = Process.GetProcessById((int)debugOutput.dwProcessId)) processName = p.ProcessName; } catch (Exception) { }

            // build output
            string text = $"{processName ?? debugOutput.dwProcessId.ToString()}\r\n{debugOutput.outputDebugString}";

            // add to history
            if (MessageHistory.Count >= Config.MaxDebugMessageHistory)
            {
                var removed = MessageHistory.Dequeue();
                if (MessagePanel.Children[MessagePanel.Children.Count - 1].Uid == removed.uid)
                    MessagePanel.Children.RemoveAt(MessagePanel.Children.Count - 1);
            }
            string uid = Guid.NewGuid().ToString();
            MessageHistory.Enqueue((uid: uid, text: text));

            // apply filters
            text = ApplyFilters(text);
            if (string.IsNullOrEmpty(text)) return;

            Message msg = new Message(text, uid);

            // add to message panel
            TextBlock textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
            textBlock.Inlines.Add(new Run(msg.title) { FontWeight = FontWeights.Bold });
            if (!string.IsNullOrEmpty(msg.body))
            {
                textBlock.Inlines.Add("\r\n");
                textBlock.Inlines.Add(msg.body);
            }
            textBlock.Uid = msg.uid;
            MessagePanel.Children.Insert(0, textBlock);


            // add notification
            if (Config.ShowNotifications)
            {
                if (Config.Throttle && NotificationWait > DateTime.Now) return;

                if (Config.Debounce)
                {
                    // There is a weird bug, such that when DebugOutputStrings are rapidly sent,
                    // NotifyCancel.Dispose will also dispose the DebugOutputMonitor instance's CancellationTokenSource.
                    // Likely due to due to some form of hash collision and CancellationTokenSource lacking thread safety.
                    if (NotifyTask == null || NotifyTask.IsCompleted || NotifyTask.IsCanceled)
                    {
                        NotifyCancel = new CancellationTokenSource();
                        NotifyTask = Notify(msg, NotifyCancel.Token);//.ContinueWith(t => { try { NotifyCancel.Dispose(); } catch (Exception) { } });
                    }
                    else
                    {
                        if (NotifyCancel != null && !NotifyCancel.IsCancellationRequested) NotifyCancel.Cancel();

                        NotifyCancel = new CancellationTokenSource();
                        NotifyTask = Notify(msg, NotifyCancel.Token);//.ContinueWith(t => { try { NotifyCancel.Dispose(); } catch (Exception) { } });
                    }
                }
                else
                {
                    NotifyTask = Notify(msg);
                }
            }
        }

        private async Task Notify(Message msg, CancellationToken? token = null)
        {
            // debounce
            if (Config.Debounce && token.HasValue)
            {
                await Task.Delay(Config.DebounceTime, token.Value);
                token.Value.ThrowIfCancellationRequested();
            }

            if (Config.Throttle) { NotificationWait = DateTime.Now.AddMilliseconds(Config.ThrottleTime); }

            ToastContentBuilder toast = new ToastContentBuilder().AddText(msg.title);
            if (!string.IsNullOrEmpty(msg.body)) toast.AddText(msg.body);
            if (!Config.PlaySound) toast.AddAudio(null, silent: true);
            toast.SetToastDuration(ToastDuration.Short);
            toast.Show();
        }

        public struct Message
        {
            public string uid;
            public string title;
            public string body;

            public Message(string text, string uid)
            {
                this.uid = uid;
                var matchTitleBody = Regex.Match(text, @"^(.*?)\r\n(.*)$", RegexOptions.Singleline);
                if (matchTitleBody.Success)
                {
                    title = matchTitleBody.Groups[1].Value;
                    body = matchTitleBody.Groups[2].Value;
                }
                else
                {
                    title = text;
                    body = null;
                }
            }
        }

        private async Task ReapplyFilters(CancellationToken token)
        {
            var filtered = await Task.Run(() => MessageHistory
                .Select(msg => (uid: msg.uid, text: ApplyFilters(msg.text)))
                .Where(msg => !string.IsNullOrEmpty(msg.text))
                .Select(msg => new Message(msg.text, msg.uid))
                .Reverse()
                , token);
                
            token.ThrowIfCancellationRequested();
            MessagePanel.Children.Clear();
            foreach (var msg in filtered)
            {
                // add to message panel
                TextBlock textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
                textBlock.Inlines.Add(new Run(msg.title) { FontWeight = FontWeights.Bold });
                if (!string.IsNullOrEmpty(msg.body))
                {
                    textBlock.Inlines.Add("\r\n");
                    textBlock.Inlines.Add(msg.body);
                }
                textBlock.Uid = msg.uid;
                MessagePanel.Children.Add(textBlock);
            };
        }
        
        private string ApplyFilters(string text)
        {
            if (Config.InclusionFilters.All(filter => string.IsNullOrEmpty(ApplyFilter(text, filter, FilterType.Inclusion))))
                return string.Empty;

            if (Config.ExclusionFilters.Any(filter => string.IsNullOrEmpty(ApplyFilter(text, filter, FilterType.Exclusion))))
                return string.Empty;

            foreach (var filter in Config.ReplacementFilters)
                text = ApplyFilter(text, filter, FilterType.Replacement);

            return text;
        }

        private string ApplyFilter(string input, Filter filter, FilterType filterType)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (string.IsNullOrEmpty(filter.Find)) return input;

            string output = string.Empty;

            switch (filterType)
            {
                case FilterType.Inclusion:
                    if (filter.IsUseRegex)
                    {
                        if (filter.IsMatchCase)
                        {
                            if (Regex.IsMatch(input, filter.Find, RegexOptions.None)) return input;
                        }
                        else
                        {
                            if (Regex.IsMatch(input, filter.Find, RegexOptions.IgnoreCase)) return input;
                        }
                    }
                    else
                    {
                        if (filter.IsMatchCase)
                        {
                            if (input.Contains(filter.Find)) return input;
                        }
                        else
                        {
                            if (input.ToLower().Contains(filter.Find.ToLower())) return input;
                        }
                    }
                    return output;
                case FilterType.Exclusion:
                    if (filter.IsUseRegex)
                    {
                        if (filter.IsMatchCase)
                        {
                            if (Regex.IsMatch(input, filter.Find, RegexOptions.None)) return output;
                        }
                        else
                        {
                            if (Regex.IsMatch(input, filter.Find, RegexOptions.IgnoreCase)) return output;
                        }
                    }
                    else
                    {
                        if (filter.IsMatchCase)
                        {
                            if (input.Contains(filter.Find)) return output;
                        }
                        else
                        {
                            if (input.ToLower().Contains(filter.Find.ToLower())) return output;
                        }
                    }
                    return input;
                case FilterType.Replacement:
                    if (filter.IsUseRegex)
                    {
                        if (filter.IsMatchCase)
                        {
                            output = Regex.Replace(input, filter.Find, ((ReplacementFilter)filter).Replace, RegexOptions.None);
                        }
                        else
                        {
                            output = Regex.Replace(input, filter.Find, ((ReplacementFilter)filter).Replace, RegexOptions.IgnoreCase);
                        }
                    }
                    else
                    {
                        if (filter.IsMatchCase)
                        {
                            output = input.Replace(filter.Find, ((ReplacementFilter)filter).Replace);
                        }
                        else
                        {
                            output = Regex.Replace(input, Regex.Escape(filter.Find), ((ReplacementFilter)filter).Replace.Replace("$", "$$"), RegexOptions.IgnoreCase);
                        }
                    }
                    return output;
            }
            return output;
        }

        private static Filter AddConfigFilter(Configuration config, FilterType filterType)
        {
            Filter retVal = null;
            Filter[] newFilters;
            Filter[] oldFilters;

            switch (filterType)
            {
                case FilterType.Inclusion:
                    retVal = new Filter();
                    newFilters = new Filter[config.InclusionFilters.Length + 1];
                    newFilters[newFilters.Length - 1] = retVal;
                    oldFilters = config.InclusionFilters;
                    for (int i = 0; i < oldFilters.Length; i++) newFilters[i] = oldFilters[i];
                    config.InclusionFilters = newFilters;
                    break;
                case FilterType.Exclusion:
                    retVal = new Filter();
                    newFilters = new Filter[config.ExclusionFilters.Length + 1];
                    newFilters[newFilters.Length - 1] = retVal;
                    oldFilters = config.ExclusionFilters;
                    for (int i = 0; i < oldFilters.Length; i++) newFilters[i] = oldFilters[i];
                    config.ExclusionFilters = newFilters;
                    break;
                case FilterType.Replacement:
                    retVal = new ReplacementFilter();
                    newFilters = new ReplacementFilter[config.ReplacementFilters.Length + 1];
                    newFilters[newFilters.Length - 1] = retVal;
                    oldFilters = config.ReplacementFilters;
                    for (int i = 0; i < oldFilters.Length; i++) newFilters[i] = oldFilters[i];
                    config.ReplacementFilters = (ReplacementFilter[])newFilters;
                    break;
            }
            return retVal;
        }

        private static void RemoveConfigFilter(Configuration config, int index, FilterType filterType)
        {

            Filter[] newFilters = null;
            Filter[] oldFilters = null;
            
            switch (filterType)
            {
                case FilterType.Inclusion:
                    newFilters = new Filter[config.InclusionFilters.Length - 1];
                    oldFilters = config.InclusionFilters;
                    break;
                case FilterType.Exclusion:
                    newFilters = new Filter[config.ExclusionFilters.Length - 1];
                    oldFilters = config.ExclusionFilters;
                    break;
                case FilterType.Replacement:
                    newFilters = new ReplacementFilter[config.ReplacementFilters.Length - 1];
                    oldFilters = config.ReplacementFilters;
                    break;
            }

            if (index > 0 && index < oldFilters.Length - 1)
            {
                for (int i = 0; i < index; i++) newFilters[i] = oldFilters[i];
                for (int i = index; i < newFilters.Length; i++) newFilters[i] = oldFilters[i + 1];
            }
            else if (index <= 0)
            {
                for (int i = 0; i < newFilters.Length; i++) newFilters[i] = oldFilters[i + 1];
            }
            else if (index >= oldFilters.Length - 1 && newFilters.Length > 0)
            {
                for (int i = 0; i < newFilters.Length; i++) newFilters[i] = oldFilters[i];
            }

            switch (filterType)
            {
                case FilterType.Inclusion:
                    config.InclusionFilters = newFilters;
                    break;
                case FilterType.Exclusion:
                    config.ExclusionFilters = newFilters;
                    break;
                case FilterType.Replacement:
                    config.ReplacementFilters = (ReplacementFilter[])newFilters;
                    break;
            }
        }

        private void AddGridFilterRow(Grid grid, Filter filter)
        {
            string label = null;
            string label2 = null;
            switch (grid.Name)
            {
                case "InclusionGrid":
                    label = "Include:";
                    break;
                case "ExclusionGrid":
                    label = "Exclude:";
                    break;
                case "ReplacementGrid":
                    label = "Find:";
                    label2 = "Replace:";
                    break;
            }

            // limit
            if (grid.RowDefinitions.Count > 100) return;

            // create row elements
            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            int rowIndex = grid.RowDefinitions.Count() - 2;

            var btnAdd = grid.Children[0];
            Grid.SetRow(btnAdd, rowIndex + 1);

            var margin = new Thickness(2);
            var lblInput = new Label() { Content = label };
            var txtInput = new TextBox() { Margin = margin, Text = filter.Find };
            var chkCase = new CheckBox() { Content = "A/a", ToolTip = "Match Case", IsChecked = filter.IsMatchCase };
            var lblCase = new Label() { Content = chkCase };
            var chkRegex = new CheckBox() { Content = "Rx", ToolTip = "Regular Expression", IsChecked = filter.IsUseRegex };
            var lblRegex = new Label() { Content = chkRegex };
            var btnUp = new Button() { Width = 25, Margin = margin, Padding = new Thickness(0, 0, 1, 2), Content = "▲" };
            var btnDown = new Button() { Width = 25, Margin = margin, Padding = new Thickness(0, 0, 1, 0), Content = "▼" };
            var btnDelete = new Button() { Width = 50, Margin = margin, Content = "Delete" };

            grid.Children.Add(lblInput);
            grid.Children.Add(txtInput);
            grid.Children.Add(lblCase);
            grid.Children.Add(lblRegex);
            grid.Children.Add(btnUp);
            grid.Children.Add(btnDown);
            grid.Children.Add(btnDelete);

            txtInput.TextChanged += TextBox_TextChanged_Find;
            chkCase.Checked += CheckBox_Checked_Aa;
            chkCase.Unchecked += CheckBox_Unchecked_Aa;
            chkRegex.Checked += CheckBox_Checked_Rx;
            chkRegex.Unchecked += CheckBox_Unchecked_Rx;
            btnUp.Click += Button_Click_Up;
            btnDown.Click += Button_Click_Down;
            btnDelete.Click += Button_Click_Delete;

            Grid.SetRow(lblInput, rowIndex);
            Grid.SetRow(txtInput, rowIndex);
            Grid.SetRow(lblCase, rowIndex);
            Grid.SetRow(lblRegex, rowIndex);
            Grid.SetRow(btnUp, rowIndex);
            Grid.SetRow(btnDown, rowIndex);
            Grid.SetRow(btnDelete, rowIndex);

            Grid.SetColumn(lblInput, 0);
            Grid.SetColumn(txtInput, 1);

            if (string.IsNullOrEmpty(label2))
            {
                Grid.SetColumn(lblCase, 2);
                Grid.SetColumn(lblRegex, 3);
                Grid.SetColumn(btnUp, 4);
                Grid.SetColumn(btnDown, 5);
                Grid.SetColumn(btnDelete, 6);
            }
            else
            {
                var lblReplace = new Label() { Content = label2 };
                var txtReplace = new TextBox() { Margin = margin, Text = ((ReplacementFilter)filter).Replace };

                grid.Children.Add(lblReplace);
                grid.Children.Add(txtReplace);

                txtReplace.TextChanged += TextBox_TextChanged_Replace;

                Grid.SetRow(lblReplace, rowIndex);
                Grid.SetRow(txtReplace, rowIndex);

                Grid.SetColumn(lblReplace, 2);
                Grid.SetColumn(txtReplace, 3);
                Grid.SetColumn(lblCase, 4);
                Grid.SetColumn(lblRegex, 5);
                Grid.SetColumn(btnUp, 6);
                Grid.SetColumn(btnDown, 7);
                Grid.SetColumn(btnDelete, 8);
            }
        }

        private void RemoveGridFilterRow(Grid grid, int index)
        {
            List<UIElement> toRemove = new List<UIElement>();
            
            foreach(UIElement element in grid.Children)
            {
                if (Grid.GetRow(element) == index)
                    toRemove.Add(element);
                if (Grid.GetRow(element) > index)
                    Grid.SetRow(element, Grid.GetRow(element) - 1);
            }

            foreach (UIElement element in toRemove)
                grid.Children.Remove(element);

            grid.RowDefinitions.RemoveAt(index);
        }

        private static void SwapAdjacentFilters(Filter[] filters, int filterIndex, Direction direction)
        {
            if (direction == Direction.Up && filterIndex < 1) return;
            if (direction == Direction.Down && filterIndex > filters.Length - 2) return;

            int adjacentIndex = filterIndex;
            if (direction == Direction.Up) adjacentIndex--;
            if (direction == Direction.Down) adjacentIndex++;

            var target = filters[filterIndex];
            filters[filterIndex] = filters[adjacentIndex];
            filters[adjacentIndex] = target;
        }

        private void SwapAdjacentGridRows(Grid grid, int rowIndex, Direction direction)
        {
            if (direction == Direction.Up && rowIndex < 1) return;
            if (direction == Direction.Down && rowIndex > grid.RowDefinitions.Count - 3) return;

            var adjacentIndex = rowIndex;
            if (direction == Direction.Up) adjacentIndex--;
            else if (direction == Direction.Down) adjacentIndex++;
            else return;

            // switch ui rows
            var rowElements = new List<UIElement>();
            var adjacentElements = new List<UIElement>();
            for (int i = 0; i < grid.Children.Count; i++)
            {
                var element = grid.Children[i];
                var elementRowIndex = Grid.GetRow(element);
                if (elementRowIndex == adjacentIndex)
                    adjacentElements.Add(element);
                else if (elementRowIndex == rowIndex)
                    rowElements.Add(element);
            }

            foreach (var element in adjacentElements)
                Grid.SetRow(element, rowIndex);

            foreach (var element in rowElements)
                Grid.SetRow(element, adjacentIndex);
        }
        #endregion

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (Config.MinimizeToTrayIcon && this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                NotifyIcon.Visible = true;
                //NotifyIcon.ShowBalloonTip(5000, "Minimized to a tray icon", "Left-click to show. Right-click for options.", System.Windows.Forms.ToolTipIcon.None);
            }
        }
    }
}
