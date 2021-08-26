﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;

namespace OpenUtau.Classic {
    class PluginLoader {
        public static Plugin[] LoadAll(string basePath) {
            var encoding = Encoding.GetEncoding("shift_jis");
            return Directory.EnumerateFiles(basePath, "plugin.txt", SearchOption.AllDirectories)
                .Select(filePath => ParsePluginTxt(filePath, encoding))
                .ToArray();
        }

        private static Plugin ParsePluginTxt(string filePath, Encoding encoding) {
            using (var stream = File.OpenRead(filePath)) {
                using (var reader = new StreamReader(stream, encoding)) {
                    var plugin = new Plugin();
                    var otherLines = new List<string>();
                    while (!reader.EndOfStream) {
                        string line = reader.ReadLine().Trim();
                        var s = line.Split(new char[] { '=' });
                        if (s.Length != 2) {
                            s = line.Split(new char[] { ':' });
                        }
                        Array.ForEach(s, temp => temp.Trim());
                        if (s.Length == 2) {
                            s[0] = s[0].ToLowerInvariant();
                            if (s[0] == "name") {
                                plugin.Name = s[1];
                            } else if (s[0] == "execute") {
                                plugin.Executable = Path.Combine(Path.GetDirectoryName(filePath), s[1]);
                            } else if (s[0] == "notes" && s[1] == "all") {
                                plugin.AllNotes = true;
                            } else if (s[0] == "shell" && s[1] == "use") {
                                plugin.UseShell = true;
                            } else {
                                otherLines.Add(line);
                            }
                        } else {
                            otherLines.Add(line);
                        }
                    }
                    if (string.IsNullOrWhiteSpace(plugin.Name) || string.IsNullOrWhiteSpace(plugin.Executable)) {
                        throw new FileFormatException($"Failed to load {filePath} using encoding {encoding.EncodingName}");
                    }
                    Log.Information($"Loaded plugin {plugin.Name} {plugin.Executable}");
                    return plugin;
                }
            }
        }
    }
}
