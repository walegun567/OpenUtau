﻿using System;
using System.Collections.Generic;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using System.Linq;
using System.IO;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Use this class as a base for easier phonemizer configuration. Works for vb styles like VCV, VCCV, CVC etc;
    /// 
    /// - Supports dictionary;
    /// - Automatically align phonemes to notes;
    /// - Supports syllable extension;
    /// - Automatically calculates transition phonemes length, with constants by default,
    /// but there is a pre-created function to use Oto value;
    /// - The transition length is scaled based on Tempo and note length.
    /// 
    /// Note that here "Vowel" means "stretchable phoneme" and "Consonant" means "non-stretchable phoneme".
    /// 
    /// So if a diphthong is represented with several phonemes, like English "byke" -> [b a y k], 
    /// then [a] as a stretchable phoneme would be a "Vowel", and [y] would be a "Consonant".
    /// 
    /// Some reclists have consonants that also may behave as vowels, like long "M" and "N". They are "Vowels".
    /// 
    /// If your oto hase same symbols for them, like "n" for stretchable "n" from a long note and "n" from CV,
    /// then you can use a vitrual symbol [N], and then replace it with [n] in ValidateAlias().
    /// </summary>
    public abstract class SyllableBasedPhonemizer : Phonemizer {

        /// <summary>
        /// Syllable is [V] [C..] [V]
        /// </summary>
        protected struct Syllable {
            /// <summary>
            /// vowel from previous syllable for VC
            /// </summary>
            public string prevV;
            /// <summary>
            /// CCs, may be empty
            /// </summary>
            public string[] cc;
            /// <summary>
            /// "base" note. May not actually be vowel, if only consonants way provided
            /// </summary>
            public string v;
            /// <summary>
            /// Start position for vowel. All VC CC goes before this position
            /// </summary>
            public int position;
            /// <summary>
            /// previous note duration, i.e. this is container for VC and CC notes
            /// </summary>
            public int duration;
            /// <summary>
            /// Tone for VC and CC
            /// </summary>
            public int tone;
            /// <summary>
            /// tone for base "vowel" phoneme
            /// </summary>
            public int vowelTone;

            /// <summary>
            /// 0 if no consonants are taken from previous word;
            /// 1 means first one is taken from previous word, etc.
            /// </summary>
            public int prevWordConsonantsCount;

            // helpers
            public bool IsRV => prevV == "" && cc.Length == 0;
            public bool IsVV => prevV != "" && cc.Length == 0;

            public bool IsRCV => prevV == "" && cc.Length > 0;
            public bool IsVCV => prevV != "" && cc.Length > 0;

            public bool IsRC1V => prevV == "" && cc.Length == 1;
            public bool IsVC1V => prevV != "" && cc.Length == 1;

            public bool IsRCmV => prevV == "" && cc.Length > 1;
            public bool IsVCmV => prevV != "" && cc.Length > 1;

            public string[] PreviousWordCc => cc.Take(prevWordConsonantsCount).ToArray();
            public string[] CurrentWordCc => cc.Skip(prevWordConsonantsCount).ToArray();

            public override string ToString() {
                return $"({prevV}) {(cc != null ? string.Join(" ", cc) : "")} {v}";
            }
        }

        protected struct Ending {
            /// <summary>
            /// vowel from the last syllable to make VC
            /// </summary>
            public string prevV;
            /// <summary>
            ///  actuall CC at the ending
            /// </summary>
            public string[] cc;
            /// <summary>
            /// last note position + duration, all phonemes must be less than this
            /// </summary>
            public int position;
            /// <summary>
            /// last syllable length, max container for all VC CC C-
            /// </summary>
            public int duration;
            /// <summary>
            /// the tone from last syllable, for all ending phonemes
            /// </summary>
            public int tone;

            // helpers
            public bool IsVR => cc.Length == 0;
            public bool IsVCR => cc.Length > 0;
            public bool IsVC1R => cc.Length == 1;
            public bool IsVCmR => cc.Length > 1;

            public override string ToString() {
                return $"({prevV}) {(cc != null ? string.Join(" ", cc) : "")}";
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            error = "";
            var mainNote = notes[0];
            if (mainNote.lyric.StartsWith(FORCED_ALIAS_SYMBOL)) {
                return MakeForcedAliasResult(mainNote);
            }

            var syllables = MakeSyllables(notes, MakeEnding(prevNeighbours));
            if (syllables == null) {
                return HandleError();
            }

            var phonemes = new List<Phoneme>();
            foreach (var syllable in syllables) {
                phonemes.AddRange(MakePhonemes(ProcessSyllable(syllable), mainNote.phonemeAttributes, phonemes.Count, syllable.duration, syllable.position,
                    syllable.tone, syllable.vowelTone, false));
            }
            if (!nextNeighbour.HasValue) {
                var tryEnding = MakeEnding(notes);
                if (tryEnding.HasValue) {
                    var ending = tryEnding.Value;
                    phonemes.AddRange(MakePhonemes(ProcessEnding(ending), mainNote.phonemeAttributes, phonemes.Count, ending.duration, ending.position,
                        ending.tone, ending.tone, true));
                }
            }

            return new Result() {
                phonemes = phonemes.ToArray()
            };
        }

        private Result HandleError() {
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme() {
                        phoneme = error
                    }
                }
            };
        }

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            if (!hasDictionary) {
                ReadDictionary();
            }
            Init();
        }

        protected USinger singer;
        protected bool hasDictionary => dictionaries.ContainsKey(GetType());
        protected G2pDictionary dictionary => dictionaries[GetType()];
        protected double TransitionBasicLengthMs => 100;

        private static Dictionary<Type, G2pDictionary> dictionaries = new Dictionary<Type, G2pDictionary>();
        private const string FORCED_ALIAS_SYMBOL = "?";
        private string error = "";
        private readonly string[] wordSeparators = new[] { " ", "_" };

        /// <summary>
        /// Returns list of vowels
        /// </summary>
        /// <returns></returns>
        protected abstract string[] GetVowels();

        /// <summary>
        /// Returns list of consonants. Only needed if there is a dictionary
        /// </summary>
        /// <returns></returns>
        protected virtual string[] GetConsonants() {
            throw new NotImplementedException();
        }

        /// <summary>
        /// returns phoneme symbols, like, VCV, or VC + CV, or -CV, etc
        /// </summary>
        /// <returns>List of phonemes</returns>
        protected abstract List<string> ProcessSyllable(Syllable syllable);

        /// <summary>
        /// phoneme symbols for ending, like, V-, or VC-, or VC+C
        /// </summary>
        protected abstract List<string> ProcessEnding(Ending ending);

        /// <summary>
        /// simple alias to alias fallback
        /// </summary>
        /// <returns></returns>
        protected virtual Dictionary<string, string> GetAliasesFallback() { return null; }

        /// <summary>
        /// Use to some custom init, if needed
        /// </summary>
        protected virtual void Init() { }

        /// <summary>
        /// Dictionary name. Must be stored in Dictionaries folder.
        /// If missing or can't be read, phonetic input is used
        /// </summary>
        /// <returns></returns>
        protected virtual string GetDictionaryName() { return null; }

        /// <summary>
        /// extracts array of phoneme symbols from note. Override for procedural dictionary or something
        /// reads from dictionary if provided
        /// </summary>
        /// <param name="note"></param>
        /// <returns></returns>
        protected virtual string[] GetSymbols(Note note) {
            string[] getSymbolsRaw(string lyrics) {
                if (lyrics == null) {
                    return new string[0];
                } else return lyrics.Split(" ");
            }

            if (hasDictionary) {
                if (!string.IsNullOrEmpty(note.phoneticHint)) {
                    return getSymbolsRaw(note.phoneticHint);
                }
                var result = new List<string>();
                foreach (var subword in note.lyric.Trim().ToLowerInvariant().Split(wordSeparators, StringSplitOptions.RemoveEmptyEntries)) {
                    var subResult = dictionary.Query(subword);
                    if (subResult == null) {
                        subResult = HandleWordNotFound(subword);
                        if (subword == null) {
                            return null;
                        }
                    }
                    result.AddRange(subResult);
                }
                return result.ToArray();
            }
            else {
                return getSymbolsRaw(note.lyric);
            }
        }

        /// <summary>
        /// Instead of changing symbols in cmudict itself for each reclist, 
        /// you may leave it be and provide symbol replacements with this method.
        /// </summary>
        /// <returns></returns>
        protected virtual Dictionary<string, string> GetDictionaryPhonemesReplacement() { return null; }

        /// <summary>
        /// separates symbols to syllables, without an ending.
        /// </summary>
        /// <param name="inputNotes"></param>
        /// <param name="prevWord"></param>
        /// <returns></returns>
        protected virtual Syllable[] MakeSyllables(Note[] inputNotes, Ending? prevEnding) {
            (var symbols, var vowelIds, var notes) = GetSymbolsAndVowels(inputNotes);
            if (symbols == null || vowelIds == null || notes == null) {
                return null;
            }
            var firstVowelId = vowelIds[0];
            if (notes.Length < vowelIds.Length) {
                error = $"Not enough extension notes, {vowelIds.Length - notes.Length} more expected";
                return null;
            }

            var syllables = new Syllable[vowelIds.Length];

            // Making the first syllable
            if (prevEnding.HasValue) {
                var prevEndingValue = prevEnding.Value;
                var beginningCc = prevEndingValue.cc.ToList();
                beginningCc.AddRange(symbols.Take(firstVowelId));

                // If we had a prev neighbour ending, let's take info from it
                syllables[0] = new Syllable() {
                    prevV = prevEndingValue.prevV,
                    cc = beginningCc.ToArray(),
                    v = symbols[firstVowelId],
                    tone = prevEndingValue.tone,
                    duration = prevEndingValue.duration,
                    position = 0,
                    vowelTone = notes[0].tone,
                    prevWordConsonantsCount = prevEndingValue.cc.Count()
                };
            } else {
                // there is only empty space before us
                syllables[0] = new Syllable() {
                    prevV = "",
                    cc = symbols.Take(firstVowelId).ToArray(),
                    v = symbols[firstVowelId],
                    tone = notes[0].tone,
                    duration = -1,
                    position = 0,
                    vowelTone = notes[0].tone
                };
            }

            // normal syllables after the first one
            var syllableI = 1;
            var noteI = 1;
            var ccs = new List<string>();
            var position = 0;
            var lastSymbolI = firstVowelId + 1;
            for (; lastSymbolI < symbols.Length & syllableI < notes.Length; lastSymbolI++) {
                if (!vowelIds.Contains(lastSymbolI)) {
                    ccs.Add(symbols[lastSymbolI]);
                } else {
                    position += notes[syllableI - 1].duration;
                    syllables[syllableI] = new Syllable() {
                        prevV = syllables[syllableI - 1].v,
                        cc = ccs.ToArray(),
                        v = symbols[lastSymbolI],
                        tone = syllables[syllableI - 1].vowelTone,
                        duration = notes[noteI - 1].duration,
                        position = position,
                        vowelTone = notes[noteI].tone
                    };
                    ccs = new List<string>();
                    syllableI++;
                }
            }

            return syllables;
        }

        /// <summary>
        /// extracts word ending
        /// </summary>
        /// <param inputNotes="notes"></param>
        /// <returns></returns>
        protected Ending? MakeEnding(Note[] inputNotes) {
            if (inputNotes.Length == 0 || inputNotes[0].lyric.StartsWith(FORCED_ALIAS_SYMBOL)) {
                return null;
            }

            (var symbols, var vowelIds, var notes) = GetSymbolsAndVowels(inputNotes);
            if (symbols == null || vowelIds == null || notes == null) {
                return null;
            }

            return new Ending() {
                prevV = symbols[vowelIds.Last()],
                cc = symbols.Skip(vowelIds.Last() + 1).ToArray(),
                tone = notes.Last().tone,
                duration = notes.Skip(vowelIds.Length - 1).Sum(n => n.duration),
                position = notes.Sum(n => n.duration)
            };
        }

        /// <summary>
        /// extracts and validates symbols and vowels
        /// </summary>
        /// <param name="notes"></param>
        /// <returns></returns>
        private (string[], int[], Note[]) GetSymbolsAndVowels(Note[] notes) {
            var mainNote = notes[0];
            var symbols = GetSymbols(mainNote);
            if (symbols == null) {
                return (null, null, null);
            }
            if (symbols.Length == 0) {
                symbols = new string[] { "" };
            }
            symbols = ApplyExtensions(symbols, notes);
            List<int> vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            if (notes.Length < vowelIds.Count) {
                notes = HandleNotEnoughNotes(notes, vowelIds);
            }
            return (symbols, vowelIds.ToArray(), notes);
        }

        /// <summary>
        /// When there are more syllables than notes, recombines notes to match syllables count
        /// </summary>
        /// <param name="notes"></param>
        /// <param name="vowelIds"></param>
        /// <returns></returns>
        protected virtual Note[] HandleNotEnoughNotes(Note[] notes, List<int> vowelIds) {
            var newNotes = new List<Note>();
            newNotes.AddRange(notes.SkipLast(1));
            var lastNote = notes.Last();
            var position = lastNote.position;
            var notesToSplit = vowelIds.Count - newNotes.Count;
            var duration = lastNote.duration / notesToSplit / 15 * 15;
            for (var i = 0; i < notesToSplit; i++) {
                var durationFinal = i != notesToSplit - 1 ? duration : lastNote.duration - duration * (notesToSplit - 1);
                newNotes.Add(new Note() {
                    position = position,
                    duration = durationFinal,
                    phonemeAttributes = lastNote.phonemeAttributes
                });
                position += durationFinal;
            }

            return newNotes.ToArray();
        }

        /// <summary>
        /// Override this method, if you want to implement some machine converting from a word to phonemes
        /// </summary>
        /// <param name="word"></param>
        /// <returns></returns>
        protected virtual string[] HandleWordNotFound(string word) {
            error = "word not found";
            return null;
        }

        /// <summary>
        /// Does this note extend the previous syllable?
        /// </summary>
        /// <param name="note"></param>
        /// <returns></returns>
        protected bool IsSyllableVowelExtensionNote(Note note) {
            return note.lyric.StartsWith("+~") || note.lyric.StartsWith("+*");
        }

        /// <summary>
        /// Used to extract phonemes from CMU Dict word. Override if you need some extra logic
        /// </summary>
        /// <param name="phonemesString"></param>
        /// <returns></returns>
        protected virtual string[] GetDictionaryWordPhonemes(string phonemesString) {
            return phonemesString.Split(' ');
        }

        /// <summary>
        /// use to validate alias
        /// </summary>
        /// <param name="alias"></param>
        /// <returns></returns>
        protected virtual string ValidateAlias(string alias) {
            return alias;
        }

        /// <summary>
        /// Defines basic transition length before scaling it according to tempo and note length
        /// Use GetTransitionBasicLengthMsByConstant, GetTransitionBasicLengthMsByOto or your own implementation
        /// </summary>
        /// <param name="alias">Mapped alias</param>
        /// <returns></returns>
        protected virtual double GetTransitionBasicLengthMs(string alias = "") {
            return GetTransitionBasicLengthMsByConstant();
        }

        protected double GetTransitionBasicLengthMsByOto(string alias) {
            if (alias != null && alias.Length > 0 && singer.Otos.TryGetValue(alias, out var oto)) {
                return oto.Preutter * GetTempoNoteLengthFactor();
            } else {
                return GetTransitionBasicLengthMsByConstant();
            }
        }

        protected double GetTransitionBasicLengthMsByConstant() {
            return TransitionBasicLengthMs * GetTempoNoteLengthFactor();
        }

        /// <summary>
        /// a note length modifier, from 1 to 0.3. Used to make transition notes shorter on high tempo
        /// </summary>
        /// <returns></returns>
        protected double GetTempoNoteLengthFactor() {
            return (300 - Math.Clamp(bpm, 90, 300)) / (300 - 90) / 3 + 0.33;
        }

        /// <summary>
        /// Parses CMU dictionary, when phonemes are separated by spaces, and word vs phonemes are separated with two spaces,
        /// and replaces phonemes with replacement table
        /// </summary>
        /// <param name="dictionaryText"></param>
        /// <param name="builder"></param>
        protected virtual void ParseDictionary(string dictionaryText, G2pDictionary.Builder builder) {
            var replacements = GetDictionaryPhonemesReplacement();

            dictionaryText.Split('\n')
                    .Where(line => !line.StartsWith(";;;"))
                    .Select(line => line.Trim())
                    .Select(line => line.Split(new string[] { "  " }, StringSplitOptions.None))
                    .Where(parts => parts.Length == 2)
                    .ToList()
                    .ForEach(parts => builder.AddEntry(parts[0].ToLowerInvariant(), GetDictionaryWordPhonemes(parts[1]).Select(
                        n => replacements != null && replacements.ContainsKey(n) ? replacements[n] : n)));
        }

        #region helpers

        /// <summary>
        /// May be used if you have different logic for short and long notes
        /// </summary>
        /// <param name="syllable"></param>
        /// <returns></returns>
        protected bool IsShort(Syllable syllable) {
            return syllable.duration != -1 && TickToMs(syllable.duration) < GetTransitionBasicLengthMs() * 2;
        }
        protected bool IsShort(Ending ending) {
            return TickToMs(ending.duration) < GetTransitionBasicLengthMs() * 2;
        }

        /// <summary>
        /// Checks if mapped and validated alias exists in oto
        /// </summary>
        /// <param name="alias"></param>
        /// <param name="tone"></param>
        /// <returns></returns>
        protected bool HasOto(string alias, int tone) {
            return singer.TryGetMappedOto(ValidateAlias(alias), tone, out _);
        }

        /// <summary>
        /// Can be used for different variants, like exhales [v R], [v -] etc
        /// </summary>
        /// <param name="sourcePhonemes">phonemes container to add to</param>
        /// <param name="tone">to map alias</param>
        /// <param name="targetPhonemes">target phoneme variants</param>
        /// <returns>returns true if added any</returns>
        protected bool TryAddPhoneme(List<string> sourcePhonemes, int tone, params string[] targetPhonemes) {
            foreach (var phoneme in targetPhonemes) {
                if (HasOto(phoneme, tone)) {
                    sourcePhonemes.Add(phoneme);
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region private

        private Result MakeForcedAliasResult(Note note) {
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme {
                        phoneme = note.lyric.Substring(1)
                    }
                }
            };
        }

        private void ReadDictionary() {
            var dictionaryName = GetDictionaryName();
            if (dictionaryName == null)
                return;
            var filename = Path.Combine("Dictionaries", dictionaryName);
            if (!File.Exists(filename)) {
                Log.Error("Dictionary not found");
                return;
            }
            try {
                var dictionaryText = File.ReadAllText(filename);
                var builder = G2pDictionary.NewBuilder();
                var vowels = GetVowels();
                foreach (var vowel in vowels) {
                    builder.AddSymbol(vowel, true);
                }
                var consonants = GetConsonants();
                foreach (var consonant in consonants) {
                    builder.AddSymbol(consonant, false);
                }
                builder.AddEntry("a", new string[] { "a" });
                ParseDictionary(dictionaryText, builder);
                var dict = builder.Build();
                dictionaries[GetType()] = dict;
            }
            catch (Exception ex) {
                Log.Error(ex, "Failed to read dictionary");
            }
        }

        private string[] ApplyExtensions(string[] symbols, Note[] notes) {
            var newSymbols = new List<string>();
            var vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            var lastVowelI = 0;
            newSymbols.AddRange(symbols.Take(vowelIds[lastVowelI] + 1));
            for (var i = 1; i < notes.Length && lastVowelI + 1 < vowelIds.Count; i++) {
                if (!IsSyllableVowelExtensionNote(notes[i])) {
                    var prevVowel = vowelIds[lastVowelI];
                    lastVowelI++;
                    var vowel = vowelIds[lastVowelI];
                    newSymbols.AddRange(symbols.Skip(prevVowel + 1).Take(vowel - prevVowel));
                } else {
                    newSymbols.Add(symbols[vowelIds[lastVowelI]]);
                }
            }
            newSymbols.AddRange(symbols.Skip(vowelIds[lastVowelI] + 1));
            return newSymbols.ToArray();
        }

        private List<int> ExtractVowels(string[] symbols) {
            var vowelIds = new List<int>();
            var vowels = GetVowels();
            for (var i = 0; i < symbols.Length; i++) {
                if (vowels.Contains(symbols[i])) {
                    vowelIds.Add(i);
                }
            }
            return vowelIds;
        }

        private Phoneme[] MakePhonemes(List<string> phonemeSymbols, PhonemeAttributes[] phonemeAttributes, int phonemesOffset,
            int containerLength, int position, int tone, int lastTone, bool isEnding) {

            var phonemes = new Phoneme[phonemeSymbols.Count];
            for (var i = 0; i < phonemeSymbols.Count; i++) {
                var phonemeI = phonemeSymbols.Count - i - 1;
                var validatedAlias = ValidateAlias(phonemeSymbols[phonemeI]);
                var attr = phonemeAttributes?.FirstOrDefault(attr => attr.index == phonemesOffset + phonemeI) ?? default;
                var currentTone = phonemeI == phonemeSymbols.Count - 1 ? lastTone : tone;
                phonemes[phonemeI].phoneme = MapPhoneme(validatedAlias, currentTone, attr.voiceColor, attr.alternate?.ToString() ?? string.Empty, singer);

                var transitionLengthTick = MsToTick(GetTransitionBasicLengthMs(phonemes[phonemeI].phoneme));
                if (i == 0) {
                    if (!isEnding) {
                        transitionLengthTick = 0;
                    }
                    else {
                        transitionLengthTick *= 2;
                    }
                }
                // yet it's actually a length; will became position in ScalePhonemes
                phonemes[phonemeI].position = transitionLengthTick;
            }
            
            return ScalePhonemes(phonemes, position, isEnding ? phonemeSymbols.Count : phonemeSymbols.Count - 1, containerLength);
        }

        private Phoneme[] ScalePhonemes(Phoneme[] phonemes, int startPosition, int phonemesCount, int containerLengthTick = -1) {
            var offset = 0;
            // reserved length for prev vowel, double length of a transition;
            var containerSafeLengthTick = MsToTick(GetTransitionBasicLengthMsByConstant() * 2); 
            var lengthModifier = 1.0;
            if (containerLengthTick > 0) {
                var allTransitionsLengthTick = phonemes.Sum(n => n.position);
                if (allTransitionsLengthTick + containerSafeLengthTick > containerLengthTick) {
                    lengthModifier = (double)containerLengthTick / (allTransitionsLengthTick + containerSafeLengthTick);
                }
            }

            for (var i = phonemes.Length - 1; i >= 0; i--) {
                var finalLengthTick = (int)(phonemes[i].position * lengthModifier) / 5 * 5;
                phonemes[i].position = startPosition - finalLengthTick - offset;
                offset += finalLengthTick;
            }

            return phonemes;
        }

        #endregion
    }
}
