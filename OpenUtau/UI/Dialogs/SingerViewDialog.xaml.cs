﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.UI.Dialogs {
    /// <summary>
    /// Interaction logic for SingerViewDialog.xaml
    /// </summary>
    public partial class SingerViewDialog : Window, ICmdSubscriber {
        List<string> singerNames;
        string location;

        public SingerViewDialog() {
            InitializeComponent();
            UpdateSingers();
            DocManager.Inst.AddSubscriber(this);
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            DocManager.Inst.RemoveSubscriber(this);
        }

        private void UpdateSingers() {
            singerNames = new List<string>();
            foreach (var pair in DocManager.Inst.Singers) {
                singerNames.Add(pair.Value.Name);
            }
            if (singerNames.Count > 0) {
                name.SelectedIndex = 0;
                SetSinger(singerNames[0]);
            }
            name.ItemsSource = singerNames;
        }

        public void SetSinger(string singerName) {
            USinger singer = null;
            foreach (var pair in DocManager.Inst.Singers)
                if (pair.Value.Name == singerName) {
                    singer = pair.Value;
                }
            if (singer == null) {
                location = null;
                return;
            }
            avatar.Source = singer.Avatar;
            info.Inlines.Clear();
            info.Inlines.Add($"Author: {singer.Author}\n");
            info.Inlines.Add("Web: ");
            if (!string.IsNullOrWhiteSpace(singer.Web)) {
                var h = new Hyperlink() {
                    NavigateUri = new Uri(singer.Web),
                };
                h.RequestNavigate += new RequestNavigateEventHandler(hyperlink_RequestNavigate);
                h.Inlines.Add(singer.Web);
                info.Inlines.Add(h);
            }
            info.Inlines.Add("\n");
            info.Inlines.Add(singer.OtherInfo);
            location = singer.Location;
            otoview.Items.Clear();
            foreach (var set in singer.OtoSets) {
                foreach (var oto in set.Otos.Values) {
                    otoview.Items.Add(oto);
                }
            }
        }

        private void name_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            SetSinger(singerNames[name.SelectedIndex]);
        }

        private void locationButton_Click(object sender, RoutedEventArgs e) {
            OpenFolder(location);
        }

        private void hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void OpenFolder(string folderPath) {
            if (Directory.Exists(folderPath)) {
                Process.Start(new ProcessStartInfo {
                    Arguments = folderPath,
                    FileName = "explorer.exe",
                });
            }
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is SingersChangedNotification) {
                UpdateSingers();
            }
        }

        #endregion
    }
}
