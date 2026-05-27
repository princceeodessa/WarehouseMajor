using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Chat;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 12 (AI Chat assistant): окно «Спроси склад».
// Hosts ClaudeChatService с warehouse tools. История чата — in-memory на время сессии.
public partial class WarehouseAssistantView : UserControl
{
    private static readonly SolidColorBrush UserBubble = new(Color.FromRgb(0xEE, 0xF2, 0xFF));
    private static readonly SolidColorBrush AssistantBubble = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ToolBubble = new(Color.FromRgb(0xF4, 0xF7, 0xFF));

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
            return;
        }

        _chat = bundle.Chat;
        StatusText.Text = $"✓ Подключён ({_chat.ProviderName}). Доступные инструменты: остатки, ячейки, рекомендации.";
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

        // Эхо user-сообщения сразу.
        AddBubble(new ChatBubble(
            RoleLabel: "Вы",
            Body: text,
            BubbleBrush: UserBubble,
            Alignment: HorizontalAlignment.Right,
            Footer: string.Empty,
            FooterVisibility: Visibility.Collapsed));

        var thinkingBubble = new ChatBubble(
            RoleLabel: "Claude",
            Body: "⏳ Думает...",
            BubbleBrush: AssistantBubble,
            Alignment: HorizontalAlignment.Left,
            Footer: string.Empty,
            FooterVisibility: Visibility.Collapsed);
        AddBubble(thinkingBubble);

        StatusText.Text = $"⏳ Отправка запроса в Claude...";

        try
        {
            var response = await _chat.SendAsync(_history, text);
            // Обновим историю: добавляем user (наш) + assistant final reply.
            _history.Add(new ChatMessage(ChatRole.User, text));
            _history.Add(new ChatMessage(ChatRole.Assistant, response.Text));

            // Удалим thinking-bubble.
            _bubbles.Remove(thinkingBubble);

            // Вставим tool-bubbles из appendedHistory (для прозрачности — оператор видит что Claude дёрнул).
            foreach (var msg in response.AppendedHistory)
            {
                if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 } calls)
                {
                    var summary = string.Join(", ", calls.Select(c => $"🔧 {c.ToolName}"));
                    AddBubble(new ChatBubble(
                        RoleLabel: "Инструмент",
                        Body: summary,
                        BubbleBrush: ToolBubble,
                        Alignment: HorizontalAlignment.Left,
                        Footer: string.Empty,
                        FooterVisibility: Visibility.Collapsed));
                }
            }

            // Финальный текст ответа.
            AddBubble(new ChatBubble(
                RoleLabel: "Claude",
                Body: response.Text,
                BubbleBrush: AssistantBubble,
                Alignment: HorizontalAlignment.Left,
                Footer: $"⏱ {response.Duration.TotalSeconds:F1}с · 🔧 {response.ToolCallsCount} вызовов · {response.ProviderName}",
                FooterVisibility: Visibility.Visible));

            StatusText.Text = $"Готово ({response.Duration.TotalSeconds:F1}с, {response.ToolCallsCount} tool calls).";
        }
        catch (Exception ex)
        {
            _bubbles.Remove(thinkingBubble);
            AddBubble(new ChatBubble(
                RoleLabel: "Ошибка",
                Body: ex.Message,
                BubbleBrush: AssistantBubble,
                Alignment: HorizontalAlignment.Left,
                Footer: string.Empty,
                FooterVisibility: Visibility.Collapsed));
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
        StatusText.Text = "История очищена. Можно начинать новый диалог.";
    }

    private void AddBubble(ChatBubble bubble)
    {
        _bubbles.Add(bubble);
        // Прокрутка к концу — выполняем после layout pass.
        Dispatcher.BeginInvoke(new Action(() => MessagesScroll.ScrollToEnd()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    public sealed record ChatBubble(
        string RoleLabel,
        string Body,
        Brush BubbleBrush,
        HorizontalAlignment Alignment,
        string Footer,
        Visibility FooterVisibility);
}
