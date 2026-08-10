using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace UsbScreenMirror
{
    public class DeviceInfo
    {
        public string Serial { get; set; }
        public string Model { get; set; }
        public string Status { get; set; }
        public string ConnectionType { get; set; }
        public string IpAddress { get; set; }

        public override string ToString()
        {
            return string.Format("{0} ({1}) - {2}", Model ?? "Android Mobile", ConnectionType, Serial);
        }
    }

    public class MainWindow : Window
    {
        // Material & Fluid Theme Colors
        private readonly SolidColorBrush _mdBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14"));
        private readonly SolidColorBrush _mdCardBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121826"));
        private readonly SolidColorBrush _mdCardHover = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A2234"));
        private readonly SolidColorBrush _mdAndroidGreen = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3DDC84"));
        private readonly SolidColorBrush _mdCyanAccent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
        private readonly SolidColorBrush _mdIndigoAccent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"));
        private readonly SolidColorBrush _mdTextMuted = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

        // UI Controls (WPF)
        private System.Windows.Controls.ComboBox _deviceComboBox;
        private Border _statusBadge;
        private Ellipse _statusDot;
        private TextBlock _statusBadgeText;

        // Android Phone Shell Elements
        private Border _phoneFrame;
        private Ellipse _phoneCameraPunch;
        private TextBlock _phoneModelText;
        private TextBlock _phoneSerialText;
        private TextBlock _phoneStatusText;
        private Border _phonePulseRing;

        private System.Windows.Controls.Button _mirrorButton;
        private System.Windows.Controls.Button _recordButton;
        private System.Windows.Controls.Button _screenshotButton;
        private System.Windows.Controls.Button _wifiButton;
        private System.Windows.Controls.Button _clearLogButton;

        // Responsive Interactive Sliders
        private System.Windows.Controls.Slider _sliderBitrate;
        private TextBlock _lblBitrateValue;
        private System.Windows.Controls.Slider _sliderFps;
        private TextBlock _lblFpsValue;

        private System.Windows.Controls.CheckBox _chkTurnScreenOff;
        private System.Windows.Controls.CheckBox _chkStayAwake;
        private System.Windows.Controls.CheckBox _chkAudioForward;
        private System.Windows.Controls.CheckBox _chkAlwaysOnTop;
        private System.Windows.Controls.CheckBox _chkAutoStartOnPlug;
        private System.Windows.Controls.CheckBox _chkKeyboardMouseControl;

        private Border _cardPresetFast;
        private Border _cardPresetBalanced;
        private Border _cardPresetUltra;
        private string _selectedPreset = "Balanced";

        // Remote Control UI Inputs
        private System.Windows.Controls.TextBox _txtSendText;
        private System.Windows.Controls.TextBox _logConsole;

        // Tray Icon (Forms)
        private Forms.NotifyIcon _notifyIcon;

        // Paths & Process
        private string _adbPath;
        private string _scrcpyPath;
        private Process _scrcpyProcess;
        private DispatcherTimer _pollTimer;
        private DispatcherTimer _pulseTimer;
        private ManagementEventWatcher _usbWatcher;
        private List<DeviceInfo> _connectedDevices = new List<DeviceInfo>();

        // Laptop Trackpad Control Fields
        private System.Windows.Point _trackpadDragStart;
        private bool _trackpadIsDragging = false;
        private DateTime _trackpadLastTap = DateTime.MinValue;
        private Border _trackpadZone;
        private TextBlock _trackpadStatusLabel;
        private int _phoneScreenWidth  = 1080;
        private int _phoneScreenHeight = 1920;

        [STAThread]
        public static void Main()
        {
            Forms.Application.EnableVisualStyles();
            var app = new System.Windows.Application();
            app.Run(new MainWindow());
        }

        public MainWindow()
        {
            Title = "Android Kit - Responsive Screen Mirror & Remote Controller";
            Width = 1080;
            Height = 760;
            MinWidth = 860;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = _mdBg;

            ResolveToolPaths();
            BuildUI();
            InitTrayIcon();
            StartUsbMonitoring();
            StartPulseAnimation();

            Loaded += (s, e) => {
                AnimateWindowEntrance();
                RefreshDevices();
            };
            Closing += OnWindowClosing;
        }

        private void ResolveToolPaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] adbCandidates = new[]
            {
                System.IO.Path.Combine(localAppData, @"Android\platform-tools\adb.exe"),
                @"C:\platform-tools\adb.exe",
                "adb.exe"
            };

            foreach (var candidate in adbCandidates)
            {
                if (File.Exists(candidate) || candidate == "adb.exe")
                {
                    _adbPath = candidate;
                    break;
                }
            }

            string[] scrcpyCandidates = new[]
            {
                System.IO.Path.Combine(localAppData, @"Programs\scrcpy\scrcpy.exe"),
                @"C:\Program Files\scrcpy\scrcpy.exe",
                @"C:\scrcpy\scrcpy.exe",
                "scrcpy.exe"
            };

            foreach (var candidate in scrcpyCandidates)
            {
                if (File.Exists(candidate) || candidate == "scrcpy.exe")
                {
                    _scrcpyPath = candidate;
                    break;
                }
            }
        }

        #region Responsive UI Layout & UX Micro-Interactions
        private void BuildUI()
        {
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header Bar
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Adaptive Scroll Body
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(135) }); // Log Console

            // 1. Header Bar (Responsive Layout)
            var headerBorder = new Border
            {
                Background = CreateLinearGradient("#0F1420", "#080C14", 90),
                Padding = new Thickness(24, 14, 24, 14),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var iconBorder = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(14),
                Background = CreateLinearGradient("#3DDC84", "#10B981", 45),
                Margin = new Thickness(0, 0, 14, 0),
                Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#3DDC84"), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.45 }
            };
            var iconText = new TextBlock
            {
                Text = "🤖",
                FontSize = 24,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBorder.Child = iconText;

            var textStack = new StackPanel();
            var titleText = new TextBlock
            {
                Text = "Android Kit - Adaptive Screen Mirror & Remote Controller",
                FontSize = 19,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White
            };
            var subtitleText = new TextBlock
            {
                Text = "Responsive UX Engine • Adaptive Resizing & High-Precision Controls",
                FontSize = 11.5,
                Foreground = _mdTextMuted,
                Margin = new Thickness(0, 2, 0, 0)
            };
            textStack.Children.Add(titleText);
            textStack.Children.Add(subtitleText);

            titleStack.Children.Add(iconBorder);
            titleStack.Children.Add(textStack);

            // Material Pill Badge Status
            _statusBadge = new Border
            {
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(14, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121826")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };

            var badgeStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            _statusDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = _mdTextMuted,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _statusBadgeText = new TextBlock
            {
                Text = "Scanning USB...",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeStack.Children.Add(_statusDot);
            badgeStack.Children.Add(_statusBadgeText);
            _statusBadge.Child = badgeStack;

            headerGrid.Children.Add(titleStack);
            Grid.SetColumn(_statusBadge, 1);
            headerGrid.Children.Add(_statusBadge);

            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            mainGrid.Children.Add(headerBorder);

            // 2. Adaptive Scroll Viewer Body for Perfect Responsiveness on Any Display
            var mainScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(20)
            };

            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

            // Left Column (Realistic Phone Shell & Quick Remote Keys)
            var leftStack = new StackPanel { Margin = new Thickness(0, 0, 14, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

            // Phone Mockup Frame
            _phonePulseRing = new Border
            {
                CornerRadius = new CornerRadius(36),
                Padding = new Thickness(3),
                Background = Brushes.Transparent,
                BorderBrush = _mdAndroidGreen,
                BorderThickness = new Thickness(2),
                Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#3DDC84"), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.4 }
            };

            _phoneFrame = new Border
            {
                Width = 270,
                Height = 430,
                CornerRadius = new CornerRadius(32),
                Background = CreateLinearGradient("#0F1420", "#080C14", 180),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                BorderThickness = new Thickness(2.5),
                Padding = new Thickness(14)
            };

            var phoneInnerGrid = new Grid();
            phoneInnerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            phoneInnerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            phoneInnerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _phoneCameraPunch = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#020617")),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 8)
            };
            Grid.SetRow(_phoneCameraPunch, 0);

            var screenCard = new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = CreateLinearGradient("#121826", "#0A0F1A", 135),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14)
            };

            var screenStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

            var phoneIconLarge = new TextBlock
            {
                Text = "📲",
                FontSize = 46,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };

            _phoneModelText = new TextBlock
            {
                Text = "No Phone Detected",
                FontSize = 14.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            _phoneSerialText = new TextBlock
            {
                Text = "Connect USB Data Cable",
                FontSize = 11,
                Foreground = _mdTextMuted,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };

            _phoneStatusText = new TextBlock
            {
                Text = "OFFLINE",
                FontSize = 10,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#451A1A")),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            screenStack.Children.Add(phoneIconLarge);
            screenStack.Children.Add(_phoneModelText);
            screenStack.Children.Add(_phoneSerialText);
            screenStack.Children.Add(_phoneStatusText);
            screenCard.Child = screenStack;
            Grid.SetRow(screenCard, 1);

            // On-Screen Remote Bar attached to Phone Shell
            var phoneNavBar = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F1420")),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(8, 4, 8, 4),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                BorderThickness = new Thickness(1)
            };

            var navGrid = new Grid();
            navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnNavBack = CreatePhoneNavButton("◀", "Inject BACK Key", (s, e) => SendAdbKeyevent(4));
            var btnNavHome = CreatePhoneNavButton("●", "Inject HOME Key", (s, e) => SendAdbKeyevent(3));
            var btnNavRecents = CreatePhoneNavButton("█", "Open RECENTS", (s, e) => SendAdbKeyevent(187));

            Grid.SetColumn(btnNavBack, 0);
            Grid.SetColumn(btnNavHome, 1);
            Grid.SetColumn(btnNavRecents, 2);

            navGrid.Children.Add(btnNavBack);
            navGrid.Children.Add(btnNavHome);
            navGrid.Children.Add(btnNavRecents);
            phoneNavBar.Child = navGrid;
            Grid.SetRow(phoneNavBar, 2);

            phoneInnerGrid.Children.Add(_phoneCameraPunch);
            phoneInnerGrid.Children.Add(screenCard);
            phoneInnerGrid.Children.Add(phoneNavBar);

            _phoneFrame.Child = phoneInnerGrid;
            _phonePulseRing.Child = _phoneFrame;
            leftStack.Children.Add(_phonePulseRing);

            Grid.SetColumn(leftStack, 0);
            bodyGrid.Children.Add(leftStack);

            // Right Column (Material Control Hub & Dynamic Sliders)
            var rightStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };

            // Card 1: Connection & Primary Mirror Button
            var devCard = CreateMaterialCard("ANDROID DEVICE CONNECTIVITY", "📱 Select & Launch Mirror Stream");
            var devStack = new StackPanel();

            var devSelectGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            devSelectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            devSelectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _deviceComboBox = new System.Windows.Controls.ComboBox
            {
                Height = 38,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };
            _deviceComboBox.SelectionChanged += (s, e) => UpdateSelectedDeviceDetails();

            var btnRefresh = CreateInteractiveButton("🔄", "Refresh Device Scan", (s, e) => RefreshDevices());
            Grid.SetColumn(btnRefresh, 1);
            devSelectGrid.Children.Add(_deviceComboBox);
            devSelectGrid.Children.Add(btnRefresh);

            _mirrorButton = new System.Windows.Controls.Button
            {
                Content = "▶   START SCREEN MIRROR & REMOTE CONTROL",
                Height = 48,
                FontSize = 14,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White,
                Background = CreateLinearGradient("#3DDC84", "#10B981", 90),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#3DDC84"), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 }
            };
            AttachHoverEffects(_mirrorButton);
            _mirrorButton.Click += OnMirrorButtonClick;

            devStack.Children.Add(devSelectGrid);
            devStack.Children.Add(_mirrorButton);
            SetCardContent(devCard, devStack);
            rightStack.Children.Add(devCard);

            // Card 2: Interactive Dynamic UX Sliders (Bitrate & FPS Control)
            var sliderCard = CreateMaterialCard("HIGH-RESPONSE BITRATE & FPS TUNER", "🎛️ Live Performance Tuning");
            var sliderStack = new StackPanel();

            // Bitrate Slider Row
            var bitrateHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            bitrateHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bitrateHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lblBitrateTitle = new TextBlock { Text = "Video Bitrate Stream Quality:", FontSize = 11.5, Foreground = _mdTextMuted };
            _lblBitrateValue = new TextBlock { Text = "8 Mbps", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = _mdCyanAccent };
            Grid.SetColumn(_lblBitrateValue, 1);
            bitrateHeaderGrid.Children.Add(lblBitrateTitle);
            bitrateHeaderGrid.Children.Add(_lblBitrateValue);

            _sliderBitrate = new System.Windows.Controls.Slider
            {
                Minimum = 2,
                Maximum = 32,
                Value = 8,
                TickFrequency = 2,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _sliderBitrate.ValueChanged += (s, e) => {
                _lblBitrateValue.Text = string.Format("{0} Mbps", (int)_sliderBitrate.Value);
            };

            // FPS Slider Row
            var fpsHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            fpsHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fpsHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lblFpsTitle = new TextBlock { Text = "Max Frame Rate Limit:", FontSize = 11.5, Foreground = _mdTextMuted };
            _lblFpsValue = new TextBlock { Text = "60 FPS", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = _mdAndroidGreen };
            Grid.SetColumn(_lblFpsValue, 1);
            fpsHeaderGrid.Children.Add(lblFpsTitle);
            fpsHeaderGrid.Children.Add(_lblFpsValue);

            _sliderFps = new System.Windows.Controls.Slider
            {
                Minimum = 30,
                Maximum = 120,
                Value = 60,
                TickFrequency = 15,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _sliderFps.ValueChanged += (s, e) => {
                _lblFpsValue.Text = string.Format("{0} FPS", (int)_sliderFps.Value);
            };

            sliderStack.Children.Add(bitrateHeaderGrid);
            sliderStack.Children.Add(_sliderBitrate);
            sliderStack.Children.Add(fpsHeaderGrid);
            sliderStack.Children.Add(_sliderFps);
            SetCardContent(sliderCard, sliderStack);
            rightStack.Children.Add(sliderCard);

            // Card 3: Quick Key & Text Injector
            var textCard = CreateMaterialCard("REMOTE INPUT & HARDWARE KEYS", "📤 Remote Input Tools");
            var cardTextStack = new StackPanel();

            var quickKeyGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            quickKeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            quickKeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            quickKeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnVolUp = CreateRemoteButton("🔊 VOL +", "Increase Volume", (s, e) => SendAdbKeyevent(24));
            var btnVolDown = CreateRemoteButton("🔉 VOL -", "Decrease Volume", (s, e) => SendAdbKeyevent(25));
            var btnPower = CreateRemoteButton("⚡ POWER", "Toggle Screen/Power", (s, e) => SendAdbKeyevent(26));

            Grid.SetColumn(btnVolUp, 0);
            Grid.SetColumn(btnVolDown, 1);
            Grid.SetColumn(btnPower, 2);

            quickKeyGrid.Children.Add(btnVolUp);
            quickKeyGrid.Children.Add(btnVolDown);
            quickKeyGrid.Children.Add(btnPower);

            var textInjectGrid = new Grid();
            textInjectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            textInjectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _txtSendText = new System.Windows.Controls.TextBox
            {
                Height = 36,
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                Text = "Type text to inject to phone input..."
            };
            _txtSendText.GotFocus += (s, e) => { if (_txtSendText.Text == "Type text to inject to phone input...") _txtSendText.Text = ""; };

            var btnSendText = CreateInteractiveButton("📤", "Inject Text to Active Field", (s, e) => SendTextToPhone());
            Grid.SetColumn(btnSendText, 1);

            textInjectGrid.Children.Add(_txtSendText);
            textInjectGrid.Children.Add(btnSendText);

            cardTextStack.Children.Add(quickKeyGrid);
            cardTextStack.Children.Add(textInjectGrid);
            SetCardContent(textCard, cardTextStack);
            rightStack.Children.Add(textCard);

            // Card 4: Quick Settings Toggles
            var settingsCard = CreateMaterialCard("ANDROID QUICK SETTINGS", "⚙️ Session Toggles");
            var settingsStack = new StackPanel();

            _chkKeyboardMouseControl = CreateStyledCheckBox("🖱️  Laptop Mouse & Keyboard Control Sync", "Left-Click to tap, Drag to swipe, Keyboard typing sync", true);
            _chkTurnScreenOff = CreateStyledCheckBox("📱  Stealth Screen Off (Saves Mobile Battery)", "Darkens phone physical display while keeping laptop control live", true);
            _chkStayAwake = CreateStyledCheckBox("⚡  Keep Mobile Screen Awake", "Prevents smartphone lock during remote work", true);
            _chkAudioForward = CreateStyledCheckBox("🔊  Forward Mobile Audio to Laptop Speakers", "Stream phone games/apps audio through laptop speakers", true);
            _chkAlwaysOnTop = CreateStyledCheckBox("📌  Always-On-Top Overlay Window", "Floats mirror window on top of other laptop apps", false);
            _chkAutoStartOnPlug = CreateStyledCheckBox("🔌  Auto-Launch on USB Plug-in", "Starts mirror session automatically when phone is plugged in", true);

            settingsStack.Children.Add(_chkKeyboardMouseControl);
            settingsStack.Children.Add(_chkTurnScreenOff);
            settingsStack.Children.Add(_chkStayAwake);
            settingsStack.Children.Add(_chkAudioForward);
            settingsStack.Children.Add(_chkAlwaysOnTop);
            settingsStack.Children.Add(_chkAutoStartOnPlug);
            SetCardContent(settingsCard, settingsStack);
            rightStack.Children.Add(settingsCard);

            // Card 5: Presets & Tools Grid
            var presetsToolsGrid = new Grid();
            presetsToolsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            presetsToolsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var presetsCard = CreateMaterialCard("PRESET PROFILE", "🎯 Resolution Profiles");
            var presetsStack = new StackPanel();
            _cardPresetFast = CreatePresetCard("⚡ Performance", "720p @ 60 FPS", "Fast", false);
            _cardPresetBalanced = CreatePresetCard("⚖️ Balanced HD", "1080p @ 60 FPS", "Balanced", true);
            _cardPresetUltra = CreatePresetCard("🌟 Ultra Quality", "2K @ 60 FPS", "Ultra", false);
            presetsStack.Children.Add(_cardPresetFast);
            presetsStack.Children.Add(_cardPresetBalanced);
            presetsStack.Children.Add(_cardPresetUltra);
            SetCardContent(presetsCard, presetsStack);

            var toolsCard = CreateMaterialCard("UTILITIES", "🛠️ Quick Tools");
            var toolsSubGrid = new Grid();
            toolsSubGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolsSubGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _recordButton = CreateToolActionButton("🎥 Record MP4", "Record session video", (s, e) => ToggleRecording());
            _screenshotButton = CreateToolActionButton("📸 Screenshot", "Capture full-res screen", (s, e) => TakeScreenshot());
            _wifiButton = CreateToolActionButton("📶 Wi-Fi Switch", "Enable wireless mode", (s, e) => SwitchToWifiMode());
            var btnHelp = CreateToolActionButton("❓ Guide & Tips", "Shortcuts manual", (s, e) => ShowSetupGuide());

            var toolsCol1 = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
            toolsCol1.Children.Add(_recordButton);
            toolsCol1.Children.Add(_wifiButton);

            var toolsCol2 = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            toolsCol2.Children.Add(_screenshotButton);
            toolsCol2.Children.Add(btnHelp);

            Grid.SetColumn(toolsCol1, 0);
            Grid.SetColumn(toolsCol2, 1);
            toolsSubGrid.Children.Add(toolsCol1);
            toolsSubGrid.Children.Add(toolsCol2);

            SetCardContent(toolsCard, toolsSubGrid);

            Grid.SetColumn(presetsCard, 0);
            Grid.SetColumn(toolsCard, 1);
            presetsToolsGrid.Children.Add(presetsCard);
            presetsToolsGrid.Children.Add(toolsCard);

            rightStack.Children.Add(presetsToolsGrid);

            // Card 6: Laptop Trackpad Control Panel
            var trackpadCard = CreateMaterialCard("LAPTOP TRACKPAD CONTROL", "🖱️ Tap • Swipe • Scroll • Drag");
            var trackpadStack = new StackPanel();

            var tpInfoBar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            tpInfoBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tpInfoBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _trackpadStatusLabel = new TextBlock
            {
                Text = "Ready — move mouse over zone to control phone",
                FontSize = 10.5,
                Foreground = _mdTextMuted,
                VerticalAlignment = VerticalAlignment.Center
            };

            var tpResInfo = new TextBlock
            {
                Text = "1080×1920",
                FontSize = 10,
                Foreground = _mdCyanAccent,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tpResInfo, 1);
            tpInfoBar.Children.Add(_trackpadStatusLabel);
            tpInfoBar.Children.Add(tpResInfo);
            trackpadStack.Children.Add(tpInfoBar);

            // Virtual Touchpad Zone Canvas
            _trackpadZone = new Border
            {
                Height = 180,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                BorderThickness = new Thickness(1.5),
                Cursor = System.Windows.Input.Cursors.Cross,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var tpCanvas = new Canvas { Background = Brushes.Transparent };

            // Finger-Print Hint icon in center
            var tpHintIcon = new TextBlock
            {
                Text = "☝",
                FontSize = 36,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tpHintText = new TextBlock
            {
                Text = "Tap · Swipe · Scroll · Right-click BACK · Double-click HOME",
                FontSize = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A2E")),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 50, 0, 0),
                TextAlignment = TextAlignment.Center
            };
            var tpOverlay = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            tpOverlay.Children.Add(tpHintIcon);
            tpOverlay.Children.Add(tpHintText);
            _trackpadZone.Child = tpOverlay;

            // Attach all gesture events
            _trackpadZone.MouseLeftButtonDown  += OnTrackpadMouseDown;
            _trackpadZone.MouseLeftButtonUp    += OnTrackpadMouseUp;
            _trackpadZone.MouseMove            += OnTrackpadMouseMove;
            _trackpadZone.MouseRightButtonUp   += (s, e) => { SendAdbKeyevent(4); _trackpadStatusLabel.Text = "Right-click → BACK"; };
            _trackpadZone.MouseWheel           += OnTrackpadMouseWheel;

            trackpadStack.Children.Add(_trackpadZone);

            // Gesture Quick-Ref row
            var gestureGrid = new Grid();
            gestureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gestureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gestureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gestureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var gestureItems = new[]
            {
                new { Icon = "👆", Label = "Tap" },
                new { Icon = "👉", Label = "Swipe" },
                new { Icon = "🖱", Label = "Scroll" },
                new { Icon = "✌", Label = "Home" }
            };

            for (int gIdx = 0; gIdx < gestureItems.Length; gIdx++)
            {
                var item = gestureItems[gIdx];
                var gb = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14")),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 5, 6, 5),
                    Margin = gIdx < 3 ? new Thickness(0, 0, 6, 0) : new Thickness(0),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                    BorderThickness = new Thickness(1)
                };
                var gs = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                gs.Children.Add(new TextBlock { Text = item.Icon, FontSize = 14, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
                gs.Children.Add(new TextBlock { Text = item.Label, FontSize = 9.5, Foreground = _mdTextMuted, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
                gb.Child = gs;
                Grid.SetColumn(gb, gIdx);
                gestureGrid.Children.Add(gb);
            }

            trackpadStack.Children.Add(gestureGrid);
            SetCardContent(trackpadCard, trackpadStack);
            rightStack.Children.Add(trackpadCard);

            Grid.SetColumn(rightStack, 1);
            bodyGrid.Children.Add(rightStack);

            mainScrollViewer.Content = bodyGrid;
            Grid.SetRow(mainScrollViewer, 1);
            mainGrid.Children.Add(mainScrollViewer);

            // 3. Log Console Widget (Android Logcat Style)
            var logBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#040710")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#151C2C")),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 8, 20, 8)
            };

            var logGrid = new Grid();
            logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var logHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            logHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            logHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var logHeader = new TextBlock
            {
                Text = "REAL-TIME DIAGNOSTIC & LOGCAT CONSOLE",
                FontSize = 10.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                VerticalAlignment = VerticalAlignment.Center
            };

            _clearLogButton = new System.Windows.Controls.Button
            {
                Content = "Clear Console",
                FontSize = 10,
                Foreground = _mdTextMuted,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _clearLogButton.Click += (s, e) => _logConsole.Clear();
            Grid.SetColumn(_clearLogButton, 1);

            logHeaderGrid.Children.Add(logHeader);
            logHeaderGrid.Children.Add(_clearLogButton);

            _logConsole = new System.Windows.Controls.TextBox
            {
                Background = Brushes.Transparent,
                Foreground = _mdCyanAccent,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas, Cascadia Code, Courier New, Monospace"),
                FontSize = 11.5,
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap
            };

            Grid.SetRow(logHeaderGrid, 0);
            Grid.SetRow(_logConsole, 1);
            logGrid.Children.Add(logHeaderGrid);
            logGrid.Children.Add(_logConsole);

            logBorder.Child = logGrid;
            Grid.SetRow(logBorder, 2);
            mainGrid.Children.Add(logBorder);

            Content = mainGrid;

            Log("Android Kit Responsive UX Engine Loaded.");
            Log("ADB Binary: " + (_adbPath ?? "Not Found"));
            Log("Scrcpy Binary: " + (_scrcpyPath ?? "Not Found"));
        }

        private Border CreateMaterialCard(string title, string subtitle)
        {
            var card = new Border
            {
                Background = _mdCardBg,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                BorderThickness = new Thickness(1)
            };

            var outerStack = new StackPanel();

            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeights.ExtraBold,
                Foreground = _mdTextMuted
            };

            var subtitleText = new TextBlock
            {
                Text = subtitle,
                FontSize = 9.5,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            headerGrid.Children.Add(titleText);
            Grid.SetColumn(subtitleText, 1);
            headerGrid.Children.Add(subtitleText);

            var contentContainer = new ContentControl();

            outerStack.Children.Add(headerGrid);
            outerStack.Children.Add(contentContainer);

            card.Child = outerStack;
            card.Tag = contentContainer;
            return card;
        }

        private void SetCardContent(Border card, UIElement content)
        {
            var cc = card.Tag as ContentControl;
            if (cc != null)
            {
                cc.Content = content;
            }
        }

        private System.Windows.Controls.Button CreatePhoneNavButton(string icon, string tooltip, RoutedEventHandler onClick)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = icon,
                ToolTip = tooltip,
                Height = 28,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private Border CreatePresetCard(string title, string specs, string presetKey, bool isSelected)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(1.5)
            };

            var stack = new StackPanel();
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            var specBadge = new TextBlock
            {
                Text = specs,
                FontSize = 10,
                Foreground = _mdCyanAccent,
                Margin = new Thickness(0, 2, 0, 0)
            };

            stack.Children.Add(titleText);
            stack.Children.Add(specBadge);
            card.Child = stack;

            Action updateSelection = () =>
            {
                _selectedPreset = presetKey;
                if (presetKey == "Fast")
                {
                    _sliderBitrate.Value = 4;
                    _sliderFps.Value = 60;
                }
                else if (presetKey == "Ultra")
                {
                    _sliderBitrate.Value = 16;
                    _sliderFps.Value = 60;
                }
                else
                {
                    _sliderBitrate.Value = 8;
                    _sliderFps.Value = 60;
                }
                UpdatePresetCardStyles();
            };

            card.MouseDown += (s, e) => updateSelection();

            ApplyPresetCardStyle(card, isSelected);
            return card;
        }

        private void ApplyPresetCardStyle(Border card, bool isSelected)
        {
            if (isSelected)
            {
                card.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B"));
                card.BorderBrush = _mdAndroidGreen;
                card.Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#3DDC84"), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.4 };
            }
            else
            {
                card.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14"));
                card.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                card.Effect = null;
            }
        }

        private void UpdatePresetCardStyles()
        {
            ApplyPresetCardStyle(_cardPresetFast, _selectedPreset == "Fast");
            ApplyPresetCardStyle(_cardPresetBalanced, _selectedPreset == "Balanced");
            ApplyPresetCardStyle(_cardPresetUltra, _selectedPreset == "Ultra");
            Log("Selected quality profile: " + _selectedPreset);
        }

        private System.Windows.Controls.Button CreateRemoteButton(string title, string tooltip, RoutedEventHandler onClick)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = title,
                ToolTip = tooltip,
                Height = 32,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14")),
                Foreground = _mdCyanAccent,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            AttachHoverEffects(btn);
            btn.Click += onClick;
            return btn;
        }

        private System.Windows.Controls.CheckBox CreateStyledCheckBox(string title, string tooltip, bool isChecked)
        {
            return new System.Windows.Controls.CheckBox
            {
                Content = title,
                ToolTip = tooltip,
                IsChecked = isChecked,
                Foreground = Brushes.White,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 7),
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private System.Windows.Controls.Button CreateInteractiveButton(string icon, string tooltip, RoutedEventHandler onClick)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = icon,
                ToolTip = tooltip,
                Width = 38,
                Height = 38,
                FontSize = 14,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            AttachHoverEffects(btn);
            btn.Click += onClick;
            return btn;
        }

        private System.Windows.Controls.Button CreateToolActionButton(string title, string tooltip, RoutedEventHandler onClick)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = title,
                ToolTip = tooltip,
                Height = 34,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            AttachHoverEffects(btn);
            btn.Click += onClick;
            return btn;
        }

        private void AttachHoverEffects(System.Windows.Controls.Button btn)
        {
            btn.MouseEnter += (s, e) => {
                btn.Background = _mdCardHover;
                btn.BorderBrush = _mdCyanAccent;
            };
            btn.MouseLeave += (s, e) => {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080C14"));
                btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
            };
        }

        private LinearGradientBrush CreateLinearGradient(string hexStart, string hexEnd, double angle)
        {
            var gradient = new LinearGradientBrush();
            gradient.StartPoint = new Point(0, 0);
            gradient.EndPoint = angle == 90 ? new Point(0, 1) : new Point(1, 1);
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hexStart), 0.0));
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hexEnd), 1.0));
            return gradient;
        }
        #endregion

        #region Animations & Motion Design
        private void AnimateWindowEntrance()
        {
            var fadeAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeAnim);
        }

        private void StartPulseAnimation()
        {
            _pulseTimer = new DispatcherTimer();
            _pulseTimer.Interval = TimeSpan.FromSeconds(2);
            bool glowing = false;

            _pulseTimer.Tick += (s, e) =>
            {
                if (_connectedDevices.Count > 0)
                {
                    glowing = !glowing;
                    var anim = new DoubleAnimation
                    {
                        To = glowing ? 0.7 : 0.2,
                        Duration = TimeSpan.FromMilliseconds(1200),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                    };
                    var ds = _phonePulseRing.Effect as DropShadowEffect;
                    if (ds != null)
                    {
                        ds.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
                    }
                }
            };
            _pulseTimer.Start();
        }
        #endregion

        #region System Tray & USB Watcher
        private void InitTrayIcon()
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "Android Kit Responsive Mirror & Control",
                Visible = true
            };

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("▶ Start Mirror & Control", null, (s, e) => OnMirrorButtonClick(null, null));
            contextMenu.Items.Add("📱 Stealth Screen Off", null, (s, e) => { _chkTurnScreenOff.IsChecked = true; });
            contextMenu.Items.Add("◀ Inject BACK Key", null, (s, e) => SendAdbKeyevent(4));
            contextMenu.Items.Add("● Inject HOME Key", null, (s, e) => SendAdbKeyevent(3));
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Open Control Hub", null, (s, e) => { Show(); WindowState = WindowState.Normal; Activate(); });
            contextMenu.Items.Add("Exit", null, (s, e) => Close());

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => { Show(); WindowState = WindowState.Normal; Activate(); };
        }

        private void StartUsbMonitoring()
        {
            _pollTimer = new DispatcherTimer();
            _pollTimer.Interval = TimeSpan.FromSeconds(3);
            _pollTimer.Tick += (s, e) => CheckDeviceStateChange();
            _pollTimer.Start();

            Task.Run(() =>
            {
                try
                {
                    var query = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBControllerDevice'");
                    _usbWatcher = new ManagementEventWatcher(query);
                    _usbWatcher.EventArrived += (s, e) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            Log("⚡ USB Plug-in event detected!");
                            RefreshDevices(autoStartIfConfigured: true);
                        });
                    };
                    _usbWatcher.Start();
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Log("WMI Scanner active. (" + ex.Message + ")"));
                }
            });
        }
        #endregion

        #region Laptop Trackpad Gesture Engine
        // Maps laptop mouse/trackpad coordinates to real Android screen ADB touch commands
        private System.Windows.Point ScaleToPhone(System.Windows.Point canvasPoint)
        {
            double zoneW = _trackpadZone.ActualWidth  > 0 ? _trackpadZone.ActualWidth  : 500;
            double zoneH = _trackpadZone.ActualHeight > 0 ? _trackpadZone.ActualHeight : 180;
            double px = (canvasPoint.X / zoneW) * _phoneScreenWidth;
            double py = (canvasPoint.Y / zoneH) * _phoneScreenHeight;
            return new System.Windows.Point(
                Math.Max(0, Math.Min(_phoneScreenWidth  - 1, (int)px)),
                Math.Max(0, Math.Min(_phoneScreenHeight - 1, (int)py))
            );
        }

        private void OnTrackpadMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _trackpadIsDragging = true;
            _trackpadDragStart  = e.GetPosition(_trackpadZone);
            _trackpadZone.CaptureMouse();

            // Highlight zone border on press
            _trackpadZone.BorderBrush = _mdAndroidGreen;
            _trackpadStatusLabel.Text = string.Format("Press at ({0:0},{1:0})", _trackpadDragStart.X, _trackpadDragStart.Y);
        }

        private void OnTrackpadMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _trackpadZone.ReleaseMouseCapture();
            _trackpadZone.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));

            if (!_trackpadIsDragging) return;
            _trackpadIsDragging = false;

            var endPoint = e.GetPosition(_trackpadZone);
            double dx    = endPoint.X - _trackpadDragStart.X;
            double dy    = endPoint.Y - _trackpadDragStart.Y;
            double dist  = Math.Sqrt(dx * dx + dy * dy);

            var device = _deviceComboBox.SelectedItem as DeviceInfo;
            if (device == null || string.IsNullOrEmpty(_adbPath))
            {
                _trackpadStatusLabel.Text = "No device connected!";
                return;
            }

            var pStart = ScaleToPhone(_trackpadDragStart);
            var pEnd   = ScaleToPhone(endPoint);

            if (dist < 8)
            {
                // Check double-click (< 400ms) → HOME key
                if ((DateTime.Now - _trackpadLastTap).TotalMilliseconds < 400)
                {
                    SendAdbKeyevent(3);
                    _trackpadStatusLabel.Text = "Double-tap → HOME";
                    _trackpadLastTap = DateTime.MinValue;
                    return;
                }
                _trackpadLastTap = DateTime.Now;

                // Single tap → adb shell input tap x y
                string tapArgs = string.Format("-s \"{0}\" shell input tap {1} {2}",
                    device.Serial, (int)pStart.X, (int)pStart.Y);
                Task.Run(() => RunCommand(_adbPath, tapArgs));
                _trackpadStatusLabel.Text = string.Format("Tap → phone ({0},{1})", (int)pStart.X, (int)pStart.Y);
            }
            else
            {
                // Swipe gesture: duration proportional to distance for natural feel
                int duration = Math.Max(80, (int)(dist * 1.8));
                string swipeArgs = string.Format("-s \"{0}\" shell input swipe {1} {2} {3} {4} {5}",
                    device.Serial, (int)pStart.X, (int)pStart.Y, (int)pEnd.X, (int)pEnd.Y, duration);
                Task.Run(() => RunCommand(_adbPath, swipeArgs));

                // Determine swipe direction label
                string dir = Math.Abs(dx) > Math.Abs(dy)
                    ? (dx > 0 ? "Swipe Right →" : "← Swipe Left")
                    : (dy > 0 ? "Swipe Down ↓" : "↑ Swipe Up");
                _trackpadStatusLabel.Text = string.Format("{0} ({1}px)", dir, (int)dist);
                Log(string.Format("🖱️ Trackpad swipe: ({0},{1}) → ({2},{3}) in {4}ms",
                    (int)pStart.X, (int)pStart.Y, (int)pEnd.X, (int)pEnd.Y, duration));
            }
        }

        private void OnTrackpadMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_trackpadIsDragging) return;
            var pos   = e.GetPosition(_trackpadZone);
            var phone = ScaleToPhone(pos);
            _trackpadStatusLabel.Text = string.Format("Dragging → phone ({0},{1})", (int)phone.X, (int)phone.Y);
        }

        private void OnTrackpadMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var device = _deviceComboBox.SelectedItem as DeviceInfo;
            if (device == null || string.IsNullOrEmpty(_adbPath)) return;

            // Wheel up = scroll up on phone (swipe down), wheel down = swipe up
            int scrollDelta = e.Delta > 0 ? -400 : 400;
            int cx = _phoneScreenWidth  / 2;
            int cy = _phoneScreenHeight / 2;
            string scrollArgs = string.Format("-s \"{0}\" shell input swipe {1} {2} {3} {4} 200",
                device.Serial, cx, cy, cx, cy + scrollDelta);
            Task.Run(() => RunCommand(_adbPath, scrollArgs));
            _trackpadStatusLabel.Text = e.Delta > 0 ? "Scroll Wheel ↑ Up" : "Scroll Wheel ↓ Down";
        }
        #endregion

        #region Remote Control & Keyevent Injection
        private void SendAdbKeyevent(int keycode)
        {
            var device = _deviceComboBox.SelectedItem as DeviceInfo;
            if (device == null || string.IsNullOrEmpty(_adbPath))
            {
                Log("No device connected to send remote keyevent " + keycode);
                return;
            }

            Task.Run(() =>
            {
                string res = RunCommand(_adbPath, string.Format("-s \"{0}\" shell input keyevent {1}", device.Serial, keycode));
                Dispatcher.Invoke(() => Log(string.Format("🎮 Injected remote keyevent {0} to {1}", keycode, device.Model)));
            });
        }

        private void SendTextToPhone()
        {
            var device = _deviceComboBox.SelectedItem as DeviceInfo;
            if (device == null || string.IsNullOrEmpty(_adbPath))
            {
                System.Windows.MessageBox.Show("Connect a device first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string text = _txtSendText.Text;
            if (string.IsNullOrEmpty(text) || text == "Type text to inject to phone input...") return;

            string escaped = text.Replace(" ", "%s").Replace("\"", "\\\"");

            Task.Run(() =>
            {
                RunCommand(_adbPath, string.Format("-s \"{0}\" shell input text \"{1}\"", device.Serial, escaped));
                Dispatcher.Invoke(() =>
                {
                    Log("📤 Sent text to mobile input: " + text);
                    _txtSendText.Clear();
                });
            });
        }
        #endregion

        #region ADB & Device Operations
        private async void RefreshDevices(bool autoStartIfConfigured = false)
        {
            Log("Scanning connected Android devices...");

            var devices = await Task.Run(() => GetAdbDevices());
            _connectedDevices = devices;

            _deviceComboBox.Items.Clear();
            foreach (var dev in _connectedDevices)
            {
                _deviceComboBox.Items.Add(dev);
            }

            if (_connectedDevices.Count > 0)
            {
                _deviceComboBox.SelectedIndex = 0;
                UpdateStatusBadge(true, string.Format("{0} Device(s) Ready", _connectedDevices.Count));

                if (autoStartIfConfigured && _chkAutoStartOnPlug.IsChecked == true && (_scrcpyProcess == null || _scrcpyProcess.HasExited))
                {
                    Log("⚡ Auto-starting mirror session...");
                    _notifyIcon.ShowBalloonTip(3000, "Android Kit", "Device connected! Auto-starting screen mirror & control...", Forms.ToolTipIcon.Info);
                    StartMirroring();
                }
            }
            else
            {
                UpdateSelectedDeviceDetails();
                UpdateStatusBadge(false, "No Device Connected");
            }
        }

        private void CheckDeviceStateChange()
        {
            Task.Run(() =>
            {
                var devices = GetAdbDevices();
                bool stateChanged = devices.Count != _connectedDevices.Count ||
                                    !devices.Select(d => d.Serial).SequenceEqual(_connectedDevices.Select(d => d.Serial));

                if (stateChanged)
                {
                    Dispatcher.Invoke(() => RefreshDevices(autoStartIfConfigured: true));
                }
            });
        }

        private List<DeviceInfo> GetAdbDevices()
        {
            var list = new List<DeviceInfo>();
            if (string.IsNullOrEmpty(_adbPath)) return list;

            string output = RunCommand(_adbPath, "devices -l");
            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith("List of devices")) continue;

                var match = Regex.Match(line, @"^(\S+)\s+(\S+)(.*)$");
                if (match.Success)
                {
                    string serial = match.Groups[1].Value.Trim();
                    string status = match.Groups[2].Value.Trim();
                    string extra = match.Groups[3].Value.Trim();

                    string model = "Android Mobile";
                    var modelMatch = Regex.Match(extra, @"model:(\S+)");
                    if (modelMatch.Success) model = modelMatch.Groups[1].Value.Replace("_", " ");

                    bool isWifi = serial.Contains(":") || serial.Contains(".");

                    list.Add(new DeviceInfo
                    {
                        Serial = serial,
                        Status = status,
                        Model = model,
                        ConnectionType = isWifi ? "Wi-Fi" : "USB"
                    });
                }
            }

            return list;
        }

        private void UpdateSelectedDeviceDetails()
        {
            var selected = _deviceComboBox.SelectedItem as DeviceInfo;
            if (selected == null)
            {
                _phoneModelText.Text = "No Phone Connected";
                _phoneSerialText.Text = "Connect via USB & enable USB Debugging";
                _phoneStatusText.Text = "OFFLINE";
                _phoneStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                _phoneStatusText.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#451A1A"));
                return;
            }

            _phoneModelText.Text = selected.Model;
            _phoneSerialText.Text = string.Format("Mode: {0} • Serial: {1}", selected.ConnectionType, selected.Serial);

            if (selected.Status == "unauthorized")
            {
                _phoneStatusText.Text = "UNAUTHORIZED";
                _phoneStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                _phoneStatusText.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45301A"));
            }
            else
            {
                _phoneStatusText.Text = "READY (" + selected.ConnectionType.ToUpper() + ")";
                _phoneStatusText.Foreground = _mdAndroidGreen;
                _phoneStatusText.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B"));
            }
        }

        private void UpdateStatusBadge(bool connected, string text)
        {
            _statusBadgeText.Text = text;
            if (connected)
            {
                _statusDot.Fill = _mdAndroidGreen;
                _statusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B"));
                _statusBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
            }
            else
            {
                _statusDot.Fill = _mdTextMuted;
                _statusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121826"));
                _statusBadgeText.Foreground = _mdTextMuted;
            }
        }
        #endregion

        #region Mirroring & Remote Controller
        private void OnMirrorButtonClick(object sender, RoutedEventArgs e)
        {
            if (_scrcpyProcess != null && !_scrcpyProcess.HasExited)
            {
                StopMirroring();
            }
            else
            {
                StartMirroring();
            }
        }

        private void StartMirroring()
        {
            if (string.IsNullOrEmpty(_scrcpyPath))
            {
                System.Windows.MessageBox.Show("scrcpy engine executable not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var device = _deviceComboBox.SelectedItem as DeviceInfo;
            if (device == null)
            {
                System.Windows.MessageBox.Show("Please connect a mobile device over USB first.", "No Device", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (device.Status == "unauthorized")
            {
                System.Windows.MessageBox.Show("Mobile device is unauthorized. Check phone screen and allow USB Debugging authorization.", "Unauthorized Device", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int bitrateM = (int)_sliderBitrate.Value;
            int fpsLimit = (int)_sliderFps.Value;

            var args = new StringBuilder();
            args.Append(string.Format("-s \"{0}\" ", device.Serial));
            args.Append(string.Format("--window-title=\"Android Kit - {0}\" ", device.Model));

            // Power & Display Toggles
            if (_chkTurnScreenOff.IsChecked == true) args.Append("-S ");
            if (_chkStayAwake.IsChecked == true) args.Append("-w ");
            if (_chkAlwaysOnTop.IsChecked == true) args.Append("--always-on-top ");
            if (_chkAudioForward.IsChecked == false) args.Append("--no-audio ");

            // Keyboard & Mouse Full Remote Control
            if (_chkKeyboardMouseControl.IsChecked == false)
            {
                args.Append("--no-control ");
            }

            // Dynamic Responsive Bitrate & FPS parameters
            args.Append(string.Format("--video-bit-rate={0}M --max-fps={1} ", bitrateM, fpsLimit));

            if (_selectedPreset == "Fast")
            {
                args.Append("--max-size=1280 ");
            }
            else if (_selectedPreset == "Ultra")
            {
                args.Append("--max-size=2560 ");
            }
            else // Balanced
            {
                args.Append("--max-size=1920 ");
            }

            Log("Launching Dynamic Mirror & Remote Session: " + args.ToString());

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _scrcpyPath,
                    Arguments = args.ToString(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _scrcpyProcess = Process.Start(psi);
                _mirrorButton.Content = "⏹   STOP MIRROR & REMOTE CONTROL";
                _mirrorButton.Background = CreateLinearGradient("#EF4444", "#DC2626", 90);
                _mirrorButton.Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#EF4444"), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 };

                UpdateStatusBadge(true, "🔴 MIRROR STREAM LIVE");
                Log("Remote control & mirror session live for " + device.Model);

                Task.Run(() =>
                {
                    _scrcpyProcess.WaitForExit();
                    Dispatcher.Invoke(() =>
                    {
                        _mirrorButton.Content = "▶   START SCREEN MIRROR & REMOTE CONTROL";
                        _mirrorButton.Background = CreateLinearGradient("#3DDC84", "#10B981", 90);
                        _mirrorButton.Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#3DDC84"), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 };
                        UpdateStatusBadge(_connectedDevices.Count > 0, _connectedDevices.Count > 0 ? string.Format("{0} Device(s) Ready", _connectedDevices.Count) : "No Device Connected");
                        Log("Screen mirror & remote control session ended.");
                    });
                });
            }
            catch (Exception ex)
            {
                Log("Error starting scrcpy: " + ex.Message);
                System.Windows.MessageBox.Show("Failed to launch mirroring: " + ex.Message, "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopMirroring()
        {
            if (_scrcpyProcess != null && !_scrcpyProcess.HasExited)
            {
                try
                {
                    _scrcpyProcess.Kill();
                }
                catch { }
            }
        }
        #endregion

        #region Utilities & Tools
        private void ToggleRecording()
        {
            if (string.IsNullOrEmpty(_scrcpyPath)) return;

            var device = _deviceComboBox.SelectedItem as DeviceInfo;
            if (device == null)
            {
                System.Windows.MessageBox.Show("Connect a device first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string downloadsFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string fileName = string.Format("Mirror_Record_{0:yyyyMMdd_HHmmss}.mp4", DateTime.Now);
            string fullPath = System.IO.Path.Combine(downloadsFolder, fileName);

            Log("Starting MP4 Screen Recording to: " + fullPath);
            StopMirroring();

            var args = string.Format("-s \"{0}\" -S -w --record=\"{1}\" --window-title=\"Recording - {2}\"", device.Serial, fullPath, device.Model);
            Process.Start(_scrcpyPath, args);

            _notifyIcon.ShowBalloonTip(3000, "Recording Started", "Saving MP4 video to Downloads: " + fileName, Forms.ToolTipIcon.Info);
        }

        private void TakeScreenshot()
        {
            var device = _deviceComboBox.SelectedItem as DeviceInfo;
            if (device == null || string.IsNullOrEmpty(_adbPath)) return;

            string downloadsFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string fileName = string.Format("Screenshot_{0:yyyyMMdd_HHmmss}.png", DateTime.Now);
            string fullPath = System.IO.Path.Combine(downloadsFolder, fileName);

            Task.Run(() =>
            {
                RunCommand(_adbPath, string.Format("-s \"{0}\" shell screencap -p /sdcard/screen.png", device.Serial));
                RunCommand(_adbPath, string.Format("-s \"{0}\" pull /sdcard/screen.png \"{1}\"", device.Serial, fullPath));
                RunCommand(_adbPath, string.Format("-s \"{0}\" shell rm /sdcard/screen.png", device.Serial));

                Dispatcher.Invoke(() =>
                {
                    Log("📸 HD Screenshot saved to: " + fullPath);
                    _notifyIcon.ShowBalloonTip(3000, "Screenshot Saved", "Saved to Downloads: " + fileName, Forms.ToolTipIcon.Info);
                    Process.Start("explorer.exe", string.Format("/select,\"{0}\"", fullPath));
                });
            });
        }

        private async void SwitchToWifiMode()
        {
            var device = _deviceComboBox.SelectedItem as DeviceInfo;
            if (device == null || string.IsNullOrEmpty(_adbPath))
            {
                System.Windows.MessageBox.Show("Connect phone via USB cable first to enable wireless pairing.", "USB Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (device.ConnectionType == "Wi-Fi")
            {
                System.Windows.MessageBox.Show("Device is already connected via Wi-Fi!", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Log("Enabling Wireless ADB on port 5555...");

            string ipAddress = await Task.Run(() =>
            {
                RunCommand(_adbPath, string.Format("-s \"{0}\" tcpip 5555", device.Serial));
                Thread.Sleep(1000);

                string ipOutput = RunCommand(_adbPath, string.Format("-s \"{0}\" shell ip route", device.Serial));
                var match = Regex.Match(ipOutput, @"src\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)");
                if (match.Success) return match.Groups[1].Value;

                ipOutput = RunCommand(_adbPath, string.Format("-s \"{0}\" shell ip addr show wlan0", device.Serial));
                match = Regex.Match(ipOutput, @"inet\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)");
                return match.Success ? match.Groups[1].Value : null;
            });

            if (!string.IsNullOrEmpty(ipAddress))
            {
                Log(string.Format("Connecting over Wi-Fi to IP: {0}:5555...", ipAddress));
                string connectResult = await Task.Run(() => RunCommand(_adbPath, string.Format("connect {0}:5555", ipAddress)));
                Log("Wi-Fi Connect Result: " + connectResult);

                System.Windows.MessageBox.Show(string.Format("Wireless pairing successful for {0}:5555!\n\nYou can now unplug the USB cable and control mobile wire-free over Wi-Fi.", ipAddress), "Wi-Fi Ready", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshDevices();
            }
            else
            {
                Log("Could not retrieve Wi-Fi IP. Ensure laptop and mobile are on the same Wi-Fi network.");
                System.Windows.MessageBox.Show("Could not retrieve phone Wi-Fi IP address. Ensure phone is connected to Wi-Fi.", "Wi-Fi Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowSetupGuide()
        {
            string guide =
                "🤖 Android Kit Responsive UX & Laptop Shortcuts:\n\n" +
                "🖱️ Laptop Mouse Control:\n" +
                "• Left-Click: Tap on mobile screen\n" +
                "• Click & Drag: Swipe / Scroll (Up, Down, Left, Right)\n" +
                "• Right-Click: Mobile BACK button\n" +
                "• Middle-Click: Mobile HOME button\n" +
                "• Right-Click (when screen is dark): Turn On Mobile Screen\n\n" +
                "🎛️ Dynamic UX Controls:\n" +
                "• Live Bitrate Slider: Adjust video stream quality from 2 Mbps to 32 Mbps on the fly!\n" +
                "• FPS Limit Slider: Lock max frame rate from 30 FPS to 120 FPS\n\n" +
                "⌨️ Laptop Keyboard Controls:\n" +
                "• Direct Typing: Type text directly into any mobile app (WhatsApp, Messages, Search, Notes)\n" +
                "• Alt + O: Turn off phone physical screen while keeping laptop control active\n" +
                "• Alt + F / F11: Toggle Fullscreen Mode\n" +
                "• Ctrl + V: Copy text on laptop and paste directly into mobile app!\n" +
                "• Drag & Drop .APK: Automatically install app on phone!";

            System.Windows.MessageBox.Show(guide, "Android Kit Guide", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        #endregion

        #region Helper Routines
        private string RunCommand(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    return output;
                }
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        private void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string line = string.Format("[{0}] {1}\n", timestamp, message);
            _logConsole.AppendText(line);
            _logConsole.ScrollToEnd();
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopMirroring();

            if (_pollTimer != null) _pollTimer.Stop();
            if (_pulseTimer != null) _pulseTimer.Stop();
            if (_usbWatcher != null)
            {
                try { _usbWatcher.Stop(); _usbWatcher.Dispose(); } catch { }
            }
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }
        #endregion
    }
}
