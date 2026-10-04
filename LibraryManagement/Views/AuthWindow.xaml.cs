using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LibraryManagement.Models;
using LibraryManagement.Data;
using LibraryManagement.ViewModels;
using LibraryManagement.Services;

namespace LibraryManagement.Views
{
    public partial class AuthWindow : Window
    {
        private const double FormSlideDistance = 280;
        private const double OverlayContentSlideDistance = 220;
        private const double OverlayTravel = 520;          // overlay đi từ X=455 (Sign in) tới X=-65 (Register)
        private const double DragStartThreshold = 8;       // px tối thiểu để coi là kéo
        private const double CommitRatio = 0.2;            // kéo quá 20% quãng đường thì chuyển mode

        private bool _isRegisterMode;
        private bool _isAnimating;

        // Drag state
        private bool _dragArmed, _dragging;
        private double _dragStartX, _dragBase;

        // Cache bitmap cho các phần tử chuyển động (chỉ bật khi đang kéo/animate)
        private UIElement[] _cacheTargets = Array.Empty<UIElement>();
        private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        public AuthWindow()
        {
            InitializeComponent();

            CardContainer.Background = Brushes.Transparent;   // vùng trống (panel tối) mới nhận chuột
            CardContainer.PreviewMouseMove += CardContainer_PreviewMouseMove;
            BuildCacheTargets();

            Loaded += AuthWindow_Loaded;
        }

        // ================= Progress: 0 = Sign in, 1 = Register =================
        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(nameof(Progress), typeof(double), typeof(AuthWindow),
                new PropertyMetadata(0.0, (d, e) => ((AuthWindow)d).ApplyProgress((double)e.NewValue)));

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        private void ApplyProgress(double p)
        {
            TransOverlay.X = 455 - OverlayTravel * p;

            TransSignIn.X = -FormSlideDistance * p;
            PanelSignIn.Opacity = Math.Clamp(1 - p * 1.6, 0, 1);
            PanelSignIn.IsHitTestVisible = p < 0.05;

            TransRegister.X = FormSlideDistance * (1 - p);
            PanelRegister.Opacity = Math.Clamp(p * 1.6 - 0.6, 0, 1);
            PanelRegister.IsHitTestVisible = p > 0.95;

            TransWelcomeBack.X = -OverlayContentSlideDistance * p;
            PanelWelcomeBack.Opacity = Math.Clamp(1 - p * 1.6, 0, 1);

            TransStartPage.X = OverlayContentSlideDistance * (1 - p);
            PanelStartPage.Opacity = Math.Clamp(p * 1.6 - 0.6, 0, 1);
        }

        // ================= BitmapCache khi chuyển động =================
        private void BuildCacheTargets()
        {
            // Chỉ cache các "lá" (không chứa phần tử đang animate bên trong) để cache không bị vô hiệu mỗi frame
            var list = new List<UIElement> { PanelSignIn, PanelRegister, PanelWelcomeBack, PanelStartPage };
            foreach (UIElement c in SlidingDarkOverlay.Children)
            {
                if (c != PanelWelcomeBack && c != PanelStartPage) list.Add(c);
            }
            _cacheTargets = list.ToArray();
        }

        private void SetCache(bool on)
        {
            CacheMode? cache = on ? new BitmapCache() : null;
            foreach (var el in _cacheTargets) el.CacheMode = cache;
        }

        // ================= Init / Events =================
        private void AuthWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AuthViewModel vm)
            {
                vm.LoginSuccessful += OnLoginSuccessful;
                vm.RequestSwipeToRegister += () => SwipeToRegister();
                vm.RequestSwipeToSignIn += () => SwipeToSignIn();
            }

