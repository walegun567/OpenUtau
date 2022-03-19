﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Format {
    public class Ustx {
        public static readonly Version kUstxVersion = new Version(0, 5);

        public const string DYN = "dyn";
        public const string PITD = "pitd";
        public const string CLR = "clr";
        public const string ENG = "eng";
        public const string VEL = "vel";
        public const string VOL = "vol";
        public const string ATK = "atk";
        public const string DEC = "dec";
        public const string GEN = "gen";
        public const string BRE = "bre";
        public const string LPF = "lpf";
        public const string MOD = "mod";
        public const string ALT = "alt";
        public const string SHFT = "shft";

        public static readonly string[] required = { DYN, PITD, CLR, ENG, VEL, VOL, ATK, DEC, };

        public static void AddBuiltInExpressions(UProject project) {
            project.RegisterExpression(new UExpressionDescriptor("dynamics", DYN, -240, 120, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("pitch deviation", PITD, -1200, 1200, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("voice color", CLR, false, new string[0]));
            project.RegisterExpression(new UExpressionDescriptor("resampler engine", ENG, false, new string[] { "", WorldlineResampler.name }));
            project.RegisterExpression(new UExpressionDescriptor("velocity", VEL, 0, 200, 100));
            project.RegisterExpression(new UExpressionDescriptor("volume", VOL, 0, 200, 100));
            project.RegisterExpression(new UExpressionDescriptor("attack", ATK, 0, 200, 100));
            project.RegisterExpression(new UExpressionDescriptor("decay", DEC, 0, 100, 0));
        }

        public static void AddDefaultExpressions(UProject project) {
            AddBuiltInExpressions(project);
            project.RegisterExpression(new UExpressionDescriptor("gender", GEN, -100, 100, 0, "g"));
            project.RegisterExpression(new UExpressionDescriptor("breath", BRE, 0, 100, 0, "B"));
            project.RegisterExpression(new UExpressionDescriptor("lowpass", LPF, 0, 100, 0, "H"));
            project.RegisterExpression(new UExpressionDescriptor("modulation", MOD, 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("alternate", ALT, 0, 16, 0));
            project.RegisterExpression(new UExpressionDescriptor("tone shift", SHFT, -36, 36, 0));
        }

        public static UProject Create() {
            UProject project = new UProject() { Saved = false };
            AddDefaultExpressions(project);
            return project;
        }

        public static void Save(string filePath, UProject project) {
            project.ustxVersion = kUstxVersion;
            project.FilePath = filePath;
            project.BeforeSave();
            File.WriteAllText(filePath, Yaml.DefaultSerializer.Serialize(project), Encoding.UTF8);
            project.Saved = true;
            project.AfterSave();
        }

        public static UProject Load(string filePath) {
            string text = File.ReadAllText(filePath, Encoding.UTF8);
            UProject project = Yaml.DefaultDeserializer.Deserialize<UProject>(text);
            AddDefaultExpressions(project);
            project.FilePath = filePath;
            project.Saved = true;
            project.AfterLoad();
            project.ValidateFull();
            if (project.ustxVersion < kUstxVersion) {
                Log.Information($"Upgrading project from {project.ustxVersion} to {kUstxVersion}");
            }
            if (project.ustxVersion < new Version(0, 4)) {
                if (project.expressions.TryGetValue("acc", out var exp) && exp.name == "accent") {
                    project.expressions.Remove("acc");
                    exp.abbr = ATK;
                    exp.name = "attack";
                    project.expressions[ATK] = exp;
                    project.parts
                        .Where(part => part is UVoicePart)
                        .Select(part => part as UVoicePart)
                        .SelectMany(part => part.notes)
                        .SelectMany(note => note.phonemeExpressions)
                        .Where(exp => exp.abbr == "acc")
                        .ToList()
                        .ForEach(exp => exp.abbr = ATK);
                    project.ValidateFull();
                }
            }
            if (project.ustxVersion < new Version(0, 5)) {
                project.parts
                    .Where(part => part is UVoicePart)
                    .Select(part => part as UVoicePart)
                    .SelectMany(part => part.notes)
                    .Where(note => note.lyric.StartsWith("..."))
                    .ToList()
                    .ForEach(note => note.lyric = note.lyric.Replace("...", "+"));
                project.ValidateFull();
            }
            project.ustxVersion = kUstxVersion;
            return project;
        }
    }
}
