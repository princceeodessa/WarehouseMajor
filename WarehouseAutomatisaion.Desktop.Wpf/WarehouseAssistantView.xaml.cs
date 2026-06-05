using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Chat;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseAssistantView : UserControl
{
    private static readonly SolidColorBrush UserBubble = new(Color.FromRgb(0xEE, 0xF2, 0xFF));
    private static readonly SolidColorBrush UserBorder = new(Color.FromRgb(0xC8, 0xD1, 0xFF));
    private static readonly SolidColorBrush UserLabel = new(Color.FromRgb(0x2F, 0x3F, 0x74));

    private static readonly SolidColorBrush AssistantBubble = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush AssistantBorder = new(Color.FromRgb(0xE7, 0xEC, 0xF7));
    private static readonly SolidColorBrush AssistantLabel = new(Color.FromRgb(0x4F, 0x5B, 0xFF));

    private static readonly SolidColorBrush ToolBubble = new(Color.FromRgb(0xF7, 0xF9, 0xFD));
    private static readonly SolidColorBrush ToolBorder = new(Color.FromRgb(0xD9, 0xE1, 0xFF));
    private static readonly SolidColorBrush ToolLabel = new(Color.FromRgb(0x7A, 0x86, 0xA5));

    private static readonly FontFamily DefaultFont = new("Segoe UI");
    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, monospace");

    private readonly ObservableCollection<ChatBubble> _bubbles = new();
    private readonly List<ChatMessage> _history = new();
    private IChatService? _chat;
    private bool _isInitialized;

    public WarehouseAssistantView()
    {
        InitializeComponent();
        MessagesList.ItemsSource = _bubbles;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        var bundle = WarehouseChatFactory.TryCreate();
        if (bundle is null)
        {
            StatusText.Text = "Помощник отключён в OneCSyncAssistant.";
            HintText.Text = "Проверь секцию OneCSyncAssistant в appsettings.local.json.";
            InputBox.IsEnabled = false;
            SendButton.IsEnabled = false;
            StatusBadge.Visibility = Visibility.Visible;
            StatusBadgeText.Text = "Отключено";
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xE3));
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0xA3));
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x66, 0x00));
            _isInitialized = true;
            return;
        }

        _chat = bundle.Chat;
        StatusText.Text = $"Готов к вопросу. Провайдер: {_chat.ProviderName}";
        StatusBadge.Visibility = Visibility.Visible;
        StatusBadgeText.Text = bundle.Backplane is null
            ? "1C/Ollama"
            : "1C/Ollama + WMS";
        HintText.Text = bundle.Backplane is null
            ? "Подключён 1C/Ollama API. Точные вопросы по ячейкам включатся после подключения MySQL Major."
            : "Подключены 1C/Ollama API и точные WMS-инструменты Major.";
        _isInitialized = true;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _ = SendCurrentInputAsync();
        }
    }

    private void OnSendClicked(object sender, RoutedEventArgs e)
    {
        _ = SendCurrentInputAsync();
    }

    private void OnSuggestionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string suggestion })
        {
            InputBox.Text = suggestion;
            _ = SendCurrentInputAsync();
        }
    }

    private async Task SendCurrentInputAsync()
    {
        if (_chat is null)
        {
            return;
        }

        var text = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        InputBox.Clear();
        InputBox.IsEnabled = false;
        SendButton.IsEnabled = false;

        if (_bubbles.Count == 0)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            MessagesScroll.Visibility = Visibility.Visible;
        }

        AddBubble(BuildUserBubble(text));
        var thinking = BuildThinkingBubble();
        AddBubble(thinking);

        StatusText.Text = "Модель считает ответ. Для 1C/Ollama это может занять до нескольких минут...";

        try
        {
            var response = await _chat.SendAsync(_history, text);
            _history.Add(new ChatMessage(ChatRole.User, text));
            _history.Add(new ChatMessage(ChatRole.Assistant, response.Text));

            _bubbles.Remove(thinking);

            foreach (var msg in response.AppendedHistory)
            {
                if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 } calls)
                {
                    AddBubble(BuildToolCallBubble(calls));
                }
            }

            AddBubble(BuildAssistantBubble(
                response.Text,
                footer: $"{response.Duration.TotalSeconds:F1} с · {response.ToolCallsCount} инструментов · {response.ProviderName}"));

            StatusText.Text = $"Готово за {response.Duration.TotalSeconds:F1} с. Источник: {response.ProviderName}.";
        }
        catch (Exception ex)
        {
            _bubbles.Remove(thinking);
            AddBubble(BuildErrorBubble(ex.Message));
            StatusText.Text = ex.Message;
        }
        finally
        {
            InputBox.IsEnabled = true;
            SendButton.IsEnabled = true;
            InputBox.Focus();
        }
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        _bubbles.Clear();
        _history.Clear();
        EmptyStatePanel.Visibility = Visibility.Visible;
        MessagesScroll.Visibility = Visibility.Collapsed;
        StatusText.Text = "История очищена. Можно начать новый диалог.";
    }

    private void OnOpenConfigClicked(object sender, RoutedEventArgs e)
    {
        var directory = AppConfigLocator.UserConfigDirectory;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
            StatusText.Text = $"Открыта папка настроек: {directory}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Не удалось открыть папку настроек: {ex.Message}";
        }
    }

    private void AddBubble(ChatBubble bubble)
    {
        _bubbles.Add(bubble);
        Dispatcher.BeginInvoke(new Action(() => MessagesScroll.ScrollToEnd()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private static ChatBubble BuildUserBubble(string text) => new(
        RoleIcon: "\uE77B",
        RoleLabel: "Вы",
        RoleLabelBrush: UserLabel,
        Body: text,
        BubbleBrush: UserBubble,
        BorderBrush: UserBorder,
        Alignment: HorizontalAlignment.Right,
        Footer: string.Empty,
        FooterVisibility: Visibility.Collapsed,
        TypingVisibility: Visibility.Collapsed,
        BodyVisibility: Visibility.Visible,
        BodyFontFamily: DefaultFont);

    private static ChatBubble BuildThinkingBubble() => new(
        RoleIcon: "\uE8BD",
        RoleLabel: "Помощник",
        RoleLabelBrush: AssistantLabel,
        Body: string.Empty,
        BubbleBrush: AssistantBubble,
        BorderBrush: AssistantBorder,
        Alignment: HorizontalAlignment.Left,
        Footer: string.Empty,
        FooterVisibility: Visibility.Collapsed,
        TypingVisibility: Visibility.Visible,
        BodyVisibility: Visibility.Collapsed,
        BodyFontFamily: DefaultFont);

    private static ChatBubble BuildAssistantBubble(string text, string footer) => new(
        RoleIcon: "\uE8BD",
        RoleLabel: "Помощник",
        RoleLabelBrush: AssistantLabel,
        Body: text,
        BubbleBrush: AssistantBubble,
        BorderBrush: AssistantBorder,
        Alignment: HorizontalAlignment.Left,
        Footer: footer,
        FooterVisibility: string.IsNullOrEmpty(footer) ? Visibility.Collapsed : Visibility.Visible,
        TypingVisibility: Visibility.Collapsed,
        BodyVisibility: Visibility.Visible,
        BodyFontFamily: DefaultFont);

    private static ChatBubble BuildToolCallBubble(IReadOnlyList<ChatToolCall> calls)
    {
        var body = string.Join("\n", calls.Select(c => $"{c.ToolName}({c.ArgumentsJson})"));
        return new ChatBubble(
            RoleIcon: "\uE713",
            RoleLabel: "Инструмент",
            RoleLabelBrush: ToolLabel,
            Body: body,
            BubbleBrush: ToolBubble,
            BorderBrush: ToolBorder,
            Alignment: HorizontalAlignment.Left,
            Footer: calls.Count > 1 ? "Локальные WMS-запросы" : "Локальный WMS-запрос",
            FooterVisibility: Visibility.Visible,
            TypingVisibility: Visibility.Collapsed,
            BodyVisibility: Visibility.Visible,
            BodyFontFamily: MonoFont);
    }

    private static ChatBubble BuildErrorBubble(string message) => new(
        RoleIcon: "\uE783",
        RoleLabel: "Ошибка",
        RoleLabelBrush: new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F)),
        Body: message,
        BubbleBrush: new SolidColorBrush(Color.FromRgb(0xFD, 0xEC, 0xEC)),
        BorderBrush: new SolidColorBrush(Color.FromRgb(0xF5, 0xAE, 0xAE)),
        Alignment: HorizontalAlignment.Left,
        Footer: string.Empty,
        FooterVisibility: Visibility.Collapsed,
        TypingVisibility: Visibility.Collapsed,
        BodyVisibility: Visibility.Visible,
        BodyFontFamily: DefaultFont);

    public sealed record ChatBubble(
        string RoleIcon,
        string RoleLabel,
        Brush RoleLabelBrush,
        string Body,
        Brush BubbleBrush,
        Brush BorderBrush,
        HorizontalAlignment Alignment,
        string Footer,
        Visibility FooterVisibility,
        Visibility TypingVisibility,
        Visibility BodyVisibility,
        FontFamily BodyFontFamily);
}
