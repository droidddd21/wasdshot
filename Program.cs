using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Wasdshot
{
    sealed class CapturePreview : Control
    {
        Image image;

        public CapturePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            TabStop = false;
        }

        public Image Image
        {
            get { return image; }
            set { image = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (image == null) return;
            e.Graphics.DrawImage(image, ClientRectangle);
            using (Brush shade = new SolidBrush(Color.FromArgb(77, 7, 16, 29)))
                e.Graphics.FillRectangle(shade, ClientRectangle);
        }
    }

    sealed class OverlayForm : Form
    {
        const int DwmwaTransitionsForceDisabled = 3;
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
        Bitmap screenshot;
        string shortcut;
        bool webReady;
        bool initializationStarted;
        bool captureActive;
        int captureVersion;
        readonly WebView2 web;
        readonly CapturePreview preview;
        static readonly object EnvironmentLock = new object();
        static Task<CoreWebView2Environment> environmentTask;

        public OverlayForm(Rectangle desktop)
        {
            FormBorderStyle = FormBorderStyle.None;
            Text = "wasdshot-overlay";
            StartPosition = FormStartPosition.Manual;
            Bounds = desktop;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Opacity = 1;
            BackColor = Color.Black;
            web = new WebView2 { Dock = DockStyle.Fill };
            web.KeyDown += delegate(object sender, KeyEventArgs args) { if (args.KeyCode == Keys.Escape) Dismiss(); };
            Controls.Add(web);
            preview = new CapturePreview { Dock = DockStyle.Fill };
            Controls.Add(preview);
            Shown += delegate { EnsureWebView(); };
        }

        public void WarmUp()
        {
            if (IsDisposed || initializationStarted) return;
            if (!IsHandleCreated) CreateControl();
        }

        void EnsureWebView()
        {
            if (initializationStarted || IsDisposed) return;
            initializationStarted = true;
            InitializeWebView();
        }

        static Task<CoreWebView2Environment> GetEnvironment()
        {
            lock (EnvironmentLock)
            {
                if (environmentTask == null)
                {
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wasdshot", "webview2-v1-final");
                    environmentTask = CoreWebView2Environment.CreateAsync(null, folder);
                }
                return environmentTask;
            }
        }

        public void BeginCapture(Bitmap image, Rectangle desktop, string activeShortcut)
        {
            Bounds = desktop;
            Opacity = 1;
            if (!IsHandleCreated) CreateControl();
            preview.Image = null;
            if (screenshot != null) screenshot.Dispose();
            screenshot = image;
            captureVersion++;
            captureActive = true;
            preview.Image = screenshot;
            preview.Visible = true;
            preview.BringToFront();
            shortcut = activeShortcut;
            int disableTransitions = 1;
            DwmSetWindowAttribute(Handle, DwmwaTransitionsForceDisabled, ref disableTransitions, sizeof(int));
            if (!Visible)
            {
                Show();
                preview.Refresh();
                Update();
            }
            EnsureWebView();
            if (webReady) SendCapture();
        }

        public bool IsCaptureActive { get { return captureActive && Visible; } }

        void SendCapture()
        {
            if (screenshot == null || web.CoreWebView2 == null) return;
            Bitmap copy = (Bitmap)screenshot.Clone();
            int version = captureVersion;
            string activeShortcut = shortcut;
            Task.Run(delegate
            {
                using (copy)
                using (MemoryStream stream = new MemoryStream())
                {
                    try
                    {
                        copy.Save(stream, ImageFormat.Png);
                        string encoded = Convert.ToBase64String(stream.ToArray());
                        if (!IsDisposed) BeginInvoke((Action)delegate
                        {
                            if (version != captureVersion || !webReady || IsDisposed) return;
                            web.CoreWebView2.PostWebMessageAsString("capture:" + encoded);
                            web.CoreWebView2.PostWebMessageAsString("shortcut:" + activeShortcut);
                        });
                    }
                    catch (Exception ex)
                    {
                        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "wasdshot-error.log"), DateTime.Now + " Encode: " + ex + Environment.NewLine); } catch { }
                    }
                }
            });
        }

        public void Dismiss()
        {
            if (IsDisposed) return;
            captureActive = false;
            captureVersion++;
            preview.Image = null;
            if (screenshot != null) { screenshot.Dispose(); screenshot = null; }
            Close();
        }

        public void SetTheme(string theme)
        {
            if (webReady) web.CoreWebView2.PostWebMessageAsString("theme:" + theme);
        }

        public void SetPetEnabled(bool enabled)
        {
            if (webReady) web.CoreWebView2.PostWebMessageAsString(enabled ? "pet:on" : "pet:off");
        }

        public void SetPetKind(string kind)
        {
            if (webReady) web.CoreWebView2.PostWebMessageAsString("pet-kind:" + kind);
        }

        public void SetQuickSave(bool enabled)
        {
            if (webReady) web.CoreWebView2.PostWebMessageAsString(enabled ? "quick-save:on" : "quick-save:off");
        }

        public void SetLanguage(string language)
        {
            if (webReady) web.CoreWebView2.PostWebMessageAsString("language:" + language);
        }

        public void RequestCopy()
        {
            if (IsCaptureActive && webReady) web.CoreWebView2.PostWebMessageAsString("copy-request");
        }

        public void RequestUndo()
        {
            if (IsCaptureActive && webReady) web.CoreWebView2.PostWebMessageAsString("undo-request");
        }

        public event Action<string> ThemeChanged;
        public event Action<string> PetChanged;
        public event Action<string> PetKindChanged;
        public event Action<string> QuickSaveChanged;
        public event Action<string> LanguageChanged;

        async void InitializeWebView()
        {
            try
            {
                CoreWebView2Environment environment = await GetEnvironment();
                await web.EnsureCoreWebView2Async(environment);
                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                web.CoreWebView2.WebMessageReceived += OnWebMessage;
                web.CoreWebView2.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs args)
                {
                    if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                        args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive ||
                        args.ProcessFailedKind == CoreWebView2ProcessFailedKind.GpuProcessExited ||
                        args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                        BeginInvoke((Action)delegate { if (!IsDisposed) Close(); });
                };
                string page = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui", "dist", "index.html");
                if (!File.Exists(page)) throw new FileNotFoundException("Не найдены файлы интерфейса", page);
                web.Source = new Uri(page);
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "wasdshot-error.log"), DateTime.Now + " WebView2: " + ex + Environment.NewLine);
                MessageBox.Show("Не удалось открыть оверлей: " + ex.Message, "wasdshot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (message == "ready")
            {
                webReady = true;
                if (captureActive) SendCapture();
                if (ThemeChanged != null) ThemeChanged(null);
                if (PetChanged != null) PetChanged(null);
                if (PetKindChanged != null) PetKindChanged(null);
                if (QuickSaveChanged != null) QuickSaveChanged(null);
                if (LanguageChanged != null) LanguageChanged(null);
            }
            else if (message == "loaded" && captureActive)
            {
                // The renderer reports this only after the captured image has reached two animation frames.
                // Defer once more so the native preview never exposes WebView's blank startup surface.
                BeginInvoke((Action)delegate
                {
                    if (!captureActive || IsDisposed) return;
                    web.BringToFront();
                    preview.Visible = false;
                    BringToFront();
                    Activate();
                    web.Focus();
                });
            }
            else if (message == "cancel") Dismiss();
            else if (message.StartsWith("copy:")) Export(message.Substring(5), false);
            else if (message.StartsWith("save:")) Export(message.Substring(5), true);
            else if (message.StartsWith("quick-save:")) ExportQuick(message.Substring(11));
            else if (message.StartsWith("error:")) MessageBox.Show("Не удалось подготовить снимок: " + message.Substring(6), "wasdshot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else if (message.StartsWith("theme:") && ThemeChanged != null) ThemeChanged(message.Substring(6));
            else if (message.StartsWith("pet:") && PetChanged != null) PetChanged(message.Substring(4));
        }

        void Export(string encoded, bool save)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(Convert.FromBase64String(encoded)))
                using (Image decoded = Image.FromStream(stream))
                using (Bitmap image = new Bitmap(decoded))
                {
                    if (!save) { Clipboard.SetDataObject(image, true, 10, 50); Dismiss(); return; }
                    Hide();
                    using (SaveFileDialog dialog = new SaveFileDialog())
                    {
                        dialog.Filter = "PNG (*.png)|*.png";
                        dialog.DefaultExt = "png";
                        dialog.FileName = "wasdshot-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".png";
                        if (dialog.ShowDialog() == DialogResult.OK) { image.Save(dialog.FileName, ImageFormat.Png); Dismiss(); return; }
                    }
                    Show(); Activate();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось " + (save ? "сохранить" : "скопировать") + ": " + ex.Message, "wasdshot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (!IsDisposed && !Visible) { Show(); Activate(); }
            }
        }

        void ExportQuick(string encoded)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(Convert.FromBase64String(encoded)))
                using (Image decoded = Image.FromStream(stream))
                using (Bitmap image = new Bitmap(decoded))
                {
                    string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    if (String.IsNullOrEmpty(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string file = Path.Combine(folder, "wasdshot-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".png");
                    image.Save(file, ImageFormat.Png);
                    Dismiss();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось быстро сохранить снимок: " + ex.Message, "wasdshot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && screenshot != null) { preview.Image = null; screenshot.Dispose(); }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Dismiss(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { RequestCopy(); return true; }
            if (keyData == (Keys.Control | Keys.Z)) { RequestUndo(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    sealed class HotkeyWindow : NativeWindow
    {
        public const int HotkeyMessage = 0x0312;
        public const int CaptureMessage = 0x8001;
        public const int DismissMessage = 0x8002;
        public const int CopyMessage = 0x8003;
        public const int UndoMessage = 0x8004;
        readonly Action capture;
        readonly Action dismiss;
        readonly Action copy;
        readonly Action undo;
        public HotkeyWindow(Action onCapture, Action onDismiss, Action onCopy, Action onUndo)
        {
            capture = onCapture;
            dismiss = onDismiss;
            copy = onCopy;
            undo = onUndo;
            CreateHandle(new CreateParams { Caption = "wasdshot-shadcn-controller" });
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == HotkeyMessage || m.Msg == CaptureMessage) { capture(); return; }
            if (m.Msg == DismissMessage) { dismiss(); return; }
            if (m.Msg == CopyMessage) { copy(); return; }
            if (m.Msg == UndoMessage) { undo(); return; }
            base.WndProc(ref m);
        }
        public void Close() { DestroyHandle(); }
    }

    sealed class WindowsMenuColorTable : ProfessionalColorTable
    {
        readonly Color background;
        readonly Color selected;
        readonly Color border;

        public WindowsMenuColorTable(bool light)
        {
            background = light ? Color.FromArgb(250, 250, 250) : Color.FromArgb(32, 32, 32);
            selected = light ? Color.FromArgb(226, 241, 255) : Color.FromArgb(61, 61, 61);
            border = light ? Color.FromArgb(218, 218, 218) : Color.FromArgb(75, 75, 75);
        }

        public override Color ToolStripDropDownBackground { get { return background; } }
        public override Color MenuItemSelected { get { return selected; } }
        public override Color MenuItemSelectedGradientBegin { get { return selected; } }
        public override Color MenuItemSelectedGradientEnd { get { return selected; } }
        public override Color MenuItemBorder { get { return border; } }
        public override Color ImageMarginGradientBegin { get { return background; } }
        public override Color ImageMarginGradientMiddle { get { return background; } }
        public override Color ImageMarginGradientEnd { get { return background; } }
        public override Color MenuBorder { get { return border; } }
    }

    sealed class CaptureContext : ApplicationContext
    {
        const int HotkeyId = 0x5753;
        const int KeyboardHookId = 13;
        const int KeyDownMessage = 0x0100;
        const int KeyUpMessage = 0x0101;
        const int SysKeyDownMessage = 0x0104;
        const int SysKeyUpMessage = 0x0105;
        delegate IntPtr KeyboardHookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr SetWindowsHookEx(int idHook, KeyboardHookProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string moduleName);
        readonly HotkeyWindow hotkey;
        readonly NotifyIcon tray;
        readonly KeyboardHookProc hookCallback;
        OverlayForm overlay;
        IntPtr keyboardHook;
        bool interceptSnipShortcut;
        bool snipKeyHeld;
        bool copyKeyHeld;
        bool undoKeyHeld;
        bool registered;
        string shortcut = "Ctrl + Shift + S";
        string fallbackShortcut = "Ctrl + Shift + S";
        string theme = "blue";
        string language = "ru";
        string petKind = "axolotl";
        bool petEnabled;
        bool quickSave;
        bool exiting;
        readonly string themeFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wasdshot", "theme.txt");
        readonly string petFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wasdshot", "pet.txt");
        readonly string petKindFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wasdshot", "pet-kind.txt");
        readonly string quickSaveFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wasdshot", "mode.txt");
        readonly string languageFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wasdshot", "language.txt");
        ToolStripMenuItem petItem;

        public CaptureContext()
        {
            try { if (File.Exists(themeFile)) theme = NormalizeTheme(File.ReadAllText(themeFile).Trim()); } catch { }
            try { if (File.Exists(petFile)) petEnabled = File.ReadAllText(petFile).Trim() == "on"; } catch { }
            try { if (File.Exists(petKindFile)) petKind = NormalizePetKind(File.ReadAllText(petKindFile).Trim()); } catch { }
            try { if (File.Exists(quickSaveFile)) quickSave = File.ReadAllText(quickSaveFile).Trim() == "quick"; } catch { }
            try { if (File.Exists(languageFile)) language = File.ReadAllText(languageFile).Trim() == "en" ? "en" : "ru"; } catch { }
            hotkey = new HotkeyWindow(OpenOverlay, delegate { if (overlay != null && !overlay.IsDisposed && overlay.IsCaptureActive) overlay.Dismiss(); }, RequestCopy, RequestUndo);
            registered = RegisterHotKey(hotkey.Handle, HotkeyId, 0x0002 | 0x0004, (uint)Keys.S);
            if (!registered)
            {
                registered = RegisterHotKey(hotkey.Handle, HotkeyId, 0x0002 | 0x0001, (uint)Keys.S);
                shortcut = registered ? "Ctrl + Alt + S" : "меню трея";
            }
            fallbackShortcut = shortcut;
            hookCallback = OnKeyboardHook;
            keyboardHook = SetWindowsHookEx(KeyboardHookId, hookCallback, GetModuleHandle(null), 0);
            interceptSnipShortcut = keyboardHook != IntPtr.Zero;
            if (interceptSnipShortcut) shortcut = "Win + Shift + S";
            tray = new NotifyIcon();
            tray.Icon = CreateIcon(theme);
            tray.Text = "wasdshot — " + shortcut;
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += delegate { ApplySystemMenuTheme(); };
            tray.ContextMenuStrip.Items.Add("Сделать скриншот", null, delegate { OpenOverlay(); });
            ToolStripMenuItem colors = new ToolStripMenuItem("Цвет интерфейса и ацелота");
            foreach (string choice in new[] { "blue", "red", "green" })
            {
                string selected = choice;
                ToolStripMenuItem item = new ToolStripMenuItem(choice == "blue" ? "Голубой" : choice == "red" ? "Красный" : "Зелёный");
                item.Checked = theme == choice;
                item.Click += delegate { SetTheme(selected); foreach (ToolStripMenuItem other in colors.DropDownItems) other.Checked = other == item; };
                colors.DropDownItems.Add(item);
            }
            tray.ContextMenuStrip.Items.Add(colors);
            ToolStripMenuItem languageMenu = new ToolStripMenuItem("Язык / Language");
            ToolStripMenuItem russian = new ToolStripMenuItem("Русский") { Checked = language == "ru" };
            ToolStripMenuItem english = new ToolStripMenuItem("English") { Checked = language == "en" };
            russian.Click += delegate { SetLanguage("ru"); russian.Checked = true; english.Checked = false; };
            english.Click += delegate { SetLanguage("en"); russian.Checked = false; english.Checked = true; };
            languageMenu.DropDownItems.Add(russian);
            languageMenu.DropDownItems.Add(english);
            tray.ContextMenuStrip.Items.Add(languageMenu);
            petItem = new ToolStripMenuItem("Показывать питомца");
            petItem.CheckOnClick = true;
            petItem.Checked = petEnabled;
            petItem.CheckedChanged += delegate { SetPetEnabled(petItem.Checked); };
            tray.ContextMenuStrip.Items.Add(petItem);
            ToolStripMenuItem petMenu = new ToolStripMenuItem("Выбрать питомца");
            foreach (string choice in new[] { "axolotl", "ivava", "miku" })
            {
                string selected = choice;
                string title = choice == "axolotl" ? "Acelot" : choice == "ivava" ? "Iwawwa" : "Hatsune Miku";
                ToolStripMenuItem item = new ToolStripMenuItem(title) { Checked = petKind == choice };
                item.Click += delegate { SetPetKind(selected); foreach (ToolStripMenuItem other in petMenu.DropDownItems) other.Checked = other == item; };
                petMenu.DropDownItems.Add(item);
            }
            tray.ContextMenuStrip.Items.Add(petMenu);
            ToolStripMenuItem modeMenu = new ToolStripMenuItem("Режим скриншота");
            ToolStripMenuItem editMode = new ToolStripMenuItem("Редактировать после выделения") { Checked = !quickSave };
            ToolStripMenuItem quickMode = new ToolStripMenuItem("Быстро сохранить в Изображения") { Checked = quickSave };
            editMode.Click += delegate { SetQuickSave(false); editMode.Checked = true; quickMode.Checked = false; };
            quickMode.Click += delegate { SetQuickSave(true); quickMode.Checked = true; editMode.Checked = false; };
            modeMenu.DropDownItems.Add(editMode);
            modeMenu.DropDownItems.Add(quickMode);
            tray.ContextMenuStrip.Items.Add(modeMenu);
            ToolStripMenuItem interceptItem = new ToolStripMenuItem("Перехватывать Win + Shift + S");
            interceptItem.CheckOnClick = true;
            interceptItem.Checked = interceptSnipShortcut;
            interceptItem.Enabled = keyboardHook != IntPtr.Zero;
            interceptItem.CheckedChanged += delegate
            {
                interceptSnipShortcut = interceptItem.Checked;
                shortcut = interceptSnipShortcut ? "Win + Shift + S" : fallbackShortcut;
                tray.Text = "wasdshot — " + shortcut;
            };
            tray.ContextMenuStrip.Items.Add(interceptItem);
            tray.ContextMenuStrip.Items.Add("Выход", null, delegate { ExitThread(); });
            ApplySystemMenuTheme();
            tray.DoubleClick += delegate { OpenOverlay(); };
            tray.Visible = true;
            tray.ShowBalloonTip(3000, "wasdshot запущен", "Снимок: Win + Shift + S. Настройки и выход — в трее.", ToolTipIcon.Info);
            PrepareOverlay();
        }

        void RequestCopy()
        {
            if (overlay != null && !overlay.IsDisposed) overlay.RequestCopy();
        }

        void RequestUndo()
        {
            if (overlay != null && !overlay.IsDisposed) overlay.RequestUndo();
        }

        void CreateOverlay(Rectangle desktop)
        {
            OverlayForm created = new OverlayForm(desktop);
            overlay = created;
            created.ThemeChanged += delegate(string selected) { if (selected == null && !created.IsDisposed) created.SetTheme(theme); else if (selected != null) SetTheme(selected); };
            created.PetChanged += delegate(string selected) { if (selected == null && !created.IsDisposed) created.SetPetEnabled(petEnabled); else if (selected != null) SetPetEnabled(selected == "on"); };
            created.PetKindChanged += delegate(string selected) { if (selected == null && !created.IsDisposed) created.SetPetKind(petKind); else if (selected != null) SetPetKind(selected); };
            created.QuickSaveChanged += delegate(string selected) { if (selected == null && !created.IsDisposed) created.SetQuickSave(quickSave); else if (selected != null) SetQuickSave(selected == "on"); };
            created.LanguageChanged += delegate(string selected) { if (selected == null && !created.IsDisposed) created.SetLanguage(language); else if (selected != null) SetLanguage(selected); };
            created.FormClosed += delegate
            {
                if (!ReferenceEquals(overlay, created)) return;
                overlay = null;
                if (!exiting) PrepareOverlay();
            };
        }

        void PrepareOverlay()
        {
            if (exiting || (overlay != null && !overlay.IsDisposed)) return;
            CreateOverlay(SystemInformation.VirtualScreen);
            overlay.WarmUp();
        }

        static string NormalizeTheme(string value) { return value == "red" || value == "green" ? value : "blue"; }

        static string NormalizePetKind(string value) { return value == "ivava" || value == "miku" ? value : "axolotl"; }

        static bool UsesLightApps()
        {
            if (SystemInformation.HighContrast) return true;
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return Convert.ToInt32(value) != 0;
            }
            catch { return true; }
        }

        void ApplySystemMenuTheme()
        {
            ContextMenuStrip menu = tray.ContextMenuStrip;
            bool light = UsesLightApps();
            menu.Renderer = new ToolStripProfessionalRenderer(new WindowsMenuColorTable(light));
            ApplyMenuColors(menu.Items, light);
            if (menu.IsHandleCreated) SetWindowTheme(menu.Handle, light ? "Explorer" : "DarkMode_Explorer", null);
        }

        static void ApplyMenuColors(ToolStripItemCollection items, bool light)
        {
            Color background = light ? Color.FromArgb(250, 250, 250) : Color.FromArgb(32, 32, 32);
            Color foreground = light ? Color.FromArgb(25, 25, 25) : Color.FromArgb(244, 244, 244);
            foreach (ToolStripItem item in items)
            {
                item.BackColor = background;
                item.ForeColor = foreground;
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem != null)
                {
                    menuItem.DropDown.BackColor = background;
                    menuItem.DropDown.ForeColor = foreground;
                    ApplyMenuColors(menuItem.DropDownItems, light);
                    if (menuItem.DropDown.IsHandleCreated) SetWindowTheme(menuItem.DropDown.Handle, light ? "Explorer" : "DarkMode_Explorer", null);
                }
            }
        }

        void SetTheme(string choice)
        {
            theme = NormalizeTheme(choice);
            Icon old = tray.Icon;
            tray.Icon = CreateIcon(theme);
            if (old != null) old.Dispose();
            try { Directory.CreateDirectory(Path.GetDirectoryName(themeFile)); File.WriteAllText(themeFile, theme); } catch { }
            if (overlay != null && !overlay.IsDisposed) overlay.SetTheme(theme);
            foreach (ToolStripItem menuItem in tray.ContextMenuStrip.Items)
                if (menuItem is ToolStripMenuItem && menuItem.Text == "Цвет интерфейса и ацелота")
                    foreach (ToolStripMenuItem item in ((ToolStripMenuItem)menuItem).DropDownItems)
                        item.Checked = item.Text == (theme == "blue" ? "Голубой" : theme == "red" ? "Красный" : "Зелёный");
        }

        void SetPetEnabled(bool enabled)
        {
            petEnabled = enabled;
            if (petItem.Checked != enabled) petItem.Checked = enabled;
            try { Directory.CreateDirectory(Path.GetDirectoryName(petFile)); File.WriteAllText(petFile, enabled ? "on" : "off"); } catch { }
            if (overlay != null && !overlay.IsDisposed) overlay.SetPetEnabled(enabled);
        }

        void SetPetKind(string choice)
        {
            petKind = NormalizePetKind(choice);
            try { Directory.CreateDirectory(Path.GetDirectoryName(petKindFile)); File.WriteAllText(petKindFile, petKind); } catch { }
            if (overlay != null && !overlay.IsDisposed) overlay.SetPetKind(petKind);
        }

        void SetQuickSave(bool enabled)
        {
            quickSave = enabled;
            try { Directory.CreateDirectory(Path.GetDirectoryName(quickSaveFile)); File.WriteAllText(quickSaveFile, enabled ? "quick" : "edit"); } catch { }
            if (overlay != null && !overlay.IsDisposed) overlay.SetQuickSave(enabled);
        }

        void SetLanguage(string choice)
        {
            language = choice == "en" ? "en" : "ru";
            try { Directory.CreateDirectory(Path.GetDirectoryName(languageFile)); File.WriteAllText(languageFile, language); } catch { }
            if (overlay != null && !overlay.IsDisposed) overlay.SetLanguage(language);
        }

        IntPtr OnKeyboardHook(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && Marshal.ReadInt32(data) == (int)Keys.C)
            {
                int kind = message.ToInt32();
                bool pressed = kind == KeyDownMessage || kind == SysKeyDownMessage;
                bool released = kind == KeyUpMessage || kind == SysKeyUpMessage;
                if (released && copyKeyHeld) { copyKeyHeld = false; return new IntPtr(1); }
                bool control = GetAsyncKeyState((int)Keys.ControlKey) < 0;
                if (pressed && control && overlay != null && !overlay.IsDisposed && overlay.IsCaptureActive)
                {
                    if (!copyKeyHeld) PostMessage(hotkey.Handle, HotkeyWindow.CopyMessage, IntPtr.Zero, IntPtr.Zero);
                    copyKeyHeld = true;
                    return new IntPtr(1);
                }
            }
            if (code >= 0 && Marshal.ReadInt32(data) == (int)Keys.Z)
            {
                int kind = message.ToInt32();
                bool pressed = kind == KeyDownMessage || kind == SysKeyDownMessage;
                bool released = kind == KeyUpMessage || kind == SysKeyUpMessage;
                if (released && undoKeyHeld) { undoKeyHeld = false; return new IntPtr(1); }
                bool control = GetAsyncKeyState((int)Keys.ControlKey) < 0;
                if (pressed && control && overlay != null && !overlay.IsDisposed && overlay.IsCaptureActive)
                {
                    if (!undoKeyHeld) PostMessage(hotkey.Handle, HotkeyWindow.UndoMessage, IntPtr.Zero, IntPtr.Zero);
                    undoKeyHeld = true;
                    return new IntPtr(1);
                }
            }
            if (code >= 0 && Marshal.ReadInt32(data) == (int)Keys.Escape && overlay != null && !overlay.IsDisposed && overlay.IsCaptureActive)
            {
                int kind = message.ToInt32();
                if (kind == KeyDownMessage || kind == SysKeyDownMessage)
                    PostMessage(hotkey.Handle, HotkeyWindow.DismissMessage, IntPtr.Zero, IntPtr.Zero);
                return new IntPtr(1);
            }
            if (code >= 0 && interceptSnipShortcut && Marshal.ReadInt32(data) == (int)Keys.S)
            {
                int kind = message.ToInt32();
                bool pressed = kind == KeyDownMessage || kind == SysKeyDownMessage;
                bool released = kind == KeyUpMessage || kind == SysKeyUpMessage;
                bool windows = GetAsyncKeyState((int)Keys.LWin) < 0 || GetAsyncKeyState((int)Keys.RWin) < 0;
                bool shift = GetAsyncKeyState((int)Keys.ShiftKey) < 0;
                if (pressed && windows && shift)
                {
                    if (!snipKeyHeld) PostMessage(hotkey.Handle, HotkeyWindow.CaptureMessage, IntPtr.Zero, IntPtr.Zero);
                    snipKeyHeld = true;
                    return new IntPtr(1);
                }
                if (released && snipKeyHeld) { snipKeyHeld = false; return new IntPtr(1); }
            }
            return CallNextHookEx(keyboardHook, code, message, data);
        }

        void OpenOverlay()
        {
            if (overlay != null && !overlay.IsDisposed && overlay.IsCaptureActive) { overlay.Dismiss(); return; }
            Bitmap screenshot = null;
            try
            {
                Rectangle desktop = SystemInformation.VirtualScreen;
                screenshot = new Bitmap(desktop.Width, desktop.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(screenshot))
                {
                    if (Environment.GetEnvironmentVariable("WASDSHOT_DEMO") == "1") graphics.Clear(Color.FromArgb(57, 78, 105));
                    else graphics.CopyFromScreen(desktop.Location, Point.Empty, desktop.Size);
                }
                if (overlay == null || overlay.IsDisposed) CreateOverlay(desktop);
                overlay.BeginCapture(screenshot, desktop, shortcut);
                screenshot = null;
                SetForegroundWindow(overlay.Handle);
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "wasdshot-error.log"), DateTime.Now + " Capture: " + ex + Environment.NewLine);
                if (screenshot != null) screenshot.Dispose();
                tray.ShowBalloonTip(3000, "wasdshot", "Не удалось снять экран: " + ex.Message, ToolTipIcon.Error);
            }
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            if (overlay != null && !overlay.IsDisposed) overlay.Close();
            if (registered) UnregisterHotKey(hotkey.Handle, HotkeyId);
            if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
            hotkey.Close();
            tray.Visible = false; tray.Dispose();
            base.ExitThreadCore();
        }

        static Icon CreateIcon(string theme)
        {
            string mascot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", theme == "blue" ? "axolotl-tray-96.png" : "axolotl-tray-" + theme + "-96.png");
            using (Bitmap bitmap = new Bitmap(64, 64))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (File.Exists(mascot))
                {
                    using (Bitmap source = new Bitmap(mascot)) graphics.DrawImage(source, 0, 0, 64, 64);
                }
                else
                {
                    using (Brush blue = new SolidBrush(Color.FromArgb(112, 199, 255))) graphics.FillEllipse(blue, 2, 2, 60, 60);
                    using (Pen outline = new Pen(Color.FromArgb(9, 23, 38), 4)) graphics.DrawRectangle(outline, 15, 18, 34, 28);
                }
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }

    static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\wasdshot-shadcn-7a4c65f0", out first))
            {
                if (!first)
                {
                    MessageBox.Show("wasdshot уже запущен в трее. Закройте старую версию через меню и пункт «Выход», затем запустите эту сборку снова.", "wasdshot уже запущен", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new CaptureContext());
            }
        }
    }
}

