﻿using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NetSparkleUpdater.Enums;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class UpdaterDialog : Window {
        public readonly UpdaterViewModel ViewModel;
        public UpdaterDialog() {
            InitializeComponent();
            DataContext = ViewModel = new UpdaterViewModel();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent() {
            AvaloniaXamlLoader.Load(this);
        }

        void OnClosing(object sender, CancelEventArgs e) {
            ViewModel.OnClosing();
        }

        public static void CheckForUpdate(Action<Window> showDialog, Action closeApplication, TaskScheduler scheduler) {
            Task.Run(async () => {
                using (var updater = UpdaterViewModel.NewUpdater()) {
                    var info = await updater.CheckForUpdatesQuietly(true);
                    if (info.Status == UpdateStatus.UpdateAvailable) {
                        if (info.Updates[0].Version.ToString() == Preferences.Default.SkipUpdate) {
                            return false;
                        }
                        return true;
                    }
                }
                return false;
            }).ContinueWith(t => {
                if (t.IsCompletedSuccessfully && t.Result) {
                    var dialog = new UpdaterDialog();
                    dialog.ViewModel.CloseApplication = closeApplication;
                    showDialog.Invoke(dialog);
                }
                if (t.IsFaulted) {
                    Log.Error(t.Exception, "Failed to check for update");
                }
            }, scheduler).ContinueWith((t2, _) => {
                if (t2.IsFaulted) {
                    Log.Error(t2.Exception, "Failed to check for update");
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
