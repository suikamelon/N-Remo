using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace N_Remo
{
    public static class LanguageManager
    {
        private static ResourceDictionary? languageResources;
        private static bool classHandlerRegistered;

        private static readonly DependencyProperty OriginalTitleProperty =
            DependencyProperty.RegisterAttached(
                "OriginalTitle", typeof(string), typeof(LanguageManager));
        private static readonly DependencyProperty OriginalTextProperty =
            DependencyProperty.RegisterAttached(
                "OriginalText", typeof(string), typeof(LanguageManager));
        private static readonly DependencyProperty OriginalContentProperty =
            DependencyProperty.RegisterAttached(
                "OriginalContent", typeof(string), typeof(LanguageManager));
        private static readonly DependencyProperty OriginalHeaderProperty =
            DependencyProperty.RegisterAttached(
                "OriginalHeader", typeof(string), typeof(LanguageManager));
        private static readonly DependencyProperty OriginalToolTipProperty =
            DependencyProperty.RegisterAttached(
                "OriginalToolTip", typeof(string), typeof(LanguageManager));

        public static string CurrentLanguage { get; private set; } = "ja-JP";

        public static void Initialize(string language)
        {
            if (!classHandlerRegistered)
            {
                EventManager.RegisterClassHandler(
                    typeof(Window),
                    FrameworkElement.LoadedEvent,
                    new RoutedEventHandler((sender, _) =>
                    {
                        if (sender is Window window)
                            Apply(window);
                    }));
                classHandlerRegistered = true;
            }

            SetLanguage(language, applyToOpenWindows: false);
        }

        public static void SetLanguage(
            string language,
            bool applyToOpenWindows = true)
        {
            CurrentLanguage = string.Equals(
                language, "ja-JP", StringComparison.OrdinalIgnoreCase)
                    ? "ja-JP"
                    : "en-US";

            ResourceDictionary newLanguageResources =
                LoadLanguageResources(CurrentLanguage);

            if (System.Windows.Application.Current != null)
            {
                if (languageResources != null)
                {
                    System.Windows.Application.Current.Resources
                        .MergedDictionaries.Remove(languageResources);
                }

                System.Windows.Application.Current.Resources
                    .MergedDictionaries.Add(newLanguageResources);
            }

            languageResources = newLanguageResources;

            CultureInfo culture = CultureInfo.GetCultureInfo(CurrentLanguage);
            CultureInfo.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            if (!applyToOpenWindows || System.Windows.Application.Current == null)
                return;

            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is Edit editWindow)
                    editWindow.RefreshLocalizedState();
                Apply(window);
            }
        }

        public static string Get(string source)
        {
            if (languageResources != null &&
                languageResources.Contains(source) &&
                languageResources[source] is string translated)
            {
                return translated;
            }

            return source;
        }

        private static ResourceDictionary LoadLanguageResources(
            string language)
        {
            string executableDirectory =
                Path.GetDirectoryName(Environment.ProcessPath ?? "") ??
                AppContext.BaseDirectory;

            string externalPath = Path.Combine(
                executableDirectory,
                "Language",
                $"{language}.xaml");

            if (File.Exists(externalPath))
            {
                try
                {
                    // Source代入だけでは解析が遅延し、呼び出し元のtry/catchを
                    // 抜けた後にXAML例外が発生することがある。ここで強制的に
                    // 読み込み、壊れた外部辞書を確実に検出する。
                    using FileStream stream = File.OpenRead(externalPath);
                    if (XamlReader.Load(stream) is ResourceDictionary dictionary)
                        return dictionary;
                }
                catch (Exception ex)
                {
                    // 外部ファイルが壊れている場合は内蔵版を使用する。
                    AutomationDiagnosticLog.Write(
                        $"外部言語ファイルの読込失敗 ({language}): {ex.Message}");
                }
            }

            try
            {
                Uri resourceUri = new(
                    $"/N-Remo;component/Language/{language}.xaml",
                    UriKind.RelativeOrAbsolute);
                return (ResourceDictionary)System.Windows.Application
                    .LoadComponent(resourceUri);
            }
            catch (Exception ex) when (!string.Equals(
                language, "ja-JP", StringComparison.OrdinalIgnoreCase))
            {
                // 内蔵英語辞書にも問題があっても起動不能にしない。
                // 設定を日本語へ戻し、次回起動も安全な辞書を使用する。
                AutomationDiagnosticLog.Write(
                    $"内蔵言語ファイルの読込失敗 ({language})。日本語へ復旧: {ex}");
                CurrentLanguage = "ja-JP";
                Properties.Settings.Default.Language = "ja-JP";
                Properties.Settings.Default.Save();
                PersistentAppSettings.SaveFromProperties();
                return LoadLanguageResources("ja-JP");
            }
        }

        public static void Apply(Window window)
        {
            HashSet<object> visited = new();
            ApplyObject(window, window is MainWindow, visited);
        }

        private static void ApplyObject(
            object item,
            bool isMainWindow,
            HashSet<object> visited)
        {
            if (!visited.Add(item))
                return;

            if (item is Window window)
            {
                string original = Remember(
                    window, OriginalTitleProperty, window.Title);
                window.Title = Get(original);
            }

            if (item is TextBlock textBlock &&
                !IsExpression(textBlock, TextBlock.TextProperty))
            {
                string original = Remember(
                    textBlock, OriginalTextProperty, textBlock.Text);
                textBlock.Text = Get(original);
            }

            // 見出し・説明文として使用している読み取り専用TextBox。
            // TextBoxButtonNameやTextBoxIRなど、ユーザー入力欄は対象外にする。
            if (item is System.Windows.Controls.TextBox textBox &&
                (textBox.IsReadOnly || !textBox.IsHitTestVisible) &&
                !IsExpression(textBox, System.Windows.Controls.TextBox.TextProperty))
            {
                string original = Remember(
                    textBox, OriginalTextProperty, textBox.Text);
                textBox.Text = Get(original);
            }

            if (item is HeaderedContentControl headeredContent &&
                headeredContent.Header is string contentHeader &&
                !IsExpression(headeredContent, HeaderedContentControl.HeaderProperty))
            {
                string original = Remember(
                    headeredContent, OriginalHeaderProperty, contentHeader);
                headeredContent.Header = Get(original);
            }

            if (item is HeaderedItemsControl headeredItems &&
                headeredItems.Header is string itemsHeader &&
                !IsExpression(headeredItems, HeaderedItemsControl.HeaderProperty))
            {
                string original = Remember(
                    headeredItems, OriginalHeaderProperty, itemsHeader);
                headeredItems.Header = Get(original);
            }

            if (item is ContentControl contentControl &&
                contentControl.Content is string content &&
                !IsExpression(contentControl, ContentControl.ContentProperty) &&
                !(isMainWindow && contentControl is System.Windows.Controls.Button))
            {
                string original = Remember(
                    contentControl, OriginalContentProperty, content);
                contentControl.Content = Get(original);
            }

            if (item is FrameworkElement element &&
                element.ToolTip is string toolTip &&
                !IsExpression(element, FrameworkElement.ToolTipProperty))
            {
                string original = Remember(
                    element, OriginalToolTipProperty, toolTip);
                element.ToolTip = Get(original);
            }

            if (item is System.Windows.Controls.DataGrid dataGrid)
            {
                foreach (DataGridColumn column in dataGrid.Columns)
                {
                    if (column.Header is not string header)
                        continue;

                    if (IsExpression(column, DataGridColumn.HeaderProperty))
                        continue;

                    string original = Remember(
                        column, OriginalHeaderProperty, header);
                    column.Header = Get(original);
                }
            }

            if (item is FrameworkElement frameworkElement &&
                frameworkElement.ContextMenu != null)
            {
                ApplyObject(
                    frameworkElement.ContextMenu,
                    isMainWindow,
                    visited);
            }

            if (item is DependencyObject dependencyObject)
            {
                foreach (object child in
                    LogicalTreeHelper.GetChildren(dependencyObject))
                {
                    ApplyObject(child, isMainWindow, visited);
                }

            }
        }

        private static string Remember(
            DependencyObject target,
            DependencyProperty property,
            string current)
        {
            if (target.GetValue(property) is string original)
                return original;

            target.SetValue(property, current);
            return current;
        }

        private static bool IsExpression(
            DependencyObject target,
            DependencyProperty property)
        {
            return DependencyPropertyHelper
                .GetValueSource(target, property)
                .IsExpression;
        }
    }
}
