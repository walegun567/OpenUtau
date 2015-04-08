﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.ComponentModel;

using OpenUtau.Core.USTx;
using OpenUtau.UI.Controls;

namespace OpenUtau.UI.Models
{
    public class MidiViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        public UProject Project;
        public UPart Part;
        public Canvas MidiCanvas;

        protected bool _updated = false;
        public void MarkUpdate() { _updated = true; }

        string _title = "New Part";
        double _trackHeight = UIConstants.NoteDefaultHeight;
        double _quarterCount = UIConstants.DefaultQuarterCount;
        double _quarterWidth = UIConstants.MidiQuarterDefaultWidth;
        double _viewWidth;
        double _viewHeight;
        double _offsetX = 0;
        double _offsetY = UIConstants.NoteDefaultHeight * 5 * 12;
        double _quarterOffset = 0;
        double _minTickWidth = UIConstants.MidiTickMinWidth;
        int _beatPerBar = 4;
        int _beatUnit = 4;

        public string Title { set { _title = value; } get { return _title; } }
        public double TotalHeight { get { return UIConstants.MaxNoteNum * _trackHeight - _viewHeight; } }
        public double TotalWidth { get { return _quarterCount * _quarterWidth - _viewWidth; } }
        public double QuarterCount { set { _quarterCount = value; } get { return _quarterCount; } }
        public double TrackHeight
        {
            set
            {
                _trackHeight = Math.Max(ViewHeight / UIConstants.MaxNoteNum, Math.Max(UIConstants.NoteMinHeight, Math.Min(UIConstants.NoteMaxHeight, value)));
                VerticalPropertiesChanged();
            }
            get { return _trackHeight; }
        }

        public double QuarterWidth
        {
            set
            {
                _quarterWidth = Math.Max(ViewWidth / QuarterCount, Math.Max(UIConstants.MidiQuarterMinWidth, Math.Min(UIConstants.MidiQuarterMaxWidth, value)));
                HorizontalPropertiesChanged();
            }
            get { return _quarterWidth; }
        }

        public double ViewWidth { set { _viewWidth = value; HorizontalPropertiesChanged(); } get { return _viewWidth; } }
        public double ViewHeight { set { _viewHeight = value; VerticalPropertiesChanged(); } get { return _viewHeight; } }
        public double OffsetX { set { _offsetX = Math.Max(0, value); HorizontalPropertiesChanged(); } get { return _offsetX; } }
        public double OffsetY { set { _offsetY = Math.Max(0, value); VerticalPropertiesChanged(); } get { return _offsetY; } }
        public double ViewportSizeX { get { if (TotalWidth <= 0) return 10000; else return ViewWidth * (TotalWidth + ViewWidth) / TotalWidth; } }
        public double ViewportSizeY { get { if (TotalHeight <= 0) return 10000; else return ViewHeight * (TotalHeight + ViewHeight) / TotalHeight; } }
        public double SmallChangeX { get { return ViewportSizeX / 10; } }
        public double SmallChangeY { get { return ViewportSizeY / 10; } }
        public double QuarterOffset { set { _quarterOffset = value; HorizontalPropertiesChanged(); } get { return _quarterOffset; } }
        public double MinTickWidth { set { _minTickWidth = value; HorizontalPropertiesChanged(); } get { return _minTickWidth; } }
        public int BeatPerBar { set { _beatPerBar = value; HorizontalPropertiesChanged(); } get { return _beatPerBar; } }
        public int BeatUnit { set { _beatUnit = value; HorizontalPropertiesChanged(); } get { return _beatUnit; } }

        public void HorizontalPropertiesChanged()
        {
            OnPropertyChanged("QuarterWidth");
            OnPropertyChanged("TotalWidth");
            OnPropertyChanged("OffsetX");
            OnPropertyChanged("ViewportSizeX");
            OnPropertyChanged("SmallChangeX");
            OnPropertyChanged("QuarterOffset");
            OnPropertyChanged("MinTickWidth");
            OnPropertyChanged("BeatPerBar");
            OnPropertyChanged("BeatUnit");
            MarkUpdate();
        }

        public void VerticalPropertiesChanged()
        {
            OnPropertyChanged("TrackHeight");
            OnPropertyChanged("TotalHeight");
            OnPropertyChanged("OffsetY");
            OnPropertyChanged("ViewportSizeY");
            OnPropertyChanged("SmallChangeY");
            MarkUpdate();
        }

        public List<UNote> SelectedNotes = new List<UNote>();
        public List<UNote> TempSelectedNotes = new List<UNote>();
        public List<NoteControl> NoteControls = new List<NoteControl>();

        public MidiViewModel() { }

        public void RedrawIfUpdated()
        {
            if (_updated)
            {
                foreach (NoteControl noteControl in NoteControls)
                {
                    noteControl.Width = Math.Max(UIConstants.NoteMinDisplayWidth, QuarterWidth * noteControl.Note.DurTick / Project.Resolution - 1);
                    noteControl.Height = TrackHeight - 2;
                    Canvas.SetLeft(noteControl, QuarterWidth * noteControl.Note.PosTick / Project.Resolution - OffsetX + 1);
                    Canvas.SetTop(noteControl, NoteNumToCanvas(noteControl.Note.NoteNum) + 1);
                }
            }
            _updated = false;
        }

