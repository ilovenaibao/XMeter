﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using XMeter2.Annotations;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace XMeter2
{
    public partial class MainWindow : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer = new DispatcherTimer();

        private static readonly TimeSpan _showAnimationDelay = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan _showAnimationDuration = TimeSpan.FromMilliseconds(200);
        
        private readonly LinkedList<TimeEntry> _upPoints = new LinkedList<TimeEntry>();
        private readonly LinkedList<TimeEntry> _downPoints = new LinkedList<TimeEntry>();

        private ulong _lastMaxUp;
        private ulong _lastMaxDown;
        private Icon _icon;

        private string _upSpeed = "0 B/s";
        private string _downSpeed = "0 B/s";
        private string _startTime = DateTime.Now.AddSeconds(-1).ToString("HH:mm:ss");
        private string _endTime = DateTime.Now.ToString("HH:mm:ss");
        private Brush _popupBackground;
        private Brush _popupBorder;
        private bool _isPopupOpen;
        private Brush _popupPanel;
        private bool _opening;
        private bool _shown;
        private string _downSpeedMax;
        private string _upSpeedMax;
        private DoubleAnimation _showOpacityAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = _showAnimationDuration,
            DecelerationRatio = 1
        };
        private DoubleAnimation _showTopAnimation = new DoubleAnimation
        {
            Duration = _showAnimationDuration,
            DecelerationRatio = 1
        };

