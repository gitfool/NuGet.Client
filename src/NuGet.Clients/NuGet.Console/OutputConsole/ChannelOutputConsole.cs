// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.RpcContracts.OutputChannel;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using NuGet.VisualStudio;
using Task = System.Threading.Tasks.Task;

namespace NuGetConsole
{
    internal sealed class ChannelOutputConsole : IConsole, IConsoleDispatcher
    {
        private const int DefaultConsoleWidth = 120;

        private IOutputChannelStore _outputChannelStore;
        private PipeWriter ChannelWriter => _channelPipe.Writer;
        private Pipe _channelPipe;
        private readonly IAsyncServiceProvider _asyncServiceProvider;
        private readonly string _channelId;
        private readonly string _outputName;

        public ChannelOutputConsole(IAsyncServiceProvider asyncServiceProvider, string channelId, string outputName)
        {
            _asyncServiceProvider = asyncServiceProvider ?? throw new ArgumentNullException(nameof(asyncServiceProvider));
            _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
            _outputName = outputName ?? throw new ArgumentNullException(nameof(outputName));
        }

        private async Task AcquireServiceAsync(CancellationToken cancellationToken)
        {
            ServiceActivationOptions options = new ServiceActivationOptions();
            options.SetClientDefaults();
            var container = await GetBrokeredServiceContainerAsync();
            IServiceBroker sb = container.GetFullAccessServiceBroker();
            _outputChannelStore = await sb.GetProxyAsync<IOutputChannelStore>(VisualStudioServices.VS2019_4.OutputChannelStore, options, cancellationToken);
            Assumes.Present(_outputChannelStore);
        }

        private async Task<IBrokeredServiceContainer> GetBrokeredServiceContainerAsync()
        {
            return (IBrokeredServiceContainer) await _asyncServiceProvider.GetServiceAsync(typeof(SVsBrokeredServiceContainer));
        }

        public int ConsoleWidth => DefaultConsoleWidth;

        public void Activate()
        {
            // No-Op
        }

        public void Clear()
        {
            // No-Op
        }

        public async Task PrepareToSendOutputAsync(string channelId, string displayNameResourceId, CancellationToken cancellationToken)
        {
            if (_channelPipe == null)
            {
                await AcquireServiceAsync(cancellationToken);
                Pipe pipe = new Pipe();
                await _outputChannelStore.CreateChannelAsync(channelId, displayNameResourceId, pipe.Reader, Encoding.UTF8, cancellationToken);
                _channelPipe = pipe;
            }
        }

        public async Task SendOutputAsync(string message, CancellationToken cancellationToken)
        {
            await PrepareToSendOutputAsync(_channelId, _outputName, cancellationToken);
            ReadOnlyMemory<byte> messageMemory = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(message));
            await ChannelWriter.WriteAsync(messageMemory, cancellationToken);
            await ChannelWriter.FlushAsync(cancellationToken);
        }

        public void CloseChannel()
        {
            ChannelWriter.CancelPendingFlush();
            ChannelWriter.Complete();
            _channelPipe.Reader.CancelPendingRead();
            _channelPipe.Reader.Complete();
        }

        public async Task ClearThePaneAsync(string channelId, string displayNameResourceId, CancellationToken cancellationToken)
        {
            CloseChannel();
            await PrepareToSendOutputAsync(channelId, displayNameResourceId, cancellationToken);
        }

        public void Write(string text)
        {
            NuGetUIThreadHelper.JoinableTaskFactory.Run(() => SendOutputAsync(text, CancellationToken.None));
        }

        public void Write(string text, Color? foreground, Color? background) => Write(text);

        public void WriteBackspace()
        {
            throw new NotSupportedException();
        }

        public void WriteLine(string text) => Write(text + Environment.NewLine);

        public void WriteLine(string format, params object[] args)
        {
            WriteLine(string.Format(CultureInfo.CurrentCulture, format, args));
        }

        public void WriteProgress(string currentOperation, int percentComplete)
        {
        }

        public void Start()
        {
            if (!IsStartCompleted)
            {
                _ = AcquireServiceAsync(CancellationToken.None); // TODO NK
                StartCompleted?.Invoke(this, EventArgs.Empty);
            }

            IsStartCompleted = true;
        }

        public event EventHandler StartCompleted;

        event EventHandler IConsoleDispatcher.StartWaitingKey
        {
            add { }
            remove { }
        }

        public bool IsStartCompleted { get; private set; }

        public bool IsExecutingCommand
        {
            get { return false; }
        }

        public bool IsExecutingReadKey
        {
            get { throw new NotSupportedException(); }
        }

        public bool IsKeyAvailable
        {
            get { throw new NotSupportedException(); }
        }

        public void AcceptKeyInput()
        {
        }

        public VsKeyInfo WaitKey()
        {
            throw new NotSupportedException();
        }

        public void ClearConsole()
        {
            Clear();
        }

        public IHost Host { get; set; }

        public bool ShowDisclaimerHeader => false;

        public IConsoleDispatcher Dispatcher => this;

    }
}
