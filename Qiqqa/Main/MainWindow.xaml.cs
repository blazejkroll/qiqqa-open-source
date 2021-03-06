﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using icons;
using Qiqqa.Common;
using Qiqqa.Common.BackgroundWorkerDaemonStuff;
using Qiqqa.Common.Configuration;
using Qiqqa.Common.GUI;
using Qiqqa.DocumentLibrary.WebLibraryStuff;
using Qiqqa.Main.IPC;
using Qiqqa.Main.LogoutStuff;
using Qiqqa.StartPage;
using Qiqqa.UtilisationTracking;
using Utilities;
using Utilities.GUI;
using Utilities.Maintainable;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Forms.KeyEventArgs;

namespace Qiqqa.Main
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : StandardWindow, IDisposable
    {
        internal StartPageControl ObjStartPage = new StartPageControl();

        public static readonly string TITLE_START_PAGE = "Home (F1)";
        private KeyboardHook keyboard_hook;
        private IPCServer ipc_server;

        public MainWindow()
        {
            MainWindowServiceDispatcher.Instance.MainWindow = this;

            Theme.Initialize();

            InitializeComponent();

            WPFDoEvents.SetHourglassCursor();

            DataContext = ConfigurationManager.Instance.ConfigurationRecord_Bindable;

            // Set a DEV title if necessary
            Title = String.Format("Qiqqa v{0}", ClientVersion.CurrentBuild);
            if (WebsiteAccess.IsTestEnvironment)
            {
                Title = String.Format("Qiqqa v{0} (TEST ENVIRONMENT)", ClientVersion.CurrentBuild);
            }

            // Check that we actually are fitting on the user's screen
            if (Left > SystemParameters.VirtualScreenWidth || Width > SystemParameters.FullPrimaryScreenWidth)
            {
                Left = 0;
                Width = SystemParameters.FullPrimaryScreenWidth;
            }
            if (Top > SystemParameters.VirtualScreenHeight || Height > SystemParameters.FullPrimaryScreenHeight)
            {
                Top = 0;
                Height = SystemParameters.FullPrimaryScreenHeight;
            }

            DockingManager.WindowIcon = Icons.GetAppIconICO(Icons.Qiqqa);
            DockingManager.OwnerWindow = this;

            DockingManager.AddContent(TITLE_START_PAGE, TITLE_START_PAGE, Icons.GetAppIcon(Icons.ModuleStartPage), false, true, ObjStartPage);
            DockingManager.MakeActive(TITLE_START_PAGE);

            ObjStatusBar.Background = ThemeColours.Background_Brush_Blue_LightToDark;
            ObjTabBackground.Background = ThemeColours.Background_Brush_Blue_VeryDark;

            SizeChanged += MainWindow_SizeChanged;
            KeyUp += MainWindow_KeyUp;

            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;

            // We've looked for the LAST event that triggers dependably at the start of the application:
            //   ContentRendered
            // is the last one triggered of this bunch:
            //
            //this.Activated += MainWindow_Activated;
            ContentRendered += MainWindow_ContentRendered;
            //this.Initialized += MainWindow_Initialized;
            //this.LayoutUpdated += MainWindow_LayoutUpdated;
            //this.Loaded += MainWindow_Loaded;
            //this.ManipulationCompleted += MainWindow_ManipulationCompleted;
            //this.ManipulationStarting += MainWindow_ManipulationStarting;
            //this.SourceInitialized += MainWindow_SourceInitialized;
            //this.StateChanged += MainWindow_StateChanged;

            ObjTabWelcome.GetGoing += ObjTabWelcome_GetGoing;

            // Put this in a background thread
            Dispatcher.BeginInvoke(((Action)(() => PostStartupWork())), DispatcherPriority.Normal);
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            Logging.Debug特("MainWindow::ContentRendered event");

            // hold off: level 2 -> 1
            MaintainableManager.Instance.BumpHoldOffPendingLevel();

            WPFDoEvents.ResetHourglassCursor();
        }

        private void keyboard_hook_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keys.Z == e.KeyCode && KeyboardTools.IsWinDown())
            {
                Logging.Info("Qiqqa is being activated by WIN-Z");
                MainWindowServiceDispatcher.Instance.OpenPopupInCite();
                Activate();
                e.Handled = true;
            }
        }

        private void PostStartupWork()
        {
            if (ConfigurationManager.Instance.ConfigurationRecord.GUI_RestoreWindowsAtStartup)
            {
                RestoreDesktopManager.RestoreDesktop();
            }
            else
            {
                // Open the most recently accessed web library
                List<WebLibraryDetail> web_libary_details = WebLibraryManager.Instance.WebLibraryDetails_WorkingWebLibrariesWithoutGuest;
                WebLibraryManager.Instance.SortWebLibraryDetailsByLastAccessed(web_libary_details);
                if (0 < web_libary_details.Count)
                {
                    MainWindowServiceDispatcher.Instance.OpenLibrary(web_libary_details[0].library);
                }

                // Also open guest under some circumstances
                bool should_open_guest = false;

                // No web libraries
                if (0 == web_libary_details.Count)
                {
                    should_open_guest = true;
                }
                // Web library is small compared to guest library
                if (0 < web_libary_details.Count && WebLibraryManager.Instance.Library_Guest.PDFDocuments_IncludingDeleted_Count > 2 * web_libary_details[0].library.PDFDocuments_IncludingDeleted_Count)
                {
                    should_open_guest = true;
                }

                if (should_open_guest)
                {
                    MainWindowServiceDispatcher.Instance.OpenLibrary(WebLibraryManager.Instance.Library_Guest);
                }

                // Make sure the start page is selected
                MainWindowServiceDispatcher.Instance.OpenStartPage();
            }

            if (ConfigurationManager.Instance.ConfigurationRecord.GUI_RestoreLocationAtStartup)
            {
                SetupConfiguredDimensions();
            }
            else
            {
                if (!RegistrySettings.Instance.IsSet(RegistrySettings.StartNotMaximized))
                {
                    WindowState = WindowState.Maximized;
                }
            }

            // Install the global keyboard and mouse hooks
            if (!RegistrySettings.Instance.IsSet(RegistrySettings.DisableGlobalKeyHook))
            {
                keyboard_hook = new KeyboardHook();
                keyboard_hook.KeyDown += keyboard_hook_KeyDown;
                keyboard_hook.Start();
            }
            else
            {
                Logging.Warn("DisableGlobalKeyHook is set!");
            }

            if (true)
            {
                // Start listening for other apps
                ipc_server = new IPCServer();
                ipc_server.IPCServerMessage += ipc_server_IPCServerMessage;

                ipc_server.Start();
            }

            if (ConfigurationManager.Instance.ConfigurationRecord.TermsAndConditionsAccepted)
            {
                StartBackgroundWorkerDaemon();
            }
            else
            {
                // TODO: nothing to do any more; user hasn't accepted the Terms & Conditions... Allow update via UI maybe? Nah, just quit!
                WPFDoEvents.ResetHourglassCursor();

                MessageBoxes.Info("You have not accepted the Qiqqa Terms and Conditions. Consequently, no service can be provided. The application will terminate now.\n\nYou may want to re-install the software and check&accept the Terms & Conditions, which are shown during the installation procedure.");

                MainWindowServiceDispatcher.Instance.ShutdownQiqqa(suppress_exit_warning: true);
            }
        }

        private void ipc_server_IPCServerMessage(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MainWindowServiceDispatcher.Instance.ProcessCommandLineFile(message);
            }
            ));
        }

        private static bool already_exiting = false;
        public bool suppress_exit_warning = false;

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!already_exiting && !suppress_exit_warning)
            {
                if (ConfigurationManager.Instance.ConfigurationRecord.GUI_AskOnExit)
                {
                    LogoutWindow logout_window = new LogoutWindow();
                    if (false == logout_window.ShowDialog())
                    {
                        e.Cancel = true;
                        Logging.Info("User has requested not to quit Qiqqa on window close");
                        return;
                    }
                }
            }

            if (ConfigurationManager.Instance.ConfigurationRecord.GUI_RestoreWindowsAtStartup)
            {
                RestoreDesktopManager.SaveDesktop();
            }

            // Close all windows
            DockingManager.CloseAllContent();

            MainEntry.SignalShutdown();

            // If we get this far, they want out
            already_exiting = true;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Logging.Info("+Explicitly shutting down application");

            MainEntry.SignalShutdown();

            ipc_server?.Stop();
            ipc_server = null;

            FeatureTrackingManager.Instance.UseFeature(Features.App_Close);

            Application.Current.Shutdown();

            Logging.Info("-Explicitly shutting down application");
        }

        private void MainWindow_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!KeyboardTools.IsCTRLDown())
            {
                switch (e.Key)
                {
                    case Key.F1:
                        if (KeyboardTools.IsShiftDown())
                        {
                            MainWindowServiceDispatcher.Instance.OpenControlPanel();
                        }
                        else
                        {
                            MainWindowServiceDispatcher.Instance.OpenStartPage();
                        }
                        e.Handled = true;
                        break;

                    default:
                        break;
                }
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //Logging.Info("Size is now {0}", e.NewSize.ToString());
        }

        private void StartBackgroundWorkerDaemon()
        {
            BackgroundWorkerDaemon d = BackgroundWorkerDaemon.Instance;
        }

        private void ObjTabWelcome_GetGoing()
        {
            StartBackgroundWorkerDaemon();

            // Start the Wizard if necessary
            if (!ConfigurationManager.Instance.ConfigurationRecord.Wizard_HasSeenIntroWizard)
            {
                ConfigurationManager.Instance.ConfigurationRecord.Wizard_HasSeenIntroWizard = true;
                ConfigurationManager.Instance.ConfigurationRecord_Bindable.NotifyPropertyChanged(() => ConfigurationManager.Instance.ConfigurationRecord.Wizard_HasSeenIntroWizard);
                MainWindowServiceDispatcher.Instance.OpenWelcomeWizard();
            }
        }

        #region --- IDisposable ------------------------------------------------------------------------

        ~MainWindow()
        {
            Logging.Debug("~MainWindow()");
            Dispose(false);
        }

        public void Dispose()
        {
            Logging.Debug("Disposing MainWindow");
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private int dispose_count = 0;
        protected virtual void Dispose(bool disposing)
        {
            Logging.Debug("MainWindow::Dispose({0}) @{1}", disposing, dispose_count);

            try
            {
                if (dispose_count == 0)
                {
                    WPFDoEvents.InvokeInUIThread(() =>
                    {
                        // Get rid of managed resources
                        ObjTabWelcome.GetGoing -= ObjTabWelcome_GetGoing;

                        ObjStartPage?.Dispose();

                        ipc_server?.Stop();
                    }, Dispatcher);
                }

                ObjStartPage = null;

                keyboard_hook = null;
                ipc_server = null;
                DataContext = null;
            }
            catch (Exception ex)
            {
                Logging.Error(ex);
            }

            ++dispose_count;
        }

        #endregion

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Dispose() does a few things that are also done in MainWindow_Closed(), which is invoked via base.OnClosed(), 
            // so we flipped the order of exec to reduce the number of surprises for yours truly.
            Dispose();
        }
    }
}
