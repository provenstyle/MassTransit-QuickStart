using System;
using System.Threading.Tasks;
using Castle.MicroKernel.Registration;
using GreenPipes;
using MassTransit;
using Castle.Windsor;

namespace QuickStart
{
    class Program
    {
        static void Main(string[] args)
        {
            var rabbitUri = new Uri("rabbitmq://localhost");
            var queueName = "test_queue";
            var queueUri  = new Uri($"{rabbitUri}{queueName}");

            var container = new WindsorContainer();
            container.Register(
                Component.For<SomethingINeed>(),
                Classes.FromThisAssembly().BasedOn(typeof(IConsumer<>))
            );
            container.AddMassTransit(c =>
            {
                c.AddBus(context => Bus.Factory.CreateUsingRabbitMq(cfg =>
                {
                    var host = cfg.Host(rabbitUri, h =>
                    {
                        h.Username("guest");
                        h.Password("guest");
                    });

                    cfg.UseInMemoryOutbox();
                    cfg.ReceiveEndpoint(host, queueName, ep =>
                    {
                        ep.Consumer<YourMessageConsumer1>(context);
                        ep.Consumer<RequestConsumer1>(context);
                    });

                    //first level retry
                    cfg.UseRetry(r =>
                    {
                        r.Incremental(10, new TimeSpan(0, 0, 1), new TimeSpan(0, 0, 2));
                    });
                    //second level retry
                    //cfg.UseScheduledRedelivery(r => r.Intervals(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30)));
                }));
            });

            var bus= container.Kernel.Resolve<IBusControl>();
            bus.Start(); // This is important!

            Task.Factory.StartNew(async () =>
            {
                await bus.Publish(new YourMessage { Text = "I was published." });

                var sendEndpoint = await bus.GetSendEndpoint(queueUri);
                await sendEndpoint.Send(new YourMessage{Text = "I was sent."});

                var requestClient = bus.CreateRequestClient<Request1, Response1>(queueUri, TimeSpan.FromSeconds(2));
                var result = await requestClient.Request(new Request1{Message = "Hello request response."});
                Console.Out.WriteLine($"Response: {result.Message}");
            });

            Console.WriteLine("Press any key to exit");
            Console.ReadKey();

            bus.Stop();
        }
    }

    public class YourMessage
    {
        public string Text { get; set; }
    }

    public class YourMessageConsumer1: IConsumer<YourMessage>
    {
        private readonly SomethingINeed _somethingINeed;

        public YourMessageConsumer1(SomethingINeed somethingINeed)
        {
            _somethingINeed = somethingINeed;
        }

        public Task Consume(ConsumeContext<YourMessage> context)
        {
            return Console.Out.WriteLineAsync($"Received by {nameof(YourMessageConsumer1)}: {context.Message.Text}");
        }
    }

    public class SomethingINeed
    {

    }

    public class YourMessageConsumer2: IConsumer<YourMessage>
    {
        public Task Consume(ConsumeContext<YourMessage> context)
        {
            return Console.Out.WriteLineAsync($"Received by {nameof(YourMessageConsumer2)}: {context.Message.Text}");
        }
    }

    public class RequestConsumer1: IConsumer<Request1>
    {
        public async Task Consume(ConsumeContext<Request1> context)
        {
            await context.RespondAsync(new Response1{Message = context.Message.Message});
        }
    }

    public class Request1
    {
        public string Message { get; set; }
    }

    public class Response1
    {
        public string Message { get; set; }
    }
}
