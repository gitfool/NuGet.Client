// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Windows.Media;
using NuGet.VisualStudio;

namespace NuGetConsole
{
    /// <summary>
    /// Contains a shared implementation of <see cref="IOutputConsole"/>. This class declares the methods that need implemented as abstract.
    /// </summary>
    internal abstract class SharedOutputConsole : IOutputConsole
    {
        // The next 3 methods are the ones that need to be overriden
        public abstract void Activate();

        public abstract void Clear();

        public abstract void Write(string text);

        public int ConsoleWidth => 120;

        public void Write(string text, Color? foreground, Color? background)
        {
            // the output window doesn't allow setting text color
            Write(text);
        }

        public void WriteBackspace()
        {
            throw new NotSupportedException();
        }

        public void WriteLine(string text)
        {
            Write(text + Environment.NewLine);
        }

        public void WriteLine(string format, params object[] args)
        {
            WriteLine(string.Format(CultureInfo.CurrentCulture, format, args));
        }

        public void WriteProgress(string operation, int percentComplete)
        {
        }
    }
}
