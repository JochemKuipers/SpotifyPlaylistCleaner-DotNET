using System;
using System.Windows.Input;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public class DelegateCommand<T>(Action<T> execute, Predicate<T>? canExecute = null) : ICommand
{
    private readonly Action<T> _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    public bool CanExecute(object? parameter)
    {
        return canExecute == null || (parameter is T t && canExecute(t));
    }

    public void Execute(object? parameter)
    {
        if (parameter is T t)
            _execute(t);
    }

    public event EventHandler? CanExecuteChanged;
}