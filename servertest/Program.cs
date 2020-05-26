using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DotNetty.Handlers.Logging;
using DotNetty.Handlers.Tls;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using System.Runtime.InteropServices.ComTypes;

namespace GenericHost
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
                    services.AddSingleton<IHostedService, DiscardServer>();
                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                });
            
            await hostBuilder.RunConsoleAsync();
        }

        class DiscardServerHandler : SimpleChannelInboundHandler<object>
        {
            private readonly ILogger _logger;

            public DiscardServerHandler(ILogger logger)
            {
                _logger = logger;
            }


            protected override void ChannelRead0(IChannelHandlerContext context, object message)
            {
                _logger.LogInformation(message.ToString());
            }

            public override void ExceptionCaught(IChannelHandlerContext context, Exception e)
            {
                _logger.LogError(e.Message);
                context.CloseAsync();
            }
        }

        class DiscardServer : IHostedService, IDisposable
        {
            private readonly ILogger _logger;
            private readonly IOptions<AppConfig> _appConfig;
            private readonly ServerBootstrap _bootstrap = new ServerBootstrap();
            private  Task<IChannel> bootstrapChannel;
            private MultithreadEventLoopGroup _bossGroup;
            private MultithreadEventLoopGroup _workGroup;

            


            public DiscardServer(ILogger<PrintTextToConsoleSvc> logger, IOptions<AppConfig> appConfig)
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

                _bossGroup = new MultithreadEventLoopGroup(1);
                _workGroup = new MultithreadEventLoopGroup();

                    
                _bootstrap
                .Group(_bossGroup, _workGroup)
                .Channel<TcpServerSocketChannel>()
                .Option(ChannelOption.SoBacklog, 100)
                .Handler(new LoggingHandler("LSTN"))
                .ChildHandler(new ActionChannelInitializer<ISocketChannel>(channel =>
                { 
                    IChannelPipeline pipeline = channel.Pipeline;
                    pipeline.AddLast(new LoggingHandler("CONN"));
                    pipeline.AddLast(new DiscardServerHandler(_logger));
                }));

                bootstrapChannel = _bootstrap.BindAsync(8088);


                return Task.CompletedTask;

            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                _logger.LogInformation("Stopping DiscardServer.");
                bootstrapChannel.Wait();
                Task.WaitAll(_bossGroup.ShutdownGracefullyAsync(), _workGroup.ShutdownGracefullyAsync());
                return Task.CompletedTask;
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
        }
    }
}