        # region Selection

        public void UpdateSelectedVisual()
        {
            foreach (NoteControl noteControl in NoteControls)
            {
                if (SelectedNotes.Contains(noteControl.Note) || TempSelectedNotes.Contains(noteControl.Note)) noteControl.Selected = true;
                else noteControl.Selected = false;
            }
        }

        public void SelectAll() { SelectedNotes.Clear(); foreach (UNote note in Part.Notes) SelectedNotes.Add(note); UpdateSelectedVisual(); }
        public void DeselectAll() { SelectedNotes.Clear(); UpdateSelectedVisual(); }

        public void SelectNote(UNote note) { SelectedNotes.Add(note); }
        public void DeselectNote(UNote note) { SelectedNotes.Remove(note); }

        public void SelectTempNote(UNote note) { TempSelectedNotes.Add(note); }
        public void TempSelectInBox(double quarter1, double quarter2, int noteNum1, int noteNum2)
        {
            if (quarter2 < quarter1) { double temp = quarter1; quarter1 = quarter2; quarter2 = temp; }
            if (noteNum2 < noteNum1) { int temp = noteNum1; noteNum1 = noteNum2; noteNum2 = temp; }
            int tick1 = (int)(quarter1 * Project.Resolution);
            int tick2 = (int)(quarter2 * Project.Resolution);
            TempSelectedNotes.Clear();
            foreach (UNote note in Part.Notes)
            {
                if (note.PosTick <= tick2 && note.PosTick + note.DurTick >= tick1 &&
                    note.NoteNum >= noteNum1 && note.NoteNum <= noteNum2) SelectTempNote(note);
            }
            UpdateSelectedVisual();
        }

        public void DoneTempSelect()
        {
            foreach (UNote note in TempSelectedNotes) SelectNote(note);
            TempSelectedNotes.Clear();
            UpdateSelectedVisual();
        }

        # endregion

        # region Note operation

        public void UnloadPart()
        {
            foreach (NoteControl noteControl in NoteControls)
            {
                MidiCanvas.Children.Remove(noteControl);
            }
            SelectedNotes.Clear();
            NoteControls.Clear();
        }

        public void LoadPart(UPart part, UProject project)
        {
            UnloadPart();
            Part = part;
            Project = project;

            foreach (UNote note in part.Notes) AddNoteControl(note);

            Title = part.Name;
            QuarterOffset = (double)part.PosTick / project.Resolution;
            QuarterCount = (double)part.DurTick / project.Resolution;
            OffsetX = OffsetX;
            QuarterWidth = QuarterWidth;
            MarkUpdate();
        }

        public NoteControl GetNoteControl(UNote note)
        {
            foreach (NoteControl nc in NoteControls)
            {
                if (nc.Note == note) return nc;
            }
            return null;
        }

        public void AddNote(UNote note)
        {
            Part.Notes.Add(note);
            AddNoteControl(note);
        }

        public void AddNoteControl(UNote note)
        {
            NoteControl noteControl = new NoteControl()
            {
                Note = note,
                Channel = note.Channel,
                Lyric = note.Lyric
            };
            MidiCanvas.Children.Add(noteControl);
            NoteControls.Add(noteControl);
        }

        public void RemoveNote(NoteControl nc)
        {
            Part.Notes.Remove(nc.Note);
            MidiCanvas.Children.Remove(nc);
            NoteControls.Remove(nc);
        }

        public void RemoveNote(UNote note)
        {
            NoteControl nc = GetNoteControl(note);
            RemoveNote(nc);
        }

        # endregion

        # region Calculation

        public double GetSnapUnit() { return OpenUtau.Core.MusicMath.getZoomRatio(QuarterWidth, BeatPerBar, BeatUnit, MinTickWidth); }
        public double CanvasToQuarter(double X) { return (X + OffsetX) / QuarterWidth; }
        public double QuarterToCanvas(double X) { return X * QuarterWidth - OffsetX; }
        public double CanvasToSnappedQuarter(double X)
        {
            double quater = CanvasToQuarter(X);
            double snapUnit = GetSnapUnit();
            return (int)(quater / snapUnit) * snapUnit;
        }
        public double CanvasToNextSnappedQuarter(double X)
        {
            double quater = CanvasToQuarter(X);
            double snapUnit = GetSnapUnit();
            return (int)(quater / snapUnit) * snapUnit + snapUnit;
        }
        public double CanvasRoundToSnappedQuarter(double X)
        {
            double quater = CanvasToQuarter(X);
            double snapUnit = GetSnapUnit();
            return Math.Round(quater / snapUnit) * snapUnit;
        }
        public int CanvasToSnappedTick(double X) { return (int)(CanvasToSnappedQuarter(X) * Project.Resolution); }

        public int CanvasToNoteNum(double Y) { return UIConstants.MaxNoteNum - 1 - (int)((Y + OffsetY) / TrackHeight); }
        public double NoteNumToCanvas(int noteNum) { return TrackHeight * (UIConstants.MaxNoteNum - 1 - noteNum) - OffsetY; }

        # endregion
    }
}
