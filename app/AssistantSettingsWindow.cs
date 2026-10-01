using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

public sealed class AssistantSettingsState
{
    public string Description { get; set; } = "";
    public string SelectedBackend { get; set; } = "codex";
    public string Account { get; set; } = "";
    public string DeviceCodeText { get; set; } = "";
    public bool CodexManaged { get; set; } = true;
    public bool SignedIn { get; set; }
    public IReadOnlyList<CodexModel> Models { get; set; } = Array.Empty<CodexModel>();
    public string SelectedModel { get; set; } = "";
    public string DefaultModelLabel { get; set; } = "";
    public bool IsApiProvider { get; set; }
    public string BaseUrl { get; set; } = "";
    public bool ApiKeySet { get; set; }
    public Action<string> ChooseApiKey { get; set; } = _ => { };
    public Action<string> ChooseBaseUrl { get; set; } = _ => { };
    public Action<string> ChooseProvider { get; set; } = _ => { };
    public Action<CodexModel> ChooseModel { get; set; } = _ => { };
    public Action ResetModel { get; set; } = () => { };
    public Action SignIn { get; set; } = () => { };
    public Action DeviceCode { get; set; } = () => { };
    public Action SignOut { get; set; } = () => { };
}

public sealed class AssistantSettingsWindow : Window
{
    public const string CodexNotice =
        "ChatGPT sign-in happens in your browser or with a device code. BGS never asks for or stores your " +
        "ChatGPT password, OAuth token, or OpenAI API key. Opening this drawer makes no model request. " +
        "Prompts send only bounded editor context and BGS tool results when you press Send.";

    public const string CliNotice =
        "This provider signs in through its own command-line tool, so BGS never sees or stores a credential. " +
        "Opening this drawer makes no model request. Prompts send only bounded editor context and BGS tool " +
        "results when you press Send.";

    private readonly AssistantSettingsState _state;
    private readonly ComboBox _provider = new() { MinWidth = 200 };
    private readonly AssistantModelPicker _modelPicker = new();
    private readonly TextBox _apiBase = Ux.Field("Base URL", 360);
    private readonly TextBox _apiKey = new() { PasswordChar = '\u2022', MinWidth = 360, MinHeight = Ux.ControlHeight };
    private readonly TextBlock _apiKeyState = new();
    private readonly StackPanel _apiSection = new() { Spacing = 4 };
    private readonly Button _modelReset = Ux.Secondary("Use provider default");
    private readonly TextBlock _providerText = new();
    private readonly TextBlock _accountText = new();
    private readonly TextBlock _deviceText = new();
    private readonly TextBlock _notice = new();
    private readonly StackPanel _authRow = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Button _signIn = Ux.Primary("Sign in with ChatGPT");
    private readonly Button _deviceCode = Ux.Secondary("Use device code");
    private readonly Button _signOut = Ux.Secondary("Sign out");
    private bool _binding;

