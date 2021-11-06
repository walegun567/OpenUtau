﻿using System;
using System.Collections.Generic;
using System.Text;
using OpenUtau.Api;
using System.Linq;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Russian VCCV Phonemizer", "RU VCCV", "Heiden.BZR")]
    public class RussianVCCVPhonemizer : AdvancedPhonemizer {

        private readonly string[] vowels = "a,e,o,u,y,i,M,N,ex,ax,x".Split(",");
        private readonly string[] consonants = "sh',sh,zh,j,ts,ch,b',b,v',v,g',g,d',d,z',z,k',k,l',l,m',m,n',n,p',p,r',r,s',s,t',t,f',f,h',h".Split(",");
        private readonly string[] burstConsonants = "t,t',k,k',p,p',ch,ts,b,b',g,g',d,d'".Split(",");
        private readonly Dictionary<string, string> dictionaryReplacements = ("a=ax;aa=a;ay=ax;b=b;bb=b';c=ts;ch=ch;d=d;dd=d';ee=e;" +
            "f=f;ff=f';g=g;gg=g';h=h;hh=h';i=x;ii=i;j=j;ja=a;je=e;jo=o;ju=u;k=k;kk=k';l=l;ll=l';m=m;mm=m';n=n;nn=n';oo=o;ae=e;" +
            "p=p;pp=p';r=r;rr=r';s=s;sch=sh';sh=sh;ss=s';t=t;tt=t';u=u;uj=u;uu=u;v=v;vv=v';y=ex;yy=y;z=z;zh=zh;zz=z'").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryPath() => "Plugins/cmudict_ru.txt";
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;

        protected override List<string> TrySyllable(Syllable syllable) {
            string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;

            string basePhoneme;
            var phonemes = new List<string>();
            if (prevV == "") {
                if (cc.Length == 0) {
                    basePhoneme = $"- {v}";
                } else if (cc.Length == 1) {
                    // -CV or -C CV
                    var rcv = $"- {cc[0]}{v}";
                    if (HasOto(rcv, syllable.tone)) {
                        basePhoneme = rcv;
                    }
                    else {
                        basePhoneme = $"{cc[0]}{v}";
                        phonemes.Add($"- {cc[0]}");
                    }
                } else {
                    basePhoneme = $"{cc.Last()}{v}";
                    phonemes.Add($"- {cc[0]}");
                }
            } else if (cc.Length == 0) {
                basePhoneme = $"{prevV} {v}";
            } else {
                basePhoneme = $"{cc.Last()}{v}";
                phonemes.Add($"{prevV} {cc[0]}");
            }
            for (var i = 0; i < cc.Length - 1; i++) {
                var currentCc = $"{cc[i]} {cc[i + 1]}";
                if (!HasOto(currentCc, syllable.tone)) {
                    continue;
                }
                phonemes.Add(currentCc);
            }
            phonemes.Add(basePhoneme);
            return phonemes;
        }

        protected override List<string> TryEnding(Ending ending) {
            string[] cc = ending.cc;
            string v = ending.prevV;

            var phonemes = new List<string>();
            if (cc.Length == 0) {
                phonemes.Add($"{v} -");
            } else if (cc.Length == 1) {
                // VC- or VC C-
                var vcr = $"{v}{cc[0]} -";
                if (HasOto(vcr, ending.tone)) {
                    phonemes.Add(vcr);
                } else {
                    phonemes.Add($"{v} {cc[0]}");
                    phonemes.Add($"{cc[0]} -");
                }
            } else {
                phonemes.Add($"{v} {cc[0]}");
                for (var i = 0; i < cc.Length - 1; i++) {
                    var currentCc = $"{cc[i]} {cc[i + 1]}";
                    if (!HasOto(currentCc, ending.tone)) {
                        continue;
                    }
                    phonemes.Add(currentCc);
                }
                if (burstConsonants.Contains(cc.Last())) {
                    phonemes.Add($"{cc.Last()} -");
                }
            }
            return phonemes;
        }
    }
}
