// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using NuGet.VisualStudio;
using IAsyncServiceProvider = Microsoft.VisualStudio.Shell.IAsyncServiceProvider;

namespace NuGetConsole
{
    [Export(typeof(IOutputConsoleProvider))]
    public class OutputConsoleProvider : IOutputConsoleProvider
    {
        private readonly IEnumerable<Lazy<IHostProvider, IHostMetadata>> _hostProviders;
        private readonly Lazy<IConsole> _cachedOutputConsole;
        private IAsyncServiceProvider _asyncServiceProvider;

        private readonly AsyncLazy<IVsOutputWindow> _vsOutputWindow;
        private IVsOutputWindow VsOutputWindow => NuGetUIThreadHelper.JoinableTaskFactory.Run(_vsOutputWindow.GetValueAsync);

        [ImportingConstructor]
        OutputConsoleProvider(
            [ImportMany]
            IEnumerable<Lazy<IHostProvider, IHostMetadata>> hostProviders)
            : this(AsyncServiceProvider.GlobalProvider, hostProviders)
        { }

        OutputConsoleProvider(
            IAsyncServiceProvider asyncServiceProvider,
            IEnumerable<Lazy<IHostProvider, IHostMetadata>> hostProviders)
        {
            _asyncServiceProvider = asyncServiceProvider ?? throw new ArgumentNullException(nameof(asyncServiceProvider));
            _hostProviders = hostProviders ?? throw new ArgumentNullException(nameof(hostProviders));

            _vsOutputWindow = new AsyncLazy<IVsOutputWindow>(
                async () =>
                {
                    return await asyncServiceProvider.GetServiceAsync<SVsOutputWindow, IVsOutputWindow>();
                },
                NuGetUIThreadHelper.JoinableTaskFactory);

            _cachedOutputConsole = new Lazy<IConsole>(
                () => new ChannelOutputConsole(_asyncServiceProvider, GuidList.guidNuGetOutputWindowPaneGuid.ToString(), Resources.OutputConsolePaneName));
        }

        public IOutputConsole CreateBuildOutputConsole()
        {
            return new BuildOutputConsole(VsOutputWindow);
        }

        public IOutputConsole CreatePackageManagerConsole()
        {
            return _cachedOutputConsole.Value;
        }

        public IConsole CreatePowerShellConsole()
        {
            var console = _cachedOutputConsole.Value;

            if (console.Host == null)
            {
                var hostProvider = GetPowerShellHostProvider();
                console.Host = hostProvider.CreateHost(@async: false);
            }

            return console;
        }

        private IHostProvider GetPowerShellHostProvider()
        {
            // The PowerConsole design enables multiple hosts (PowerShell, Python, Ruby)
            // For the Output window console, we're only interested in the PowerShell host. 
            // Here we filter out the PowerShell host provider based on its name.

            // The PowerShell host provider name is defined in PowerShellHostProvider.cs
            const string PowerShellHostProviderName = "NuGetConsole.Host.PowerShell";

            var psProvider = _hostProviders
                .Single(export => export.Metadata.HostName == PowerShellHostProviderName);

            return psProvider.Value;
        }
    }
}
