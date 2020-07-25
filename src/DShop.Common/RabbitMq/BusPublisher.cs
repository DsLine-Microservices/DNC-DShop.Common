using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DShop.Common.Messages;
using RawRabbit;
using RawRabbit.Enrichers.MessageContext;
using RawRabbit.Pipe;

namespace DShop.Common.RabbitMq
{
    public class BusPublisher : IBusPublisher
    {
        private readonly List<IBusClient> _busClient;

        public BusPublisher(List<IBusClient> busClient)
        {
            _busClient = busClient;
        }

        public async Task SendAsync<TCommand>(TCommand command, ICorrelationContext context, string tenant = null)
            where TCommand : ICommand

        {
            _busClient.ForEach(async x => { await x.PublishAsync(command, ctx => ctx.UseMessageContext(context)); });
            IBusClient client = _busClient.Where(x =>
            {
                if (GetVirtualHostInRawRabbitConfiguration(x) == tenant)
                {
                    return true;
                }

                return false;
            }).SingleOrDefault();

            await client.PublishAsync(command, ctx => ctx.UseMessageContext(context));
        }


        public async Task PublishAsync<TEvent>(TEvent @event, ICorrelationContext context, string tenant = null)
            where TEvent : IEvent
        {
            IBusClient client = _busClient.Where(x =>
            {
                if (GetVirtualHostInRawRabbitConfiguration(x) == tenant)
                {
                    return true;
                }

                return false;
            }).SingleOrDefault();


            await client.PublishAsync(@event, ctx => ctx.UseMessageContext(context));
        }

        private string GetVirtualHostInRawRabbitConfiguration(object busClient)
        {
            RawRabbit.Pipe.PipeContextFactory pipeContextFactory = typeof(RawRabbit.BusClient).GetField("_contextFactory", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(busClient) as PipeContextFactory;

            RawRabbit.Configuration.RawRabbitConfiguration RawRabbitConfiguration = typeof(RawRabbit.Pipe.PipeContextFactory).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pipeContextFactory) as RawRabbit.Configuration.RawRabbitConfiguration;

            return RawRabbitConfiguration.VirtualHost;

        }

    }
}