﻿namespace SqualrCore.Source.Engine.Proxy
{
    using Output;
    using SqualrCore.Source.Analytics;
    using SqualrProxy;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.ServiceModel;
    using System.Threading;

    /// <summary>
    /// Communicates with proxy services. These issue commands that require 32 or 64 bit specifically.
    /// </summary>
    internal class ProxyCommunicator
    {
        /// <summary>
        /// The 32 bit proxy service executable
        /// </summary>
        private const String Proxy32Executable = "SqualrProxy32.exe";

        /// <summary>
        /// The 64 bit proxy service executable
        /// </summary>
        private const String Proxy64Executable = "SqualrProxy64.exe";

        /// <summary>
        /// The event name for a wait event, which allows us to wait for a proxy service to start
        /// </summary>
        private const String WaitEventName = @"Global\Squalr";

        /// <summary>
        /// Uri prefix for IPC channel names
        /// </summary>
        private const String UriPrefix = "net.pipe://localhost/";

        /// <summary>
        /// Singleton instance of the <see cref="ProxyCommunicator" /> class
        /// </summary>
        private static Lazy<ProxyCommunicator> proxyCommunicatorInstance = new Lazy<ProxyCommunicator>(
            () => { return new ProxyCommunicator(); },
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Prevents a default instance of the <see cref="ProxyCommunicator" /> class from being created
        /// </summary>
        private ProxyCommunicator()
        {
            // Initialize channel names
            String proxy32ServerName = ProxyCommunicator.UriPrefix + Guid.NewGuid().ToString();
            String proxy64ServerName = ProxyCommunicator.UriPrefix + Guid.NewGuid().ToString();

            // Start 32 and 64 bit proxy services
            this.Proxy32 = this.StartProxyService(ProxyCommunicator.Proxy32Executable, proxy32ServerName);
            this.Proxy64 = this.StartProxyService(ProxyCommunicator.Proxy64Executable, proxy64ServerName);
        }

        /// <summary>
        /// Gets or sets the 32 bit proxy service
        /// </summary>
        private IProxyService Proxy32 { get; set; }

        /// <summary>
        /// Gets or sets the 64 bit proxy service
        /// </summary>
        private IProxyService Proxy64 { get; set; }

        /// <summary>
        /// Gets a singleton instance of the <see cref="ProxyCommunicator"/> class
        /// </summary>
        /// <returns>A singleton instance of the class</returns>
        public static ProxyCommunicator GetInstance()
        {
            return proxyCommunicatorInstance.Value;
        }

        /// <summary>
        /// Gets the proxy service based on provided parameters
        /// </summary>
        /// <param name="is32Bit">Whether or not to get the 32 or 64 bit service</param>
        /// <returns>The 32 or 64 bit service</returns>
        public IProxyService GetProxyService(Boolean is32Bit)
        {
            if (is32Bit)
            {
                return this.Proxy32;
            }
            else
            {
                return this.Proxy64;
            }
        }

        /// <summary>
        /// Starts a proxy service
        /// </summary>
        /// <param name="executableName">The executable name of the service to start</param>
        /// <param name="channelServerName">The channel name for IPC</param>
        /// <returns>The proxy service that is created</returns>
        private IProxyService StartProxyService(String executableName, String channelServerName)
        {
            try
            {
                // Start the proxy service
                EventWaitHandle processStartEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ProxyCommunicator.WaitEventName);
                ProcessStartInfo processInfo = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), executableName));
                processInfo.Arguments = Process.GetCurrentProcess().Id.ToString() + " " + channelServerName + " " + ProxyCommunicator.WaitEventName;
                processInfo.UseShellExecute = false;
                processInfo.CreateNoWindow = true;
                Process.Start(processInfo);
                processStartEvent.WaitOne();

                // Create connection
                NetNamedPipeBinding binding = new NetNamedPipeBinding(NetNamedPipeSecurityMode.None);
                binding.ReceiveTimeout = TimeSpan.MaxValue;
                binding.MaxReceivedMessageSize = Int32.MaxValue;
                binding.MaxBufferSize = Int32.MaxValue;

                EndpointAddress endpoint = new EndpointAddress(channelServerName);
                IProxyService proxyService = ChannelFactory<IProxyService>.CreateChannel(binding, endpoint);

                OutputViewModel.GetInstance().Log(OutputViewModel.LogLevel.Info, "Started proxy service: " + executableName + " over channel " + channelServerName);

                return proxyService;
            }
            catch (Exception ex)
            {
                OutputViewModel.GetInstance().Log(OutputViewModel.LogLevel.Fatal, "Failed to start proxy service: " + executableName + ". This may impact Scripts and .NET explorer", ex);
                AnalyticsService.GetInstance().SendEvent(AnalyticsService.AnalyticsAction.General, ex);
                return null;
            }
        }
    }
    //// End class
}
//// End namespace