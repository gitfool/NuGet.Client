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
    internal class BuildChannelOutputConsole : SharedOutputConsole, IDisposable
    {
        private static readonly Encoding TextEncoding = Encoding.UTF8;

        private readonly List<string> _deferredOutputMessages = new List<string>();
        private readonly AsyncSemaphore _pipeLock = new AsyncSemaphore(1);

        private readonly IAsyncServiceProvider _asyncServiceProvider;
        private readonly string _channelId;

        private ServiceBrokerClient _serviceBrokerClient;
        private PipeWriter _channelPipeWriter;
        private bool _disposedValue = false;

        public BuildChannelOutputConsole(IAsyncServiceProvider asyncServiceProvider, string channelId)
        {
            _asyncServiceProvider = asyncServiceProvider ?? throw new ArgumentNullException(nameof(asyncServiceProvider));
            _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
        }

        public override void Activate()
        {
            ThrowIfDisposed();
            // TODO NK 
        }

        public override void Clear()
        {
            ThrowIfDisposed();
            // It's not our job to clear the build.
        }

        public override void Write(string text)
        {
            ThrowIfDisposed();
            NuGetUIThreadHelper.JoinableTaskFactory.Run(() => SendOutputAsync(text, CancellationToken.None));
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

        private async Task AcquireServiceAsync()
        {
            IBrokeredServiceContainer container = (IBrokeredServiceContainer)await _asyncServiceProvider.GetServiceAsync(typeof(SVsBrokeredServiceContainer));
            Assumes.Present(container);
            IServiceBroker sb = container.GetFullAccessServiceBroker();
            _serviceBrokerClient = new ServiceBrokerClient(sb, NuGetUIThreadHelper.JoinableTaskFactory);
        }

        private async Task WriteToOutputChannelAsync(string channelId, string content, CancellationToken cancellationToken)
        {
            using (await _pipeLock.EnterAsync())
            {
                if (_channelPipeWriter == null)
                {
                    await AcquireServiceAsync();

                    var pipe = new Pipe();

                    using (var outputChannelStore = await _serviceBrokerClient.GetProxyAsync<IOutputChannelStore>(VisualStudioServices.VS2019_4.OutputChannelStore, cancellationToken))
                    {
                        if (outputChannelStore.Proxy != null)
                        {
                            // TODO NK - does this work?
                            await outputChannelStore.Proxy.CreateChannelAsync(channelId, "Build", pipe.Reader, TextEncoding, cancellationToken);

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
            await WriteToOutputChannelAsync(_channelId, message, cancellationToken);
        }

        private Task ClearThePaneAsync()
        {
            return Task.CompletedTask;
            // TODO NK - Figure out how to clean the pane
            // await CloseChannelAsync();
            // await PrepareToSendOutputAsync(channelId, displayNameResourceId, cancellationToken);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _serviceBrokerClient?.Dispose();
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

        private void ThrowIfDisposed()
        {
            if (_disposedValue)
            {
                throw new ObjectDisposedException(nameof(BuildChannelOutputConsole));
            }
        }
    }
}
