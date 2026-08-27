using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace GetMan;

public partial class App : Application
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);

    protected override void OnStartup(StartupEventArgs e)
    {
        if (Environment.GetEnvironmentVariable("GETMAN_TRACE") == "1")
            EnableBindingTrace();

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogCrash(ex);
        };

        base.OnStartup(e);

        // The startup window is created here rather than through StartupUri so alternate
        // entry points (such as --self-check) can take over.
        if (e.Args.Contains("--self-check"))
        {
            AttachConsole(-1);
            Environment.Exit(RunSelfCheck());
            return;
        }

        // Offscreen render of a single view, for reviewing layout without driving the UI.
        //   GetMan.exe --render auth out.png [light]
        var renderIndex = Array.IndexOf(e.Args, "--render");
        if (renderIndex >= 0 && e.Args.Length > renderIndex + 2)
        {
            AttachConsole(-1);
            Environment.Exit(RenderView(e.Args[renderIndex + 1], e.Args[renderIndex + 2],
                e.Args.Contains("light")));
            return;
        }

        // Documentation screenshots, taken from a sandboxed workspace so the shots are the same
        // on every machine and the user's own collections never end up in the README.
        //   GetMan.exe --shots docs/images
        var shotsIndex = Array.IndexOf(e.Args, "--shots");
        if (shotsIndex >= 0 && e.Args.Length > shotsIndex + 1)
        {
            AttachConsole(-1);
            Environment.Exit(RenderShots(e.Args[shotsIndex + 1]));
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>
    /// Renders the whole window in each theme and language. The window is shown off-screen
    /// because Material's floating hints and selection states only settle in a live window.
    /// </summary>
    private static int RenderShots(string outputDir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(outputDir);

            // Each shot closes its window, and the default shutdown mode would tear the whole
            // application down on the first close.
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var sandbox = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GetMan.Shots");
            try { if (System.IO.Directory.Exists(sandbox)) System.IO.Directory.Delete(sandbox, true); } catch { }
            Services.PersistenceService.RootDir = sandbox;

            var shots = new (string File, string Language, bool Light)[]
            {
                ("main-dark", "en", false),
                ("main-light", "en", true),
                ("main-ru", "ru", false),
                ("main-uz", "uz", false)
            };

            foreach (var shot in shots)
            {
                Controls.ThemeManager.Apply(shot.Light ? Controls.AppTheme.Light : Controls.AppTheme.Dark);

                // WindowStyle is left alone: the title bar is part of the content now, so
                // stripping the chrome would hide the very thing the shot should show.
                var window = new MainWindow
                {
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                    Width = 1440,
                    Height = 900
                };
                window.Show();
                Settle();

                // After the window, not before: the view model reads the persisted language in
                // its constructor and would overwrite an earlier switch.
                var vm = (ViewModels.MainViewModel)window.DataContext;
                vm.SelectedLanguage = Services.Loc.Languages.First(l => l.Code == shot.Language);
                vm.SelectedEnvironment = vm.Environments.FirstOrDefault();
                SeedResponse(vm);
                Settle();

                Capture(window, System.IO.Path.Combine(outputDir, shot.File + ".png"));
                window.Close();
                Settle();
            }

            // The secondary windows, in English and dark, one shot each.
            Controls.ThemeManager.Apply(Controls.AppTheme.Dark);

            // Constructed first: the view model restores the language the last shot persisted.
            var vmForDialogs = new ViewModels.MainViewModel();
            vmForDialogs.SelectedLanguage = Services.Loc.Languages.First(l => l.Code == "en");
            var collection = vmForDialogs.Collections.FirstOrDefault();

            Shoot("environments", 900, 600, () => new Views.EnvironmentWindow(vmForDialogs));
            Shoot("settings", 820, 700, () => new Views.SettingsWindow(vmForDialogs.Settings));
            if (collection != null)
                Shoot("runner", 1000, 640, () =>
                {
                    var runner = new Views.RunnerWindow(vmForDialogs, collection);
                    runner.SeedPreview();
                    return runner;
                });
            // The dialog sizes itself to its message, so it goes through the measure-first path
            // rather than being forced into a fixed window.
            RenderView("dialog", System.IO.Path.Combine(outputDir, "dialog.png"), false);

            void Shoot(string file, double width, double height, Func<Window> factory)
            {
                var window = factory();
                window.ShowInTaskbar = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = 0;
                window.Top = 0;
                window.Opacity = 0;
                window.ShowActivated = false;
                if (width > 0) window.Width = width;
                if (height > 0) window.Height = height;
                window.Show();
                Settle(1600);
                Capture(window, System.IO.Path.Combine(outputDir, file + ".png"));
                window.Close();
                Settle(120);
            }

            try { if (System.IO.Directory.Exists(sandbox)) System.IO.Directory.Delete(sandbox, true); } catch { }
            return 0;
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            Console.WriteLine("shots failed: " + ex);
            return 1;
        }
    }

    /// <summary>A canned response, so the screenshots show the app doing its job.</summary>
    private static void SeedResponse(ViewModels.MainViewModel vm)
    {
        var request = vm.Collections.FirstOrDefault()?.Flatten()
            .FirstOrDefault(n => n.Kind == Models.NodeKind.Request);
        if (request != null) vm.OpenNode(request);

        var tab = vm.SelectedTab;
        if (tab == null) return;

        // One tab only: a leftover blank "Untitled Request" is noise in a screenshot.
        foreach (var other in vm.Tabs.Where(t => t != tab).ToList()) vm.Tabs.Remove(other);
        vm.SelectedNode = request;

        const string body = """
            {
              "args": { "foo": "bar" },
              "headers": {
                "host": "postman-echo.com",
                "accept": "*/*",
                "user-agent": "GetMan/1.0"
              },
              "url": "https://postman-echo.com/get?foo=bar"
            }
            """;

        var response = new Models.ResponseModel
        {
            StatusCode = 200,
            StatusText = "OK",
            ElapsedMs = 214,
            SizeBytes = 486,
            HeaderBytes = 231,
            BodyBytes = 255,
            BodyText = body,
            RawBody = System.Text.Encoding.UTF8.GetBytes(body),
            ContentType = "application/json; charset=utf-8",
            HttpVersion = "HTTP/1.1",
            FinalUrl = "https://postman-echo.com/get?foo=bar",
            Timing = new Models.TimingInfo
            {
                DnsMs = 12, ConnectMs = 38, TlsMs = 74,
                RequestSentMs = 2, FirstByteMs = 81, DownloadMs = 7, TotalMs = 214
            }
        };
        response.Headers.Add(new KeyValuePair<string, string>("content-type", "application/json; charset=utf-8"));
        response.Headers.Add(new KeyValuePair<string, string>("content-length", "255"));
        response.Headers.Add(new KeyValuePair<string, string>("date", "Tue, 26 Aug 2025 09:14:02 GMT"));

        // Fed through the normal result path so the status line, timings and test tallies are
        // computed exactly the way a real send computes them.
        var result = new Services.ExecutionResult { Response = response };
        result.Tests.Add(new Models.TestResult { Name = "Status code is 200", Status = Models.TestStatus.Pass, DurationMs = 1 });
        result.Tests.Add(new Models.TestResult { Name = "Response is JSON", Status = Models.TestStatus.Pass, DurationMs = 1 });
        tab.ApplyResult(result);
    }

    /// <summary>
    /// Pumps the dispatcher for a moment of real time. Idle passes alone are not enough: the
    /// reveal animations need the clock to advance or they are captured half faded in.
    /// </summary>
    private static void Settle(int milliseconds = 500)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(milliseconds),
            DispatcherPriority.Background, (_, _) => frame.Continue = false, dispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();

        for (int i = 0; i < 4; i++)
            dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>
    /// Entrance animations never reach their last frame in a window parked off-screen, so the
    /// shot would catch controls half faded in. Clearing the animation and pinning the value
    /// puts them where they end up for a real user. Unselected tabs keep their dimming.
    /// </summary>
    private static void ForceOpaque(DependencyObject node)
    {
        if (node is TabItem { IsSelected: false }) return;

        // Only partial fades are pinned. Ripples and hover washes sit at exactly zero on
        // purpose, and forcing those to one paints white blobs over every toggle.
        if (node is UIElement element and not System.Windows.Shapes.Shape
            && element.IsEnabled && element.Opacity > 0.2 && element.Opacity < 1)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (element.Opacity < 1) element.SetCurrentValue(UIElement.OpacityProperty, 1.0);
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            ForceOpaque(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
    }

    private static void Capture(Window window, string outputPath)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        ForceOpaque(root);
        root.UpdateLayout();

        var width = (int)Math.Round(root.ActualWidth);
        var height = (int)Math.Round(root.ActualHeight);

        // The window's own Background is painted by the window chrome, not by its content, so
        // it has to be laid down first or anything the content leaves bare comes out transparent.
        var visual = new System.Windows.Media.DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (window.Background != null)
                context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new System.Windows.Media.VisualBrush(root) { Stretch = System.Windows.Media.Stretch.None,
                    AlignmentX = System.Windows.Media.AlignmentX.Left, AlignmentY = System.Windows.Media.AlignmentY.Top },
                null, new Rect(0, 0, width, height));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(outputPath);
        encoder.Save(stream);

        Console.WriteLine($"shot {outputPath} ({(int)root.ActualWidth}x{(int)root.ActualHeight})");
    }

    /// <summary>
    /// Builds every window once and reports failures. XAML problems only surface at run time,
    /// so this gives the build pipeline a way to catch a broken dialog before a user clicks it.
    /// </summary>
    private static int RunSelfCheck()
    {
        var failures = new List<string>();

        // Never touch the real workspace: the checks create and delete nodes.
        var sandbox = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GetMan.SelfCheck");
        try { if (System.IO.Directory.Exists(sandbox)) System.IO.Directory.Delete(sandbox, true); } catch { }
        Services.PersistenceService.RootDir = sandbox;

        var vm = new ViewModels.MainViewModel();
        var node = vm.Collections.FirstOrDefault();
        var env = vm.Environments.FirstOrDefault() ?? vm.Globals;

        void Build(string name, Func<Window> factory)
        {
            try
            {
                var window = factory();
                window.Measure(new Size(1400, 900));
                window.Arrange(new Rect(0, 0, 1400, 900));
                Console.WriteLine("  ok    " + name);
            }
            catch (Exception ex)
            {
                failures.Add(name + ": " + ex.Message);
                Console.WriteLine("  FAIL  " + name + " -> " + ex.Message);
                LogCrash(ex);
            }
        }

        Build("MainWindow", () => new MainWindow());
        Build("PostmanImportWindow", () => new Views.PostmanImportWindow(vm));
        Build("ImportWindow", () => new Views.ImportWindow(vm));
        Build("EnvironmentWindow", () => new Views.EnvironmentWindow(vm));
        Build("SettingsWindow", () => new Views.SettingsWindow(vm.Settings));
        Build("CookieWindow", () => new Views.CookieWindow(vm.Engine));
        Build("CodeWindow", () => new Views.CodeWindow(new Services.PreparedRequest { Method = "GET", Url = "https://example.com" }));
        Build("SaveRequestWindow", () => new Views.SaveRequestWindow(vm, "Sample"));
        Build("VariableEditorWindow", () => new Views.VariableEditorWindow("Variables", env.Variables, env));
        if (node != null)
        {
            Build("NodeSettingsWindow", () => new Views.NodeSettingsWindow(node));
            Build("RunnerWindow", () => new Views.RunnerWindow(vm, node));
        }

        CheckLanguages(failures);
        CheckInteractions(failures);

        try { if (System.IO.Directory.Exists(sandbox)) System.IO.Directory.Delete(sandbox, true); } catch { }

        Console.WriteLine(failures.Count == 0
            ? "self-check passed"
            : $"self-check failed: {failures.Count} check(s) broken");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Every language has to carry the whole table. A missing key falls back to English at run
    /// time, which is easy to ship without noticing, so the gap is reported here instead.
    /// </summary>
    private static void CheckLanguages(List<string> failures)
    {
        var english = Services.Loc.Keys("en");
        if (english.Count == 0)
        {
            failures.Add("languages: the English table is empty or not embedded");
            Console.WriteLine("  FAIL  languages -> en.json missing");
            return;
        }

        foreach (var language in Services.Loc.Languages)
        {
            var table = Services.Loc.Keys(language.Code);
            var missing = english.Except(table).OrderBy(k => k).ToList();
            var extra = table.Except(english).OrderBy(k => k).ToList();

            if (missing.Count == 0 && extra.Count == 0)
            {
                Console.WriteLine($"  ok    language {language.Code} ({table.Count} strings)");
                continue;
            }

            var detail = string.Join(", ",
                missing.Take(4).Select(k => "missing " + k).Concat(extra.Take(4).Select(k => "extra " + k)));
            failures.Add($"language {language.Code}: {missing.Count} missing, {extra.Count} extra ({detail})");
            Console.WriteLine($"  FAIL  language {language.Code} -> {missing.Count} missing, {extra.Count} extra");
        }

        // A switch has to actually change what a binding would read.
        var before = Services.Loc.T("s.send");
        Services.Loc.Instance.SetLanguage("ru");
        var after = Services.Loc.T("s.send");
        Services.Loc.Instance.SetLanguage("en");

        if (before == after)
        {
            failures.Add("languages: switching to ru did not change 's.send'");
            Console.WriteLine("  FAIL  language switch -> no change");
        }
        else
        {
            Console.WriteLine($"  ok    language switch ({before} -> {after})");
        }
    }

    /// <summary>
    /// Drives the flows a user actually performs. These are the checks that catch the class of
    /// bug where a control renders correctly but does not respond - the inline rename editor
    /// appearing without ever taking focus, for instance.
    /// </summary>
    private static void CheckInteractions(List<string> failures)
    {
        Window window = null;
        try
        {
            var main = new MainWindow();
            window = main;
            main.WindowStyle = WindowStyle.None;
            main.ShowInTaskbar = false;
            main.WindowStartupLocation = WindowStartupLocation.Manual;
            main.Left = -4000;
            main.Top = -4000;
            main.Show();
            Pump();

            var vm = (ViewModels.MainViewModel)main.DataContext;

            void Verify(string name, Func<bool> test, Func<string> detail = null)
            {
                try
                {
                    if (test())
                    {
                        Console.WriteLine("  ok    " + name);
                        return;
                    }
                    var extra = detail == null ? string.Empty : " -> " + detail();
                    failures.Add(name + extra);
                    Console.WriteLine("  FAIL  " + name + extra);
                }
                catch (Exception ex)
                {
                    failures.Add(name + ": " + ex.Message);
                    Console.WriteLine("  FAIL  " + name + " -> " + ex.Message);
                }
            }

            var collection = vm.Collections.FirstOrDefault();
            if (collection == null)
            {
                Console.WriteLine("  skip  interactions (empty workspace)");
                return;
            }

            // --- rename an existing request ------------------------------------------------
            var request = collection.Flatten().FirstOrDefault(n => n.Kind == Models.NodeKind.Request);
            Verify("rename editor takes focus", () =>
            {
                if (request == null) return false;
                vm.RenameNodeCommand.Execute(request);
                Pump();
                var editor = FindRenameEditor(main, request);
                var focused = editor != null && (editor.IsFocused ||
                    ReferenceEquals(FocusManager.GetFocusedElement(main), editor));
                request.IsRenaming = false;
                return focused;
            });

            // --- create a folder, then a request inside it ----------------------------------
            var folderCountBefore = collection.Children.Count;
            vm.NewFolderCommand.Execute(collection);
            Pump();
            var folder = collection.Children.LastOrDefault();
            Verify("new folder is created under the selected collection",
                () => collection.Children.Count == folderCountBefore + 1 &&
                      folder is { Kind: Models.NodeKind.Folder });

            Verify("new folder opens its rename editor", () =>
            {
                var editor = FindRenameEditor(main, folder);
                return editor is { IsVisible: true };
            });
            if (folder != null) folder.IsRenaming = false;
            Pump();

            var tabsBefore = vm.Tabs.Count;
            vm.NewRequestInCommand.Execute(folder);
            Pump();
            var created = folder?.Children.LastOrDefault();

            Verify("new request lands in the folder",
                () => created is { Kind: Models.NodeKind.Request });
            Verify("new request opens a tab",
                () => vm.Tabs.Count == tabsBefore + 1 && vm.SelectedTab?.Node == created);
            Verify("new request opens its rename editor", () =>
            {
                var editor = FindRenameEditor(main, created);
                return editor is { IsVisible: true };
            });
            if (created != null) created.IsRenaming = false;
            Pump();

            // --- search filters the tree ----------------------------------------------------
            Verify("search hides non-matching nodes", () =>
            {
                vm.SearchText = "zzz-no-such-request-zzz";
                Pump();
                var hidden = collection.Flatten().All(n => !n.IsVisible);
                vm.SearchText = string.Empty;
                Pump();
                var restored = collection.Flatten().All(n => n.IsVisible);
                return hidden && restored;
            });

            // --- drag and drop reparents ----------------------------------------------------
            Verify("moving a request reparents it", () =>
            {
                if (created == null) return false;
                vm.MoveNode(created, collection);
                return created.Parent == collection && collection.Children.Contains(created)
                       && !folder.Children.Contains(created);
            });

            // --- closing tabs ---------------------------------------------------------------
            Verify("closing a tab removes it", () =>
            {
                var tab = vm.SelectedTab;
                var before = vm.Tabs.Count;
                vm.CloseTabCommand.Execute(tab);
                Pump();
                return !vm.Tabs.Contains(tab) && (vm.Tabs.Count == before - 1 || vm.Tabs.Count == 1);
            });

            Verify("closing the last tab leaves a blank one", () =>
            {
                foreach (var tab in vm.Tabs.ToList()) vm.CloseTabCommand.Execute(tab);
                Pump();
                return vm.Tabs.Count >= 1 && vm.SelectedTab != null;
            });

            // --- reload with an active environment ------------------------------------------
            // Regression guard: a workspace that already has an environment selected used to
            // crash on start-up, because assigning it raised a save before the timer existed.
            Verify("view model loads a workspace with an active environment", () =>
            {
                var environment = vm.Environments.FirstOrDefault();
                if (environment == null) return true;

                vm.SelectedEnvironment = environment;
                vm.SaveWorkspace();

                var reloaded = new ViewModels.MainViewModel();
                return reloaded.SelectedEnvironment?.Id == environment.Id;
            });

            // --- theme round trip -----------------------------------------------------------
            Verify("theme toggles and comes back", () =>
            {
                var started = Controls.ThemeManager.Current;
                vm.ToggleThemeCommand.Execute(null);
                Pump();
                var switched = Controls.ThemeManager.Current != started;
                vm.ToggleThemeCommand.Execute(null);
                Pump();
                return switched && Controls.ThemeManager.Current == started;
            });

            // --- tidy up --------------------------------------------------------------------
            if (created != null) collection.Children.Remove(created);
            if (folder != null) collection.Children.Remove(folder);
        }
        catch (Exception ex)
        {
            failures.Add("interactions: " + ex.Message);
            Console.WriteLine("  FAIL  interactions -> " + ex.Message);
            LogCrash(ex);
        }
        finally
        {
            try { window?.Close(); } catch { }
        }
    }

    private static void Pump()
    {
        for (int i = 0; i < 5; i++)
            Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static TextBox FindRenameEditor(DependencyObject root, object dataContext)
    {
        if (root is TextBox box && ReferenceEquals(box.DataContext, dataContext) && box.IsVisible)
            return box;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindRenameEditor(System.Windows.Media.VisualTreeHelper.GetChild(root, i), dataContext);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Takes a window's content out of it so it can be hosted somewhere else.</summary>
    private static FrameworkElement DetachContent(Window window)
    {
        var content = window.Content as FrameworkElement;
        window.Content = null;
        return content;
    }

    /// <summary>Renders one view to a PNG so layout and spacing can be reviewed offscreen.</summary>
    private static int RenderView(string name, string outputPath, bool light)
    {
        try
        {
            if (light) Controls.ThemeManager.Apply(Controls.AppTheme.Light);

            FrameworkElement view = name.ToLowerInvariant() switch
            {
                "auth" => new Views.AuthView
                {
                    DataContext = new Models.AuthConfig
                    {
                        Type = Models.AuthType.OAuth2,
                        OauthGrantType = "authorization_code",
                        OauthAccessTokenUrl = "https://id.example.com/oauth/token",
                        OauthAuthUrl = "https://id.example.com/oauth/authorize",
                        OauthClientId = "getman-desktop",
                        OauthClientSecret = "{{clientSecret}}",
                        OauthScope = "read write",
                        OauthAudience = "https://api.example.com"
                    }
                },
                "dialog" => DetachContent(Views.MessageDialog.Build(
                    Services.Loc.T("s.dlg_delete_collection_title"),
                    Services.Loc.T("s.dlg_delete_node_body", "Billing API"),
                    Views.DialogKind.Question, "Delete", "Cancel")),
                _ => null
            };

            if (view == null)
            {
                Console.WriteLine("unknown view: " + name);
                return 1;
            }

            var fitToContent = name.Equals("dialog", StringComparison.OrdinalIgnoreCase);
            var host = new Border
            {
                Background = (System.Windows.Media.Brush)Current.TryFindResource(fitToContent ? "Bg0" : "Bg0"),
                Width = fitToContent ? 500 : 980,
                Child = view
            };

            if (fitToContent)
            {
                // Height follows the message, exactly as the real dialog does.
                host.Measure(new Size(host.Width, double.PositiveInfinity));
                host.Height = Math.Ceiling(host.DesiredSize.Height);
            }
            else
            {
                host.Height = 620;
            }

            // Shown off-screen rather than measured in isolation: Material's floating hints and
            // other visual states only settle once the element is in a live window.
            var window = new Window
            {
                Content = host,
                Width = host.Width + 20,
                Height = host.Height + 20,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000
            };
            window.Show();

            for (int i = 0; i < 3; i++)
                Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            host.UpdateLayout();

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)host.Width, (int)host.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(host);
            window.Close();

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(outputPath);
            encoder.Save(stream);

            Console.WriteLine($"rendered {name} -> {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            Console.WriteLine("render failed: " + ex.Message);
            return 1;
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        try
        {
            Views.MessageDialog.Error(e.Exception.Message, Services.Loc.T("s.dlg_error_title"));
        }
        catch
        {
            // If the theme itself is what failed, fall back to the OS dialog rather than
            // swallowing the error silently.
            MessageBox.Show(e.Exception.Message, "GetMan hit an error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    /// <summary>Diagnostic hook: writes WPF data-binding failures to bindings.log.</summary>
    private static void EnableBindingTrace()
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GetMan");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "bindings.log");
            System.IO.File.WriteAllText(path, string.Empty);

            var listener = new System.Diagnostics.TextWriterTraceListener(path)
            {
                TraceOutputOptions = System.Diagnostics.TraceOptions.None
            };
            System.Diagnostics.PresentationTraceSources.Refresh();
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
            System.Diagnostics.PresentationTraceSources.ResourceDictionarySource.Listeners.Add(listener);
            System.Diagnostics.PresentationTraceSources.ResourceDictionarySource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
            System.Diagnostics.Trace.AutoFlush = true;
        }
        catch { }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GetMan");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "errors.log"),
                $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch { }
    }
}