public string StartTime
        {
            get => _startTime;
            set
            {
                if (value == _startTime) return;
                _startTime = value;
                OnPropertyChanged();
            }
        }

        public string EndTime
        {
            get => _endTime;
            set
            {
                if (value == _endTime) return;
                _endTime = value;
                OnPropertyChanged();
            }
        }

        public string UpSpeed
        {
            get => _upSpeed;
            set
            {
                if (value == _upSpeed) return;
                _upSpeed = value;
                OnPropertyChanged();
            }
        }

        public string DownSpeed
        {
            get => _downSpeed;
            set
            {
                if (value == _downSpeed) return;
                _downSpeed = value;
                OnPropertyChanged();
            }
        }

        public string DownSpeedMax
        {
            get => _downSpeedMax;
            set
            {
                if (value == _downSpeedMax) return;
                _downSpeedMax = value;
                OnPropertyChanged();
            }
        }

        public string UpSpeedMax
        {
            get => _upSpeedMax;
            set
            {
                if (value == _upSpeedMax) return;
                _upSpeedMax = value;
                OnPropertyChanged();
            }
        }

        public Icon TrayIcon
        {
            get => _icon;
            set
            {
                if (ReferenceEquals(_icon, value)) return;
                _icon = value;
                NotificationIcon.Icon = value;
                OnPropertyChanged();
            }
        }

        public Brush PopupBackground
        {
            get => _popupBackground;
            private set
            {
                if (Equals(value, _popupBackground)) return;
                _popupBackground = value;
                OnPropertyChanged();
            }
        }

        public Brush PopupBorder
        {
            get => _popupBorder;
            set
            {
                if (Equals(value, _popupBorder)) return;
                _popupBorder = value;
                OnPropertyChanged();
            }
        }

        public Brush PopupPanel
        {
            get => _popupPanel;
            set
            {
                if (Equals(value, _popupPanel)) return;
                _popupPanel = value;
                OnPropertyChanged();
            }
        }

        public bool IsPopupOpen
        {
            get => _isPopupOpen;
            set
            {
                if (value == _isPopupOpen) return;
                _isPopupOpen = value;
                OnPropertyChanged();
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            SettingsManager.ReadSettings();

            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.IsEnabled = true;
            
            UpdateIcon(false, false);

            UpdateSpeeds();

            SystemEvents.UserPreferenceChanging += SystemEvents_UserPreferenceChanging;

            UpdateAccentColor();

            Natives.EnableBlur(this);

            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Hide));
        }

        private void UpdateAccentColor()
        {
            File.WriteAllLines(@"F:\Accents.txt", AccentColorSet.ActiveSet.GetAllColorNames().Select(s => {
                var c = AccentColorSet.ActiveSet[s];
                return $"{s}: {c}";
            }));
            var c1 = AccentColorSet.ActiveSet["SystemAccent"];
            var c2 = AccentColorSet.ActiveSet["SystemAccentDark2"];
            var c3 = Color.FromArgb(160,255,255,255); //AccentColorSet.ActiveSet["SystemTextDarkTheme"];
            c2.A = 192;
            PopupBackground = new SolidColorBrush(c2);
            PopupBorder = new SolidColorBrush(c1);
            PopupPanel = new SolidColorBrush(c3);
        }

        private void SystemEvents_UserPreferenceChanging(object sender, UserPreferenceChangingEventArgs e)
        {
            UpdateAccentColor();
        }

        private void NotificationIcon_OnMouseLeftButtonUp(object sender, RoutedEventArgs routedEventArgs)
        {
            Popup();
        }

        private void Popup()
        {
            UpdateGraph2();
            _opening = true;
            DelayInvoke(250, () => {
                _opening = false;
                UpdateGraph2();
            });

            BeginAnimation(OpacityProperty, null);
            BeginAnimation(TopProperty, null);
            Left = SystemParameters.WorkArea.Width - Width;
            Top = SystemParameters.WorkArea.Height;
            Opacity = 0;

            _shown = true;
            Dispatcher.BeginInvoke(new Action(Show));
        }

        private void MainWindow_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible || !_shown)
            {
                Opacity = 0;
                return;
            }

            _shown = false;
            Dispatcher.BeginInvoke(new Action(() => Activate()));

            _showTopAnimation.From = SystemParameters.WorkArea.Height;
            _showTopAnimation.To = SystemParameters.WorkArea.Height - Height;

            DelayInvoke(_showAnimationDelay, () => {
                BeginAnimation(OpacityProperty, _showOpacityAnimation);
                BeginAnimation(TopProperty, _showTopAnimation);
            });
        }

        private void DelayInvoke(uint ms, Action callback)
        {
            DelayInvoke(TimeSpan.FromMilliseconds(ms), callback);
        }

        private void DelayInvoke(TimeSpan time, Action callback)
        {
            if (time.TotalSeconds < float.Epsilon)
            {
                Dispatcher.BeginInvoke(callback);
                return;
            }

            var timer = new DispatcherTimer
            {
                Interval = time
            };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                Dispatcher.Invoke(callback);
            };
            timer.Start();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Hide();
            Opacity = 0;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SettingsManager.WriteSettings();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
        
        private void Timer_Tick(object o, EventArgs e)
        {
            PerformUpdate();
        }

        private void PerformUpdate()
        {
            UpdateSpeeds();

            var sendActivity = _upPoints.Last.Value.Bytes > 0;
            var recvActivity = _downPoints.Last.Value.Bytes > 0;
            UpdateIcon(sendActivity, recvActivity);

            UpSpeed = Util.FormatUSize(_upPoints.Last.Value.Bytes);
            DownSpeed = Util.FormatUSize(_downPoints.Last.Value.Bytes);

            if (!IsVisible || _opening)
                return;

            var upTime = (_upPoints.Last.Value.TimeStamp - _upPoints.First.Value.TimeStamp).TotalSeconds;
            var downTime = (_downPoints.Last.Value.TimeStamp - _downPoints.First.Value.TimeStamp).TotalSeconds;
            var spanSeconds = Math.Max(upTime, downTime);

            var currentCheck = DateTime.Now;
            StartTime = currentCheck.AddSeconds(-spanSeconds).ToString("HH:mm:ss");
            EndTime = currentCheck.ToString("HH:mm:ss");

            UpdateGraph2();
        }

        private void UpdateIcon(bool sendActivity, bool recvActivity)
        {
            if (sendActivity && recvActivity)
            {
                TrayIcon = Properties.Resources.U1D1;
            }
            else if (sendActivity)
            {
                TrayIcon = Properties.Resources.U1D0;
            }
            else if (recvActivity)
            {
                TrayIcon = Properties.Resources.U0D1;
            }
            else
            {
                TrayIcon = Properties.Resources.U0D0;
            }
        }

        private void UpdateSpeeds()
        {
            var maxStamp = NetTracker.UpdateNetwork(out ulong bytesReceivedPerSec, out ulong bytesSentPerSec);

            AddData(_upPoints, maxStamp, bytesSentPerSec);
            AddData(_downPoints, maxStamp, bytesReceivedPerSec);

            _lastMaxDown = _downPoints.Select(s => s.Bytes).Max();
            _lastMaxUp = _upPoints.Select(s => s.Bytes).Max();

            UpSpeedMax = Util.FormatUSize(_lastMaxUp);
            DownSpeedMax = Util.FormatUSize(_lastMaxDown);
        }

        private static void AddData(LinkedList<TimeEntry> points, DateTime maxStamp, ulong bytesSentPerSec)
        {
            points.AddLast(new TimeEntry(maxStamp, bytesSentPerSec));

            var totalSpan = points.Last.Value.TimeStamp - points.First.Value.TimeStamp;
            while (totalSpan.TotalSeconds > NetTracker.MaxSecondSpan && points.Count > 1)
            {
                points.RemoveFirst();
                totalSpan = points.Last.Value.TimeStamp - points.First.Value.TimeStamp;
            }
        }

        private void UpdateGraph2()
        {
            Graph.Children.Clear();
            
            var sqUp = Math.Max(32, Math.Sqrt(_lastMaxUp));
            var sqDown = Math.Max(32, Math.Sqrt(_lastMaxDown));
            var max2 = sqDown + sqUp;
            var maxUp = max2 * _lastMaxUp / sqUp;
            var maxDown = max2 * _lastMaxDown / sqDown;

            BuildPolygon(_upPoints, (ulong) maxUp, 255, 24, 32, true);
            BuildPolygon(_downPoints, (ulong) maxDown, 48, 48, 255,  false);

            var yy = sqDown * Graph.ActualHeight / max2;
            var line = new Line
            {
                X1 = 0,
                X2 = Graph.ActualWidth,
                Y1 = yy,
                Y2 = yy,
                Stroke = Brushes.White,
                Opacity = .6,
                StrokeDashArray = new DoubleCollection(new[] {1.0, 2.0}),
                StrokeDashCap = PenLineCap.Flat
            };
            Graph.Children.Add(line);

            GraphDown.Margin = new Thickness(0, 0, 0, Graph.ActualHeight - yy);
            GraphUp.Margin = new Thickness(0, yy, 0, 0);
        }

        private void BuildPolygon(LinkedList<TimeEntry> points, ulong max, byte r, byte g, byte b, bool up)
        {
            if (points.Count == 0)
                return;

            var bottom = Graph.ActualHeight;
            var right = Graph.ActualWidth;

            var lastTime = points.Last.Value.TimeStamp;

            var elapsed = (lastTime - points.First.Value.TimeStamp).TotalSeconds;

            var scale = 1.0;
            if (elapsed > 0 && elapsed < Graph.ActualWidth)
                scale = Graph.ActualWidth / elapsed;

            var polygon = new Polygon();
            for (var current = points.Last; current != null; current = current.Previous)
            {
                var td = (lastTime - current.Value.TimeStamp).TotalSeconds;

                var xx = right - td * scale;
                var yy = current.Value.Bytes * Graph.ActualHeight / max;

                polygon.Points.Add(new Point(xx, up ? bottom - yy : yy));
            }

            polygon.Points.Add(new Point(right, up ? bottom : 0));

            polygon.Fill = new SolidColorBrush(Color.FromArgb(160, r, g, b));
            Graph.Children.Add(polygon);
        }

        private class TimeEntry
        {
            public readonly DateTime TimeStamp;
            public readonly ulong Bytes;

            public TimeEntry(DateTime t, ulong b)
            {
                TimeStamp = t;
                Bytes = b;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