    public AssistantSettingsWindow(AssistantSettingsState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));

        Title = "Assistant settings";
        Width = 560;
        Height = 430;
        MinWidth = 460;
        MinHeight = 360;
        Background = Ux.BaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowActivated = true;

        _provider.SelectionChanged += (_, _) =>
        {
            if (_binding) return;
            if (_provider.SelectedItem is AssistantProviderItem item) _state.ChooseProvider(item.Wire);
        };
        _modelPicker.Chosen += model =>
        {
            if (_binding) return;
            _state.ChooseModel(model);
        };
        _modelReset.Click += (_, _) => _state.ResetModel();
        _signIn.Click += (_, _) => _state.SignIn();
        _deviceCode.Click += (_, _) => _state.DeviceCode();
        _signOut.Click += (_, _) => _state.SignOut();
        _apiBase.LostFocus += (_, _) => SubmitApiBase();
        _apiBase.KeyDown += (_, args) => { if (args.Key == Key.Enter) SubmitApiBase(); };
        _apiKey.LostFocus += (_, _) => SubmitApiKey();
        _apiKey.KeyDown += (_, args) => { if (args.Key == Key.Enter) SubmitApiKey(); };

        var close = Ux.Secondary("Close");
        close.Click += (_, _) => Close();

        _providerText.Foreground = Ux.TitleBrush;
        _providerText.FontSize = Ux.FontSmall;
        _providerText.TextWrapping = TextWrapping.Wrap;
        _accountText.Foreground = Ux.MetaBrush;
        _accountText.FontSize = Ux.FontSmall;
        _accountText.TextWrapping = TextWrapping.Wrap;
        _notice.Foreground = Ux.MutedBrush;
        _notice.FontSize = Ux.FontSmall;
        _notice.TextWrapping = TextWrapping.Wrap;
        _deviceText.Foreground = Ux.CodeBrush;
        _deviceText.FontSize = Ux.FontSmall;
        _deviceText.TextWrapping = TextWrapping.Wrap;
        _apiKeyState.Foreground = Ux.MutedBrush;
        _apiKeyState.FontSize = Ux.FontSmall;
        _apiKeyState.TextWrapping = TextWrapping.Wrap;
        _apiSection.Children.Add(new TextBlock
        {
            Text = "Connection",
            Foreground = Ux.MetaBrush,
            FontSize = Ux.FontSmall,
        });
        _apiSection.Children.Add(Ux.Label("Base URL"));
        _apiSection.Children.Add(_apiBase);
        _apiSection.Children.Add(Ux.Label("API key (this session only)"));
        _apiSection.Children.Add(_apiKey);
        _apiSection.Children.Add(_apiKeyState);

        _authRow.Children.Add(_signIn);
        _authRow.Children.Add(_deviceCode);
        _authRow.Children.Add(_signOut);

        var modelRow = new StackPanel
        {
            Spacing = 6,
            Children = { _modelPicker, _modelReset },
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Assistant settings",
                        Foreground = Ux.TitleBrush,
                        FontSize = 16,
                    },
                    Section("Provider", _provider),
                    _providerText,
                    _apiSection,
                    _accountText,
                    _deviceText,
                    _authRow,
                    Section("Model", modelRow),
                    _notice,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 6, 0, 0),
                        Children = { close },
                    },
                },
            },
        };

        Refresh();
    }

    public ComboBox ProviderSelector => _provider;
    public string? SelectedProvider => (_provider.SelectedItem as AssistantProviderItem)?.Wire;
    public AssistantModelPicker ModelSelector => _modelPicker;
    public bool SignInVisible => _signIn.IsVisible;
    public bool DeviceCodeVisible => _deviceCode.IsVisible;
    public bool SignOutVisible => _signOut.IsVisible;
    public string AccountText => _accountText.Text ?? "";
    public string NoticeText => _notice.Text ?? "";
    public string ProviderText => _providerText.Text ?? "";
    public bool ApiSectionVisible => _apiSection.IsVisible;
    public TextBox ApiBaseForTest => _apiBase;
    public TextBox ApiKeyForTest => _apiKey;
    public string ApiKeyStateText => _apiKeyState.Text ?? "";
    public void CloseForTest() => Close();

    public void Present(Window owner)
    {
        if (!IsVisible) Show(owner);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    public void Refresh()
    {
        _binding = true;
        try
        {
            var providers = new List<AssistantProviderItem>(AssistantCliLocator.All.Length);
            AssistantProviderItem? current = null;
            foreach (AssistantBackend backend in AssistantCliLocator.All)
                providers.Add(new AssistantProviderItem(
                    AssistantCliLocator.CommandName(backend), AssistantCliLocator.DisplayName(backend)));
            _provider.ItemsSource = providers;
            foreach (AssistantProviderItem item in providers)
                if (item.Wire == _state.SelectedBackend) current = item;
            _provider.SelectedItem = current;

            AssistantBackend selectedBackend =
                AssistantCliLocator.Parse(_state.SelectedBackend) ?? AssistantBackend.Codex;
            _modelPicker.SetModels(_state.Models, AssistantModelCatalog.AgentFor(selectedBackend), _state.SelectedModel);

            _providerText.Text = _state.Description;
            _accountText.Text = _state.Account;
            _deviceText.Text = _state.DeviceCodeText;
            _deviceText.IsVisible = _state.DeviceCodeText.Length > 0;
            _notice.Text = _state.IsApiProvider
                ? ApiProviders.Notice
                : _state.CodexManaged ? CodexNotice : CliNotice;

            _apiSection.IsVisible = _state.IsApiProvider;
            _apiBase.Text = _state.BaseUrl;
            _apiKey.Text = "";
            _apiKeyState.Text = !_state.IsApiProvider
                ? ""
                : _state.ApiKeySet
                    ? "Key set for this session. Sign out clears it."
                    : "No key for this session.";

            _signIn.IsVisible = _state.CodexManaged && !_state.SignedIn;
            _deviceCode.IsVisible = _state.CodexManaged && !_state.SignedIn;
            _signOut.IsVisible = _state.CodexManaged && _state.SignedIn;
            _modelReset.IsVisible = _state.SelectedModel.Length > 0;
            _modelPicker.IsVisible = _state.Models.Count > 0;
        }
        finally
        {
            _binding = false;
        }
    }

    private void SubmitApiBase()
    {
        if (_binding) return;
        string value = (_apiBase.Text ?? "").Trim();
        if (string.Equals(value, _state.BaseUrl, StringComparison.Ordinal)) return;
        _state.ChooseBaseUrl(value);
    }

    private void SubmitApiKey()
    {
        if (_binding) return;
        string key = (_apiKey.Text ?? "").Trim();
        if (key.Length == 0) return;
        _state.ChooseApiKey(key);
    }

    private static StackPanel Section(string label, Control control) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, Foreground = Ux.MetaBrush, FontSize = Ux.FontSmall },
            control,
        },
    };
}
