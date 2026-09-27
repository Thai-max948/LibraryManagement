using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;

namespace LibraryManagement.Tests
{
    public static class StaHelper
    {
        private static bool _appInitialized;
        private static readonly object _initLock = new();

        public static void EnsureApplication()
        {
            if (_appInitialized) return;

            lock (_initLock)
            {
                if (_appInitialized) return;

                if (Application.Current == null)
                {
                    try
                    {
                        var app = new LibraryManagement.App();
                        app.InitializeComponent();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error initializing App: {ex}");
                    }
                }

                _appInitialized = true;
            }
        }

        public static void RunInSta(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                EnsureApplication();
                action();
                return;
            }

            Exception? ex = null;
            var thread = new Thread(() =>
            {
                try
                {
                    EnsureApplication();
                    action();
                }
                catch (Exception e)
                {
                    ex = e;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (ex != null)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }

        public static T RunInSta<T>(Func<T> func)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                EnsureApplication();
                return func();
            }

            T result = default!;
            Exception? ex = null;
            var thread = new Thread(() =>
            {
                try
                {
                    EnsureApplication();
                    result = func();
                }
                catch (Exception e)
                {
                    ex = e;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (ex != null)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }

            return result;
        }
    }
}
