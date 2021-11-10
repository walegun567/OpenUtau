﻿using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    interface IValueTip {
        void ShowValueTip();
        void HideValueTip();
        void UpdateValueTip(string text);
    }

    public partial class PianoRollWindow : Window, IValueTip {
        public MainWindow? MainWindow { get; set; }
        public readonly PianoRollViewModel ViewModel;

        private readonly KeyModifiers cmdKey =
            OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        private KeyboardPlayState? keyboardPlayState;
        private NoteEditState? editState;
        private Rectangle? selectionBox;
        private Canvas? valueTipCanvas;
        private Border? valueTip;
        private TextBlock? valueTipText;
        private Point valueTipPointerPosition;
        private LyricBox? lyricBox;
        private ContextMenu? pitchContextMenu;
        private bool openingPitchContextMenu;
        private ExpSelector? expSelector1;
        private ExpSelector? expSelector2;
        private ExpSelector? expSelector3;
        private ExpSelector? expSelector4;
        private ExpSelector? expSelector5;

        public PianoRollWindow() {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            DataContext = ViewModel = new PianoRollViewModel();
        }

        private void InitializeComponent() {
            AvaloniaXamlLoader.Load(this);
            valueTipCanvas = this.FindControl<Canvas>("ValueTipCanvas");
            valueTip = this.FindControl<Border>("ValueTip");
            valueTip.IsVisible = false;
            valueTipText = this.FindControl<TextBlock>("ValueTipText");
            lyricBox = this.FindControl<LyricBox>("LyricBox");
            pitchContextMenu = this.FindControl<ContextMenu>("PitchContextMenu");
            expSelector1 = this.FindControl<ExpSelector>("expSelector1");
            expSelector2 = this.FindControl<ExpSelector>("expSelector2");
            expSelector3 = this.FindControl<ExpSelector>("expSelector3");
            expSelector4 = this.FindControl<ExpSelector>("expSelector4");
            expSelector5 = this.FindControl<ExpSelector>("expSelector5");
        }

        public void WindowDeactivated(object sender, EventArgs args) {
            lyricBox?.EndEdit();
        }

        void WindowClosing(object? sender, CancelEventArgs e) {
            Hide();
            e.Cancel = true;
        }

        void OnMenuRenamePart(object? sender, RoutedEventArgs e) {
            var part = ViewModel.NotesViewModel.Part;
            if (part == null) {
                return;
            }
            var dialog = new TypeInDialog();
            dialog.Title = "Rename";
            dialog.SetText(part.name);
            dialog.onFinish = name => {
                if (!string.IsNullOrWhiteSpace(name) && name != part.name) {
                    ViewModel.RenamePart(part, name);
                }
            };
            dialog.ShowDialog(this);
        }

        void OnMenuEditLyrics(object? sender, RoutedEventArgs e) {
            if (ViewModel.NotesViewModel.SelectedNotes.Count == 0) {
                _ = MessageBox.Show(
                    this,
                    ThemeManager.GetString("lyrics.selectnotes"),
                    ThemeManager.GetString("lyrics.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            var dialog = new LyricsDialog() {
                DataContext = ViewModel.NotesViewModel.PrepareInsertLyrics(),
            };
            dialog.ShowDialog(this);
        }

        public void OnExpButtonClick(object sender, RoutedEventArgs args) {
            var dialog = new ExpressionsDialog() {
                DataContext = new ExpressionsViewModel(),
            };
            dialog.ShowDialog(this);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }

        public void KeyboardPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            lyricBox?.EndEdit();
            var scrollbar = this.FindControl<ScrollBar>("VScrollBar");
            VScrollPointerWheelChanged(scrollbar, args);
        }

        public void KeyboardPointerPressed(object sender, PointerPressedEventArgs args) {
            lyricBox?.EndEdit();
            if (keyboardPlayState != null) {
                return;
            }
            var element = (TrackBackground)sender;
            keyboardPlayState = new KeyboardPlayState(element, ViewModel);
            keyboardPlayState.Begin(args.Pointer, args.GetPosition(element));
        }

        public void KeyboardPointerMoved(object sender, PointerEventArgs args) {
            if (keyboardPlayState != null) {
                var element = (TrackBackground)sender;
                keyboardPlayState.Update(args.Pointer, args.GetPosition(element));
            }
        }

        public void KeyboardPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (keyboardPlayState != null) {
                var element = (TrackBackground)sender;
                keyboardPlayState.End(args.Pointer, args.GetPosition(element));
                keyboardPlayState = null;
            }
        }

        public void HScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var scrollbar = (ScrollBar)sender;
            scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            lyricBox?.EndEdit();
        }

        public void VScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var scrollbar = (ScrollBar)sender;
            scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            lyricBox?.EndEdit();
        }

        public void TimelinePointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var canvas = (Canvas)sender;
            var position = args.GetCurrentPoint((IVisual)sender).Position;
            var size = canvas.Bounds.Size;
            position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
            ViewModel.NotesViewModel.OnXZoomed(position, 0.1 * args.Delta.Y);
            lyricBox?.EndEdit();
        }

        public void ViewScalerPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            ViewModel.NotesViewModel.OnYZoomed(new Point(0, 0.5), 0.1 * args.Delta.Y);
            lyricBox?.EndEdit();
        }

        public void TimelinePointerPressed(object sender, PointerPressedEventArgs args) {
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            if (point.Properties.IsLeftButtonPressed) {
                args.Pointer.Capture(canvas);
                int tick = ViewModel.NotesViewModel.PointToSnappedTick(point.Position)
                    + ViewModel.NotesViewModel.Part?.position ?? 0;
                ViewModel.PlaybackViewModel?.MovePlayPos(tick);
            }
            lyricBox?.EndEdit();
        }

        public void TimelinePointerMoved(object sender, PointerEventArgs args) {
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            if (point.Properties.IsLeftButtonPressed) {
                int tick = ViewModel.NotesViewModel.PointToSnappedTick(point.Position)
                    + ViewModel.NotesViewModel.Part?.position ?? 0;
                ViewModel.PlaybackViewModel?.MovePlayPos(tick);
            }
        }

        public void TimelinePointerReleased(object sender, PointerReleasedEventArgs args) {
            args.Pointer.Capture(null);
        }

        public void NotesCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            lyricBox?.EndEdit();
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                NotesCanvasLeftPointerPressed(canvas, point, args);
            } else if (point.Properties.IsRightButtonPressed) {
                NotesCanvasRightPointerPressed(canvas, point, args);
            } else if (point.Properties.IsMiddleButtonPressed) {
                editState = new NotePanningState(canvas, ViewModel, this);
                Cursor = ViewConstants.cursorHand;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        private void NotesCanvasLeftPointerPressed(Canvas canvas, PointerPoint point, PointerPressedEventArgs args) {
            if (ViewModel.NotesViewModel.EraserTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteEraseEditState(canvas, ViewModel, this, MouseButton.Left);
                Cursor = ViewConstants.cursorNo;
                return;
            }
            var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
            if (pitHitInfo.Note != null) {
                editState = new PitchPointEditState(canvas, ViewModel, this,
                    pitHitInfo.Note, pitHitInfo.Index, pitHitInfo.OnPoint, pitHitInfo.X, pitHitInfo.Y);
                return;
            }
            var vbrHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
            if (vbrHitInfo.hit) {
                if (vbrHitInfo.hitToggle) {
                    ViewModel.NotesViewModel.ToggleVibrato(vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitStart) {
                    editState = new VibratoChangeStartState(canvas, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitIn) {
                    editState = new VibratoChangeInState(canvas, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitOut) {
                    editState = new VibratoChangeOutState(canvas, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitDepth) {
                    editState = new VibratoChangeDepthState(canvas, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitPeriod) {
                    editState = new VibratoChangePeriodState(canvas, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitShift) {
                    editState = new VibratoChangeShiftState(
                        canvas, ViewModel, this, vbrHitInfo.note, vbrHitInfo.point, vbrHitInfo.initialShift);
                    return;
                }
                return;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
            if (noteHitInfo.hitBody) {
                if (noteHitInfo.hitResizeArea) {
                    editState = new NoteResizeEditState(
                        canvas, ViewModel, this, noteHitInfo.note,
                        args.KeyModifiers == KeyModifiers.Alt);
                    Cursor = ViewConstants.cursorSizeWE;
                } else {
                    editState = new NoteMoveEditState(canvas, ViewModel, this, noteHitInfo.note);
                    Cursor = ViewConstants.cursorSizeAll;
                }
                return;
            }
            if (ViewModel.NotesViewModel.CursorTool) {
                if (args.KeyModifiers == KeyModifiers.None) {
                    // New selection.
                    ViewModel.NotesViewModel.DeselectNotes();
                    editState = new NoteSelectionEditState(canvas, ViewModel, this, GetSelectionBox(canvas));
                    Cursor = ViewConstants.cursorCross;
                    return;
                }
                if (args.KeyModifiers == cmdKey) {
                    // Additional selection.
                    editState = new NoteSelectionEditState(canvas, ViewModel, this, GetSelectionBox(canvas));
                    Cursor = ViewConstants.cursorCross;
                    return;
                }
                ViewModel.NotesViewModel.DeselectNotes();
            } else if (ViewModel.NotesViewModel.PencilTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteDrawEditState(canvas, ViewModel, this, ViewModel.NotesViewModel.PlayTone);
            }
        }

        private void NotesCanvasRightPointerPressed(Canvas canvas, PointerPoint point, PointerPressedEventArgs args) {
            Serilog.Log.Information("NotesCanvasRightPointerPressed");
            ViewModel.NotesViewModel.DeselectNotes();
            if (ViewModel.NotesViewModel.ShowPitch) {
                var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
                if (pitHitInfo.Note != null && pitchContextMenu != null) {
                    pitHitInfo.IsFirst = pitHitInfo.OnPoint && pitHitInfo.Index == 0;
                    pitHitInfo.CanDel = pitHitInfo.OnPoint && pitHitInfo.Index != 0
                        && pitHitInfo.Index != pitHitInfo.Note.pitch.data.Count - 1;
                    pitHitInfo.CanAdd = !pitHitInfo.OnPoint;
                    pitHitInfo.EaseInOutCommand = ViewModel.PitEaseInOutCommand;
                    pitHitInfo.LinearCommand = ViewModel.PitLinearCommand;
                    pitHitInfo.EaseInCommand = ViewModel.PitEaseInCommand;
                    pitHitInfo.EaseOutCommand = ViewModel.PitEaseOutCommand;
                    pitHitInfo.SnapCommand = ViewModel.PitSnapCommand;
                    pitHitInfo.AddCommand = ViewModel.PitAddCommand;
                    pitHitInfo.DelCommand = ViewModel.PitDelCommand;
                    pitchContextMenu.DataContext = pitHitInfo;
                    openingPitchContextMenu = true;
                    return;
                }
            }
            if (ViewModel.NotesViewModel.PencilTool || ViewModel.NotesViewModel.EraserTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteEraseEditState(canvas, ViewModel, this, MouseButton.Right);
                Cursor = ViewConstants.cursorNo;
            }
        }

        private Rectangle GetSelectionBox(Canvas canvas) {
            if (selectionBox != null) {
                return selectionBox;
            }
            selectionBox = new Rectangle() {
                Stroke = ThemeManager.ForegroundBrush,
                StrokeThickness = 2,
                Fill = ThemeManager.TickLineBrushLow,
                // radius = 8
                IsHitTestVisible = false,
            };
            canvas.Children.Add(selectionBox);
            selectionBox.ZIndex = 1000;
            return selectionBox;
        }

        public void NotesCanvasPointerMoved(object sender, PointerEventArgs args) {
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            if (valueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(valueTipCanvas!).Position;
            }
            if (editState != null) {
                editState.Update(point.Pointer, point.Position);
                return;
            }
            if (ViewModel?.NotesViewModel?.HitTest == null) {
                return;
            }
            var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
            if (pitHitInfo.Note != null) {
                Cursor = ViewConstants.cursorHand;
                return;
            }
            var vbrHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
            if (vbrHitInfo.hit) {
                if (vbrHitInfo.hitDepth) {
                    Cursor = ViewConstants.cursorSizeNS;
                } else if (vbrHitInfo.hitPeriod) {
                    Cursor = ViewConstants.cursorSizeWE;
                } else {
                    Cursor = ViewConstants.cursorHand;
                }
                return;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
            if (noteHitInfo.hitResizeArea) {
                Cursor = ViewConstants.cursorSizeWE;
                return;
            }
            Cursor = null;
        }

        public void NotesCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            editState.Update(point.Pointer, point.Position);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        public void NotesCanvasDoubleTapped(object sender, RoutedEventArgs args) {
            if (!(sender is Canvas canvas)) {
                return;
            }
            var e = (TappedEventArgs)args;
            var point = e.GetPosition(canvas);
            if (editState != null) {
                editState.End(e.Pointer, point);
                editState = null;
                Cursor = null;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point);
            if (noteHitInfo.hitBody && ViewModel?.NotesViewModel?.Part != null) {
                var note = noteHitInfo.note;
                lyricBox?.Show(ViewModel.NotesViewModel.Part, note, note.lyric);
            }
        }

        public void NotesCanvasPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            lyricBox?.EndEdit();
            var canvas = (Canvas)sender;
            var position = args.GetCurrentPoint(canvas).Position;
            var size = canvas.Bounds.Size;
            var delta = args.Delta;
            if (args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift) {
                if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) {
                    delta = new Vector(delta.Y, delta.X);
                }
                if (delta.X != 0) {
                    var scrollbar = this.FindControl<ScrollBar>("HScrollBar");
                    scrollbar.Value = Math.Max(scrollbar.Minimum,
                        Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * delta.X));
                }
                if (delta.Y != 0) {
                    var scrollbar = this.FindControl<ScrollBar>("VScrollBar");
                    scrollbar.Value = Math.Max(scrollbar.Minimum,
                        Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * delta.Y));
                }
            } else if (args.KeyModifiers == KeyModifiers.Alt) {
                position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
                ViewModel.NotesViewModel.OnYZoomed(position, 0.1 * args.Delta.Y);
            } else if (args.KeyModifiers == cmdKey) {
                var timelineCanvas = this.FindControl<Canvas>("TimelineCanvas");
                TimelinePointerWheelChanged(timelineCanvas, args);
            }
        }

        public void PitchContextMenuOpening(object sender, CancelEventArgs args) {
            if (openingPitchContextMenu) {
                openingPitchContextMenu = false;
            } else {
                args.Cancel = true;
            }
        }

        public void ExpCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            lyricBox?.EndEdit();
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                editState = new ExpSetValueState(canvas, ViewModel, this);
            } else if (point.Properties.IsRightButtonPressed) {
                editState = new ExpResetValueState(canvas, ViewModel, this);
                Cursor = ViewConstants.cursorNo;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void ExpCanvasPointerMoved(object sender, PointerEventArgs args) {
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            if (valueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(valueTipCanvas!).Position;
            }
            if (editState != null) {
                editState.Update(point.Pointer, point.Position);
            } else {
                Cursor = null;
            }
        }

        public void ExpCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            editState.Update(point.Pointer, point.Position);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        public void PhonemeCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            lyricBox?.EndEdit();
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestPhoneme(point.Position);
                if (hitInfo.hit) {
                    var phoneme = hitInfo.phoneme;
                    var note = phoneme.Parent;
                    var index = note.PhonemeOffset + note.phonemes.IndexOf(phoneme);
                    if (hitInfo.hitPosition) {
                        editState = new PhonemeMoveState(
                            canvas, ViewModel, this, note.Extends ?? note, index);
                    } else if (hitInfo.hitPreutter) {
                        editState = new PhonemeChangePreutterState(
                            canvas, ViewModel, this, note.Extends ?? note, phoneme, index);
                    } else if (hitInfo.hitOverlap) {
                        editState = new PhonemeChangeOverlapState(
                            canvas, ViewModel, this, note.Extends ?? note, phoneme, index);
                    }
                }
            } else if (point.Properties.IsRightButtonPressed) {
                editState = new PhonemeResetState(canvas, ViewModel, this);
                Cursor = ViewConstants.cursorNo;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void PhonemeCanvasPointerMoved(object sender, PointerEventArgs args) {
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            if (valueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(valueTipCanvas!).Position;
            }
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            if (editState != null) {
                editState.Update(point.Pointer, point.Position);
                return;
            }
            var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestPhoneme(point.Position);
            if (hitInfo.hit) {
                Cursor = ViewConstants.cursorSizeWE;
            } else {
                Cursor = null;
            }
        }

        public void PhonemeCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var canvas = (Canvas)sender;
            var point = args.GetCurrentPoint(canvas);
            editState.Update(point.Pointer, point.Position);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        #region value tip

        void IValueTip.ShowValueTip() {
            if (valueTip != null) {
                valueTip.IsVisible = true;
            }
        }

        void IValueTip.HideValueTip() {
            if (valueTip != null) {
                valueTip.IsVisible = false;
            }
            if (valueTipText != null) {
                valueTipText.Text = string.Empty;
            }
        }

        void IValueTip.UpdateValueTip(string text) {
            if (valueTip == null || valueTipText == null || valueTipCanvas == null) {
                return;
            }
            valueTipText.Text = text;
            Canvas.SetLeft(valueTip, valueTipPointerPosition.X);
            double tipY = valueTipPointerPosition.Y + 21;
            if (tipY + 21 > valueTipCanvas!.Bounds.Height) {
                tipY = tipY - 42;
            }
            Canvas.SetTop(valueTip, tipY);
        }

        #endregion

        void OnKeyDown(object sender, KeyEventArgs args) {
            if (lyricBox != null && lyricBox.IsVisible) {
                args.Handled = false;
                return;
            }
            var notesVm = ViewModel.NotesViewModel;
            if (notesVm.Part == null) {
                return;
            }
            if (args.KeyModifiers == KeyModifiers.None) {
                switch (args.Key) {
                    case Key.Back:
                    case Key.Delete:
                        notesVm.DeleteSelectedNotes();
                        args.Handled = true;
                        break;
                    case Key.D1: notesVm.SelectToolCommand?.Execute("1").Subscribe(); args.Handled = true; break;
                    case Key.D2: notesVm.SelectToolCommand?.Execute("2").Subscribe(); args.Handled = true; break;
                    case Key.D3: notesVm.SelectToolCommand?.Execute("3").Subscribe(); args.Handled = true; break;
                    case Key.D4: notesVm.SelectToolCommand?.Execute("4").Subscribe(); args.Handled = true; break;
                    case Key.T: notesVm.ShowTips = !notesVm.ShowTips; args.Handled = true; break;
                    case Key.Y: notesVm.PlayTone = !notesVm.PlayTone; args.Handled = true; break;
                    case Key.U: notesVm.ShowVibrato = !notesVm.ShowVibrato; args.Handled = true; break;
                    case Key.I: notesVm.ShowPitch = !notesVm.ShowPitch; args.Handled = true; break;
                    case Key.O: notesVm.ShowPhoneme = !notesVm.ShowPhoneme; args.Handled = true; break;
                    case Key.P: notesVm.IsSnapOn = !notesVm.IsSnapOn; args.Handled = true; break;
                    case Key.Up: notesVm.TransposeSelection(1); args.Handled = true; break;
                    case Key.Down: notesVm.TransposeSelection(-1); args.Handled = true; break;
                    case Key.Space:
                        if (ViewModel.PlaybackViewModel != null &&
                            !ViewModel.PlaybackViewModel.PlayOrPause()) {
                            MessageBox.Show(
                               this,
                               ThemeManager.GetString("dialogs.noresampler.message"),
                               ThemeManager.GetString("dialogs.noresampler.caption"),
                               MessageBox.MessageBoxButtons.Ok);
                        }
                        args.Handled = true;
                        break;
                    default: break;
                }
            } else if (args.KeyModifiers == cmdKey) {
                switch (args.Key) {
                    case Key.A: notesVm.SelectAllNotes(); args.Handled = true; break;
                    case Key.S: _ = MainWindow?.Save(); args.Handled = true; break;
                    case Key.Z: ViewModel.Undo(); args.Handled = true; break;
                    case Key.Y: ViewModel.Redo(); args.Handled = true; break;
                    case Key.C: notesVm.CopyNotes(); args.Handled = true; break;
                    case Key.X: notesVm.CutNotes(); args.Handled = true; break;
                    case Key.V: notesVm.PasteNotes(); args.Handled = true; break;
                    case Key.Up: notesVm.TransposeSelection(12); args.Handled = true; break;
                    case Key.Down: notesVm.TransposeSelection(-12); args.Handled = true; break;
                    default: break;
                }
            } else if (args.KeyModifiers == (cmdKey | KeyModifiers.Shift)) {
                switch (args.Key) {
                    case Key.Z: ViewModel.Redo(); args.Handled = true; break;
                    default: break;
                }
            } else if (args.KeyModifiers == KeyModifiers.Alt) {
                switch (args.Key) {
                    case Key.D1: expSelector1?.SelectExp(); args.Handled = true; break;
                    case Key.D2: expSelector2?.SelectExp(); args.Handled = true; break;
                    case Key.D3: expSelector3?.SelectExp(); args.Handled = true; break;
                    case Key.D4: expSelector4?.SelectExp(); args.Handled = true; break;
                    case Key.D5: expSelector5?.SelectExp(); args.Handled = true; break;
                    case Key.F4:
                        Hide();
                        args.Handled = true;
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
