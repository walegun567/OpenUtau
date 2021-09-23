﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    class PartsCanvas : Canvas {
        public static readonly DirectProperty<PartsCanvas, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<PartsCanvas, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<PartsCanvas, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<PartsCanvas, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<PartsCanvas, ObservableCollection<UPart>> ItemsProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, ObservableCollection<UPart>>(
                nameof(Items),
                o => o.Items,
                (o, v) => o.Items = v);

        public double TickWidth {
            get => tickWidth;
            private set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TrackHeight {
            get => trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public double TickOffset {
            get => tickOffset;
            private set => SetAndRaise(TickOffsetProperty, ref tickOffset, value);
        }
        public double TrackOffset {
            get => trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref trackOffset, value);
        }
        public ObservableCollection<UPart> Items {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }

        private double tickWidth;
        private double trackHeight;
        private double tickOffset;
        private double trackOffset;
        private ObservableCollection<UPart> _items;

        Dictionary<UPart, PartControl> partControls = new Dictionary<UPart, PartControl>();

        protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change) {
            base.OnPropertyChanged(change);
            if (!change.IsEffectiveValueChange) {
                return;
            }
            if (change.Property == ItemsProperty) {
                if (change.OldValue != null && change.OldValue.Value is ObservableCollection<UPart> oldCol) {
                    oldCol.CollectionChanged -= Items_CollectionChanged;
                }
                if (change.NewValue.HasValue && change.NewValue.Value is ObservableCollection<UPart> newCol) {
                    newCol.CollectionChanged += Items_CollectionChanged;
                }
            }
        }

        private void Items_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null) {
                        foreach (var item in e.OldItems) {
                            if (item is UPart part) {
                                Remove(part);
                            }
                        }
                    }
                    if (e.NewItems != null) {
                        foreach (var item in e.NewItems) {
                            if (item is UPart part) {
                                Add(part);
                            }
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (var (part, _) in partControls) {
                        Remove(part);
                    }
                    break;
            }
        }

        void Add(UPart part) {
            var control = new PartControl(part, this);
            Children.Add(control);
            partControls.Add(part, control);
        }

        void Remove(UPart part) {
            var control = partControls[part];
            control.Dispose();
            partControls.Remove(part);
            Children.Remove(control);
        }
    }
}
