﻿using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Vogen {
    class VogenSinger : USinger {
        public override string Id => meta.id;
        public override string Name => $"[Vogen] {meta.name}";
        public override USingerType SingerType => USingerType.Vogen;
        public override string BasePath => basePath;
        public override string Author => meta.builtBy;
        public override string Voice => meta.voiceBy;
        public override string Location => filePath;
        public override string Web => meta.web;
        public override string Version => meta.version;
        public override string OtherInfo => meta.misc;
        public override IList<string> Errors => errors;
        public override string Avatar => meta.avatar;
        public override byte[] AvatarData => avatarData;
        public override string Portrait => meta.portrait;
        public override float PortraitOpacity => meta.portraitOpacity;
        public override Encoding TextFileEncoding => Encoding.UTF8;
        public override IList<USubbank> Subbanks => subbanks;
        public override Dictionary<string, UOto> Otos => otos;

        string basePath;
        string filePath;
        VogenMeta meta;
        List<string> errors = new List<string>();
        List<USubbank> subbanks = new List<USubbank>();
        Dictionary<string, UOto> otos = new Dictionary<string, UOto>();

        public byte[] model;
        public byte[] avatarData;

        public VogenSinger(string filePath, VogenMeta meta, byte[] model, byte[] avatar) {
            basePath = Path.GetDirectoryName(filePath);
            this.filePath = filePath;
            this.meta = meta;
            this.model = model;
            this.avatarData = avatar;
            found = true;
            loaded = true;
        }

        public override bool TryGetMappedOto(string phoneme, int tone, out UOto oto) {
            oto = new UOto() {
                Alias = phoneme,
                Phonetic = phoneme,
            };
            return true;
        }

        public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
            oto = new UOto() {
                Alias = phoneme,
                Phonetic = phoneme,
            };
            return true;
        }

        public override byte[] LoadPortrait() {
            return string.IsNullOrEmpty(meta.portrait)
                ? null
                : Zip.ExtractBytes(filePath, meta.portrait);
        }
    }
}
