using System;

namespace ScrcpyNet
{
    public sealed class ScrcpyErrorEventArgs : EventArgs
    {
        public ScrcpyErrorEventArgs(Exception exception)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        public Exception Exception { get; }
    }
}
