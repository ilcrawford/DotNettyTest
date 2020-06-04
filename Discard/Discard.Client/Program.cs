using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DotNetty.Handlers.Logging;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using DotNetty.Handlers.Tls;
using System.Net;
using DotNetty.Buffers;

namespace Discard.Client
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration((hostContext, config) =>
                {
                    config.SetBasePath(Environment.CurrentDirectory);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddOptions();
                    services.Configure<AppConfig>(hostContext.Configuration.GetSection("AppConfig"));
                    services.AddSingleton<IHostedService, PrintTextToConsoleSvc>();
                    services.AddSingleton<IHostedService, DiscardClient>();
                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                });

            await hostBuilder.RunConsoleAsync();
        }



        class DiscardClient : IHostedService, IDisposable
        {
            private readonly ILogger<DiscardClient> _logger;
            private readonly IOptions<AppConfig> _appConfig;
            private readonly Bootstrap _bootstrap = new Bootstrap();
            private Task<IChannel> bootstrapChannel;
            private MultithreadEventLoopGroup _workGroup;

            public DiscardClient(ILogger<DiscardClient> logger, IOptions<AppConfig> appConfig)
            {
                _logger = logger;
                _appConfig = appConfig;
            }

            public void Dispose()
            {

            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _logger.LogInformation("Starting DiscardServer");
                _logger.LogInformation($"Connection to: {_appConfig.Value.Host}");
                _logger.LogInformation($"Connection port: {_appConfig.Value.Port}");

                _workGroup = new MultithreadEventLoopGroup();


                _bootstrap
                .Group(_workGroup)
                .Channel<TcpSocketChannel>()
                .Option(ChannelOption.TcpNodelay, true)
                .Handler(new ActionChannelInitializer<ISocketChannel>(channel =>
                {
                    IChannelPipeline pipeline = channel.Pipeline;
                    pipeline.AddLast(new LoggingHandler());
                    pipeline.AddLast(new DiscardClientHandler(_logger, _appConfig));
                }));

                IPAddress host = IPAddress.Parse(_appConfig.Value.Host);
                bootstrapChannel = _bootstrap.ConnectAsync(new IPEndPoint(host, _appConfig.Value.Port));

                return Task.CompletedTask;

            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _logger.LogInformation("Stopping Discard Client.");
                bootstrapChannel.Wait();
                Task.WaitAll(_workGroup.ShutdownGracefullyAsync());
                return Task.CompletedTask;
            }


        }

        class DiscardClientHandler : SimpleChannelInboundHandler<object>
        {
            byte[] array;
            private readonly ILogger _logger;
            private readonly IOptions<AppConfig> _appConfig;

            public DiscardClientHandler(ILogger<DiscardClient> logger, IOptions<AppConfig> appConfig)
            {
                _logger = logger;
                _appConfig = appConfig;
            }
            public override void ChannelActive(IChannelHandlerContext context)
            {
                this.array = new byte[_appConfig.Value.Size];
                this.GenerateTraffic(context);
            }
            protected override void ChannelRead0(IChannelHandlerContext context, object message)
            {
                // Server is supposed to send nothing, but if it sends something, discard it.
            }
            public override void ExceptionCaught(IChannelHandlerContext ctx, Exception e)
            {
                _logger.LogError(e.Message);
                ctx.CloseAsync();
            }

            async void GenerateTraffic(IChannelHandlerContext context)
            {
                try
                {
                    IByteBuffer buffer = Unpooled.WrappedBuffer(this.array);
                    // Flush the outbound buffer to the socket.
                    // Once flushed, generate the same amount of traffic again.
                    await context.WriteAndFlushAsync(buffer);
                    this.GenerateTraffic(context);
                }
                catch
                {
                    await context.CloseAsync();
                }
            }
        }

        class PrintTextToConsoleSvc : IHostedService, IDisposable
        {
            private readonly ILogger _logger;
            private readonly IOptions<AppConfig> _appConfig;
            private Timer _timer;
            public PrintTextToConsoleSvc(ILogger<PrintTextToConsoleSvc> logger, IOptions<AppConfig> appConfig)
            {
                _logger = logger;
                _appConfig = appConfig;
            }

            public void Dispose()
            {
                _timer?.Dispose();
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _logger.LogInformation("Starting");

                _timer = new Timer(DoWork, null, TimeSpan.Zero,
                    TimeSpan.FromSeconds(5));

                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _logger.LogInformation("Stopping.");

                _timer?.Change(Timeout.Infinite, 0);

                return Task.CompletedTask;
            }

            private void DoWork(object state)
            {
                _logger.LogInformation($"Background work with text: {_appConfig.Value.TextToPrint}");
            }

        }

        class AppConfig
        {
            public string TextToPrint { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public int Size { get; set; }
        }

    }

}
