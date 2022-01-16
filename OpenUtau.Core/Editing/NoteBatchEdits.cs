﻿using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Editing {
    public class AddTailNote : BatchEdit {
        public string Name => name;

        private string lyric;
        private string name;

        public AddTailNote(string lyric, string name) {
            this.lyric = lyric;
            this.name = name;
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            List<UNote> toAdd = new List<UNote>();
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            foreach (var note in notes) {
                if (note.lyric != lyric && (note.Next == null || note.Next.position > note.End + 120)) {
                    toAdd.Add(project.CreateNote(note.tone, note.End, 120));
                }
            }
            if (toAdd.Count == 0) {
                return;
            }
            docManager.StartUndoGroup();
            foreach (var note in toAdd) {
                note.lyric = lyric;
                docManager.ExecuteCmd(new AddNoteCommand(part, note));
            }
            docManager.EndUndoGroup();
        }
    }

    public class QuantizeNotes : BatchEdit {
        public virtual string Name => name;

        private int quantize;
        private string name;

        public QuantizeNotes(int quantize) {
            this.quantize = quantize;
            name = $"pianoroll.menu.notes.quantize{quantize}";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup();
            foreach (var note in notes) {
                int pos = note.position;
                int end = note.End;
                int newPos = (int)Math.Round(1.0 * pos / quantize) * quantize;
                int newEnd = (int)Math.Round(1.0 * end / quantize) * quantize;
                if (newPos != pos) {
                    docManager.ExecuteCmd(new MoveNoteCommand(part, note, newPos - pos, 0));
                    docManager.ExecuteCmd(new ResizeNoteCommand(part, note, newEnd - newPos - note.duration));
                } else if (newEnd != end) {
                    docManager.ExecuteCmd(new ResizeNoteCommand(part, note, newEnd - newPos - note.duration));
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class ResetPitchBends : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ResetPitchBends() {
            name = "pianoroll.menu.notes.reset.pitchbends";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup();
            foreach (var note in notes) {
                docManager.ExecuteCmd(new ResetPitchPointsCommand(note));
            }
            docManager.EndUndoGroup();
        }
    }

    public class ResetAllExpressions : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ResetAllExpressions() {
            name = "pianoroll.menu.notes.reset.exps";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup();
            foreach (var note in notes) {
                if (note.phonemeExpressions.Count > 0 || note.noteExpressions.Count > 0) {
                    docManager.ExecuteCmd(new ResetExpressionsCommand(note));
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class ClearVibratos : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ClearVibratos() {
            name = "pianoroll.menu.notes.reset.vibratos";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup();
            foreach (var note in notes) {
                if (note.vibrato.length > 0) {
                    docManager.ExecuteCmd(new VibratoLengthCommand(part, note, 0));
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class ClearTimings : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ClearTimings() {
            name = "pianoroll.menu.notes.reset.phonemetimings";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup();
            foreach (var note in notes) {
                bool shouldClear = false;
                foreach (var o in note.phonemeOverrides) {
                    if (o.offset != null || o.preutterDelta != null || o.overlapDelta != null) {
                        shouldClear = true;
                        break;
                    }
                }
                if (shouldClear) {
                    docManager.ExecuteCmd(new ClearPhonemeTimingCommand(part, note));
                }
            }
            docManager.EndUndoGroup();
        }
    }
}
