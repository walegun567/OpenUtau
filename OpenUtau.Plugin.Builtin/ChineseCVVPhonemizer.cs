﻿using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Chinese 十月式整音扩张 CVV Phonemizer.
    /// <para>It works by spliting "duang" to "duang" + "_ang", to produce the proper tail sound.</para>
    /// </summary>
    [Phonemizer("Chinese CVV (十月式整音扩张) Phonemizer", "ZH CVV")]
    public class ChineseCVVPhonemizer : Phonemizer {
        /// <summary>
        ///  The consonant table.
        /// </summary>
        static readonly string consonants = "b,p,m,f,d,t,n,l,g,k,h,j,q,x,z,c,s,zh,ch,sh,r,y,w";
        /// <summary>
        /// The vowel split table.
        /// </summary>
        static readonly string vowels = "ai=_ai,uai=_uai,an=_an,ian=_en2,uan=_an,van=_en2,ang=_ang,iang=_ang,uang=_ang,ao=_ao,iao=_ao,ou=_ou,iu=_ou,ong=_ong,iong=_ong,ei=_ei,ui=_ei,uei=_ei,en=_en,un=_un,uen=_un,eng=_eng,in=_in,ing=_ing,vn=_vn";

        static HashSet<string> cSet;
        static Dictionary<string, string> vDict;

        static ChineseCVVPhonemizer() {
            cSet = new HashSet<string>(consonants.Split(','));
            vDict = vowels.Split(',')
                .Select(s => s.Split('='))
                .ToDictionary(a => a[0], a => a[1]);
        }

        private USinger singer;

        // Simply stores the singer in a field.
        public override void SetSinger(USinger singer) => this.singer = singer;

        public override Phoneme[] Process(Note[] notes, Note? prevNeighbour, Note? nextNeighbour) {
            // The overall logic is:
            // 1. Remove consonant: "duang" -> "uang".
            // 2. Lookup the trailing sound in vowel table: "uang" -> "_ang".
            // 3. Split the total duration and returns "duang" and "_ang".
            var note = notes[0];
            string vowel = string.Empty;
            if (note.lyric.Length > 2 && cSet.Contains(note.lyric.Substring(0, 2))) {
                // First try to find consonant "zh", "ch" or "sh", and extract vowel.
                vowel = note.lyric.Substring(2);
            } else if (note.lyric.Length > 1 && cSet.Contains(note.lyric.Substring(0, 1))) {
                // Then try to find single character consonants, and extract vowel.
                vowel = note.lyric.Substring(1);
            } // Otherwise we don't need the vowel.
            string phoneme0 = note.lyric;
            // We will need to split the total duration for phonemes, so we compute it here.
            int totalDuration = notes.Sum(n => n.duration);
            // Lookup the vowel split table. For example, "uang" will match "_ang".
            if (vDict.TryGetValue(vowel, out var phoneme1)) {
                // Now phoneme0="duang" and phoneme1="_ang",
                // try to give "_ang" 120 ticks, but no more than half of the total duration.
                int length1 = 120;
                if (length1 > totalDuration / 2) {
                    length1 = totalDuration / 2;
                }
                return new Phoneme[] {
                    new Phoneme() {
                        phoneme = phoneme0,
                    },
                    new Phoneme() {
                        phoneme = phoneme1,
                        position = totalDuration - length1,
                    }
                };
            }
            // Not spliting is needed. Return as is.
            return new Phoneme[] {
                new Phoneme() {
                    phoneme = phoneme0,
                }
            };
        }
    }
}
