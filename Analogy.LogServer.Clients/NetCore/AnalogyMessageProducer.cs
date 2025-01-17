﻿#if NETCOREAPP3_1 || NET
using Analogy.Interfaces;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Analogy.LogServer.Clients
{
    public class AnalogyMessageProducer : IDisposable, IAsyncDisposable
    {
        public event EventHandler<string> OnError;
        private static readonly int ProcessId = Process.GetCurrentProcess().Id;
        private static readonly string ProcessName = Process.GetCurrentProcess().ProcessName;
        private GrpcChannel channel;
        private AsyncClientStreamingCall<AnalogyGRPCLogMessage, AnalogyMessageReply> stream;
        private bool connected = true;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        static AnalogyMessageProducer()
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="address">The address. for example: http://localhost:6000</param>
        public AnalogyMessageProducer(string address)
        {
            try
            {
                channel = GrpcChannel.ForAddress(address);
                var client = new Analogy.AnalogyClient(channel);
                stream = client.SubscribeForPublishingMessages();
            }
            catch (Exception e)
            {
                OnError?.Invoke(this, $"Error creating gRPC Connection: {e}");
            }

        }

        public async Task Log(string text, string source, AnalogyLogLevel level, string category = "",
            string machineName = null, string userName = null, string processName = null, int processId = 0, int threadId = 0, Dictionary<string, string> additionalInformation = null, [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string filePath = "")
        {
            if (!connected)
            {
                return;
            }

            var m = new AnalogyGRPCLogMessage()
            {
                Text = text,
                Category = category,
                Class = AnalogyGRPCLogClass.General,
                Date = Timestamp.FromDateTime(DateTime.UtcNow),
                FileName = filePath,
                Id = Guid.NewGuid().ToString(),
                Level = GetLogLevel(level),
                LineNumber = lineNumber,
                MachineName = machineName ?? Environment.MachineName,
                MethodName = memberName,
                Module = processName ?? ProcessName,
                ProcessId = processId,
                ThreadId = threadId != 0 ? threadId : Thread.CurrentThread.ManagedThreadId,
                Source = source,
                User = userName ?? Environment.UserName,
            };
            if (additionalInformation != null)
            {
                foreach (KeyValuePair<string, string> keyValuePair in additionalInformation)
                {
                    m.AdditionalInformation.Add(keyValuePair.Key, keyValuePair.Value);
                }
            }

            try
            {
                await _semaphoreSlim.WaitAsync();
                await stream.RequestStream.WriteAsync(m);

            }
            catch (Exception e)
            {
                connected = false;
                OnError?.Invoke(this, $"Error sending message to gRPC Server: {e}");
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        private AnalogyGRPCLogLevel GetLogLevel(AnalogyLogLevel level)
        {
            switch (level)
            {
                case AnalogyLogLevel.Unknown:
                    return AnalogyGRPCLogLevel.Unknown;
                case AnalogyLogLevel.Trace:
                    return AnalogyGRPCLogLevel.Trace;
                case AnalogyLogLevel.Verbose:
                    return AnalogyGRPCLogLevel.Verbose;
                case AnalogyLogLevel.Debug:
                    return AnalogyGRPCLogLevel.Debug;
                case AnalogyLogLevel.Information:
                    return AnalogyGRPCLogLevel.Information;
                case AnalogyLogLevel.Warning:
                    return AnalogyGRPCLogLevel.Warning;
                case AnalogyLogLevel.Error:
                    return AnalogyGRPCLogLevel.Error;
                case AnalogyLogLevel.Critical:
                    return AnalogyGRPCLogLevel.Critical;
                case AnalogyLogLevel.Analogy:
                    return AnalogyGRPCLogLevel.Analogy;
                case AnalogyLogLevel.None:
                    return AnalogyGRPCLogLevel.None;
                default:
                    return AnalogyGRPCLogLevel.Unknown;
            }
        }

        public void StopReceiving()
        {
            try
            {
                channel.ShutdownAsync().Wait(5000);
            }
            catch (Exception e)
            {
                OnError?.Invoke(this, $"Error closing  gRPC connection to Server: {e}");

            }

        }
        public async Task StopReceivingAsync()
        {
            try
            {
                await channel.ShutdownAsync();
            }
            catch (Exception e)
            {
                OnError?.Invoke(this, $"Error closing  gRPC connection to Server: {e}");

            }

        }

        public void Dispose()
        {
            try
            {
                _semaphoreSlim.Dispose();
                channel?.Dispose();
                stream?.Dispose();
            }
            catch (Exception e)
            {
                OnError?.Invoke(this, $"Error during dispose: {e}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _semaphoreSlim.Dispose();
                if (channel != null)
                {
                    await channel.ShutdownAsync();
                }

                stream?.Dispose();
            }
            catch (Exception e)
            {
                OnError?.Invoke(this, $"Error during dispose: {e}");
            }
        }
    }
}
#endif