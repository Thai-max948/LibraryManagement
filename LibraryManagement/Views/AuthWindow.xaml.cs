using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LibraryManagement.Models;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views
{
    public partial class AuthWindow : Window
    {
        private bool _isRegisterMode = false;
        private bool _isAnimating;
        private double _swipeStartX;

        public AuthWindow()
        {
            InitializeComponent();

            Loaded += AuthWindow_Loaded;
        }

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

        private void OnLoginSuccessful(User user)
        {
            // Open MainWindow and close this window
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        public void SwipeToRegister()
        {
            if (_isRegisterMode || _isAnimating) return;
            AnimateAuthMode(true);
        }

        public void SwipeToSignIn()
        {
            if (!_isRegisterMode || _isAnimating) return;
            AnimateAuthMode(false);
        }

        private const double FormSlideDistance = 280;
        private const double OverlayContentSlideDistance = 220;

        private void AnimateAuthMode(bool isRegister)
        {
            _isAnimating = true;
            bool wasRegister = _isRegisterMode;
            _isRegisterMode = isRegister;
            UpdateTabVisuals(isRegister);

            PanelSignIn.IsHitTestVisible = false;
            PanelRegister.IsHitTestVisible = false;

            var duration = new Duration(TimeSpan.FromMilliseconds(620));
            var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var storyboard = new Storyboard();

            // 1. Sliding Dark Overlay across card (520px)
            AddAnimation(storyboard, TransOverlay, TranslateTransform.XProperty,
                wasRegister ? -65 : 455, isRegister ? -65 : 455, duration, easing);

            // 2. Sign In Form: slides between 0 and -FormSlideDistance (to/from left)
            AddAnimation(storyboard, TransSignIn, TranslateTransform.XProperty,
                wasRegister ? -FormSlideDistance : 0, isRegister ? -FormSlideDistance : 0, duration, easing);
            AddAnimation(storyboard, PanelSignIn, UIElement.OpacityProperty,
                wasRegister ? 0 : 1, isRegister ? 0 : 1, duration, easing, isRegister ? 0 : 60);

            // 3. Register Form: slides between +FormSlideDistance and 0 (to/from right)
            AddAnimation(storyboard, TransRegister, TranslateTransform.XProperty,
                wasRegister ? 0 : FormSlideDistance, isRegister ? 0 : FormSlideDistance, duration, easing);
            AddAnimation(storyboard, PanelRegister, UIElement.OpacityProperty,
                wasRegister ? 1 : 0, isRegister ? 1 : 0, duration, easing, isRegister ? 60 : 0);

            // 4. Dark Overlay Content - "Welcome back."
            AddAnimation(storyboard, TransWelcomeBack, TranslateTransform.XProperty,
                wasRegister ? -OverlayContentSlideDistance : 0, isRegister ? -OverlayContentSlideDistance : 0, duration, easing);
            AddAnimation(storyboard, PanelWelcomeBack, UIElement.OpacityProperty,
                wasRegister ? 0 : 1, isRegister ? 0 : 1, duration, easing, isRegister ? 0 : 60);

            // 5. Dark Overlay Content - "Start the first page."
            AddAnimation(storyboard, TransStartPage, TranslateTransform.XProperty,
                wasRegister ? 0 : OverlayContentSlideDistance, isRegister ? 0 : OverlayContentSlideDistance, duration, easing);
            AddAnimation(storyboard, PanelStartPage, UIElement.OpacityProperty,
                wasRegister ? 1 : 0, isRegister ? 1 : 0, duration, easing, isRegister ? 60 : 0);

            storyboard.Completed += (_, _) =>
            {
                ApplyAuthMode(isRegister);
                storyboard.Remove(this);
                _isAnimating = false;
            };
            storyboard.Begin(this, true);
        }

        private static void AddAnimation(
            Storyboard storyboard,
            DependencyObject target,
            DependencyProperty property,
            double from,
            double to,
            Duration duration,
            IEasingFunction easing,
            int delayMilliseconds = 0)
        {
            var animation = new DoubleAnimation(from, to, duration)
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            storyboard.Children.Add(animation);
        }

        private void ApplyAuthMode(bool isRegister)
        {
            _isRegisterMode = isRegister;
            _isAnimating = false;
            UpdateTabVisuals(isRegister);

            TransOverlay.X = isRegister ? -65 : 455;
            PanelSignIn.Opacity = isRegister ? 0 : 1;
            PanelSignIn.IsHitTestVisible = !isRegister;
            TransSignIn.X = isRegister ? -FormSlideDistance : 0;

            PanelRegister.Opacity = isRegister ? 1 : 0;
            PanelRegister.IsHitTestVisible = isRegister;
            TransRegister.X = isRegister ? 0 : FormSlideDistance;

            PanelWelcomeBack.Opacity = isRegister ? 0 : 1;
            TransWelcomeBack.X = isRegister ? -OverlayContentSlideDistance : 0;

            PanelStartPage.Opacity = isRegister ? 1 : 0;
            TransStartPage.X = isRegister ? 0 : OverlayContentSlideDistance;
        }

        private void CardContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _swipeStartX = e.GetPosition(CardContainer).X;
        }

        private void CardContainer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            double endX = e.GetPosition(CardContainer).X;
            double deltaX = endX - _swipeStartX;

            // Swipe threshold of 60px
            if (deltaX < -60 && !_isRegisterMode)
            {
                SwipeToRegister();
            }
            else if (deltaX > 60 && _isRegisterMode)
            {
                SwipeToSignIn();
            }
        }

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
