using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Chat;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 12 + 13: окно «Спроси склад» с Claude tool use.
// Sprint 13 polish: typing dots, markdown light rendering, лучшие tool-call bubbles,
// suggestion chips на пустом экране, status badge.
public partial class WarehouseAssistantView : UserControl
{
    // Цвета 3 ролей.
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
            StatusText.Text = "❌ Claude API не настроен. Добавь AiProviders:Anthropic:ApiKey в appsettings.local.json.";
            InputBox.IsEnabled = false;
            SendButton.IsEnabled = false;
            StatusBadge.Visibility = Visibility.Visible;
            StatusBadgeText.Text = "⚠ Не подключено";
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xE3));
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0xA3));
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x66, 0x00));
            return;
        }

        _chat = bundle.Chat;
        StatusText.Text = $"Готов к работе. Провайдер: {_chat.ProviderName}";
        StatusBadge.Visibility = Visibility.Visible;
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
        if (sender is Button btn && btn.Tag is string suggestion)
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

        // Переключаемся с empty state на messages list при первом сообщении.
        if (_bubbles.Count == 0)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            MessagesScroll.Visibility = Visibility.Visible;
        }

        // 1. Эхо user-сообщения.
        AddBubble(BuildUserBubble(text));

        // 2. Typing indicator вместо текста.
        var thinking = BuildThinkingBubble();
        AddBubble(thinking);

        StatusText.Text = "⏳ Claude думает...";

        try
        {
            var response = await _chat.SendAsync(_history, text);

            // Обновим compressed history.
            _history.Add(new ChatMessage(ChatRole.User, text));
            _history.Add(new ChatMessage(ChatRole.Assistant, response.Text));

            // Убираем typing bubble.
            _bubbles.Remove(thinking);

            // Tool calls — отдельный bubble за каждую группу вызовов в одной итерации.
            foreach (var msg in response.AppendedHistory)
            {
                if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 } calls)
                {
                    AddBubble(BuildToolCallBubble(calls));
                }
            }

            // Финальный assistant reply.
            AddBubble(BuildAssistantBubble(
                response.Text,
                footer: $"⏱ {response.Duration.TotalSeconds:F1}с  ·  🔧 {response.ToolCallsCount} tool calls  ·  {response.ProviderName}"));

            StatusText.Text = $"Готово ({response.Duration.TotalSeconds:F1}с, {response.ToolCallsCount} tool calls).";
        }
        catch (Exception ex)
        {
            _bubbles.Remove(thinking);
            AddBubble(BuildErrorBubble(ex.Message));
            StatusText.Text = $"❌ {ex.Message}";
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
        StatusText.Text = "История очищена. Можно начинать новый диалог.";
    }

    private void AddBubble(ChatBubble bubble)
    {
        _bubbles.Add(bubble);
        Dispatcher.BeginInvoke(new Action(() => MessagesScroll.ScrollToEnd()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private static ChatBubble BuildUserBubble(string text) => new(
        RoleIcon: "👤",
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
        RoleIcon: "🤖",
        RoleLabel: "Claude",
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
        RoleIcon: "🤖",
        RoleLabel: "Claude",
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
        var body = string.Join("\n", calls.Select(c => $"`{c.ToolName}({c.ArgumentsJson})`"));
        return new ChatBubble(
            RoleIcon: "🔧",
            RoleLabel: "Инструмент",
            RoleLabelBrush: ToolLabel,
            Body: body,
            BubbleBrush: ToolBubble,
            BorderBrush: ToolBorder,
            Alignment: HorizontalAlignment.Left,
            Footer: $"Вызов{(calls.Count > 1 ? "ы" : string.Empty)} серверного API",
            FooterVisibility: Visibility.Visible,
            TypingVisibility: Visibility.Collapsed,
            BodyVisibility: Visibility.Visible,
            BodyFontFamily: MonoFont);
    }

    private static ChatBubble BuildErrorBubble(string message) => new(
        RoleIcon: "❌",
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
