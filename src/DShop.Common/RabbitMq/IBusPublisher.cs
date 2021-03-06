using System.Threading.Tasks;
using DShop.Common.Messages;

namespace DShop.Common.RabbitMq
{
    public interface IBusPublisher
    {
        Task SendAsync<TCommand>(TCommand command, ICorrelationContext context, string tenant)
            where TCommand : ICommand;

        Task PublishAsync<TEvent>(TEvent @event, ICorrelationContext context, string tenant)
            where TEvent : IEvent;
    }
}