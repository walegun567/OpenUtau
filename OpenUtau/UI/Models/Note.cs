﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtau.UI.Models
{
    class Note
    {
        public const double minLength = 4.0 / 64;  // Actual minimal possible note length is 1/64 note

        public int keyNo;
        public double offset;
        public double length = 1;
        public string lyric = "a";
        public System.Windows.Shapes.Rectangle shape;

        public Note()
        {
            shape = new System.Windows.Shapes.Rectangle
            {
                Fill = System.Windows.Media.Brushes.Gray,
                RadiusX = 2,
                RadiusY = 2,
                Opacity = 0.75
            };
        }
    }
}