            LoginControl.RequestNavigateToRegister += () => SwipeToRegister();
            RegisterControl.RequestNavigateToSignIn += () => SwipeToSignIn();
            ApplyAuthMode(false);
        }

        private async void OnLoginSuccessful(User user)
        {
            try
            {
                // Complete schema upgrades in dependency order before constructing MainWindow.
                var migrationResult = LibraryDatabaseStartupMigration.ApplyWithResult();
                if (Application.Current is LibraryManagement.App app)
                    app.SetStartupBookCopyMigration(migrationResult);
            }
            catch (Exception exception)
            {
                MessageBox.Show("Không thể cập nhật database cho các module của ứng dụng: " + exception.Message,
                    "Cập nhật database thất bại", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // The startup scan runs after schema migration and is isolated from login if it fails.
            await NotificationRuntime.CheckDueSoonSafelyAsync();

            // Open MainWindow and close this window
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        // ================= Mode switching =================
        public void SwipeToRegister()
        {
            if (_isRegisterMode || _isAnimating)
            {
                return;
            }
            AnimateAuthMode(true);
        }

        public void SwipeToSignIn()
        {
            if (!_isRegisterMode || _isAnimating)
            {
                return;
            }
            AnimateAuthMode(false);
        }

        private void ApplyAuthMode(bool isRegister)
        {
            _isRegisterMode = isRegister;
            _isAnimating = false;
            UpdateTabVisuals(isRegister);
            SetCache(false);
            Progress = isRegister ? 1 : 0;
            ApplyProgress(Progress);
        }

        private void AnimateAuthMode(bool isRegister)
        {
            _isAnimating = true;
            _isRegisterMode = isRegister;
            UpdateTabVisuals(isRegister);
            SetCache(true);

            double from = Progress, target = isRegister ? 1 : 0;
            Progress = target;   // giá trị cuối gán local, animation chỉ là hiệu ứng

            var anim = new DoubleAnimation(from, target,
                TimeSpan.FromMilliseconds(Math.Max(200, 620 * Math.Abs(target - from))))
            {
                EasingFunction = Ease,
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (_, _) => { _isAnimating = false; SetCache(false); };
            BeginAnimation(ProgressProperty, anim);
        }

        // ================= Kéo chuột kiểu Tinder =================
        private static bool IsInteractive(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is TextBoxBase or PasswordBox or ButtonBase) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void CardContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAnimating || IsInteractive(e.OriginalSource as DependencyObject)) return;

            var pos = e.GetPosition(CardContainer);
            if (pos.Y < 50) return;                    // vùng titlebar (DragMove)

            _dragArmed = true;
            _dragging = false;
            _dragStartX = pos.X;
            _dragBase = Progress;
        }

        private void CardContainer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragArmed) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }

            double dx = e.GetPosition(CardContainer).X - _dragStartX;
            if (!_dragging)
            {
                if (Math.Abs(dx) < DragStartThreshold) return;
                _dragging = true;
                SetCache(true);
                CardContainer.CaptureMouse();
            }
            Progress = Math.Clamp(_dragBase - dx / OverlayTravel, 0, 1);
        }

        private void CardContainer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragArmed) return;
            bool wasDragging = _dragging;
            EndDrag();
            if (!wasDragging) return;

            // Quá ngưỡng thì chuyển mode, chưa tới thì bật về
            bool toRegister = _isRegisterMode ? Progress > 1 - CommitRatio : Progress > CommitRatio;
            AnimateAuthMode(toRegister);
        }

        private void EndDrag()
        {
            _dragArmed = _dragging = false;
            CardContainer.ReleaseMouseCapture();
        }

        // ================= Window chrome =================
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TabSignIn_Click(object sender, RoutedEventArgs e)
        {
            SwipeToSignIn();
        }

        private void TabRegister_Click(object sender, RoutedEventArgs e)
        {
            SwipeToRegister();
        }

        private void UpdateTabVisuals(bool isRegister)
        {
            if (isRegister)
            {
                TabRegister.Background = Brushes.White;
                TabRegister.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                TabRegister.FontWeight = FontWeights.SemiBold;

                TabSignIn.Background = Brushes.Transparent;
                TabSignIn.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                TabSignIn.FontWeight = FontWeights.Medium;
            }
            else
            {
                TabSignIn.Background = Brushes.White;
                TabSignIn.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                TabSignIn.FontWeight = FontWeights.SemiBold;

                TabRegister.Background = Brushes.Transparent;
                TabRegister.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                TabRegister.FontWeight = FontWeights.Medium;
            }
        }
    }
}
