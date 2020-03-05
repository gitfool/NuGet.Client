// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.VisualStudio;

namespace NuGetConsole
{
    /// <summary>
    /// Wrapper console class enabling inline writing into the VS build output window pane.
    /// As the build output is not owned by NuGet but is rather shared with other providers
    /// this implementation doesn't allow certain invasive operations like Clear.
    /// </summary>
    internal sealed class BuildOutputConsole : SharedOutputConsole
    {
        private static Guid BuildWindowPaneGuid = VSConstants.BuildOutput;

        private readonly IVsOutputWindow _vsOutputWindow;
        private IVsOutputWindowPane _outputWindowPane;

        public BuildOutputConsole(IVsOutputWindow vsOutputWindow)
        {
            if (vsOutputWindow == null)
            {
                throw new ArgumentNullException(nameof(vsOutputWindow));
            }

            _vsOutputWindow = vsOutputWindow;
        }


        public override void Activate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_outputWindowPane == null)
            {
                _vsOutputWindow.GetPane(ref BuildWindowPaneGuid, out _outputWindowPane);
            }

            _outputWindowPane?.Activate();
        }

        public override void Clear()
        {
            // It's not our job to clear the build console.
        }

        public override void Write(string text)
        {
            if (_outputWindowPane != null)
            {
                NuGetUIThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    _outputWindowPane.OutputStringThreadSafe(text);
                });
            }
        }
    }
}
