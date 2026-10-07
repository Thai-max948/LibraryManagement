using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Views
{
    public partial class AuthWindow : Window
    {
        private const double AnimationDurationMilliseconds = 600;
        private const double DragStartThreshold = 8;
        private const double CommitRatio = 0.18;
        private const double LoginFadeEnd = 0.44;
        private const double RegisterFadeStart = 0.46;
        private const double FormSlideDistance = 28;

        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(AuthWindow),
            new PropertyMetadata(0d, OnProgressChanged));

        private bool _isRegisterMode;
        private bool _isAnimating;
        private bool _isDragging;
        private bool _dragArmed;
        private bool _suppressLostMouseCapture;
        private bool _hasQueuedMode;
        private bool _queuedRegisterMode;
        private bool _targetRegisterMode;
        private double _dragStartX;
        private double _dragStartProgress;

        public AuthWindow()
        {
            InitializeComponent();
            Loaded += AuthWindow_Loaded;
            Deactivated += AuthWindow_Deactivated;
            StateChanged += AuthWindow_StateChanged;
        }

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, Math.Clamp(value, 0d, 1d));
        }

        private static void OnProgressChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is AuthWindow window)
                window.ApplyProgress((double)e.NewValue);
        }

        private void AuthWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AuthViewModel viewModel)
            {
                viewModel.LoginSuccessful += OnLoginSuccessful;
                viewModel.RequestSwipeToRegister += SwipeToRegister;
                viewModel.RequestSwipeToSignIn += SwipeToSignIn;
            }

            LoginControl.RequestNavigateToRegister += SwipeToRegister;
            RegisterControl.RequestNavigateToSignIn += SwipeToSignIn;
            ApplyAuthMode(false);
        }

        private void ApplyAuthMode(bool isRegister)
        {
            BeginAnimation(ProgressProperty, null);
            _isRegisterMode = isRegister;
            _isAnimating = false;
            _isDragging = false;
            _dragArmed = false;
            _hasQueuedMode = false;
            Progress = isRegister ? 1d : 0d;
            UpdateFormBounds();
            ApplyProgress(Progress);
            UpdateInteractionState();
        }

        public void SwipeToRegister() => AnimateAuthMode(true);

        public void SwipeToSignIn() => AnimateAuthMode(false);

        private void AnimateAuthMode(bool isRegister)
        {
            if (_isAnimating)
            {
                if (isRegister != _targetRegisterMode)
                {
                    _hasQueuedMode = true;
                    _queuedRegisterMode = isRegister;
                }
                return;
            }

            double destination = isRegister ? 1d : 0d;
            if (_isRegisterMode == isRegister && Math.Abs(Progress - destination) < 0.001d)
                return;

            ReleasePointerDrag();
            _targetRegisterMode = isRegister;
            _isAnimating = true;
            _dragArmed = false;
            Keyboard.ClearFocus();
            AuthCanvas.Focus();
            UpdateInteractionState();

            var animation = new DoubleAnimation(Progress, destination,
                TimeSpan.FromMilliseconds(AnimationDurationMilliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            animation.Completed += (_, _) => CompleteAnimation(isRegister);
            BeginAnimation(ProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private void CompleteAnimation(bool isRegister)
        {
            if (!_isAnimating || _targetRegisterMode != isRegister)
                return;

            Progress = isRegister ? 1d : 0d;
            BeginAnimation(ProgressProperty, null);
            _isRegisterMode = isRegister;
            _isAnimating = false;
            ApplyProgress(Progress);
            UpdateInteractionState();

            if (_hasQueuedMode)
            {
                bool queuedMode = _queuedRegisterMode;
                _hasQueuedMode = false;
                if (queuedMode != _isRegisterMode)
                {
                    AnimateAuthMode(queuedMode);
                    return;
                }
            }

            FocusActiveInput();
        }

        private void ApplyProgress(double progress)
        {
            if (BrandTransform is null || LoginTransform is null || RegisterTransform is null)
                return;

            double normalizedProgress = Math.Clamp(progress, 0d, 1d);
            double travel = Math.Max(0d, AuthCanvas.ActualWidth - BrandPanel.Width);

            BrandTransform.X = travel * normalizedProgress;
            LoginTransform.X = BrandPanel.Width - FormSlideDistance * normalizedProgress;
            RegisterTransform.X = FormSlideDistance * (1d - normalizedProgress);
            LoginPanel.Opacity = 1d - Clamp01(normalizedProgress / LoginFadeEnd);
            RegisterPanel.Opacity = Clamp01((normalizedProgress - RegisterFadeStart) / (1d - RegisterFadeStart));
            UpdateInteractionState();
        }

        private void UpdateFormBounds()
        {
            double formWidth = Math.Max(0d, AuthCanvas.ActualWidth - BrandPanel.Width);
            if (formWidth > 0d)
            {
                LoginPanel.Width = formWidth;
                RegisterPanel.Width = formWidth;
            }
        }

        private void UpdateInteractionState()
        {
            if (LoginPanel is null || RegisterPanel is null)
                return;

            bool canInteract = !_isAnimating && !_isDragging;
            LoginPanel.IsEnabled = canInteract && !_isRegisterMode;
            LoginPanel.IsHitTestVisible = canInteract && !_isRegisterMode;
            RegisterPanel.IsEnabled = canInteract && _isRegisterMode;
            RegisterPanel.IsHitTestVisible = canInteract && _isRegisterMode;
        }

        private void FocusActiveInput()
        {
            if (!IsActive || WindowState == WindowState.Minimized)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_isRegisterMode)
                    RegisterControl.FocusFirstInput();
                else
                    LoginControl.FocusFirstInput();
            }));
        }

        private void AuthCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isDragging && (e.WidthChanged || e.HeightChanged))
                CancelPointerDrag();

            UpdateFormBounds();
            ApplyProgress(Progress);
        }

        private void AuthCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAnimating || _isDragging || IsInteractiveInput(e.OriginalSource as DependencyObject))
                return;

            _dragArmed = true;
            _dragStartX = e.GetPosition(AuthCanvas).X;
            _dragStartProgress = _isRegisterMode ? 1d : 0d;
        }

        private void AuthCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed || _isAnimating)
                return;

            double currentX = e.GetPosition(AuthCanvas).X;
            double deltaX = currentX - _dragStartX;
            if (!_isDragging)
            {
                if (Math.Abs(deltaX) < DragStartThreshold)
                    return;

                _isDragging = true;
                Keyboard.ClearFocus();
                AuthCanvas.Focus();
                if (!AuthCanvas.CaptureMouse())
                {
                    _isDragging = false;
                    _dragArmed = false;
                    UpdateInteractionState();
                    return;
                }
                UpdateInteractionState();
            }

            UpdateDragProgress(currentX);
            e.Handled = true;
        }

        private void UpdateDragProgress(double currentX)
        {
            double travel = Math.Max(1d, AuthCanvas.ActualWidth - BrandPanel.Width);
            double deltaX = currentX - _dragStartX;
            Progress = Clamp01(_dragStartProgress + deltaX / travel);
        }

        private void AuthCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragArmed)
                return;

            if (_isDragging)
            {
                UpdateDragProgress(e.GetPosition(AuthCanvas).X);
                bool targetRegister = _isRegisterMode
                    ? Progress <= 1d - CommitRatio
                    : Progress >= CommitRatio;
                _dragArmed = false;
                ReleasePointerDrag();
                e.Handled = true;
                AnimateAuthMode(targetRegister);
                return;
            }

            _dragArmed = false;
        }

        private void AuthCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_suppressLostMouseCapture || (!_isDragging && !_dragArmed))
                return;

            bool wasDragging = _isDragging;
            _dragArmed = false;
            _isDragging = false;
            UpdateInteractionState();
            if (wasDragging)
                AnimateAuthMode(_isRegisterMode);
        }

        private void CancelPointerDrag()
        {
            bool wasDragging = _isDragging;
            _dragArmed = false;
            ReleasePointerDrag();
            if (wasDragging)
                AnimateAuthMode(_isRegisterMode);
            else
                UpdateInteractionState();
        }

        private void ReleasePointerDrag()
        {
            _isDragging = false;
            if (!ReferenceEquals(Mouse.Captured, AuthCanvas))
                return;

            _suppressLostMouseCapture = true;
            AuthCanvas.ReleaseMouseCapture();
            _suppressLostMouseCapture = false;
        }

        private void AuthWindow_Deactivated(object? sender, EventArgs e)
        {
            if (_dragArmed || _isDragging)
                CancelPointerDrag();
        }

        private void AuthWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized)
                return;

            CancelPointerDrag();
            if (_isAnimating)
            {
                bool finalMode = _hasQueuedMode ? _queuedRegisterMode : _targetRegisterMode;
                _hasQueuedMode = false;
                ApplyAuthMode(finalMode);
            }
        }

        private static bool IsInteractiveInput(DependencyObject? source)
        {
            for (DependencyObject? current = source; current is not null; current = GetParent(current))
            {
                if (current is TextBoxBase or PasswordBox or ButtonBase or ScrollBar)
                    return true;
            }
            return false;
        }

        private static DependencyObject? GetParent(DependencyObject element)
        {
            if (element is Visual or Visual3D)
            {
                DependencyObject? visualParent = VisualTreeHelper.GetParent(element);
                if (visualParent is not null)
                    return visualParent;
            }

            return LogicalTreeHelper.GetParent(element);
        }

        private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

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

            // Open MainWindow and close this window.
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }
    }
}
