using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 13 (UX polish): attached property для базового markdown rendering в TextBlock.
// Поддерживает: **bold**, *italic* / _italic_, `inline code`, ```code blocks```, lists.
// Это не полный CommonMark — только то что Claude часто использует в ответах.
//
// Использование в XAML:
//   <TextBlock local:MarkdownInline.Text="{Binding Body}" />
// При изменении Text — пересоздаём Inlines TextBlock'а.
public static class MarkdownInline
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(MarkdownInline),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);

    private static readonly Brush CodeBackgroundBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFF));
    private static readonly Brush CodeForegroundBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0x5B, 0xFF));
    private static readonly FontFamily MonoFontFamily = new("Cascadia Mono, Consolas, monospace");

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb)
        {
            return;
        }

        tb.Inlines.Clear();
        var text = e.NewValue as string ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        RenderMarkdown(tb, text);
    }

    private static void RenderMarkdown(TextBlock tb, string text)
    {
        // 1. Code blocks (``` ... ```) — обрабатываем первыми, чтобы внутри не парсить markdown.
        // 2. Внутри обычного текста: bold, italic, inline code, bullet/numbered list, newlines.

        var blockPattern = new Regex("```(?:[a-zA-Z0-9]+)?\\r?\\n(.+?)```", RegexOptions.Singleline);
        var lastIndex = 0;
        foreach (Match m in blockPattern.Matches(text))
        {
            if (m.Index > lastIndex)
            {
                RenderText(tb, text.Substring(lastIndex, m.Index - lastIndex));
            }
            RenderCodeBlock(tb, m.Groups[1].Value);
            lastIndex = m.Index + m.Length;
        }

        if (lastIndex < text.Length)
        {
            RenderText(tb, text.Substring(lastIndex));
        }
    }

    private static void RenderCodeBlock(TextBlock tb, string code)
    {
        if (tb.Inlines.Count > 0)
        {
            tb.Inlines.Add(new LineBreak());
        }
        var run = new Run(code.TrimEnd())
        {
            FontFamily = MonoFontFamily,
            FontSize = (tb.FontSize > 0 ? tb.FontSize : 13) - 1,
            Background = CodeBackgroundBrush,
            Foreground = CodeForegroundBrush,
        };
        tb.Inlines.Add(run);
        tb.Inlines.Add(new LineBreak());
    }

    private static void RenderText(TextBlock tb, string text)
    {
        // Разбиваем на строки чтобы поддержать bullet lists и сохранить newlines.
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // bullet / numbered list marker
            var bulletMatch = Regex.Match(line, @"^(\s*)([\-\*•]|\d+\.)\s+(.*)$");
            if (bulletMatch.Success)
            {
                if (i > 0)
                {
                    tb.Inlines.Add(new LineBreak());
                }
                var indent = bulletMatch.Groups[1].Value.Length;
                tb.Inlines.Add(new Run(new string(' ', indent) + "•  ")
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0x5B, 0xFF)),
                    FontWeight = FontWeights.Bold
                });
                AppendInlineFormatted(tb, bulletMatch.Groups[3].Value);
            }
            else
            {
                if (i > 0)
                {
                    tb.Inlines.Add(new LineBreak());
                }
                AppendInlineFormatted(tb, line);
            }
        }
    }

    // Обрабатывает inline разметку (bold/italic/inlinecode) в одной строке.
    private static void AppendInlineFormatted(TextBlock tb, string line)
    {
        // Простой токенайзер: ищет первое вхождение **, *, _, ` и рекурсивно режет.
        var pattern = new Regex(@"(\*\*[^\*]+\*\*|`[^`]+`|\*[^\*]+\*|_[^_]+_)");
        var lastIndex = 0;
        foreach (Match m in pattern.Matches(line))
        {
            if (m.Index > lastIndex)
            {
                tb.Inlines.Add(new Run(line.Substring(lastIndex, m.Index - lastIndex)));
            }

            var token = m.Value;
            if (token.StartsWith("**") && token.EndsWith("**"))
            {
                tb.Inlines.Add(new Run(token[2..^2]) { FontWeight = FontWeights.SemiBold });
            }
            else if (token.StartsWith("`") && token.EndsWith("`"))
            {
                tb.Inlines.Add(new Run(token[1..^1])
                {
                    FontFamily = MonoFontFamily,
                    Background = CodeBackgroundBrush,
                    Foreground = CodeForegroundBrush,
                });
            }
            else if ((token.StartsWith("*") && token.EndsWith("*"))
                     || (token.StartsWith("_") && token.EndsWith("_")))
            {
                tb.Inlines.Add(new Run(token[1..^1]) { FontStyle = FontStyles.Italic });
            }
            else
            {
                tb.Inlines.Add(new Run(token));
            }

            lastIndex = m.Index + m.Length;
        }

        if (lastIndex < line.Length)
        {
            tb.Inlines.Add(new Run(line.Substring(lastIndex)));
        }
    }
}
