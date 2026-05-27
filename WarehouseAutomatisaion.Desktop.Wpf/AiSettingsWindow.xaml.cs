using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WpfApplication = System.Windows.Application;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 13 fix #2: окно настроек AI — пользователь вводит API-ключи прямо в UI,
// не редактируя файл через notepad. Записываются в %LOCALAPPDATA%\Major\appsettings.local.json
// (merge с существующими секциями, не затирая RemoteDatabase).
public partial class AiSettingsWindow : Window
{
    private bool _anthropicRevealed;
    private bool _openAiRevealed;

    public AiSettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadExistingValues();
    }

    private void LoadExistingValues()
    {
        var path = AppConfigLocator.FindLocalConfigPath();
        if (path is null)
        {
            StatusText.Text = $"Файл будет создан: {AppConfigLocator.UserConfigFilePath}";
            return;
        }

        try
        {
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            if (!doc.RootElement.TryGetProperty("AiProviders", out var ai))
            {
                StatusText.Text = $"Найден {path}, но секции AiProviders нет. Будет добавлена.";
                return;
            }

            if (ai.TryGetProperty("Anthropic", out var anthropic))
            {
                if (anthropic.TryGetProperty("ApiKey", out var key) && key.ValueKind == JsonValueKind.String)
                {
                    AnthropicKeyBox.Password = key.GetString() ?? string.Empty;
                }
                if (anthropic.TryGetProperty("Model", out var model) && model.ValueKind == JsonValueKind.String)
                {
                    SelectComboBoxByTag(AnthropicModelCombo, model.GetString());
                }
                if (anthropic.TryGetProperty("MaxTokens", out var mt) && mt.ValueKind == JsonValueKind.Number)
                {
                    AnthropicMaxTokensBox.Text = mt.GetInt32().ToString();
                }
            }

            if (ai.TryGetProperty("OpenAi", out var openai))
            {
                if (openai.TryGetProperty("ApiKey", out var key) && key.ValueKind == JsonValueKind.String)
                {
                    OpenAiKeyBox.Password = key.GetString() ?? string.Empty;
                }
            }

            if (ai.TryGetProperty("Default", out var def) && def.ValueKind == JsonValueKind.String)
            {
                if (string.Equals(def.GetString(), "OpenAi", StringComparison.OrdinalIgnoreCase))
                {
                    DefaultOpenAiRadio.IsChecked = true;
                }
                else
                {
                    DefaultAnthropicRadio.IsChecked = true;
                }
            }

            UpdateBadges();
            StatusText.Text = $"Текущие настройки из: {path}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"⚠ Не удалось прочитать {path}: {ex.Message}";
        }
    }

    private static void SelectComboBoxByTag(ComboBox combo, string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
                return;
            }
        }
    }

    private void OnAnthropicKeyChanged(object sender, RoutedEventArgs e) => UpdateBadges();
    private void OnOpenAiKeyChanged(object sender, RoutedEventArgs e) => UpdateBadges();

    private void UpdateBadges()
    {
        var anthropicOk = !string.IsNullOrWhiteSpace(AnthropicKeyBox.Password)
                          && AnthropicKeyBox.Password.StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase);
        UpdateBadge(AnthropicStatusBadge, AnthropicStatusBadgeText, anthropicOk,
            anthropicOk ? "✓ ключ задан" : "не задан");

        var openAiOk = !string.IsNullOrWhiteSpace(OpenAiKeyBox.Password)
                       && OpenAiKeyBox.Password.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
        UpdateBadge(OpenAiStatusBadge, OpenAiStatusBadgeText, openAiOk,
            openAiOk ? "✓ ключ задан" : "не задан");
    }

    private static void UpdateBadge(System.Windows.Controls.Border badge, TextBlock text, bool ok, string label)
    {
        text.Text = label;
        if (ok)
        {
            badge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0xF7, 0xEC));
            badge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE0, 0xB6));
            text.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x3F));
        }
        else
        {
            badge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF1, 0xF4, 0xFA));
            badge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD7, 0xDE, 0xEC));
            text.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6E, 0x7B, 0x98));
        }
    }

    private void OnAnthropicShowClicked(object sender, RoutedEventArgs e)
    {
        _anthropicRevealed = !_anthropicRevealed;
        ToggleReveal(AnthropicKeyBox, _anthropicRevealed);
    }

    private void OnOpenAiShowClicked(object sender, RoutedEventArgs e)
    {
        _openAiRevealed = !_openAiRevealed;
        ToggleReveal(OpenAiKeyBox, _openAiRevealed);
    }

    private static void ToggleReveal(PasswordBox box, bool reveal)
    {
        // WPF не имеет встроенного reveal — workaround: показываем простое сообщение.
        if (reveal)
        {
            MessageBox.Show(
                WpfApplication.Current.MainWindow,
                box.Password.Length == 0 ? "(пусто)" : box.Password,
                "API ключ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = AppConfigLocator.UserConfigFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Читаем существующий файл если есть — чтобы не затереть RemoteDatabase и прочее.
            JsonObject root;
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                root = (JsonNode.Parse(existing) as JsonObject) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var aiProviders = (root["AiProviders"] as JsonObject) ?? new JsonObject();
            aiProviders["Default"] = DefaultOpenAiRadio.IsChecked == true ? "OpenAi" : "Anthropic";

            var anthropic = (aiProviders["Anthropic"] as JsonObject) ?? new JsonObject();
            anthropic["ApiKey"] = AnthropicKeyBox.Password;
            anthropic["Model"] = (AnthropicModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "claude-opus-4-7";
            anthropic["MaxTokens"] = int.TryParse(AnthropicMaxTokensBox.Text, out var maxTok) ? maxTok : 8192;
            anthropic["EnableCaching"] = true;
            anthropic["TimeoutSeconds"] = 60;
            aiProviders["Anthropic"] = anthropic;

            var openai = (aiProviders["OpenAi"] as JsonObject) ?? new JsonObject();
            openai["ApiKey"] = OpenAiKeyBox.Password;
            // model и т.п. — оставляем как было / defaults
            if (openai["Model"] is null)
            {
                openai["Model"] = "gpt-4o";
            }
            if (openai["EmbeddingModel"] is null)
            {
                openai["EmbeddingModel"] = "text-embedding-3-small";
            }
            if (openai["MaxTokens"] is null)
            {
                openai["MaxTokens"] = 4096;
            }
            aiProviders["OpenAi"] = openai;

            root["AiProviders"] = aiProviders;

            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            StatusText.Text = $"✓ Сохранено: {path}";
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось сохранить: {ex.Message}";
            MessageBox.Show(this,
                $"Не удалось сохранить настройки:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
