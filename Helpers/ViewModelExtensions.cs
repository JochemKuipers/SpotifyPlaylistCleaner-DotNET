using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using SpotifyPlaylistCleaner_DotNET.ViewModels;

namespace SpotifyPlaylistCleaner_DotNET.Helpers
{
    internal static class ViewModelExtensions
    {
        /// <summary>
        /// Executes an action on the UI thread and updates a status message
        /// </summary>
        /// <param name="viewModel">The view model</param>
        /// <param name="message">Status message to set</param>
        /// <param name="action">Optional action to execute</param>
        public static async Task RunOnUIThreadWithStatus(this ViewModelBase viewModel, string message, Action? action = null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (viewModel is IStatusProvider statusProvider)
                    statusProvider.StatusMessage = message;

                action?.Invoke();
            });
        }

        /// <summary>
        /// Safely updates a property on the UI thread
        /// </summary>
        /// <typeparam name="T">Type of the property</typeparam>
        /// <param name="viewModel">The view model</param>
        /// <param name="property">Current property value</param>
        /// <param name="value">New value</param>
        /// <param name="propertyName">Name of the property to raise change notification for</param>
        /// <param name="setter">Action to set the property value</param>
        public static async Task SafelySetProperty<T>(
            this ReactiveObject viewModel,
            T property,
            T value,
            string propertyName,
            Action<T> setter)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Equals(property, value)) return;
                setter(value);
                viewModel.RaisePropertyChanged(propertyName);
            });
        }

        /// <summary>
        /// Executes an async operation with proper loading state management
        /// </summary>
        /// <param name="viewModel">The view model</param>
        /// <param name="operation">The async operation to perform</param>
        /// <param name="loadingMessage">Message to display during loading</param>
        /// <param name="successMessage">Message to display on success</param>
        /// <param name="errorMessagePrefix">Prefix for error messages</param>
        /// <returns>Whether the operation completed successfully</returns>
        public static async Task<bool> ExecuteWithLoadingState<T>(
            this T viewModel,
            Func<Task> operation,
            string loadingMessage,
            string successMessage,
            string errorMessagePrefix) where T : ViewModelBase, ILoadingState, IStatusProvider
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    viewModel.IsLoading = true;
                    viewModel.StatusMessage = loadingMessage;
                });

                await operation();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    viewModel.StatusMessage = successMessage;
                });

                return true;
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    viewModel.StatusMessage = $"{errorMessagePrefix}: {ex.Message}";
                });
                return false;
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    viewModel.IsLoading = false;
                });
            }
        }

        /// <summary>
        /// Interface for view models that provide loading state
        /// </summary>
        public interface ILoadingState
        {
            bool IsLoading { get; set; }
        }

        /// <summary>
        /// Interface for view models that provide status messages
        /// </summary>
        public interface IStatusProvider
        {
            string StatusMessage { get; set; }
        }
    }
}