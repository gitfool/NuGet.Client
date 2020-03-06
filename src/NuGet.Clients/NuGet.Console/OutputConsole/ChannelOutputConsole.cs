// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.RpcContracts.OutputChannel;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio.Threading;
using NuGet.VisualStudio;
using Task = System.Threading.Tasks.Task;

namespace NuGetConsole
{
    internal class ChannelOutputConsole : SharedOutputConsole, IConsole, IConsoleDispatcher, IDisposable
    {
        private static readonly Encoding TextEncoding = Encoding.UTF8;

        private readonly List<string> _deferredOutputMessages = new List<string>();
        private readonly AsyncSemaphore _pipeLock = new AsyncSemaphore(1);

        private readonly string _channelId;
        private readonly string _outputName;

        private AsyncLazy<ServiceBrokerClient> _serviceBrokerClient;
        private PipeWriter _channelPipeWriter;
        private bool _disposedValue = false;

        public ChannelOutputConsole(IAsyncServiceProvider asyncServiceProvider, string channelId, string outputName)
        {
            if (asyncServiceProvider == null)
            {
                throw new ArgumentNullException(nameof(asyncServiceProvider));
            }
            _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
            _outputName = outputName ?? throw new ArgumentNullException(nameof(outputName));

            _serviceBrokerClient = new AsyncLazy<ServiceBrokerClient>(async () =>
            {
                IBrokeredServiceContainer container = (IBrokeredServiceContainer)await asyncServiceProvider.GetServiceAsync(typeof(SVsBrokeredServiceContainer));
                Assumes.Present(container);
                IServiceBroker sb = container.GetFullAccessServiceBroker();
                return new ServiceBrokerClient(sb, NuGetUIThreadHelper.JoinableTaskFactory);
            }, NuGetUIThreadHelper.JoinableTaskFactory);
        }

        public override void Activate()
        {
            // No-Op
        }

        public override void Clear()
        {
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
            ClearThePaneAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
        }
        public override void Write(string text)
        {
            NuGetUIThreadHelper.JoinableTaskFactory.Run(() => SendOutputAsync(text, CancellationToken.None));
        }

        private async Task WriteToOutputChannelAsync(string channelId, string displayNameResourceId, string content, CancellationToken cancellationToken)
        {
            using (await _pipeLock.EnterAsync())
            {
                if (_channelPipeWriter == null)
                {
                    var pipe = new Pipe();

                    using (var outputChannelStore = await (await _serviceBrokerClient.GetValueAsync()).GetProxyAsync<IOutputChannelStore>(VisualStudioServices.VS2019_4.OutputChannelStore, cancellationToken))
                    {
                        if (outputChannelStore.Proxy != null)
                        {
                            await outputChannelStore.Proxy.CreateChannelAsync(channelId, displayNameResourceId, pipe.Reader, TextEncoding, cancellationToken);
                            _channelPipeWriter = pipe.Writer;

                            // write any deferred messages
                            foreach (var s in _deferredOutputMessages)
                            {
                                // Flush when the original content is logged below
                                await _channelPipeWriter.WriteAsync(GetBytes(content), cancellationToken);
                            }
                            _deferredOutputMessages.Clear();
                        }
                        else
                        {
                            // OutputChannel is not available so cache the output messages for later
                            _deferredOutputMessages.Add(content);
                            return;
                        }
                    }
                }
                await _channelPipeWriter.WriteAsync(GetBytes(content), cancellationToken);
                await _channelPipeWriter.FlushAsync(cancellationToken);
            }
        }

        private static byte[] GetBytes(string content)
        {
            return TextEncoding.GetBytes(content);
        }

        private async Task SendOutputAsync(string message, CancellationToken cancellationToken)
        {
            await WriteToOutputChannelAsync(_channelId, _outputName, message, cancellationToken);
        }

        private async Task CloseChannelAsync()
        {
            using (await _pipeLock.EnterAsync())
            {
                _channelPipeWriter?.CancelPendingFlush();
                _channelPipeWriter?.Complete();
                _channelPipeWriter = null;
            }
        }

        private async Task ClearThePaneAsync()
        {
            await CloseChannelAsync();
            // await PrepareToSendOutputAsync(channelId, displayNameResourceId, cancellationToken);
        }

        public void Start()
        {
            if (!IsStartCompleted)
            {
                _ = _serviceBrokerClient.GetValue(); // TODO NK
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

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    if (_serviceBrokerClient.IsValueCreated)
                    {
                        _serviceBrokerClient.GetValue().Dispose();
                    }
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                    CloseChannelAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
                }
                _disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
        }
    }
}
